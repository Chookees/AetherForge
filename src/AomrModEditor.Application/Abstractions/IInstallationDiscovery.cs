namespace AomrModEditor.Application.Abstractions;

public sealed record DiscoveredInstallation(
    string Path,
    string Kind,
    string Status);

public sealed record DiscoveredLocalModsRoot(
    string Path,
    string? AccountFolderName,
    int ExistingModCount,
    DateTimeOffset? LastWriteUtc);

public sealed record InstallationDiscoveryResult(
    IReadOnlyList<DiscoveredInstallation> Installations,
    IReadOnlyList<DiscoveredLocalModsRoot> LocalModsRoots,
    string? PreferredLocalModsPath,
    string? PreferredInstallPath,
    string Summary);

public interface IInstallationDiscovery
{
    InstallationDiscoveryResult Discover(string? hintPath = null);
}
