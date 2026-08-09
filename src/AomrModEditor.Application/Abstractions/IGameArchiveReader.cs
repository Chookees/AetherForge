namespace AomrModEditor.Application.Abstractions;

/// <summary>Optional CryBar/BAR adapter seam (Phase 5). Phase 1 stub.</summary>
public interface IGameArchiveReader
{
    bool IsSupported { get; }
    Task ExtractAsync(string archivePath, string outputDirectory, CancellationToken cancellationToken);
}