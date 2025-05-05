using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExtenDotNet;

internal record ExtensionEntry(object? Value, string? Path);

internal class ExtensionRegistry(
    SingletonExtensionRegistry singletonRegistry,
    ExtensionRegistryOpts opts,
    IScriptFactory scriptFactory,
    ILogger<ExtensionRegistry>? logger = null
) : IExtensionRegistry
{
    public IEnumerable<IExtensionPoint> RegisteredExtensions => opts.RegisteredExtensionPoints;

    public void ClearCache() => singletonRegistry.ClearCache();
    public void ClearCache(IExtensionPoint registration) => singletonRegistry.ClearCache(registration);
    public void ClearCache(string path) => singletonRegistry.ClearCache(path);

    public void Register(IExtensionPoint registration)
    {
        if(opts.AllowOnlyPreregisteredExtensions)
            throw new ApplicationException($"Cannot register {registration} when AllowOnlyDefinedScripts is true");
        if(opts.RegisteredExtensionPoints.Contains(registration))
        {
            logger?.LogWarning("Extension {key} is already registered", registration.Key);
            return;
        }
        opts.RegisteredExtensionPoints.Add(registration);
        scriptFactory.DefineScript(registration);
        logger?.LogInformation("Registered Extension {key}", registration.Key);
    }

    public T? Resolve<T>(ExtensionPoint<T> key, IServiceProvider provider) where T : class
        => GetRegistry(key.Lifetime, provider).Resolve(key, provider);

    public object? Resolve(IExtensionPoint key, IServiceProvider provider)
        => GetRegistry(key.Lifetime, provider).Resolve(key, provider);
        
    private ExtensionRegistryBase GetRegistry(ServiceLifetime lifetime, IServiceProvider provider)
        => lifetime switch {
            ServiceLifetime.Scoped => provider.GetRequiredService<ScopedExtensionRegistry>(),
            ServiceLifetime.Singleton => singletonRegistry,
            ServiceLifetime.Transient => provider.IsRootProvider()
                ? provider.GetRequiredService<SingletonExtensionRegistry>()
                : provider.GetRequiredService<ScopedExtensionRegistry>(),
            _ => throw new NotSupportedException($"Lifetime {lifetime} is not supported"),
        };

    public async Task<T?> ResolveAsync<T>(ExtensionPoint<T> key, IServiceProvider provider) where T : class
        => await GetRegistry(key.Lifetime, provider).ResolveAsync(key, provider);

    public async Task<object?> ResolveAsync(IExtensionPoint key, IServiceProvider provider)
        => await GetRegistry(key.Lifetime, provider).ResolveAsync(key, provider);
}

internal class ScopedExtensionRegistry(
    IScriptFactory scriptFactory,
    IServiceProvider serviceProvider,
    IOptions<ExtensionRegistryOpts> opts,
    ILogger<ScopedExtensionRegistry>? logger = null
): ExtensionRegistryBase(scriptFactory, serviceProvider, opts, logger)
{
    internal override async Task<T?> ResolveInternalAsync<T>(IExtensionPoint key, IServiceProvider provider) where T : class
        => key.Lifetime switch {
            ServiceLifetime.Singleton => await provider.GetRequiredService<SingletonExtensionRegistry>().ResolveInternalAsync<T>(key, provider),
            _ => await base.ResolveInternalAsync<T>(key, provider)
        };
}

internal class SingletonExtensionRegistry: ExtensionRegistryBase
{
    readonly IDisposable _evictionSub;
    
    internal SingletonExtensionRegistry(
        IScriptFactory scriptFactory,
        IServiceProvider serviceProvider,
        IOptions<ExtensionRegistryOpts> opts,
        ILogger<SingletonExtensionRegistry>? logger = null
    ): base(scriptFactory, serviceProvider, opts, logger)
    {
        _evictionSub = scriptFactory.ScriptEvicted.Subscribe(e => {
            if(e.Definition is IExtensionPoint ext)
                ClearCache(ext);
        });
    }
    
    internal override async Task<T?> ResolveInternalAsync<T>(IExtensionPoint key, IServiceProvider provider) where T : class
    {
        if(key.Lifetime == ServiceLifetime.Scoped)
            throw new InvalidOperationException($"Cannot resolve scoped extension {key} from singleton registry");
        return await base.ResolveInternalAsync<T>(key, provider);
    }
    
    public override void Dispose()
    {
        if(_disposed)
            return;
        base.Dispose();
        _evictionSub.Dispose();
    }
    
    public override async ValueTask DisposeAsync()
    {
        if(_disposed)
            return;
        await base.DisposeAsync();
        _evictionSub.Dispose();
    }
    
    public void ClearCache(IExtensionPoint key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock(_compilationLocks)
        {
            RemoveFromCache(key);
        }
    }
    
    public void ClearCache(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if(path == null)
            return;
            
        lock(_compilationLocks)
        {
            var keys = _cache.Keys.ToList();
            foreach(var k in keys)
            {
                if(_cache[k].Path == path)
                {
                    RemoveFromCache(k);           
                }
            }
        }
    }
    
    public void ClearCache()
    {
        _logger?.LogInformation("Clearing cache");
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock(_compilationLocks)
        {
            foreach(var r in _cache.Keys.ToList())
            {
                RemoveFromCache(r);
            }
        }
    }
}
