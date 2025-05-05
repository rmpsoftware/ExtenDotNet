namespace ExtenDotNet;

public class ScriptScope<T>(T ctx)
{
    public T ctx { get; set; } = ctx;
}


public class ScriptException(string message, Exception? inner = null)
    : Exception(message, inner) 
{
    
}

public class ExtensionExcepton(string message, Exception? inner = null)
    : ScriptException(message, inner) 
{
    
}
