using System.Collections.Concurrent;
using System.Reactive.Linq;
using Microsoft.CodeAnalysis.Text;

namespace ExtenDotNet.Tests;

class TestSourceResolver : IScriptSourceResolver
{
    public IObservable<string>? SourceChanged { get; private set; }
    
    private event EventHandler<SourceChangedEventArgs>? SourceChangedEvent;
    
    public TestSourceResolver(Dictionary<string, string>? sources = null)
    {
        SourceChanged = Observable.FromEventPattern<SourceChangedEventArgs>(
            h => SourceChangedEvent += h,
            h => SourceChangedEvent -= h
        ).Select(e => e.EventArgs.Path);
        if(sources != null)
        {
            foreach(var (key, value) in sources)
            {
                Sources[key] = value;
            }
        }
    }
    public ConcurrentDictionary<string, string> Sources { get; } = new();

    public string? ResolveDllLoadPath(ScriptDefinition registration, string path)
        => path;
    public SourceText ResolveDllLoadSource(ScriptDefinition registration, string path)
        => SourceText.From(Sources[path]);
    public SourceText ResolveSource(ScriptDefinition registration)
        => SourceText.From(Sources[registration.Key+".csx"]);
    public string? ResolveSourcePath(ScriptDefinition registration)
        => registration.Key + ".csx";
        
        
    public TestSourceResolver Set(string path, string source)
    {
        Sources[path] = source;
        SourceChangedEvent?.Invoke(this, new SourceChangedEventArgs(path, source));
        return this;
    }
    
    public TestSourceResolver Set(IDictionary<string, string> dict)
    {
        foreach(var (key, value) in dict)
        {
            Set(key, value);
        }
        return this;
    }
    
    public TestSourceResolverContext CreateContext() => new(this);
    
    public void Clear()
    {
        foreach(var key in Sources.Keys)
        {
            Sources.TryRemove(key, out var s);
            SourceChangedEvent?.Invoke(this, new SourceChangedEventArgs(key, ""));
        }
    }
    
    class SourceChangedEventArgs(string path, string source): EventArgs
    {
        public string Path { get; } = path;
        public string Source { get; } = source;
    }
}

internal class TestSourceResolverContext(TestSourceResolver resolver): IDisposable
{
    public TestSourceResolverContext Set(string path, string source)
    {
        resolver.Set(path, source);
        return this;
    }
    
    public TestSourceResolverContext Set(IDictionary<string, string> dict)
    {
        foreach(var (key, value) in dict)
        {
            Set(key, value);
        }
        return this;
    }
    
    public void Clear() => resolver.Clear();

    public void Dispose() => resolver.Clear();
}