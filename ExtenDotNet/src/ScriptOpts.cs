using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;

namespace ExtenDotNet;

public class ScriptOpts
{
    public static ScriptOpts Default => new();
    
    internal LanguageVersion LanguageVersion { get; set; }
    
    ScriptOptions _scriptOptions { get; set; } = ScriptOptions.Default;
    
    public CSharpParseOptions ParseOptions => new(LanguageVersion);
    
    public IEnumerable<MetadataReference> MetadataReferences => _scriptOptions.MetadataReferences;
    
    internal ScriptOpts() {}
    internal ScriptOpts(ScriptOpts opts)
    {
        LanguageVersion = opts.LanguageVersion;
        _scriptOptions  = opts._scriptOptions;
    }
    
    internal ScriptOptions GetScriptOptions(
        string? filePath,
        IScriptDefinition registration,
        IEnumerable<string> usings,
        IScriptSourceResolver sourceResolver,
        IScriptPreprocessor preprocessor,
        IEnumerable<MetadataReference> dependencies, 
        out CustomSourceResolver customResolver
    )
    {
        customResolver = new CustomSourceResolver(
            preprocessor,
            sourceResolver,
            registration,
            ParseOptions,
            _scriptOptions.FileEncoding ?? Encoding.UTF8
        );
        var res = _scriptOptions
            .WithSourceResolver(customResolver)
            .AddImports(usings)
            .AddReferences(dependencies)
            ;
        if(filePath != null)
            res = res.WithFilePath(filePath).WithFileEncoding(Encoding.UTF8);
        return res;
    }
    
#region ScripOptions
    public ScriptOpts WithFilePath(string? filePath)
        => new(this) { _scriptOptions = _scriptOptions.WithFilePath(filePath) };

    public ScriptOpts WithReferences(IEnumerable<MetadataReference> references)
        => new(this) { _scriptOptions = _scriptOptions.WithReferences(references) };

    public ScriptOpts WithReferences(params MetadataReference[] references)
        => new(this) { _scriptOptions = _scriptOptions.WithReferences(references) };

    public ScriptOpts WithReferences(IEnumerable<Assembly> references)
        => new(this) { _scriptOptions = _scriptOptions.WithReferences(references) };
        
    public ScriptOpts WithReferences(params Assembly[] references)
        => new(this) { _scriptOptions = _scriptOptions.WithReferences(references) };
        
    public ScriptOpts WithReferences(IEnumerable<string> references)
        => new(this) { _scriptOptions = _scriptOptions.WithReferences(references) };

    public ScriptOpts WithReferences(params string[] references)
        => new(this) { _scriptOptions = _scriptOptions.WithReferences(references) };
        
    public ScriptOpts AddReferences(IEnumerable<MetadataReference> references)
        => new(this) { _scriptOptions = _scriptOptions.AddReferences(references) };

    public ScriptOpts AddReferences(params MetadataReference[] references)
        => new(this) { _scriptOptions = _scriptOptions.AddReferences(references) };
        
    public ScriptOpts AddReferences(IEnumerable<Assembly> references)
        => new(this) { _scriptOptions = _scriptOptions.AddReferences(references) };

    public ScriptOpts AddReferences(params Assembly[] references)
        => new(this) { _scriptOptions = _scriptOptions.AddReferences(references) };
        
    public ScriptOpts AddReferences(IEnumerable<string> references)
        => new(this) { _scriptOptions = _scriptOptions.AddReferences(references) };
        
    public ScriptOpts AddReferences(params string[] references)
        => new(this) { _scriptOptions = _scriptOptions.AddReferences(references) };
        
    public ScriptOpts WithImports(IEnumerable<string> imports)
        => new(this) { _scriptOptions = _scriptOptions.WithImports(imports) };

    public ScriptOpts WithImports(params string[] imports)
        => new(this) { _scriptOptions = _scriptOptions.WithImports(imports) };
        
    public ScriptOpts AddImports(IEnumerable<string> imports)
        => new(this) { _scriptOptions = _scriptOptions.AddImports(imports) };
        
    public ScriptOpts AddImports(params string[] imports)
        => new(this) { _scriptOptions = _scriptOptions.AddImports(imports) };

    public ScriptOpts WithEmitDebugInformation(bool emitDebugInformation)
        => new(this) { _scriptOptions = _scriptOptions.WithEmitDebugInformation(emitDebugInformation) };
        
    public ScriptOpts WithFileEncoding(Encoding encoding)
        => new(this) { _scriptOptions = _scriptOptions.WithFileEncoding(encoding) };

    public ScriptOpts WithOptimizationLevel(OptimizationLevel optimizationLevel)
        => new(this) { _scriptOptions = _scriptOptions.WithOptimizationLevel(optimizationLevel) };

    public ScriptOpts WithAllowUnsafe(bool allowUnsafe)
        => new(this) { _scriptOptions = _scriptOptions.WithAllowUnsafe(allowUnsafe) };

    public ScriptOpts WithCheckOverflow(bool checkOverflow)
        => new(this) { _scriptOptions = _scriptOptions.WithCheckOverflow(checkOverflow) };
        
    public ScriptOpts WithWarningLevel(int warningLevel)
        => new(this) { _scriptOptions = _scriptOptions.WithWarningLevel(warningLevel) };
        
    public ScriptOpts WithLanguageVersion(LanguageVersion languageVersion)
        => new(this) { _scriptOptions = _scriptOptions.WithLanguageVersion(languageVersion), LanguageVersion = languageVersion };
#endregion
}

public interface IProvideAdditionalReferencesSourceResolver
{
    public IEnumerable<string> References { get; }
}

internal class CustomSourceResolver(
    IScriptPreprocessor preprocessor,
    IScriptSourceResolver resolver,
    IScriptDefinition registration,
    CSharpParseOptions parseOptions,
    Encoding encoding
) : SourceFileResolver([], null), IProvideAdditionalReferencesSourceResolver
{
    private HashSet<string> _references = [];
    public IEnumerable<string> References => _references;

    public override SourceText ReadText(string resolvedPath)
    {
        var content = File.ReadAllText(resolvedPath, encoding);
        var src = SourceText.From(content, encoding);
        var result = preprocessor.Preprocess(src, parseOptions, getUsings: false);
        if(result.References != null)
        {
            foreach(var r in result.References)
            {
                var resolved = resolver.ResolveReferencePath(registration, r, resolvedPath);
                if(resolved != null)
                    _references.Add(resolved);
            }
        }
        return result.SourceText;
    }

    public override string? ResolveReference(string path, string? baseFilePath)
    {
        return resolver.ResolveReferencePath(registration, path, baseFilePath);
    }

    public override Stream OpenRead(string resolvedPath)
    {
        throw new NotImplementedException();
        // using var stream = base.OpenRead(resolvedPath);
        // using var reader = new StreamReader(stream, encoding);
        // var content = reader.ReadToEnd();
        // var result = preprocessor.Preprocess(SourceText.From(content), parseOptions, getUsings: false);
        // return new MemoryStream(encoding.GetBytes(result.SourceText.ToString()));
    }
}


