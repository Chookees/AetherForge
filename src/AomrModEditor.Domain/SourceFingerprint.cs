namespace AomrModEditor.Domain;

public sealed record SourceFingerprint(
    string AggregateHash,
    IReadOnlyDictionary<string, string> FileHashes);