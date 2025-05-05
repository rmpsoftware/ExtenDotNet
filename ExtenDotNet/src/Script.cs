namespace ExtenDotNet;

using System.Runtime.Loader;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.CodeAnalysis.Text;

public interface IScriptScope<T>
{
    T ctx { get; }
}

public interface IScript: IDisposable
{
    IScriptDefinition Definition { get; }
    bool IsError { get; }
    bool LogicIsEmpty { get; }
    bool IsCompiled { get; }
    string? FilePath { get; }
    void Compile(CancellationToken ct = default);
    Task CompileAsync(CancellationToken ct = default);
}

public interface IScript<TContext, TReturn>: IScript
{
    Task<TReturn?> InvokeAsync(TContext context, CancellationToken ct = default);
    TReturn Invoke(TContext context, CancellationToken ct = default);
}

public interface IScript<TContext>: IScript
{
    Task InvokeAsync(TContext context, CancellationToken ct = default);
    void Invoke(TContext context, CancellationToken ct = default);
}

internal abstract class Script(IScriptDefinition reg, string? filePath): IDisposable, IScript
{
    protected IScriptDefinition _definition = reg;
    public IScriptDefinition Definition => _definition;
    public bool IsError { get; protected set; } = false;
    public bool LogicIsEmpty { get; protected set; } = false;
    public bool IsCompiled { get; protected set; } = false;
    public string? FilePath { get; protected set; } = filePath;
    
    internal IEnumerable<ScriptDllCompilationresult>? Dependencies => _dependencies;
    protected List<ScriptDllCompilationresult>? _dependencies = null;
    
    protected IEnumerable<string>? _references;
    public IEnumerable<string>? References => _references;
    
    public abstract void Dispose();
    public abstract void Compile(CancellationToken ct = default);
    public abstract Task CompileAsync(CancellationToken ct = default);
    
    internal IScript<TContext, TReturn> As<TContext, TReturn>()
    {
         if(typeof(TContext) != _definition.ContextType || typeof(TReturn) != _definition.ReturnType)
            throw new InvalidOperationException($"Script {_definition} is not of type {typeof(TContext)} -> {typeof(TReturn)}");
        return (Script<TContext, TReturn>)this;
    }
    
    internal IScript<TContext> As<TContext>()
    {
        if(typeof(TContext) != _definition.ContextType)
            throw new InvalidOperationException($"Script {_definition} is not of type {typeof(TContext)}");
        if(_definition.ReturnType != typeof(object))
            throw new InvalidOperationException($"Script {_definition} is not of type {typeof(TContext)} -> void");
        return (IScript<TContext>)this;
    }
}

internal class Script<TContext, TReturn> : Script, IScript<TContext>, IScript<TContext, TReturn>
{
    ScriptRunner<TReturn>? _script;
    
    public readonly Type ContextType = typeof(TContext);
    public readonly Type ReturnType = typeof(TReturn);
    
    bool _disposed = false;
    readonly SemaphoreSlim _sem = new(1, 1);
    readonly ScriptOpts _opts;
    readonly ScriptFactory _factory;
    readonly ScriptPreprocessResult _content;
    
    internal Script(
        IScriptDefinition definition,
        ScriptPreprocessResult content,
        string? filePath,
        ScriptOpts options,
        ScriptFactory factory
    ): base(definition, filePath)
    {
        _definition  = definition;
        _opts        = options;
        _factory     = factory;
        _content     = content;
        LogicIsEmpty = string.IsNullOrWhiteSpace(_content.SourceText.ToString());
    }

    public override void Compile(CancellationToken ct = default)
    {
        CompileAsync(ct).Wait(ct);
    }

    public override async Task CompileAsync(CancellationToken ct = default)
    {
        if (LogicIsEmpty || _disposed || IsCompiled || IsError)
            return;
            
        await _sem.WaitAsync(ct);
        try
        {
            if(IsCompiled || _disposed || IsError) //if in the meantime another thread completed the compilation or disposed this instance
                return;


            var dependencies = new List<ScriptDllCompilationresult>();
            if(_content.LibraryScripts != null)
            {
                foreach(var l in _content.LibraryScripts)
                {
                    var dep = _factory.CompileDllScript(
                        Definition,
                        l, 
                        [], 
                        _opts
                    );
                    dependencies.Add(dep);
                }
            }
            
            _dependencies = dependencies
                .SelectMany(e => e.EnumerateAllDependencies())
                .Distinct()
                .ToList();
            
            var scriptOpts = _opts
                .GetScriptOptions(
                    _factory.SourceResolver.ResolveSourcePath(Definition),
                    Definition,
                    _content.Usings ?? [], 
                    _factory.SourceResolver,
                    _factory.Preprocessor,
                    _dependencies.Select(d => d.Ref),
                    out var customResolver
                );
                
            
            using var loader = new InteractiveAssemblyLoader();
            foreach(var a in _dependencies)
            {
                loader.RegisterDependency(a.Assembly);
            }
            var res = CSharpScript.Create<TReturn>(
                _content.SourceText.ToString(),
                scriptOpts,
                globalsType: typeof(TContext),
                assemblyLoader: loader
            );
            _script = res.CreateDelegate(ct);
            GC.Collect();
            _references = customResolver.References;
            IsCompiled = true;
        }
        catch (System.Exception ex)
        {
            IsError = true;
            throw new ScriptException("Script compilation failed", ex);
        }
        finally
        {
            _sem.Release();
        }
    }
    
    async Task IScript<TContext>.InvokeAsync(TContext context, CancellationToken ct)
        => await InvokeAsync(context, ct);
    
    public virtual async Task<TReturn?> InvokeAsync(TContext context, CancellationToken ct = default)
    {
        if (IsError)
            throw new ScriptException("Script compilation failed earlier");
        if (LogicIsEmpty)
            return default;

        if (!IsCompiled)
            await CompileAsync(ct);
        
        return await _script!.Invoke(context, ct);
    }
    
    public TReturn Invoke(TContext context, CancellationToken ct = default)
    {
        var task = InvokeAsync(context, ct);
        task.Wait(ct);
        return task.Result!;
    }
    
    void IScript<TContext>.Invoke(TContext context, CancellationToken ct)
        => Invoke(context, ct);
    
    public override void Dispose()
    {
        _disposed = true;
        _sem.Dispose();
    }
}
