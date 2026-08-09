namespace AomrModEditor.Application.Abstractions;

public interface IPathSafety
{
    string NormalizeAndValidate(string root, string relativeOrAbsolute);
    void EnsureUnderRoot(string root, string fullPath);
}