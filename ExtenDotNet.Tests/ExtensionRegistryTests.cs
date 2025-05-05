using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExtenDotNet.Tests;

public interface IExtensionInterface
{
    string Hello();
}
public interface IExtensionInterface2
{
    string Hello();
}

public class ExtensionRegistryTests
{
    IServiceProvider _provider = null!;
    TestSourceResolver _resolver = null!;
    public Dictionary<string, string> Sources { get; } = new()
    {
        ["_imports.csx"] = """
            #r "ExtenDotNet\bin\Debug\net8.0\ExtenDotNet.dll"
        """,
        
        ["utils.dll.csx"] = """
            #load "_imports.csx"
            public static class Utils
            {
                public static string Hello() => "Hello";
            }
        """,
        
        ["extension.csx"] = """
            #load "_imports.csx"
            
            #region preamble
            using System;
            using ExtenDotNet.Tests;
            
            ExtensionScriptContext ctx = null!;
            #endregion

            ctx.SetResult(new Implementation());
            
            public class Implementation: IExtensionInterface
            {
                public string Hello() => "Hello";
            }
        """,
        
        ["scoped.csx"] = """
            #load "_imports.csx"
            
            #region preamble
            using System;
            using ExtenDotNet.Tests;
            
            ExtensionScriptContext ctx = null!;
            #endregion

            ctx.SetResult(new Implementation());
            
            public class Implementation: IExtensionInterface
            {
                public string Hello() => "Hello";
            }
        """,
        
        ["transient.csx"] = """
            #load "_imports.csx"
            
            #region preamble
            using System;
            using ExtenDotNet.Tests;
            
            ExtensionScriptContext ctx = null!;
            #endregion

            ctx.SetResult(new Implementation());
            
            public class Implementation: IExtensionInterface2
            {
                public string Hello() => "Hello";
            }
        """
    };
    
    static readonly ExtensionPoint<IExtensionInterface> Extension = new("extension", ServiceLifetime.Singleton);
    static readonly ExtensionPoint<IExtensionInterface> Scoped = new("scoped", ServiceLifetime.Scoped);
    static readonly ExtensionPoint<IExtensionInterface2> Transient = new("transient", ServiceLifetime.Transient);

    
    [TearDown]
    public void Testdown()
    {
    }
    
    [SetUp]
    public void Setup()
    {
        _resolver = new TestSourceResolver();
        var services = new ServiceCollection();
        services.AddTransient(typeof(ILogger<>), typeof(Logger<>));
        services.AddScripting(
            ScriptFactoryOpts.Default(
                _resolver,
                ScriptOpts.Default.WithReferences(typeof(ScriptFactoryTests).Assembly)
            )
        );
        services.AddExtensionRegistries(
            new ExtensionRegistryOpts() { AllowOnlyDefinedScripts = true }
                .Register(Extension)
                .Register(Scoped)
                .Register(Transient)
        );
        _provider = services.BuildServiceProvider();
    }

    [Test]
    public void TestExtensionPoint()
    {
        using var ctx = _resolver.CreateContext().Set(Sources);
        
        var ext = _provider.ResolveExtension(Extension);
        var r = ext.Hello();
        Assert.That(r, Is.EqualTo("Hello"), "Extension should return 'Hello'");
    }
    
    [Test]
    public void TestScopedExtensionPoint()
    {
        using var ctx = _resolver.CreateContext().Set(Sources);
        using var scope1 = _provider.CreateScope();
        using var scope2 = _provider.CreateScope();
        
        var ext1 = scope1.ServiceProvider.ResolveExtension(Scoped);
        var ext2 = scope2.ServiceProvider.GetRequiredService<IExtensionInterface>();
        
        Assert.That(ext1, Is.Not.SameAs(ext2), "Scoped extensions should be different instances");
    }
    
    [Test]
    public void TestTransientExtensionPoint()
    {
        using var ctx = _resolver.CreateContext().Set(Sources);
        var ext1 = _provider.ResolveExtension(Transient);
        var ext2 = _provider.GetRequiredService<IExtensionInterface2>();
        
        Assert.That(ext1, Is.Not.SameAs(ext2), "Transient extensions should be different instances");
    }
    
    
    [Test]
    public void TestResolveScopedFromSingleton()
    {
        using var ctx = _resolver.CreateContext().Set(Sources);
        Assert.Throws<InvalidOperationException>(() => {
            _provider.GetRequiredService<ISingletonExtensionRegistry>().Resolve(Scoped, _provider);
        });
    }
    
     [Test]
    public void TestAllowOnlyDefinedScripts()
    {
        using var ctx = _resolver.CreateContext().Set(Sources);
        Assert.Throws<ExtensionExcepton>(() => {
            _provider.GetRequiredService<ISingletonExtensionRegistry>().Resolve(new ExtensionPoint<object>("asd", ServiceLifetime.Singleton), _provider);
        });
    }
    
    [Test]
    public void TestExtensionCacheEviction()
    {
        using var ctx = _resolver.CreateContext().Set(Sources);
        var ext1 = _provider.ResolveExtension(Extension);
        _resolver.Set("extension.csx", """
            #load "_imports.csx"
            
            #region preamble
            using System;
            using ExtenDotNet.Tests;
            
            ExtensionScriptContext ctx = null!;
            #endregion

            ctx.SetResult(new Implementation());
            
            public class Implementation: IExtensionInterface
            {
                public string Hello() => "Goodbye";
            }
        """);
        var ext2 = _provider.ResolveExtension(Extension);
        Assert.Multiple(() =>
        {
            Assert.That(ext1, Is.Not.SameAs(ext2), "Extension should be evicted from cache");
            Assert.That(ext1.Hello(), Is.EqualTo("Hello"), "Should be the old implementation");
            Assert.That(ext2.Hello(), Is.EqualTo("Goodbye"), "Should be the new implementation");
        });
    }
    
    [Test]
    public void TestExtensionDllCacheEviction()
    {
        using var ctx = _resolver.CreateContext().Set(Sources);
        _resolver.Set("extension.csx", """
            #load "_imports.csx"
            #load "utils.dll.csx"
            
            #region preamble
            using System;
            using ExtenDotNet.Tests;
            
            ExtensionScriptContext ctx = null!;
            #endregion

            ctx.SetResult(new Implementation());
            
            public class Implementation: IExtensionInterface
            {
                public string Hello() => Utils.Hello();
            }
        """);
        var ext1 = _provider.ResolveExtension(Extension);
        _resolver.Set("utils.dll.csx", """
            #load "_imports.csx"
            
            public static class Utils
            {
                public static string Hello() => "Goodbye";
            }
        """);
        var ext2 = _provider.ResolveExtension(Extension);
        Assert.Multiple(() =>
        {
            Assert.That(ext1, Is.Not.SameAs(ext2), "Extension should be evicted from cache");
            Assert.That(ext1.Hello(), Is.EqualTo("Hello"), "Should be the old implementation");
            Assert.That(ext2.Hello(), Is.EqualTo("Goodbye"), "Should be the new implementation");
        });
    }
}



