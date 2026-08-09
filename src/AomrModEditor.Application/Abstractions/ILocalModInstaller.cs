namespace AomrModEditor.Application.Abstractions;

public sealed record InstallResult(string TargetDirectory, IReadOnlyList<string> CopiedFiles, string? BackupDirectory);

public interface ILocalModInstaller
{
    InstallResult InstallGeneratedOnly(string stagingDirectory, string modsRoot, string modFolderName);
}