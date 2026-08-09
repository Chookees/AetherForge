namespace AomrModEditor.Domain;

public sealed record SemanticDiffEntry(
    string EntityKey,
    string Path,
    string? OldValue,
    string? NewValue);

public sealed record DiffResult(
    IReadOnlyList<SemanticDiffEntry> Semantic,
    string RawBefore,
    string RawAfter);