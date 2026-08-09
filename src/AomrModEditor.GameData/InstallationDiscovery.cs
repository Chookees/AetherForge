using AomrModEditor.Application.Abstractions;

namespace AomrModEditor.GameData;

/// <summary>
/// Discovers AoM: Retold install and per-account local mods folders.
/// SteamID64 under Games\Age of Mythology Retold varies per Steam account.
/// </summary>
public sealed class InstallationDiscovery : IInstallationDiscovery
{
    public InstallationDiscoveryResult Discover(string? hintPath = null)
    {
        var installations = new List<DiscoveredInstallation>();
        var localMods = new List<DiscoveredLocalModsRoot>();

        if (!string.IsNullOrWhiteSpace(hintPath) && Directory.Exists(hintPath))
        {
            var full = Path.GetFullPath(hintPath);
            installations.Add(new DiscoveredInstallation(full, "Hint", "Found"));
            TryAddLocalModsFromTree(full, localMods);
        }

        foreach (var install in DiscoverCommonInstallRoots())
        {
            if (installations.Any(i => PathsEqual(i.Path, install)))
                continue;
            installations.Add(new DiscoveredInstallation(install, "Common", "Found"));
        }

        foreach (var root in DiscoverLocalModsRoots())
        {
            if (localMods.Any(m => PathsEqual(m.Path, root.Path)))
                continue;
            localMods.Add(root);
        }

        // Prefer the local-mods folder with the most mods, then most recently written.
        var preferredMods = localMods
            .OrderByDescending(m => m.ExistingModCount)
            .ThenByDescending(m => m.LastWriteUtc ?? DateTimeOffset.MinValue)
            .Select(m => m.Path)
            .FirstOrDefault();

        var preferredInstall = installations
            .Select(i => i.Path)
            .FirstOrDefault(p => Directory.Exists(p));

        var summary = preferredMods is not null
            ? $"Local mods: {preferredMods}" + (localMods.Count > 1 ? $" (+{localMods.Count - 1} other account folders)" : "")
            : localMods.Count == 0
                ? "No local mods folder found under Games\\Age of Mythology Retold"
                : "Local mods folders found";

        return new InstallationDiscoveryResult(
            installations,
            localMods,
            preferredMods,
            preferredInstall,
            summary);
    }

    private static IEnumerable<string> DiscoverCommonInstallRoots()
    {
        var candidates = new List<string>();

        // Steam library default
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Steam", "steamapps", "common", "Age of Mythology Retold"));
            candidates.Add(Path.Combine(programFiles, "Steam", "steamapps", "common", "Age of Mythology Retold", "AoMRT"));
        }

        var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(pf64))
            candidates.Add(Path.Combine(pf64, "Steam", "steamapps", "common", "Age of Mythology Retold"));

        // User Games root sometimes holds launcher bits
        var userGames = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Games",
            "Age of Mythology Retold");
        if (Directory.Exists(userGames))
            candidates.Add(userGames);

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(c))
                yield return Path.GetFullPath(c);
        }
    }

    private static IEnumerable<DiscoveredLocalModsRoot> DiscoverLocalModsRoots()
    {
        var retoldGames = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Games",
            "Age of Mythology Retold");

        if (!Directory.Exists(retoldGames))
            yield break;

        // Account folders are SteamID64 (numeric) — scan all so multi-account setups work.
        foreach (var accountDir in Directory.EnumerateDirectories(retoldGames))
        {
            var local = Path.Combine(accountDir, "mods", "local");
            if (!Directory.Exists(local))
                continue;

            var accountName = Path.GetFileName(accountDir);
            var modCount = 0;
            DateTimeOffset? lastWrite = null;
            try
            {
                foreach (var modDir in Directory.EnumerateDirectories(local))
                {
                    modCount++;
                    var write = Directory.GetLastWriteTimeUtc(modDir);
                    if (lastWrite is null || write > lastWrite)
                        lastWrite = new DateTimeOffset(write, TimeSpan.Zero);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // skip unreadable account folders
                continue;
            }

            yield return new DiscoveredLocalModsRoot(
                Path.GetFullPath(local),
                accountName,
                modCount,
                lastWrite);
        }

        // Also accept a direct mods\local under the retold games root (rare layouts)
        var direct = Path.Combine(retoldGames, "mods", "local");
        if (Directory.Exists(direct))
        {
            yield return new DiscoveredLocalModsRoot(
                Path.GetFullPath(direct),
                null,
                SafeCountDirs(direct),
                null);
        }
    }

    private static void TryAddLocalModsFromTree(string root, List<DiscoveredLocalModsRoot> sink)
    {
        // If hint is already mods\local
        if (IsLocalModsFolder(root))
        {
            sink.Add(new DiscoveredLocalModsRoot(root, Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(root))), SafeCountDirs(root), null));
            return;
        }

        var nested = Path.Combine(root, "mods", "local");
        if (Directory.Exists(nested))
            sink.Add(new DiscoveredLocalModsRoot(Path.GetFullPath(nested), Path.GetFileName(root), SafeCountDirs(nested), null));
    }

    private static bool IsLocalModsFolder(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!name.Equals("local", StringComparison.OrdinalIgnoreCase)) return false;
        var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return parent.Equals("mods", StringComparison.OrdinalIgnoreCase);
    }

    private static int SafeCountDirs(string path)
    {
        try { return Directory.EnumerateDirectories(path).Count(); }
        catch { return 0; }
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
