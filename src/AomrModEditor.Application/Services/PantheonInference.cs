using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AomrModEditor.Application.Services;

/// <summary>A1 exclusive pantheon inference for Entity Browser tags.</summary>
public static class PantheonInference
{
    private static readonly string[] Cultures =
        ["greek", "egyptian", "norse", "atlantean", "chinese", "japanese", "aztec"];

    private static readonly Regex CulturePath = new(
        @"\b(greek|egyptian|norse|atlantean|chinese|japanese|aztec)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? InferExclusive(XElement xml, string? existingPantheon)
    {
        if (!string.IsNullOrWhiteSpace(existingPantheon))
            return NormalizeDisplay(existingPantheon);

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var anim in xml.Elements().Where(e => e.Name.LocalName.Equals("animfile", StringComparison.OrdinalIgnoreCase)))
        {
            var culture = anim.Attribute("culture")?.Value;
            if (!string.IsNullOrWhiteSpace(culture))
                found.Add(culture.Trim());
            CollectFromPath(anim.Value, found);
        }

        foreach (var icon in xml.Elements().Where(e => e.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase)))
        {
            var culture = icon.Attribute("culture")?.Value;
            if (!string.IsNullOrWhiteSpace(culture))
                found.Add(culture.Trim());
            CollectFromPath(icon.Value, found);
        }

        if (found.Count == 1)
            return NormalizeDisplay(found.First());
        return null;
    }

    private static void CollectFromPath(string? path, HashSet<string> found)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        foreach (Match m in CulturePath.Matches(path))
            found.Add(m.Value);
    }

    private static string NormalizeDisplay(string raw)
    {
        var lower = raw.Trim().ToLowerInvariant();
        var match = Cultures.FirstOrDefault(c => c.Equals(lower, StringComparison.OrdinalIgnoreCase));
        if (match is null) return char.ToUpperInvariant(raw[0]) + raw[1..].ToLowerInvariant();
        return char.ToUpperInvariant(match[0]) + match[1..];
    }
}
