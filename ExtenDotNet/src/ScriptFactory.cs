using System.Collections.Concurrent;
using System.Net;
using System.Reactive.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExtenDotNet;

internal record ScriptDllCompilationresult(
    IScriptDefinition Registration,
    MetadataReference Ref,
    Assembly Assembly,
    string FilePath,
    ScriptDllCompilationresult[] Dependencies
) : IDisposable
{
    public void Dispose()
    {
    }
    
    internal IEnumerable<ScriptDllCompilationresult> EnumerateAllDependencies()
    {
        var stack = new Stack<ScriptDllCompilationresult>([this]);
        while(stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            foreach(var d in current.Dependencies)
            {
                stack.Push(d);
            }
        }
    }
}

internal class ScriptEvictedEventArgs(IScript script): EventArgs
{
    internal IScript Script { get; } = script;
}

public class ScriptFactory : IScriptFactory
{
    readonly ConcurrentDictionary<IScriptDefinition, Script> _cache = new();
    readonly ConcurrentDictionary<string, ScriptDllCompilationresult> _dllCache = new();
    readonly Dictionary<string, SemaphoreSlim> _compilationLocks = [];
    readonly ScriptFactoryOpts _opts;
    readonly ILogger? _logger;
    readonly IDisposable? _sourceChangedSub;
    readonly HashSet<IScriptDefinition> _definitions;
    
    internal IScriptPreprocessor Preprocessor => _opts.Preprocessor;
    internal event EventHandler<ScriptEvictedEventArgs>? ScriptEvictedEvent;
    public IScriptSourceResolver SourceResolver => _opts.SourceResolver;
    public ScriptOpts ScriptOpts => _opts.ScriptOpts;
    public IObservable<IScript> ScriptEvicted { get; private set; }
    public IReadOnlySet<IScriptDefinition> RegisteredScripts => _definitions;

    public ScriptFactory(
        ScriptFactoryOpts options,
        ILogger<ScriptFactory>? logger = null
    )
    {
        _logger      = logger;
        _opts        = options;
        _definitions = _opts.Definitions.ToHashSet();
        
        if(options.EnableHotReload)
            _sourceChangedSub = options.SourceResolver.SourceChanged?.Subscribe(ClearCache);
        ScriptEvicted = Observable.FromEventPattern<ScriptEvictedEventArgs>(
            h => ScriptEvictedEvent += h,
            h => ScriptEvictedEvent -= h
        ).Select(e => e.EventArgs.Script);
    }
    
    internal Script<TContext, TReturn> CreateInternal<TContext, TReturn>(
        IScriptDefinition def
    )
    {
        if(_opts.AllowOnlyDefinedScripts && !_definitions.Contains(def))
            throw new ScriptException($"Script {def} not found and only pre-defined scripts are allowed");
            
        if(def.CacheScript && _cache.TryGetValue(def, out var cached))
            return (Script<TContext, TReturn>)cached;
            
        var content = SourceResolver.ResolveSource(def);
        if(content == null)
        {
            if(def.Required)
                throw new ScriptException($"Script {def} not found and is required");
            return null!;
        }
        
        var res = CreateScriptFromContent<TContext, TReturn>(def, content);
        
        if(def.CacheScript)
            _cache[def] = res;
        return res;
    }
    
    internal Script<TContext, TReturn> CreateScriptFromContent<TContext, TReturn>(
        IScriptDefinition def,
        SourceText content
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if(def.ContextType != typeof(TContext))
            throw new ScriptException($"Script {def} context type mismatch: {def.ContextType} != {typeof(TContext)}");
        if(def.ReturnType != typeof(TReturn))
            throw new ScriptException($"Script {def} return type mismatch: {def.ReturnType} != {typeof(TReturn)}");
            
        var opts = _opts.ScriptOpts;
        var proc = Preprocessor.Preprocess(content, opts.ParseOptions);
        var path = SourceResolver.ResolveSourcePath(def)!;
        var res = new Script<TContext, TReturn>(
            def,
            proc,
            path,
            opts,
            this
        );
        return res;
    }
    
    public async Task TryCompileScriptAsync(
        string path,
        Type contextType,
        Type returnType,
        string content,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var def = new ScriptDefinition(path, contextType, returnType, required: true, cache: false);
        
        var m = CREATE_FROM_CONTENT_METHOD.MakeGenericMethod(def.ContextType, def.ReturnType);
        var script = (IScript)m.Invoke(this, [def, SourceText.From(content)])!;
        await script.CompileAsync(ct: cancellationToken);
    }
    
