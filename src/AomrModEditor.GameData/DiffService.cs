using System.Xml.Linq;
using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;

namespace AomrModEditor.GameData;

public sealed class DiffService : IDiffService
{
    public DiffResult Diff(SourceNode original, SourceNode modified)
    {
        var semantic = new List<SemanticDiffEntry>();
        CompareElements(original.InternalKey, "", original.Xml, modified.Xml, semantic);
        return new DiffResult(
            semantic,
            original.Xml.ToString(SaveOptions.None),
            modified.Xml.ToString(SaveOptions.None));
    }

    private static void CompareElements(string entity, string path, XElement a, XElement b, List<SemanticDiffEntry> sink)
    {
        var aChildren = a.Elements().ToList();
        var bChildren = b.Elements().ToList();
        var keys = aChildren.Select(KeyOf).Concat(bChildren.Select(KeyOf)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            var ac = aChildren.FirstOrDefault(c => KeyOf(c).Equals(key, StringComparison.OrdinalIgnoreCase));
            var bc = bChildren.FirstOrDefault(c => KeyOf(c).Equals(key, StringComparison.OrdinalIgnoreCase));
            var childPath = string.IsNullOrEmpty(path) ? key : $"{path}/{key}";
            if (ac is null && bc is not null)
            {
                sink.Add(new SemanticDiffEntry(entity, childPath, null,
                    bc.HasElements ? bc.ToString(SaveOptions.DisableFormatting) : bc.Value));
            }
            else if (ac is not null && bc is null)
            {
                sink.Add(new SemanticDiffEntry(entity, childPath,
                    ac.HasElements ? ac.ToString(SaveOptions.DisableFormatting) : ac.Value, null));
            }
            else if (ac is not null && bc is not null)
            {
                if (ac.HasElements || bc.HasElements)
                    CompareElements(entity, childPath, ac, bc, sink);
                else if (!string.Equals(ac.Value, bc.Value, StringComparison.Ordinal))
                    sink.Add(new SemanticDiffEntry(entity, childPath, ac.Value, bc.Value));
            }
        }
    }

    private static string KeyOf(XElement e)
    {
        if (e.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase))
            return $"protoaction[name={e.Element("name")?.Value}]";
        if (e.HasAttributes)
        {
            var attrs = string.Join(',', e.Attributes().Select(a => $"{a.Name.LocalName}={a.Value}"));
            return $"{e.Name.LocalName}[{attrs}]";
        }

        return e.Name.LocalName;
    }
}
