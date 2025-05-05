using System.Collections.Immutable;

namespace ExtenDotNet;

public class ScriptFactoryOpts
{
    public static ScriptFactoryOpts Default(IScriptSourceResolver sourceResolver, ScriptOpts scriptOpts) 
        => new(sourceResolver, scriptOpts);
        
    public static ScriptFactoryOpts Default(string dir, ScriptOpts scriptOpts) 
        => new(new DefaultScriptSourceResolver(dir), scriptOpts);
    
    public bool AllowOnlyDefinedScripts { get; init; } = false;
    public bool EnableHotReload { get; init; }         = true;
    public ScriptOpts ScriptOpts { get; init; }
    public IScriptPreprocessor Preprocessor { get; init; }
    public IScriptSourceResolver SourceResolver { get; init; }
    
    internal ImmutableArray<IScriptDefinition> Definitions { get; init; } = [];
    
    internal ScriptFactoryOpts(
        IScriptSourceResolver resolver,
        ScriptOpts scriptOpts,
        IScriptPreprocessor? preprocessor = null
    )
    {
        SourceResolver = resolver;
        ScriptOpts = scriptOpts;
        Preprocessor = preprocessor ?? ScriptPreprocessor.Default;
    }
    
    internal ScriptFactoryOpts(ScriptFactoryOpts opts)
    {
        SourceResolver          = opts.SourceResolver;
        Preprocessor            = opts.Preprocessor;
        ScriptOpts              = opts.ScriptOpts;
        EnableHotReload         = opts.EnableHotReload;
        AllowOnlyDefinedScripts = opts.AllowOnlyDefinedScripts;
        Definitions             = opts.Definitions;
    }
    
    public ScriptFactoryOpts WithAllowOnlyDefinedScripts(bool value)
        => new(this) { AllowOnlyDefinedScripts = value };
        
    public ScriptFactoryOpts WithEnableHotReload(bool value)
        => new(this) { EnableHotReload = value };
        
    public ScriptFactoryOpts WithPreprocessor(IScriptPreprocessor preprocessor)
        => new(this) { Preprocessor = preprocessor };
        
    public ScriptFactoryOpts WithSourceResolver(IScriptSourceResolver resolver)
        => new(this) { SourceResolver = resolver };
        
    public ScriptFactoryOpts WithScriptOpts(ScriptOpts opts)
        => new(this) { ScriptOpts = opts };
        
    public ScriptFactoryOpts Define(ScriptDefinition def)
        => new(this) { Definitions = [.. Definitions, def] };
}