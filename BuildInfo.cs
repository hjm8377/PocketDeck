using System.Reflection;

[assembly: AssemblyVersion(BuildInfo.Version)]
[assembly: AssemblyFileVersion(BuildInfo.Version)]
[assembly: AssemblyProduct("Pocket Deck")]

internal static class BuildInfo
{
    public const string Version = "1.4.0";
}
