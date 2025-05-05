using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExtenDotNet;

public static class IServiceCollectionExtensions
{
    // public static void RegisterExtension<T>(this IServiceCollection services, ExtensionPoint<T> key) where T: class
    // {
    //     if(typeof(IDisposable).IsAssignableFrom(typeof(T)))
    //     {
    //         throw new Exception("Extensions must not implement IDisposable");    
    //     }
    //     services.AddTransient(provider => provider.ResolveExtension(key));
    // }
    
    public static IServiceCollection AddScripting(
        this IServiceCollection services,
        ScriptFactoryOpts opts
    )
    {
        services.AddSingleton(Options.Create(opts));
        services.AddSingleton<IScriptFactory, ScriptFactory>();
        return services;
    }
    
    public static IServiceCollection AddScripting(
        this IServiceCollection services,
        Func<IServiceProvider, ScriptFactoryOpts> opts
    )
    {
        services.AddSingleton(opts.Invoke);
        services.AddSingleton<IScriptFactory, ScriptFactory>();
        return services;
    }
    
    public static IServiceCollection AddExtensionRegistry(
        this IServiceCollection services,
        ExtensionRegistryOpts opts,
        Func<IServiceProvider, IScriptFactory>? scriptFactoryProvider = null
    )
    {
        scriptFactoryProvider ??= p => p.GetRequiredService<IScriptFactory>();
        services.AddSingleton(opts);
        services.AddSingleton<RootProviderContainer>();
        
        foreach(var ext in opts.RegisteredExtensionPoints)
        {
            if(ext.Lifetime == ServiceLifetime.Singleton)
            {
                services.AddSingleton(
                    typeof(IExtensionContainer<>).MakeGenericType(ext.ExtensionType),
                    p => ExtensionContainer.Create(
                        p.GetRequiredService<IExtensionRegistry>(),
                        ext, 
                        p
                    )
                );
            }
            else if(ext.Lifetime == ServiceLifetime.Scoped)
            {
                services.AddScoped(
                    typeof(IExtensionContainer<>).MakeGenericType(ext.ExtensionType),
                    p => ExtensionContainer.Create(
                        p.GetRequiredService<IExtensionRegistry>(),
                        ext, 
                        p
                    )
                );
            }
            else
            {
                services.AddTransient(
                    typeof(IExtensionContainer<>).MakeGenericType(ext.ExtensionType),
                    p => ExtensionContainer.Create(
                        p.GetRequiredService<IExtensionRegistry>(),
                        ext, 
                        p
                    )
                );
            }
        }
        services.AddSingleton(
            p => new SingletonExtensionRegistry(
                scriptFactoryProvider.Invoke(p),
                p.GetRequiredService<IServiceProvider>(),
                p.GetRequiredService<IOptions<ExtensionRegistryOpts>>(),
                p.GetService<ILogger<SingletonExtensionRegistry>>()
            )
        );
        services.AddScoped(
            p => new ScopedExtensionRegistry(
                scriptFactoryProvider.Invoke(p),
                p.GetRequiredService<IServiceProvider>(),
                p.GetRequiredService<IOptions<ExtensionRegistryOpts>>(),
                p.GetService<ILogger<ScopedExtensionRegistry>>()
            )
        );
        services.AddSingleton<IExtensionRegistry, ExtensionRegistry>();
        return services;
    }
    
    internal static bool IsRootProvider(this IServiceProvider provider)
        => ReferenceEquals(provider.GetRequiredService<RootProviderContainer>().Provider, provider);
        
    public static IServiceCollection AddExtensionAsService<T>(
        this IServiceCollection self,
        ExtensionPoint<T> ext,
        bool ignoreDisposable = false
    ) where T: class
    {
        if(!ignoreDisposable && (
            typeof(IDisposable).IsAssignableFrom(typeof(T))
            || typeof(IAsyncDisposable).IsAssignableFrom(typeof(T))
        ))
        {
            throw new Exception("Do not register disposable extensions directly in DI");    
        }
        
        self.Add(new ServiceDescriptor(
            ext.ExtensionType,
            p => 
            {
                return p.GetRequiredService<IExtensionContainer<T>>().Value;
            },
            ext.Lifetime
        ));
        return self;
    }
    
    
    public static IServiceCollection AddExtensionAsService(
        this IServiceCollection self,
        IExtensionPoint ext,
        bool ignoreDisposable = false
    )
    {
        typeof(IServiceCollectionExtensions).GetMethods(BindingFlags.Static | BindingFlags.Public)
            .First(m => m.Name == nameof(AddExtensionAsService) && m.IsGenericMethod)
            .MakeGenericMethod(ext.ExtensionType)
            .Invoke(null, [ self, ext, ignoreDisposable ]);
        return self;
    }
    
    internal static bool IsDisposable(Type type) 
        => typeof(IDisposable).IsAssignableFrom(type) || typeof(IAsyncDisposable).IsAssignableFrom(type);
}

internal class RootProviderContainer(IServiceProvider provider)
{
    public IServiceProvider Provider { get; } = provider;
}