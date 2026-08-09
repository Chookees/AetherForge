using AomrModEditor.Domain;

namespace AomrModEditor.Application.Abstractions;

public sealed record EffectiveStatStep(
    string Step,
    string Field,
    string? Value,
    string? Source,
    bool Unsupported = false,
    string? Warning = null);

public sealed record EffectiveStatsResult(
    string EntityKey,
    IReadOnlyDictionary<string, string?> Effective,
    IReadOnlyList<EffectiveStatStep> Steps,
    IReadOnlyList<string> UnsupportedWarnings);

public interface IEffectiveStatEngine
{
    EffectiveStatsResult Compute(SourceNode unit, IEnumerable<SourceNode>? technologies = null);
}
