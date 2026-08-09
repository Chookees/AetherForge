using AomrModEditor.Domain;

namespace AomrModEditor.Application.Abstractions;

public sealed record EntitySummary(
    string InternalKey,
    EntityCategory Category,
    string SourceFile,
    string? DisplayNameId,
    string? Pantheon,
    IReadOnlyList<string> UnitTypes,
    string? IconPath = null);

public sealed record EntityDetail(
    EntitySummary Summary,
    string RawXml,
    IReadOnlyDictionary<string, FieldProvenance> Provenance,
    IReadOnlyDictionary<string, string?> NormalizedFields);

public interface IEntityCatalog
{
    IReadOnlyList<EntitySummary> Search(
        string? query,
        EntityCategory? category = null,
        int take = 100,
        string? unitType = null);
    EntityDetail? GetByInternalKey(string internalKey);
    SourceNode? GetSourceNode(string internalKey);
    IReadOnlyList<EntitySummary> All { get; }
}