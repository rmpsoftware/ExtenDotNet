namespace ExtenDotNet;

public class ExtensionScriptContext(
    IServiceProvider provider
) 
{
    public IServiceProvider Provider { get; protected set; } = provider;
    
    public object? Result { get; set; }
    public void SetResult(object o) => Result = o;
}