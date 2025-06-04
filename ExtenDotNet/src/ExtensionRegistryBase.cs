using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExtenDotNet;

internal abstract class ExtensionRegistryBase
{
    protected readonly ILogger? _logger;
    protected readonly ConcurrentDictionary<IExtensionPoint, ExtensionEntry> _cache = [];
    protected readonly Dictionary<IExtensionPoint, SemaphoreSlim> _compilationLocks = [];
    protected bool _disposed = false;
    protected readonly IOptions<ExtensionRegistryOpts> _opts;
    protected readonly List<IExtensionPoint> _registeredExtensionPoints;
    public IEnumerable<IExtensionPoint> RegisteredExtensions => _registeredExtensionPoints;
    
    public ExtensionRegistryBase(
        IScriptFactory _factory,
        IServiceProvider _provider,
        IOptions<ExtensionRegistryOpts> opts,
        ILogger? logger = null
    )
    {
        factory = _factory;
        provider = _provider;
        _logger = logger;
        _opts = opts;
        _registeredExtensionPoints = opts.Value.RegisteredExtensionPoints.ToList();
    }
    
    internal virtual async Task<T?> ResolveInternalAsync<T>(IExtensionPoint key, IServiceProvider provider) where T: class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if(_opts.Value.AllowOnlyPreregisteredExtensions && !_registeredExtensionPoints.Contains(key))
            throw new ExtensionExcepton($"ExtensionPoint {key} is not registered");
        
        if(_cache.TryGetValue(key, out var res))
            return (T?)res.Value;
            
        var sem = GetCompilationLock(key);
        await sem.WaitAsync();
        try
        {
            if(_cache.TryGetValue(key, out res))
                return (T?)res.Value;
                
            var s = ((Script?)factory.GetScript(key))?.As<ScriptScope<ExtensionScriptContext>>();
            T? r = null;
            string? path = null;
            if(s == null)
            {
                if(key.HasDefaultImplementation)
                {
                    r = (T)key.GetDefaultImplementation(provider);
                    path = factory.GetScriptPath(key);
                }
            }
            else
            {
                await s.CompileAsync();
                var context = new ExtensionScriptContext(provider);
                
                await s.InvokeAsync(new(context));
                var ir = context.Result;
                if(ir != null)
                {
                    if(ir is not T irr)
                        throw new ScriptException($"The extension result for ExtensionPoint {key} is not assignable to {typeof(T).Name}");
                    r = irr;
                }
                else
                {
                    if(key.HasDefaultImplementation)
                        r = (T)key.GetDefaultImplementation(provider);
                }
                path = s.FilePath;
            }
            
            if(r == null && key.Required)
            {
                throw new ScriptException($"ExtensionPoint {key} is required implementation was provided");    
            }
                
            if(r is IOnInit init)
                await init.OnInit(provider);
            if(r != null)
                key.OnResolved(r);
                
            if(key.Lifetime != ServiceLifetime.Transient)
                _cache[key] = new ExtensionEntry(r, path);
            return r;
        }
        catch(Exception e)
        {
            if(e is not ScriptException && e is not ExtensionExcepton)
                throw new ScriptException($"ExtensionPoint {key} failed to resolve", e);
            throw;
        }
        finally
        {
            sem.Release();
        }
    }
    
    protected T? ResolveInternal<T>(IExtensionPoint key, IServiceProvider provider) where T: class
    {
        try
        {
            var task = ResolveInternalAsync<T>(key, provider);
            task.Wait();
            return task.Result;
        }
        catch (System.AggregateException e)
        {
            if(e.InnerExceptions.Count == 1)
                throw e.InnerExceptions[0];
            throw;
        }
    }
    
    public void Register(IExtensionPoint registration)
    {
        if(_opts.Value.AllowOnlyPreregisteredExtensions)
            throw new ApplicationException($"Cannot register {registration} when AllowOnlyDefinedScripts is true");
        if(_registeredExtensionPoints.Contains(registration))
        {
            _logger?.LogWarning("Extension {key} is already registered", registration.Key);
            return;
        }
        _registeredExtensionPoints.Add(registration);
        factory.DefineScript(registration);
        _logger?.LogInformation("Registered Extension {key}", registration.Key);
    }
    
    protected static readonly MethodInfo RESOLVE_METHOD = typeof(ExtensionRegistryBase)
        .GetMethod(nameof(ResolveInternal), BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveInternal method not found");
    protected static readonly MethodInfo RESOLVE_ASYNC_METHOD = typeof(ExtensionRegistryBase)
        .GetMethod(nameof(ResolveInternalAsync), BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveInternalAsync method not found");
    private readonly IScriptFactory factory;
    private readonly IServiceProvider provider;

    public T Resolve<T>(ExtensionPoint<T> key, IServiceProvider provider) where T: class 
        => ResolveInternal<T>(key, provider)!;
        
    public object? Resolve(IExtensionPoint key, IServiceProvider provider)
        => RESOLVE_METHOD.MakeGenericMethod(key.ExtensionType)
            .Invoke(this, [key, provider])!;
            
    public async Task<T?> ResolveAsync<T>(ExtensionPoint<T> key, IServiceProvider provider) where T: class
        => await ResolveInternalAsync<T>(key, provider);
        
    public async Task<object?> ResolveAsync(IExtensionPoint key, IServiceProvider provider)
    {
        var task = (Task)RESOLVE_ASYNC_METHOD.MakeGenericMethod(key.ExtensionType)
            .Invoke(this, [key, provider])!;
        await task;
        return task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task);
    }
    
    private SemaphoreSlim GetCompilationLock(IExtensionPoint p)
    {
        lock(_compilationLocks)
        {
            if(!_compilationLocks.TryGetValue(p, out var sem))
            {
                sem = new SemaphoreSlim(1, 1);
                _compilationLocks[p] = sem;
            }
            return sem;
        }
    }
    
    protected void RemoveFromCache(IExtensionPoint key)
    {
        if(!_cache.TryRemove(key, out var e))
            return;
        if(e is IAsyncDisposable a)
            a.DisposeAsync().AsTask().Wait();
        else if(e is IDisposable d)
            d.Dispose();
        _logger?.LogInformation("Removed {key} from cache", key);
    }
    
    protected async ValueTask RemoveFromCacheAsync(IExtensionPoint key)
    {
        if(!_cache.TryRemove(key, out var e))
            return;
            
        if(e is IAsyncDisposable a)
            await a.DisposeAsync();
        else if(e is IDisposable d)
            d.Dispose();
        _logger?.LogInformation("Removed {key} from cache", key);
    }
    
    public virtual async ValueTask DisposeAsync()
    {
        if(_disposed)
            return;
        _disposed = true;
        foreach(var l in _compilationLocks.Values)
        {
            l.Dispose();
        }
        foreach(var r in _cache.Keys.ToList())
        {
            await RemoveFromCacheAsync(r);
        }
    }
    
    public virtual void Dispose()
    {
        if(_disposed)
            return;
        _disposed = true;
        foreach(var l in _compilationLocks.Values)
        {
            l.Dispose();
        }
        foreach(var r in _cache.Keys.ToList())
        {
            RemoveFromCache(r);
        }
    }
}