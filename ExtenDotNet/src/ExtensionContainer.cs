using System.Reflection;

namespace ExtenDotNet;

public interface IExtensionContainer<T> where T: class
{
    T Value { get; }
}

internal class ExtensionContainer
{
    internal static object Create(IExtensionRegistry registry, IExtensionPoint ext, IServiceProvider provider)
    {
        var type = typeof(ExtensionContainer<>).MakeGenericType(ext.ExtensionType);
        var ctor = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, // internal constructor is non-public
            binder: null,
            types: [ 
                typeof(IExtensionRegistry), 
                typeof(IExtensionPoint), 
                typeof(IServiceProvider) 
            ],
            modifiers: null
        );
        return ctor!.Invoke([registry, ext, provider])!;
    }
}

internal class ExtensionContainer<T> : IExtensionContainer<T> where T: class
{
    readonly IExtensionRegistry _registry;
    readonly IExtensionPoint _ext;
    readonly IServiceProvider _provider;
    
    public T Value => (T)_registry.Resolve(_ext, _provider)!;
    
    internal ExtensionContainer(
        IExtensionRegistry registry, 
        IExtensionPoint ext,
        IServiceProvider provider
    )
    {
        _registry = registry;
        _ext      = ext;
        _provider = provider;
    }
}