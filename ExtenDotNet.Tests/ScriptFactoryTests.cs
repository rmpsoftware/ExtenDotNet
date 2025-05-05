using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;

namespace ExtenDotNet.Tests;

public class ScriptFactoryTests
{
    IScriptFactory _factory = null!;
    TestSourceResolver _resolver = null!;
    
    public Dictionary<string, string> Sources { get; } = new()
    {
        ["_imports.csx"] = """
            #r "ExtenDotNet\bin\Debug\net8.0\ExtenDotNet.dll"
        """,
        
        ["interfaces.dll.csx"] = """
            #load "_imports.csx"

            using System;

            public class Singleton
            {
                public static Singleton Instance { get; } = new Singleton();
            }
        """,
        
        ["test1.csx"] = """
            #load "_imports.csx"
            #load "interfaces.dll.csx"
            
            return Singleton.Instance;
        """,
        
        ["test2.csx"] = """
            #load "_imports.csx"
            #load "interfaces.dll.csx"
            
            return Singleton.Instance;
        """
    };
    
    [TearDown]
    public void Testdown()
    {
    }
    
    [SetUp]
    public void Setup()
    {
        _resolver = new TestSourceResolver();
        var factoryOpts = ScriptFactoryOpts.Default(
            _resolver, 
            ScriptOpts.Default
                .AddReferences(typeof(ScriptFactoryTests).Assembly)
        );
        _factory = new ScriptFactory(factoryOpts, new Logger<ScriptFactory>());
    }

    [Test]
    public async  Task TestDllImport()
    {
        using var ctx = _resolver.CreateContext().Set(Sources);
        var test1 = _factory.GetScript<object, object>(new("test1", true));
        var test2 = _factory.GetScript<object, object>(new("test2", true));
        var r1 = await test1.InvokeAsync(new());
        var r2 = await test2.InvokeAsync(new());
        Assert.That(r2, Is.SameAs(r1), "Dll import should be the same for test1 and test2 so singletons should be the same reference");
    }
    
    [Test]
    public async Task TestDllImportCacheEviction()
    {
        using var ctx = _resolver.CreateContext().Set(Sources);
        var test1 = _factory.GetScript<object, object>(new("test1", true));
        var test2 = _factory.GetScript<object, object>(new("test2", true));
        var r1 = await test1.InvokeAsync(new());
        var r2 = await test2.InvokeAsync(new());
        
        _resolver.Set("interfaces.dll.csx", @"
            #load ""_imports.csx""

            using System;

            public class Singleton
            {
                public static Singleton Instance { get; } = new Singleton();
            }
        ");
        
        var test11 = _factory.GetScript<object, object>(new("test1", true));
        var test22 = _factory.GetScript<object, object>(new("test2", true));
        var r11 = await test11.InvokeAsync(new());
        var r22 = await test22.InvokeAsync(new());
        
        Assert.Multiple(() =>
        {
            Assert.That(r11, Is.Not.SameAs(r1), "Updating source file should evict cache");
            Assert.That(r22, Is.Not.SameAs(r2), "Updating source file should evict cache");
            Assert.That(r11, Is.SameAs(r22), "Dll import should be the same for test1 and test2 so singletons should be the same reference");
        });
    }
    
    [Test]
    public void TestScriptCacheEviction()
    {
        using var ctx = _resolver.CreateContext();
        _resolver.Set("TestScriptCacheEviction.csx", @"
            #load ""_imports.csx""
            return 1;
        ");
        var r1 = _factory.GetScript(new ScriptDefinition<object, int>("TestScriptCacheEviction", true)).Invoke(new());
        
        _resolver.Set("TestScriptCacheEviction.csx", @"
            #load ""_imports.csx""
            return 2;
        ");
        var r2 = _factory.GetScript(new ScriptDefinition<object, int>("TestScriptCacheEviction", true)).Invoke(new());
        Assert.Multiple(() =>
        {
            Assert.That(r1, Is.EqualTo(1), "Script should return 1");
            Assert.That(r2, Is.EqualTo(2), "Updated script should return 2");
        });
    }
    
    [Test]
    public void TestDisableHotReload()
    {
        using var ctx = _resolver.CreateContext();
        _resolver.Set("TestScriptCacheEviction.csx", @"
            #load ""_imports.csx""
            return 1;
        ");
        var def = new ScriptDefinition<object, int>("TestScriptCacheEviction", true);
        var factoryOpts = ScriptFactoryOpts.Default(
                _resolver, 
                ScriptOpts.Default
                    .AddReferences(typeof(ScriptFactoryTests).Assembly)
            )
            .WithEnableHotReload(false);
        var factory = new ScriptFactory(factoryOpts, new Logger<ScriptFactory>());
        var r1 = factory.GetScript(def).Invoke(new());
        
        _resolver.Set("TestScriptCacheEviction.csx", @"
            #load ""_imports.csx""
            return 2;
        ");
        var r2 = factory.GetScript(def).Invoke(new());
        
        Assert.Multiple(() =>
        {
            Assert.That(r1, Is.EqualTo(1), "Script should return 1");
            Assert.That(r2, Is.EqualTo(1), "Non cached script should not be updated and should still return 1");
        });
    }
    
    [Test]
    public void TestAutoUsings()
    {
        using var ctx = _resolver.CreateContext().Set(Sources);
        _resolver.Set("TestAutoUsings.csx", @"
            #load ""_imports.csx""
            
            using TestNamespace;
            
            return Test.HelloFromTestNs();
        ");
        var def = new ScriptDefinition<object, string>("TestAutoUsings", true);
        var r1 = _factory.GetScript(def).Invoke(new());
        Assert.That(r1, Is.EqualTo("Hello"), "Script should return 'Hello'");
    }
    
    [Test]
    public void TestDiscardUnderscoreImports()
    {
        using var ctx = _resolver.CreateContext().Set(Sources);
        _resolver.Set("_import1.csx", "object ext = null;");
        _resolver.Set("_import2.csx", "object ext = null;");
        
        _resolver.Set("TestDiscardUnderscoreImports.csx", @"
            #load ""_import1.csx""
            #load ""_import2.csx""
            
            return 10;
        ");
        
        var def = new ScriptDefinition<object, int>("TestDiscardUnderscoreImports", true);
        var r1 = _factory.GetScript(def).Invoke(new());
        Assert.That(r1, Is.EqualTo(10), "Script should return 10");
    }
    
    [Test]
    public void TestAllowOnlyDefinedScripts()
    {
        var factoryOpts = ScriptFactoryOpts.Default(
                _resolver, 
                ScriptOpts.Default.AddReferences(typeof(ScriptFactoryTests).Assembly)
            )
            .WithAllowOnlyDefinedScripts(true);
        var factory = new ScriptFactory(factoryOpts, new Logger<ScriptFactory>());
        Assert.Throws<ScriptException>(() => {
            factory.GetScript(new ScriptDefinition<object, object>("test1", true));
        });
    }
}



