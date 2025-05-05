# ExtenDotNet - A extensibility library using C# Scripting (Microsoft.CodeAnalysis.CSharp.Scripting)

This library is a small wrapper around the Microsoft.CodeAnalysis.CSharp.Scripting library to enhance some ergonomics, extend it's functionality and provide a safer api for running C# scripts.
I built it because I was chasing the following goals:
- Full diagnostics and LSP support during developing of scripts that reference types from the host application
- A way to share code between scripts 
- A good interface to use with dependency injection
- Hot reloading

## ScriptFactory
- A factory that creates and caches scripts
- ScriptFactory takes a ScriptDefinition and creates a IScript object from it. The Script definition encodes the context type and return type in it's definition which makes calling of scripts safer.
- When creating a script there is a preprocessor of the content. This preprocessor:
  - automatically discovers usings from the script
  - comments out regions of the script (useful for defining the scope type properties as variables during development so the LSP knows about them)
  - Ignores special #load directives where the loaded filename start with an "_". This is useful in combination with a convention: I have a _imports.csx file that contains all #r directives that load my host application assemblies. During development this means the LSP knows about my types and I have full support. But during execution this file should be ignored as the host assemblies are added to the script by ScriptFactory (configured in ScriptOpts)
  - Supports of #loading what I call a Dll-Script. A Dll-Script is a script that ends with .dll.csx. DllScripts are compiled as SharedLibrary (that means that Script-Directives do now work here). But the advantage is, that Dll-Scripts can be references from multiple other Scripts and will be the same Assembly, contrary to #load where when I load the same script in two different scripts it get's compiled twice and for example static references will be different.
- The IScript contains information about it's context and return type and provides a safe interface to call it. 

# ExtensionRegistry and ExtensionPoints
- This is the interface that should be used with dependency injection. The idea is to define an ```ExtensionPoint<T>``` that encodes information about an interface that can be implemented using a script. 
- ExtensionPoints can have a default implementation or be required or not required. 
- When resolving an ``ExtensionPoint`` the ExtensionRegistry will call ``ScriptFactory`` to compile the corresponding script and return the result object that implements the interface
- ```ExtensionPoints``` have a service lifetime and can be Singleton, Scoped or Transient
- ExtensionRegistry handles disposal of extensions that implement IDisposable or IAsyncDisposable. Care has to be taken when registering a disposable extension directly in the service provider, as dispose will then potentially be called twice.

# Hot Reloading
- When configured the ScriptFactory will evict scripts from it's cache. Referenced files with `#r` will also be considered.
- Singleton extensions will also be evicted ExtensionRegistry's cache
- Scoped ExtensionPoints will not be evicted, as the reference during the use of the scope should be guaranteed to be the same until the scope is disposed. (Transient ExtensionPoints will never be cached anyways)
- This is why Singleton ExtensionPoints cannot by default be resolved in an opaque way in the service provider
  - A scoped extensionpoint that was registered in the setup of ``AddExtensionRegistries()`` can be resolved with ``provider.GetRequiredService<T>()``.
  - This cannot be done by default with singletons because if the instance is evicted from the cache the service provider will not know about it and still return the old reference. To get around this you can either use ```provider.ResolveExtension<T>()``` or ```provider.GetRequiredService<ExtensionContainer<T>>()``` or you set **UnsafeSingletonResolve** to true in the **ExtensionRegistryOpts**.

# Example of using ScriptFactory

Consider a set of scripts
```csharp
//file _imports.csx (this file will be completely ignored during execution)
#r "path/to/host/development/assembly.dll"
```

```csharp
//file script1.csx
#load "_imports.csx" //this will be commented out by the preprocessor

using HostNamespace;

#region preamble //this whole region will be commented out by the preprocessor
Context ctx = null!;
#endregion

return Program.SomeStaticFunction(context.Hello); //we get full intellisense with omnisharp in vscode here (no c# dev kit, use "editor.useOmniSharp": true in settings.json)
```

We execute this script like this
```csharp
//a file in your host project
class ScriptScope<T>
{
  public T ctx { get; set; } //this property is defined in the script in the preamble region so we have intelli sense
}
class Context
{
  public string Hello { get; set; } = "Hello";
}

var resolver = new DefaultScriptSourceResolver("path/to/scripts");
var definition = new ScriptDefinition<ScriptScope, string>("script1"); //leave the .csx here if you are using DefaultScriptResolver
var factoryOpts = ScriptFactoryOpts.Default(
  resolver, 
  ScriptOpts.Default.AddReferences(typeof(Program).Assembly)
);
var factory = new ScriptFactory(factoryOpts, new Logger<ScriptFactory>());
var script = factory.Create(definition);
var result = await script.InvokeAsync(new ScriptScope<Context> { ctx = new Context() }); //returns "Hello"
```
have a look at the tests for more examples.

# Example of using ExtensionRegistry
```csharp
//file extension1.csx (this file implements the interface IExtension and provides an instance as result)
#load "_imports.csx" //this will be commented out by the preprocessor
#region preamble
using System;
using ExtenDotNet;

ExtensionScriptContext ctx = null!;
#endregion

ctx.SetResult(new Extension());

public class Extension : IExtension
{
  public void Hello()
  {
    Console.WriteLine("Hello");
  }
}
```
We can load this interface like this
```csharp
public interface IExtension
{
  public void Hello();
}
//the idea is that the extensions are predefined in the host application
static class ExtensionPoints
{
  public static ExtensionPoint<IExtension> Extension1 = new ("extension1", ServiceLifetime.Singleton);
}

//set everything up
var resolver = new DefaultScriptSourceResolver();
var services = new ServiceCollection();
services.AddTransient(typeof(ILogger<>), typeof(Logger<>));
services.AddScripting(
    ScriptFactoryOpts.Default(
        resolver,
        ScriptOpts.Default.WithReferences(typeof(Program).Assembly)
    ),
    builder => builder
        .AddExtensionRegistries(new ExtensionRegistryOpts())
        .Register(ExtensionPoints.Extension1)
);
var provider = services.BuildServiceProvider();

//resolve the extension
var extensionInstance = provider.ResolveExtension(ExtensionPoints.Extension1);
extensionInstance.Hello(); //prints "Hello"
```
