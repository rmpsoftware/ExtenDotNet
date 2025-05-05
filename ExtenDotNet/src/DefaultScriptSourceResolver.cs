
using Microsoft.CodeAnalysis.Text;

namespace ExtenDotNet;

internal class DefaultScriptSourceResolver(string rootDir) : IScriptSourceResolver
{
    public IObservable<string>? SourceChanged => null;

    private string ResolvePath(string path)
    {
        if(!Path.IsPathRooted(path))
            path = Path.Combine(rootDir, path);
        return path;
    }
    
    public string ResolveReferencePath(IScriptDefinition registration, string path, string? basePath)
    {
        var isRooted = Path.IsPathRooted(path);
        if(!isRooted && basePath == null)
            throw new ArgumentNullException(nameof(basePath), "basePath cannot be null if path is not rooted");
        string res = isRooted
            ? path
            : Path.Combine(
                Path.GetDirectoryName(basePath)!,
                path
            );
        return res;
    }

    public SourceText ResolveReferenceSource(IScriptDefinition registration, string path)
        => SourceText.From(File.ReadAllText(path));

    public SourceText ResolveSource(IScriptDefinition registration)
        => SourceText.From(File.ReadAllText(ResolveSourcePath(registration)));

    public string ResolveSourcePath(IScriptDefinition registration)
        => ResolvePath(registration.Key + ".csx");
}

