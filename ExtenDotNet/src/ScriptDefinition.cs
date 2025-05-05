namespace ExtenDotNet;

public interface IScriptDefinition
{
    string Key { get; }
    Type ContextType { get; }
    Type ReturnType { get; }
    bool Required { get; }
    bool CacheScript { get; }
}

public class ScriptDefinition(
    string key, 
    Type contextType, 
    Type returnType, 
    bool required = false,
    bool cache = true
): IScriptDefinition
{
    public string Key { get; }               = key;
    public Type ContextType { get; }         = contextType;
    public Type ReturnType { get; }          = returnType;
    public virtual bool Required { get; }    = required;
    public virtual bool CacheScript { get; } = cache;
    
    public override bool Equals(object? obj)
    {
        if(obj is not IScriptDefinition reg)
            return false;
        return reg.Key == Key && reg.ContextType == ContextType && reg.ReturnType == ReturnType;
    }
    
    public override int GetHashCode() 
    {
        return Key.GetHashCode() ^ ContextType.GetHashCode() ^ ReturnType.GetHashCode();
    }
        
    public override string ToString() => Key;
}

public class ScriptDefinition<TContext>(
    string key,
    bool required = false,
    bool cache = true
) : ScriptDefinition(key, typeof(TContext), typeof(object), required, cache);

public class ScriptDefinition<TContext, TReturn>(
    string key,
    bool required = false,
    bool cache = true
) : ScriptDefinition(key, typeof(TContext), typeof(TReturn), required, cache);
