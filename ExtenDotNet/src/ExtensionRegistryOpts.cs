using System.Collections.Immutable;

namespace ExtenDotNet;

public class ExtensionRegistryOpts
{
    public ExtensionRegistryOpts() { }
    public ExtensionRegistryOpts(ExtensionRegistryOpts opts)
    {
        AllowOnlyPreregisteredExtensions = opts.AllowOnlyPreregisteredExtensions;
        RegisteredExtensionPoints = opts.RegisteredExtensionPoints;
    }
    
    internal List<IExtensionPoint> RegisteredExtensionPoints { get; init; } = [];
    
    public bool AllowOnlyPreregisteredExtensions { get; set; } = false;
    
    public ExtensionRegistryOpts Register<T>(ExtensionPoint<T> ext) where T: class
        => new(this) { RegisteredExtensionPoints = [.. RegisteredExtensionPoints, ext] };
        
    public ExtensionRegistryOpts Register<T>(IEnumerable<ExtensionPoint<T>> ext) where T: class
        => new(this) { RegisteredExtensionPoints = [.. RegisteredExtensionPoints, ..ext] };
        
    public ExtensionRegistryOpts Register(IExtensionPoint ext)
        => new(this) { RegisteredExtensionPoints = [.. RegisteredExtensionPoints, ext] };
        
    public ExtensionRegistryOpts Register(IEnumerable<IExtensionPoint> ext)
        => new(this) { RegisteredExtensionPoints = [.. RegisteredExtensionPoints, ..ext] };
        
}