using AomrModEditor.Application.Abstractions;

namespace AomrModEditor.GameData;

public sealed class NullGameArchiveReader : IGameArchiveReader
{
    public bool IsSupported => false;

    public Task ExtractAsync(string archivePath, string outputDirectory, CancellationToken cancellationToken)
        => throw new NotSupportedException("CryBar archive extraction is not enabled in Phase 1. Use a pre-extracted directory.");
}