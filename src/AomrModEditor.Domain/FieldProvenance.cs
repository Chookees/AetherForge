namespace AomrModEditor.Domain;

public sealed record FieldProvenance(
    ProvenanceKind Kind,
    string SourceFile,
    string? FieldPath,
    string? BaseValue,
    string? OverrideValue,
    string? EffectiveValue);