using System.Text;
using System.Xml.Linq;
using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;

namespace AomrModEditor.ModGeneration;

public sealed class AdditiveModGenerator : IModGenerator
{
    /// <summary>Retold local mod layout: ModName/game/data/gameplay/proto_mods.xml</summary>
    public static readonly string GameplayRelativeDir = Path.Combine("game", "data", "gameplay");

    public GenerationResult Generate(
        IReadOnlyDictionary<string, SourceNode> originals,
        IReadOnlyDictionary<string, SourceNode> modified,
        string stagingDirectory)
    {
        Directory.CreateDirectory(stagingDirectory);
        var gameplayDir = Path.Combine(stagingDirectory, GameplayRelativeDir);
        Directory.CreateDirectory(gameplayDir);

        var issues = new List<ValidationIssue>();
        var strategies = new List<GodPowerStrategyEntry>();
        var protoRoot = new XElement("protomods");
        var techRoot = new XElement("techtreemods");
        var generated = new List<string>();

        foreach (var key in originals.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            if (!modified.TryGetValue(key, out var mod)) continue;
            var orig = originals[key];
            if (XNode.DeepEquals(Normalize(orig.Xml), Normalize(mod.Xml))) continue;

            if (orig.Category == EntityCategory.Technology)
            {
                var unitDiff = BuildTechDiff(orig.Xml, mod.Xml);
                if (unitDiff is not null) techRoot.Add(unitDiff);
            }
            else if (orig.Category == EntityCategory.GodPower)
            {
                var strategy = DecideGodPowerStrategy(orig, mod);
                strategies.Add(strategy);
                if (strategy.Strategy.Equals("Additive", StringComparison.OrdinalIgnoreCase))
                {
                    var fragment = BuildGodPowerAdditiveFragment(orig.Xml, mod.Xml);
                    if (fragment is not null)
                    {
                        var path = Path.Combine(gameplayDir, $"godpower_additive_{SanitizeFilePart(key)}.xml");
                        AtomicWrite(path, ToDeterministicXml(fragment));
                        generated.Add(path);
                        strategies[^1] = strategy with { OutputFile = path };
                    }
                }
                else
                {
                    var path = Path.Combine(gameplayDir, $"godpower_replacement_{SanitizeFilePart(key)}.godpowers");
                    var wrapper = new XElement("GodPowers", new XElement(mod.Xml));
                    AtomicWrite(path, ToDeterministicXml(wrapper));
                    generated.Add(path);
                    strategies[^1] = strategy with { OutputFile = path };
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Info,
                        "GP_REPLACE",
                        $"God power '{key}' emitted as minimal replacement: {strategy.Reason}",
                        key));
                }
            }
            else
            {
                var unitDiff = BuildUnitDiff(orig.Xml, mod.Xml);
                if (unitDiff is not null) protoRoot.Add(unitDiff);
            }
        }

        var protoXml = ToDeterministicXml(protoRoot);
        var techXml = ToDeterministicXml(techRoot);
        var protoPath = Path.Combine(gameplayDir, "proto_mods.xml");
        var techPath = Path.Combine(gameplayDir, "techtree_mods.xml");
        AtomicWrite(protoPath, protoXml);
        AtomicWrite(techPath, techXml);
        generated.Insert(0, protoPath);
        generated.Insert(1, techPath);

        var reportPath = Path.Combine(stagingDirectory, "godpower_strategy_report.json");
        var reportJson = System.Text.Json.JsonSerializer.Serialize(strategies, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        AtomicWrite(reportPath, reportJson);
        generated.Add(reportPath);

        return new GenerationResult(
            protoXml,
            techXml,
            stagingDirectory,
            generated,
            issues,
            strategies);
    }

    private static string SanitizeFilePart(string key)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            key = key.Replace(c, '_');
        return key;
    }

    private static GodPowerStrategyEntry DecideGodPowerStrategy(SourceNode orig, SourceNode mod)
    {
        var origShape = ShapeSignature(orig.Xml);
        var modShape = ShapeSignature(mod.Xml);
        if (origShape == modShape)
        {
            return new GodPowerStrategyEntry(
                orig.InternalKey,
                "Additive",
                "Structure unchanged; leaf value overrides only.",
                null);
        }

        return new GodPowerStrategyEntry(
            orig.InternalKey,
            "Replacement",
            "Structural god-power change detected; emitting minimal replacement file with explicit report.",
            null);
    }

    private static string ShapeSignature(XElement root)
    {
        var sb = new StringBuilder();
        void Walk(XElement e, string path)
        {
            var attrs = string.Join(',', e.Attributes().Select(a => a.Name.LocalName).OrderBy(n => n));
            sb.Append(path).Append('#').Append(e.Name.LocalName).Append('[').Append(attrs).Append(']');
            var i = 0;
            foreach (var child in e.Elements())
                Walk(child, path + "/" + e.Name.LocalName + i++);
        }
        Walk(root, "");
        return sb.ToString();
    }

    private static XElement? BuildGodPowerAdditiveFragment(XElement original, XElement modified)
    {
        var name = modified.Attribute("name")?.Value ?? original.Attribute("name")?.Value ?? "GodPower";
        var root = new XElement("godpowermods", new XElement("GodPower", new XAttribute("name", name)));
        var gp = root.Element("GodPower")!;
        var changed = false;
        foreach (var child in modified.Elements())
        {
            var origChild = original.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(child.Name.LocalName, StringComparison.OrdinalIgnoreCase)
                                     && AttributesEqualIgnoringMergeMode(e, child));
            if (origChild is null || !XNode.DeepEquals(Normalize(origChild), Normalize(child)))
            {
                gp.Add(WithMergeReplace(child));
                changed = true;
            }
        }

        return changed ? root : null;
    }

    private static XElement? BuildUnitDiff(XElement original, XElement modified)
    {
        var name = modified.Attribute("name")?.Value ?? original.Attribute("name")?.Value
                   ?? throw new InvalidOperationException("Unit missing name.");
        var unit = new XElement("unit");
        // Keep identifying attributes (name + e.g. spatialmapped) like retail local mods.
        foreach (var attr in modified.Attributes())
            unit.SetAttributeValue(attr.Name, attr.Value);
        if (unit.Attribute("name") is null)
            unit.SetAttributeValue("name", name);

        var changed = false;
        var used = new HashSet<XElement>();

        foreach (var child in modified.Elements())
        {
            if (child.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase))
            {
                var actionName = child.Element("name")?.Value;
                var origAction = original.Elements()
                    .FirstOrDefault(e => !used.Contains(e)
                                         && e.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(e.Element("name")?.Value, actionName, StringComparison.OrdinalIgnoreCase));
                if (origAction is not null) used.Add(origAction);
                if (origAction is null || !XNode.DeepEquals(Normalize(origAction), Normalize(child)))
                {
                    unit.Add(BuildProtoActionDiff(origAction, child));
                    changed = true;
                }

                continue;
            }

            var origChild = FindMatching(original, child, used);
            if (origChild is not null) used.Add(origChild);
            if (origChild is null || !XNode.DeepEquals(Normalize(origChild), Normalize(child)))
            {
                unit.Add(WithMergeReplace(child));
                changed = true;
            }
        }

        return changed ? unit : null;
    }

    /// <summary>
    /// Emit only changed protoaction leaves with mergeMode="replace", matching additive mod style.
    /// Falls back to full action with mergeMode when structure diverges.
    /// </summary>
    private static XElement BuildProtoActionDiff(XElement? original, XElement modified)
    {
        if (original is null || !SameProtoActionShape(original, modified))
            return WithMergeReplace(modified);

        var action = new XElement("protoaction");
        var name = modified.Element("name")?.Value;
        if (!string.IsNullOrWhiteSpace(name))
            action.Add(new XElement("name", name));

        var changed = false;
        var used = new HashSet<XElement>();
        foreach (var child in modified.Elements())
        {
            if (child.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))
                continue;

            var origChild = FindMatching(original, child, used);
            if (origChild is not null) used.Add(origChild);
            if (origChild is null || !XNode.DeepEquals(Normalize(origChild), Normalize(child)))
            {
                action.Add(WithMergeReplace(child));
                changed = true;
            }
        }

        return changed ? action : WithMergeReplace(modified);
    }

    private static bool SameProtoActionShape(XElement a, XElement b)
        => ShapeSignature(a) == ShapeSignature(b);

    private static XElement? BuildTechDiff(XElement original, XElement modified)
    {
        var name = modified.Attribute("name")?.Value ?? original.Attribute("name")?.Value
                   ?? throw new InvalidOperationException("Tech missing name.");
        if (XNode.DeepEquals(Normalize(original), Normalize(modified))) return null;
        var tech = new XElement("tech", new XAttribute("name", name));
        var used = new HashSet<XElement>();
        foreach (var child in modified.Elements())
        {
            var origChild = FindMatching(original, child, used);
            if (origChild is not null) used.Add(origChild);
            if (origChild is null || !XNode.DeepEquals(Normalize(origChild), Normalize(child)))
                tech.Add(WithMergeReplace(child));
        }

        return tech.HasElements ? tech : null;
    }

    private static XElement WithMergeReplace(XElement source)
    {
        var clone = new XElement(source);
        clone.SetAttributeValue("mergeMode", "replace");
        return clone;
    }

    private static XElement? FindMatching(XElement parent, XElement candidate, HashSet<XElement> used)
    {
        foreach (var e in parent.Elements())
        {
            if (used.Contains(e)) continue;
            if (!e.Name.LocalName.Equals(candidate.Name.LocalName, StringComparison.OrdinalIgnoreCase)) continue;
            if (XNode.DeepEquals(Normalize(e), Normalize(candidate)))
                return e;
        }

        return parent.Elements().FirstOrDefault(e =>
            !used.Contains(e)
            && e.Name.LocalName.Equals(candidate.Name.LocalName, StringComparison.OrdinalIgnoreCase)
            && AttributesEqualIgnoringMergeMode(e, candidate)
            && (!candidate.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Element("name")?.Value, candidate.Element("name")?.Value, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool AttributesEqualIgnoringMergeMode(XElement a, XElement b)
    {
        static IEnumerable<XAttribute> Significant(XElement e) =>
            e.Attributes().Where(x => !x.Name.LocalName.Equals("mergeMode", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Name.LocalName);

        var aa = Significant(a).ToList();
        var bb = Significant(b).ToList();
        if (aa.Count != bb.Count) return false;
        for (var i = 0; i < aa.Count; i++)
        {
            if (!string.Equals(aa[i].Name.LocalName, bb[i].Name.LocalName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(aa[i].Value, bb[i].Value, StringComparison.Ordinal)) return false;
        }

        return true;
    }

    private static XElement Normalize(XElement e) => XElement.Parse(e.ToString(SaveOptions.DisableFormatting));

    private static string ToDeterministicXml(XElement root)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""");
        sb.Append(root.ToString(SaveOptions.None));
        sb.AppendLine();
        return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, path, overwrite: true);
    }
}
