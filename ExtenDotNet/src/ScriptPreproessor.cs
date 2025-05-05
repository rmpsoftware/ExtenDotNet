using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ExtenDotNet;

public sealed class ScriptPreprocessor: IScriptPreprocessor
{
    internal bool ExcludeUnderscoreLoads { get; set; }           = true;
    internal ImmutableArray<string> RemovedRegions { get; set; } = ImmutableArray<string>.Empty;
    internal bool AutoUsings { get; set; }                       = true;
    internal bool EnableDllScripts { get; set; }                 = true;
    
    internal ScriptPreprocessor(
        bool excludeUnderscoreImports = true,
        ImmutableArray<string> removedRegions = default,
        bool autoUsings = true,
        bool enableDllScripts = true
    )
    {
        ExcludeUnderscoreLoads = excludeUnderscoreImports;
        RemovedRegions         = removedRegions;
        AutoUsings             = autoUsings;
        EnableDllScripts       = enableDllScripts;
    }
    
    public static ScriptPreprocessor Default => new(true, ["preamble"], true, true);

    public ScriptPreprocessor WithExcludeUnderscoreLoads(bool excludeUnderscoreLoads)
        => new(excludeUnderscoreLoads, RemovedRegions, AutoUsings);
        
    public ScriptPreprocessor WithEnableDllScripts(bool enableDllScripts)
        => new(ExcludeUnderscoreLoads, RemovedRegions, AutoUsings, enableDllScripts);
        
    public ScriptPreprocessor WithRemovedRegions(ImmutableArray<string> removedRegions)
        => new(ExcludeUnderscoreLoads, [.. RemovedRegions, .. removedRegions], AutoUsings);
            
    public ScriptPreprocessor WithAutoUsings(bool autoUsings)
        => new(ExcludeUnderscoreLoads, RemovedRegions, autoUsings);

    private static List<string> GetUsings(SourceText scriptContent, CSharpParseOptions? parseOptions = null)
    {
        parseOptions ??= CSharpParseOptions.Default;
        var tree = CSharpSyntaxTree.ParseText(scriptContent, parseOptions);
        var root = tree.GetRoot();
        return root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(e => e.Name != null)
            .Select(e => e.Name!.ToString())
            .ToList();
    }

    private static readonly string[] NEWLINE_SEP = ["/*", "//"];
    
    public bool IsDllImportPath(string path)
        => path.EndsWith(".dll.csx");
        
    private const string LOAD = "#load";
    private const string REGION = "#region";
    private const string ENDREGION = "#endregion";
    
    public ScriptPreprocessResult Preprocess(SourceText scriptContent, CSharpParseOptions? parseOptions = null, bool getUsings = true)
    {
        var usings     = getUsings && AutoUsings ? GetUsings(scriptContent, parseOptions) : null;
        var sourceText = scriptContent;
        var dllImports = new List<string>();
        var refs       = new List<string>();
        
        if(ExcludeUnderscoreLoads || EnableDllScripts || RemovedRegions.Length == 0)
        {
            var sb = new StringBuilder();
            var inRegion = new Stack<string>();
            foreach(var l in scriptContent.Lines)
            {
                if(l.Text == null)
                    continue;
                var line = l.ToString();
                var trimmed = line.ToString().TrimStart();
                trimmed = trimmed.Split(NEWLINE_SEP, StringSplitOptions.TrimEntries)[0];
                var handled = false;
                
                if(ExcludeUnderscoreLoads || EnableDllScripts)
                {
                    if(trimmed.StartsWith(LOAD))
                    {
                        var path = trimmed.Substring(LOAD.Length).Trim().Trim('"');
                        var fn = Path.GetFileName(path);
                        if(ExcludeUnderscoreLoads && fn.StartsWith('_'))
                        {
                            sb.AppendLine("//"+line);
                            handled = true;
                        }
                        else if(EnableDllScripts)
                        {
                            if(IsDllImportPath(fn))
                            {
                                sb.AppendLine("//"+line);
                                dllImports.Add(path);
                                handled = true;
                            }
                            else
                            {
                                refs.Add(path);
                            }
                        }
                    }
                }
                
                if(RemovedRegions.Length > 0)
                {
                    if(trimmed.StartsWith(REGION))
                    {
                        var region = trimmed.Substring(REGION.Length).Trim();
                        inRegion.Push(region);
                    }
                    
                    var isPreamble = inRegion.Intersect(RemovedRegions).Any();
                    if(isPreamble)
                    {
                        sb.AppendLine("//"+line);
                        handled = true;
                    }
                    
                    if(trimmed.StartsWith(ENDREGION))
                        inRegion.Pop();
                }
                
                if(!handled)
                    sb.AppendLine(line);
            }
            
            sourceText = SourceText.From(sb.ToString());
        }
        
        return new(sourceText, usings, dllImports, refs);
    }
}
