namespace AomrModEditor.Api.Contracts.V1;

public sealed record DiscoverInstallationsRequest(string? HintPath);

public sealed record InstallationDto(string Path, string Status, bool IsConfirmed, string? Kind = null);

public sealed record LocalModsRootDto(
    string Path,
    string? AccountFolderName,
    int ExistingModCount,
    DateTimeOffset? LastWriteUtc);

public sealed record DiscoveryResultDto(
    IReadOnlyList<InstallationDto> Installations,
    IReadOnlyList<LocalModsRootDto> LocalModsRoots,
    string? PreferredLocalModsPath,
    string? PreferredInstallPath,
    string Summary);

public sealed record ImportSourceRequest(string DirectoryPath);

public sealed record SourceSnapshotDto(
    Guid Id,
    string RootPath,
    string AggregateHash,
    int EntityCount,
    DateTimeOffset ImportedAtUtc);

public sealed record EntitySummaryDto(
    string InternalKey,
    string Category,
    string SourceFile,
    string? DisplayNameId,
    string? Pantheon,
    IReadOnlyList<string>? UnitTypes = null,
    string? IconPath = null,
    string? ExclusivePantheon = null);

public sealed record FieldProvenanceDto(
    string Kind,
    string SourceFile,
    string? FieldPath,
    string? BaseValue,
    string? OverrideValue,
    string? EffectiveValue);

public sealed record EntityDetailDto(
    EntitySummaryDto Summary,
    string RawXml,
    IReadOnlyDictionary<string, FieldProvenanceDto> Provenance,
    IReadOnlyDictionary<string, string?> NormalizedFields);

public sealed record CreateProjectRequest(string Name, string Directory);

public sealed record ProjectDto(
    Guid Id,
    string Name,
    string Directory,
    Guid SourceSnapshotId,
    string SourceFingerprintHash,
    int Version);

public sealed record ApplyChangeRequest(
    string EntityKey,
    string OpType,
    string? ElementName,
    string? ActionName,
    string? ChildElementName,
    string NewValue,
    string? AttributeName = null,
    Dictionary<string, string>? Attributes = null);

public sealed record GodPowerUnifiedDto(
    string PowerKey,
    string? Behavior,
    string? Damage,
    string? CooldownTechKey,
    string? Cooldown,
    string? CostFavor,
    string BehaviorSourceFile,
    string? CooldownSourceFile);

public sealed record ChangeDto(Guid Id, string EntityKey, string OpType, DateTimeOffset CreatedAtUtc);

public sealed record DiffDto(
    IReadOnlyList<SemanticDiffEntryDto> Semantic,
    string RawBefore,
    string RawAfter);

public sealed record SemanticDiffEntryDto(string EntityKey, string Path, string? OldValue, string? NewValue);

public sealed record ValidationIssueDto(string Severity, string Code, string Message, string? EntityKey);

public sealed record GenerationDto(
    string StagingDirectory,
    IReadOnlyList<string> GeneratedFiles,
    string ProtoModsXml,
    string TechtreeModsXml,
    IReadOnlyList<ValidationIssueDto> Issues,
    IReadOnlyList<GodPowerStrategyDto> GodPowerStrategies);

public sealed record GodPowerStrategyDto(string EntityKey, string Strategy, string Reason, string? OutputFile);

public sealed record InstallRequest(string ModsRoot, string ModFolderName);

public sealed record InstallDto(string TargetDirectory, IReadOnlyList<string> CopiedFiles, string? BackupDirectory);

public sealed record OperationDto(
    Guid Id,
    string Kind,
    string State,
    int ProgressPercent,
    string? Phase,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultLink);

public sealed record PagedEntitiesDto(IReadOnlyList<EntitySummaryDto> Items, string? NextCursor);

public sealed record EffectiveStatStepDto(string Step, string Field, string? Value, string? Source, bool Unsupported, string? Warning);

public sealed record EffectiveStatsDto(
    string EntityKey,
    IReadOnlyDictionary<string, string?> Effective,
    IReadOnlyList<EffectiveStatStepDto> Steps,
    IReadOnlyList<string> UnsupportedWarnings);

public sealed record MigrationInspectDto(
    string ExpectedFingerprint,
    string ActualFingerprint,
    bool RequiresMigration,
    IReadOnlyList<string> MatchedEntityKeys,
    IReadOnlyList<string> MissingInNewSource,
    IReadOnlyList<string> AddedInNewSource,
    IReadOnlyList<FuzzySuggestionDto> FuzzySuggestions);

public sealed record FuzzySuggestionDto(string ChangeEntityKey, string SuggestedKey, double Score);

public sealed record RebaseRequest(bool ApplyFuzzyBinds);

public sealed record RebaseResultDto(
    int RebasedCount,
    int DroppedCount,
    int UnresolvedFuzzyCount,
    string NewFingerprintHash,
    string ReportMarkdown);

public sealed record PackageDto(string ZipPath);

public sealed record EditorFieldDto(
    string Path,
    string Label,
    string ValueKind,
    string? CurrentValue,
    IReadOnlyDictionary<string, string>? MatchAttributes,
    string? ElementName,
    string? AttributeName,
    string DisplayFormat = "raw",
    string Presence = "present",
    string? GroupKey = null,
    IReadOnlyList<string>? Options = null,
    string? PersistHint = null,
    string? GroupKind = null);

public sealed record EditorTabDto(string Id, string Title, IReadOnlyList<EditorFieldDto> Fields);

public sealed record EntityEditorSchemaDto(
    string EntityKey,
    string Category,
    IReadOnlyList<string> UnitTypes,
    string? IconPath,
    IReadOnlyList<EditorTabDto> Tabs);
