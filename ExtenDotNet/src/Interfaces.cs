using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ExtenDotNet;

public interface IOnInit
{
    Task OnInit(IServiceProvider provider);
}

public record ScriptPreprocessResult(
    SourceText SourceText,
    IEnumerable<string>? Usings = null,
    IEnumerable<string>? LibraryScripts = null,
    IEnumerable<string>? References = null
)
{
}

public interface IScriptPreprocessor
{
    ScriptPreprocessResult Preprocess(SourceText text, CSharpParseOptions? parseOptions = null, bool getUsings = true);
    bool IsDllImportPath(string path);
}

public interface IScriptSourceResolver
{
    IObservable<string>? SourceChanged { get; }
    
    string? ResolveSourcePath(IScriptDefinition registration);
    SourceText? ResolveSource(IScriptDefinition registration);
    
    string? ResolveReferencePath(IScriptDefinition registration, string path, string? basePath);
    SourceText ResolveReferenceSource(IScriptDefinition registration, string path);
}

public interface IScriptFactory: IDisposable
{
    string? GetScriptPath(IScriptDefinition definition);
    IScript GetScript(IScriptDefinition definition);
    IScript<TScope, TResult> GetScript<TScope, TResult>(ScriptDefinition<TScope, TResult> definition);
    IScript<TScope> GetScript<TScope>(ScriptDefinition<TScope> definition);
    Task TryCompileScriptAsync(string path, Type contextType, Type returnType, string content, System.Text.Encoding? encoding = null, CancellationToken cancellationToken = default);
    void ClearCache();
    void ClearCache(IScriptDefinition definition);
    void ClearCache(string path);
    ScriptOpts ScriptOpts { get; }
    IScriptSourceResolver SourceResolver { get; }
    IReadOnlySet<IScriptDefinition> RegisteredScripts { get; }
    IObservable<IScript> ScriptEvicted { get; }
    void DefineScript(IScriptDefinition definition);
    void DefineScript(string key, Type contextType, Type returnType, bool required = false, bool cache = true);
    void DefineScript<TContext>(string key, bool required = false, bool cache = true);
    void DefineScript<TContext, TReturn>(string key, bool required = false, bool cache = true);
    void DefineScripts(IEnumerable<IScriptDefinition> definitions);
    
}


public interface IExtensionRegistry
{
    void ClearCache();
    void ClearCache(IExtensionPoint registration);
    void ClearCache(string path);
    
    T? Resolve<T>(ExtensionPoint<T> key, IServiceProvider provider) where T: class;
    Task<T?> ResolveAsync<T>(ExtensionPoint<T> key, IServiceProvider provider) where T: class;
    object? Resolve(IExtensionPoint key, IServiceProvider provider);
    Task<object?> ResolveAsync(IExtensionPoint key, IServiceProvider provider);
    IEnumerable<IExtensionPoint> RegisteredExtensions { get; }
    void Register(IExtensionPoint registration);
}

