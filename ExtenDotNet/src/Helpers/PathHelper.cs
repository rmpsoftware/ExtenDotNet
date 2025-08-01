using System.Runtime.InteropServices;

namespace ExtenDotNet.Helpers;

internal class PathHelper
{
    static StringComparison ComparisonType = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
        
    public static bool IsPathEqual(string path1, string path2)
        => path1.Equals(path2, ComparisonType);
}