using AomrModEditor.Domain;

namespace AomrModEditor.Application.Abstractions;

public sealed record SourceSnapshot(
    Guid Id,
    string RootPath,
    SourceFingerprint Fingerprint,
    DateTimeOffset ImportedAtUtc,
    int EntityCount);

public interface ISourceSnapshotStore
{
    SourceSnapshot? Current { get; }
    IEntityCatalog? Catalog { get; }
    Task<SourceSnapshot> ImportDirectoryAsync(string directory, CancellationToken cancellationToken);
    Task<SourceSnapshot> ImportPackAsync(Stream gzipPackStream, string rootLabel, CancellationToken cancellationToken);
    void ReplaceCatalog(IEntityCatalog catalog, SourceSnapshot snapshot);
}