    public IScript<TContext, TReturn> GetScript<TContext, TReturn>(
        ScriptDefinition<TContext, TReturn> reg
    ) => CreateInternal<TContext, TReturn>(reg);
    
    public IScript<TContext> GetScript<TContext>(
        ScriptDefinition<TContext> reg
    ) => CreateInternal<TContext, object>(reg);
    
    public IScript GetScript(
        IScriptDefinition reg
    ) => GetScriptInternal(reg);
    
    internal Script GetScriptInternal(
        IScriptDefinition reg
    ) => (Script)CREATE_METHOD.MakeGenericMethod(reg.ContextType, reg.ReturnType).Invoke(this, [reg])!;
    
    private static readonly MethodInfo CREATE_METHOD = typeof(ScriptFactory)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
        .First(m => m.Name == nameof(CreateInternal) && m.IsGenericMethod && m.GetParameters().Length == 1);
    
    private static readonly MethodInfo CREATE_FROM_CONTENT_METHOD = typeof(ScriptFactory)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
        .First(m => m.Name == nameof(CreateScriptFromContent) && m.IsGenericMethod && m.GetParameters().Length == 2);
    
    public string? GetScriptPath(IScriptDefinition registration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return SourceResolver.ResolveSourcePath(registration);
    }
    
    internal ScriptDllCompilationresult CompileDllScript(
        IScriptDefinition parentRegistration,
        string path,
        string[] stack, 
        ScriptOpts opts
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if(stack.Contains(path))
            throw new ScriptException("Circular reference detected: " + string.Join(" -> ", stack));
        var key = path + ".dll";
        if(_dllCache.TryGetValue(key, out var res))
            return res;
            
        var sem = GetCompilationLock(path);
        sem.Wait();
        try
        {
            if(_dllCache.TryGetValue(key, out res))
                return res;
                
            var text = SourceResolver.ResolveReferenceSource(parentRegistration, path);
            var result = Preprocessor.Preprocess(text, opts.ParseOptions);
            
            var dependencies = new List<ScriptDllCompilationresult>();
            if(result.LibraryScripts != null)
            {
                foreach(var l in result.LibraryScripts)
                {
                    var dep = CompileDllScript(
                        parentRegistration,
                        l, 
                        stack.Append(path).ToArray(), 
                        opts
                    );
                    dependencies.Add(dep);
                }
            }
            
            IEnumerable<Assembly> assemblies = [
                ..opts.MetadataReferences
                    .OfType<UnresolvedMetadataReference>()
                    .Select(e => Assembly.Load(e.Reference))
            ];
            
            var refs = assemblies
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Append(GetMetadataReferenceFromType(typeof(object)))
                .Concat(opts.MetadataReferences.Where(e => e is not UnresolvedMetadataReference))
                .ToArray();
                
            var syntaxTree = CSharpSyntaxTree.ParseText(
                result.SourceText,
                opts.ParseOptions
            );
            
            var comp = CSharpCompilation.Create(
                $"{Guid.NewGuid()}_{Path.GetFileName(path)}.dll",
                [syntaxTree],
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            
            using(var ms = new MemoryStream())
            {
                var emitResult = comp.Emit(ms);
                if (!emitResult.Success)
                {
                    throw new ScriptException("DLL Script compilation failed: " +
                        string.Join("\n", emitResult.Diagnostics));
                }
                
                ms.Seek(0, SeekOrigin.Begin);
                var bytes = ms.ToArray();
                var a = Assembly.Load(bytes);
                var r = MetadataReference.CreateFromImage(bytes);
                res = new ScriptDllCompilationresult(
                    parentRegistration, 
                    r, 
                    a, 
                    path,
                    dependencies.ToArray()
                );
                _dllCache[key] = res;
                return res;
            }
        }
        finally
        {
            sem.Release();
        }
    }
    
    private SemaphoreSlim GetCompilationLock(string path)
    {
        lock(_compilationLocks)
        {
            if(!_compilationLocks.TryGetValue(path, out var sem))
            {
                sem = new SemaphoreSlim(1, 1);
                _compilationLocks[path] = sem;
            }
            return sem;
        }
    }
    
    private static PortableExecutableReference GetMetadataReferenceFromType(Type type)
    {
        var assemblyLocation = type.Assembly.Location;
        return MetadataReference.CreateFromFile(assemblyLocation);
    }
    
    private bool _disposed = false;
    public virtual void Dispose()
    {
        if(_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
        _sourceChangedSub?.Dispose();
        foreach(var l in _compilationLocks.Values)
        {
            l.Dispose();
        }
        foreach(var s in _cache.Values)
        {
            s.Dispose();
        }
        foreach(var d in _dllCache.Values)
        {
            d.Dispose();
        }
    }
    
    public void DefineScript(IScriptDefinition def)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(nameof(def));
        if(_opts.AllowOnlyDefinedScripts)
            throw new ScriptException($"If AllowOnlyDefinedScripts is true only predefined scripts in options are allowed");
        _definitions.Add(def);
    }
    
    public void DefineScript(string key, Type contextType, Type returnType, bool required = false, bool cache = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(nameof(key));
        ArgumentNullException.ThrowIfNull(nameof(contextType));
        ArgumentNullException.ThrowIfNull(nameof(returnType));
        var def = new ScriptDefinition(key, contextType, returnType, required, cache);
        _definitions.Add(def);
    }
    
    public void DefineScript<TContext>(string key, bool required = false, bool cache = true)
        => DefineScript(key, typeof(TContext), typeof(object), required, cache);
    
    public void DefineScript<TContext, TReturn>(string key, bool required = false, bool cache = true)
        => DefineScript(key, typeof(TContext), typeof(TReturn), required, cache);
    
    public void DefineScripts(IEnumerable<IScriptDefinition> defs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(nameof(defs));
        foreach(var def in defs)
        {
            DefineScript(def);
        }
    }
    
    //Remove a ScriptDllCompilationResult and all Results that reference it from the cache
    private List<ScriptDllCompilationresult> RemoveDllImportCache(ScriptDllCompilationresult result)
    {
        var removed = new List<ScriptDllCompilationresult> { result };
        var stackPtr = 0;
        while(stackPtr < removed.Count)
        {
            var current = removed[stackPtr];
            stackPtr++;
            if(_dllCache.TryRemove(current.FilePath, out var s))
            {
                _logger?.LogInformation("Evicting {result} from dll script cache", current);
                s.Dispose();
                foreach(var key in _dllCache.Keys.ToList())
                {
                    if(_dllCache[key].EnumerateAllDependencies().Contains(current))
                        removed.Add(_dllCache[key]);
                }
            }
        }
        return removed;
    }
    
    private void TrimDllCache()
    {
        var deps = _cache.Values
            .SelectMany(e => e.Dependencies ?? [])
            .ToHashSet();
        foreach(var key in _dllCache.Keys.ToList())
        {
            var res = _dllCache[key];
            if(!deps.Contains(res))
            {
                if(_dllCache.TryRemove(key, out var r))
                {
                    _logger?.LogInformation("Evicting {filePath} from dll cache during trimming", r.FilePath);
                    r.Dispose();
                }
            }
        }
    }
    
    private void EvictScriptFromCache(IScriptDefinition def)
    {
        _logger?.LogDebug("Evicting script from cache: {key}", def.Key);
        if(_cache.TryRemove(def, out var s))
        {
            s.Dispose();
            ScriptEvictedEvent?.Invoke(this, new ScriptEvictedEventArgs(s));
        }
    }
    
    public void ClearCache(string? filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if(filePath == null)
            return;
            
        lock(_compilationLocks)
        {
            var removedDllScripts = new List<ScriptDllCompilationresult>();
            foreach(var result in _dllCache.Values
                .Where(e => e.FilePath == filePath)
                .ToList()
            )
            {
                var removed = RemoveDllImportCache(result);
                removedDllScripts.AddRange(removed);
            }
            
            
            var found = false;
            foreach(var key in _cache.Keys.ToList())
            {
                var script = _cache[key];
                if(script.FilePath == filePath 
                    || (script.Dependencies != null && script.Dependencies.Any(removedDllScripts.Contains))
                    || (script.References != null && script.References.Contains(filePath))
                )
                {
                    EvictScriptFromCache(key);
                }
            }
            
            if(found || removedDllScripts.Count > 0)
                TrimDllCache();
        }
    }
    
    public void ClearCache(IScriptDefinition definition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock(_compilationLocks)
        {
            EvictScriptFromCache(definition);
        }
    }
    
    public void ClearCache()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock(_compilationLocks)
        {
            foreach(var key in _cache.Keys.ToList())
            {
                EvictScriptFromCache(key);
            }
            foreach(var key in _dllCache.Keys.ToList())
            {
                if(_dllCache.TryRemove(key, out var s))
                {
                    s.Dispose();
                }
            }
        }
    }
}