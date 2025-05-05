using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace ExtenDotNet;

public interface IExtensionPoint: IScriptDefinition
{
    Type ExtensionType { get; }
    bool HasDefaultImplementation { get; }
    // void EnsureResolved(IServiceProvider provider);
    
    ServiceLifetime Lifetime { get; }
    
    public object GetDefaultImplementation(IServiceProvider provider);
    public void OnResolved(object? extension);
}

public static class ExtensionPoint
{
    public static ExtensionPoint<T> Singleton<T>(
        string key,
        T? defaultImplementation = null,
        bool required = true
    ) where T: class
    {
        return new ExtensionPoint<T>(
            key, 
            ServiceLifetime.Singleton, 
            defaultImplementation != null ? p => defaultImplementation : null, 
            required
        );
    }
}


public class ExtensionPoint<T> : ScriptDefinition<ScriptScope<ExtensionScriptContext>>, IExtensionPoint
    where T: class
{
    public override bool CacheScript => Lifetime == ServiceLifetime.Scoped || Lifetime == ServiceLifetime.Singleton;
    public Type ExtensionType => typeof(T);
    
    public ServiceLifetime Lifetime { get; init; }
    public Func<IServiceProvider, T>? DefaultImplementation { get; protected set; }
    public bool HasDefaultImplementation => DefaultImplementation != null;
    public Action<T>? OnResolved { get; protected set; }

    public ExtensionPoint(
        string key,
        ServiceLifetime lifetime = ServiceLifetime.Singleton,
        Func<IServiceProvider, T>? defaultImplementation = null,
        bool required = true
    ) : base(key, required && defaultImplementation == null)
    {
        Lifetime = lifetime;
        DefaultImplementation = defaultImplementation;
    }
    
    public ExtensionPoint(string key, T defaultSingleton, bool required = true) : this(key, ServiceLifetime.Singleton, p => defaultSingleton, required)
    {
        DefaultImplementation = p => defaultSingleton;
    }

    public ExtensionPoint<T> WithOnResolve(Action<T> action)
    {
        OnResolved = action;
        return this;
    }
    
    public ExtensionPoint<T> WithDefaultImplementation(Func<IServiceProvider, T> action)
    {
        DefaultImplementation = action;
        return this;
    }
    
    public ExtensionPoint<T> WithDefaultImplementation(T singleton)
    {
        DefaultImplementation = p => singleton;
        return this;
    }

    public object GetDefaultImplementation(IServiceProvider provider)
        => DefaultImplementation!.Invoke(provider);

    void IExtensionPoint.OnResolved(object? extension)
    {
        if(extension is not T t)
            throw new ArgumentException($"ExtensionPoint {Key} resolved to {extension?.GetType().Name} but expected {typeof(T).Name}");
        OnResolved?.Invoke(t);
    }

    // public T Resolve(IServiceProvider provider)
    //     => provider.GetRequiredService<SingletonExtensionRegistry>().Resolve<T>(this, provider);
    // public T? ResolveOptional(IServiceProvider provider) 
    //     => provider.GetRequiredService<SingletonExtensionRegistry>().ResolveOptional(this, provider);
    // public void EnsureResolved(IServiceProvider provider)
    //     => Resolve(provider);
}
