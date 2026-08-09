using AomrModEditor.Domain;

namespace AomrModEditor.GameData;

/// <summary>
/// Retail extracts can reuse the same name across proto units and techtree techs (e.g. Oracle, Maelstrom).
/// Disambiguate colliding keys as Category/Name so all entities remain addressable.
/// </summary>
public static class EntityKeyDeduper
{
    public static IReadOnlyList<SourceNode> Deduplicate(IEnumerable<SourceNode> nodes)
    {
        var result = new List<SourceNode>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            var key = node.InternalKey;
            if (used.Contains(key))
                key = $"{node.Category}/{node.InternalKey}";
            var unique = key;
            var n = 2;
            while (used.Contains(unique))
                unique = $"{key}#{n++}";
            used.Add(unique);
            if (!string.Equals(unique, node.InternalKey, StringComparison.Ordinal))
            {
                result.Add(new SourceNode
                {
                    InternalKey = unique,
                    Category = node.Category,
                    SourceFile = node.SourceFile,
                    Xml = node.Xml,
                    DisplayNameId = node.DisplayNameId,
                    Pantheon = node.Pantheon,
                    DlcTag = node.DlcTag,
                    UnitTypes = node.UnitTypes,
                    IconPath = node.IconPath
                });
            }
            else
            {
                result.Add(node);
            }
        }

        return result;
    }
}
