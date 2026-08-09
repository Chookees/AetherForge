using AomrModEditor.Domain;

namespace AomrModEditor.Application.Abstractions;

public sealed record ProjectInfo(
    Guid Id,
    string Name,
    string Directory,
    Guid SourceSnapshotId,
    string SourceFingerprintHash,
    int Version);

public interface IProjectStore
{
    ProjectInfo? Current { get; }
    Task<ProjectInfo> CreateAsync(string name, string directory, SourceSnapshot snapshot, CancellationToken cancellationToken);
    Task AppendChangeAsync(ChangeOp op, CancellationToken cancellationToken);
    IReadOnlyList<ChangeOp> GetChanges();
    Task UndoAsync(CancellationToken cancellationToken);
    Task RedoAsync(CancellationToken cancellationToken);
    void EnsureSourceFingerprintMatches(string currentAggregateHash);
    Task ApplyRebaseAsync(IReadOnlyList<ChangeOp> rebasedChanges, string newFingerprintHash, CancellationToken cancellationToken);
}