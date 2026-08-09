using AomrModEditor.Domain;

namespace AomrModEditor.Application.Abstractions;

public sealed record GodPowerStrategyEntry(
    string EntityKey,
    string Strategy,
    string Reason,
    string? OutputFile);

public sealed record GenerationResult(
    string ProtoModsXml,
    string TechtreeModsXml,
    string StagingDirectory,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyList<GodPowerStrategyEntry> GodPowerStrategies);

public interface IModGenerator
{
    GenerationResult Generate(
        IReadOnlyDictionary<string, SourceNode> originals,
        IReadOnlyDictionary<string, SourceNode> modified,
        string stagingDirectory);
}
