using AomrModEditor.Application.Abstractions;

namespace AomrModEditor.ModGeneration;

public sealed class LocalModInstaller : ILocalModInstaller
{
    private static readonly HashSet<string> AllowedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "proto_mods.xml",
        "techtree_mods.xml",
        "modinfo.json",
        "strings.xml"
    };

    private readonly IPathSafety _pathSafety;

    public LocalModInstaller(IPathSafety pathSafety) => _pathSafety = pathSafety;

    public InstallResult InstallGeneratedOnly(string stagingDirectory, string modsRoot, string modFolderName)
    {
        if (!Directory.Exists(stagingDirectory))
            throw new DirectoryNotFoundException(stagingDirectory);

        ValidateModFolderName(modFolderName);

        Directory.CreateDirectory(modsRoot);
        var modsRootFull = Path.GetFullPath(modsRoot);
        var target = _pathSafety.NormalizeAndValidate(modsRootFull, modFolderName);
        _pathSafety.EnsureUnderRoot(modsRootFull, target);

        string? backup = null;
        if (Directory.Exists(target))
        {
            backup = target + ".bak-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            Directory.Move(target, backup);
        }

        // Retold layout: ModName/game/data/gameplay/{proto_mods,techtree_mods}.xml
        var gameplayRel = AdditiveModGenerator.GameplayRelativeDir;
        var gameplayTarget = Path.Combine(target, gameplayRel);
        Directory.CreateDirectory(gameplayTarget);

        var copied = new List<string>();

        // Prefer staged gameplay folder; fall back to flat staging for older outputs.
        var stagedGameplay = Path.Combine(stagingDirectory, gameplayRel);
        var searchRoots = Directory.Exists(stagedGameplay)
            ? new[] { stagedGameplay, stagingDirectory }
            : new[] { stagingDirectory };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in searchRoots)
        {
            foreach (var file in Directory.EnumerateFiles(root))
            {
                var name = Path.GetFileName(file);
                if (!AllowedFileNames.Contains(name))
                    continue;
                if (!seen.Add(name))
                    continue;

                var dest = Path.Combine(gameplayTarget, name);
                if (name.Equals("modinfo.json", StringComparison.OrdinalIgnoreCase))
                    dest = Path.Combine(target, name);

                _pathSafety.EnsureUnderRoot(target, dest);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
                copied.Add(dest);
            }
        }

        if (copied.Count == 0)
            throw new InvalidOperationException("No generated mod files found to install.");

        return new InstallResult(target, copied, backup);
    }

    private static void ValidateModFolderName(string modFolderName)
    {
        if (string.IsNullOrWhiteSpace(modFolderName))
            throw new InvalidOperationException("Mod folder name is required.");
        if (Path.IsPathRooted(modFolderName))
            throw new InvalidOperationException("Mod folder name must be a relative path segment.");
        if (modFolderName.Contains("..", StringComparison.Ordinal)
            || modFolderName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar]) >= 0)
            throw new InvalidOperationException("Mod folder name must be a single safe relative segment.");
        if (modFolderName is "." or "..")
            throw new InvalidOperationException("Mod folder name must be a single safe relative segment.");
    }
}
