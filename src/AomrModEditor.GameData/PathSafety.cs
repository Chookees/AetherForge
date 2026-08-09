using AomrModEditor.Application.Abstractions;

namespace AomrModEditor.GameData;

public sealed class PathSafety : IPathSafety
{
    public string NormalizeAndValidate(string root, string relativeOrAbsolute)
    {
        var rootFull = Path.GetFullPath(root);
        var candidate = Path.IsPathRooted(relativeOrAbsolute)
            ? Path.GetFullPath(relativeOrAbsolute)
            : Path.GetFullPath(Path.Combine(rootFull, relativeOrAbsolute));
        EnsureUnderRoot(rootFull, candidate);
        return candidate;
    }

    public void EnsureUnderRoot(string root, string fullPath)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(fullPath);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetFullPath(root), full, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal rejected.");
    }
}