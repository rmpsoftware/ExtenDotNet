using Microsoft.Extensions.DependencyInjection;

namespace ExtenDotNet;

public static class ExtensionServiceProviderExtension
{
    public static T ResolveExtension<T>(this IServiceProvider provider, ExtensionPoint<T> key) where T: class
    {
        if(!key.Required && !key.HasDefaultImplementation)
            throw new ScriptException($"You passed a non-required extension point {key.Key} to ResolveExtension, use ResolveOptionalExtension instead");
        return provider.GetRequiredService<IExtensionRegistry>().Resolve(key, provider)!;
    }
    
    public static object? ResolveExtension(this IServiceProvider provider, IExtensionPoint key)
    {
        if(!key.Required && !key.HasDefaultImplementation)
            throw new ScriptException($"You passed a non-required extension point {key.Key} to ResolveExtension, use ResolveOptionalExtension instead");
        return provider.GetRequiredService<IExtensionRegistry>().Resolve(key, provider);
    }
    
    public static T? ResolveOptionalExtension<T>(this IServiceProvider provider, ExtensionPoint<T> key) where T: class
    {
        if(key.Required)
            throw new ScriptException($"You passed a required extension point {key.Key} to ResolveOptionalExtension, use ResolveExtension instead");
        return provider.GetRequiredService<IExtensionRegistry>().Resolve(key, provider);
    }
    
    public static object? ResolveOptionalExtension(this IServiceProvider provider, IExtensionPoint key)
    {
        if(key.Required)
            throw new ScriptException($"You passed a required extension point {key.Key} to ResolveOptionalExtension, use ResolveExtension instead");
        return provider.GetRequiredService<IExtensionRegistry>().Resolve(key, provider);
    }
}