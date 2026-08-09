using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;

namespace AomrModEditor.GameData;

public sealed class InMemoryEntityCatalog : IEntityCatalog
{
    private readonly Dictionary<string, SourceNode> _nodes;
    private readonly List<EntitySummary> _all;

    public InMemoryEntityCatalog(IEnumerable<SourceNode> nodes)
    {
        var deduped = EntityKeyDeduper.Deduplicate(nodes);
        _nodes = deduped.ToDictionary(n => n.InternalKey, n => n, StringComparer.OrdinalIgnoreCase);
        _all = _nodes.Values
            .Select(ToSummary)
            .OrderBy(e => e.InternalKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<EntitySummary> All => _all;

    public EntityDetail? GetByInternalKey(string internalKey)
    {
        if (!_nodes.TryGetValue(internalKey, out var node)) return null;
        var fields = ExtractFields(node);
        var provenance = fields.ToDictionary(
            kv => kv.Key,
            kv => new FieldProvenance(ProvenanceKind.Base, node.SourceFile, kv.Key, kv.Value, null, kv.Value),
            StringComparer.OrdinalIgnoreCase);
        return new EntityDetail(ToSummary(node), node.Xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting), provenance, fields);
    }

    public SourceNode? GetSourceNode(string internalKey)
        => _nodes.TryGetValue(internalKey, out var n) ? n : null;

    public IReadOnlyList<EntitySummary> Search(string? query, EntityCategory? category = null, int take = 100, string? unitType = null)
    {
        IEnumerable<EntitySummary> q = _all;
        if (category is not null)
            q = q.Where(e => e.Category == category);
        if (!string.IsNullOrWhiteSpace(unitType))
            q = q.Where(e => e.UnitTypes.Any(t => t.Equals(unitType, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(query))
        {
            q = q.Where(e =>
                e.InternalKey.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (e.DisplayNameId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.Pantheon?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || e.UnitTypes.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        return q.Take(take).ToList();
    }

    private static EntitySummary ToSummary(SourceNode node)
        => new(node.InternalKey, node.Category, node.SourceFile, node.DisplayNameId, node.Pantheon, node.UnitTypes, node.IconPath);

    private static Dictionary<string, string?> ExtractFields(SourceNode node)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in node.Xml.Elements())
        {
            if (child.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!child.HasAttributes)
                dict[child.Name.LocalName] = child.Value;
            else
            {
                var attrKey = string.Join(',', child.Attributes().Select(a => $"{a.Name.LocalName}={a.Value}"));
                dict[$"{child.Name.LocalName}[{attrKey}]"] = child.Value;
            }
        }

        return dict;
    }
}
