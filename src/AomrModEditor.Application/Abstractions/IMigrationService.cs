using AomrModEditor.Domain;

namespace AomrModEditor.Application.Abstractions;

public sealed record MigrationInspectReport(
    string ExpectedFingerprint,
    string ActualFingerprint,
    bool RequiresMigration,
    IReadOnlyList<string> MatchedEntityKeys,
    IReadOnlyList<string> MissingInNewSource,
    IReadOnlyList<string> AddedInNewSource,
    IReadOnlyList<FuzzySuggestion> FuzzySuggestions);

public sealed record FuzzySuggestion(string ChangeEntityKey, string SuggestedKey, double Score);

public sealed record RebaseResult(
    IReadOnlyList<ChangeOp> RebasedChanges,
    IReadOnlyList<ChangeOp> DroppedChanges,
    IReadOnlyList<FuzzySuggestion> UnresolvedFuzzy,
    string NewFingerprintHash,
    string ReportMarkdown);

public interface IMigrationService
{
    MigrationInspectReport Inspect(IEntityCatalog oldCatalog, IEntityCatalog newCatalog, string expectedHash, string actualHash);
    RebaseResult Rebase(
        IReadOnlyList<ChangeOp> changes,
        IEntityCatalog oldCatalog,
        IEntityCatalog newCatalog,
        string newFingerprintHash,
        bool applyFuzzyBinds = false);
}
