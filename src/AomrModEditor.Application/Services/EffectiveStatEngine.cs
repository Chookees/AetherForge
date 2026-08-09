using System.Globalization;
using System.Xml.Linq;
using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;

namespace AomrModEditor.Application.Services;

public sealed class EffectiveStatEngine : IEffectiveStatEngine
{
    public EffectiveStatsResult Compute(SourceNode unit, IEnumerable<SourceNode>? technologies = null)
    {
        var steps = new List<EffectiveStatStep>();
        var effective = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var child in unit.Xml.Elements())
        {
            if (child.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase))
                continue;
            if (child.HasElements && !child.HasAttributes)
            {
                warnings.Add($"Unsupported nested effect container '{child.Name.LocalName}' on {unit.InternalKey}.");
                steps.Add(new EffectiveStatStep("unsupported", child.Name.LocalName, null, unit.SourceFile, true, warnings[^1]));
                continue;
            }

            var key = FieldKey(child);
            effective[key] = child.Value;
            steps.Add(new EffectiveStatStep("base", key, child.Value, unit.SourceFile));
        }

        foreach (var tech in technologies ?? Array.Empty<SourceNode>())
        {
            foreach (var effect in tech.Xml.Elements().Where(e =>
                         e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)
                         || e.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase)))
            {
                if (effect.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var nested in effect.Elements())
                        ApplyTechEffect(nested, tech, effective, steps, warnings);
                }
                else
                {
                    ApplyTechEffect(effect, tech, effective, steps, warnings);
                }
            }
        }

        return new EffectiveStatsResult(unit.InternalKey, effective, steps, warnings);
    }

    private static void ApplyTechEffect(
        XElement effect,
        SourceNode tech,
        Dictionary<string, string?> effective,
        List<EffectiveStatStep> steps,
        List<string> warnings)
    {
        var target = effect.Attribute("type")?.Value ?? effect.Attribute("stat")?.Value ?? effect.Name.LocalName;
        var mode = effect.Attribute("relativity")?.Value ?? effect.Attribute("mode")?.Value ?? "absolute";
        var raw = effect.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            warnings.Add($"Unsupported empty effect on tech {tech.InternalKey}.");
            steps.Add(new EffectiveStatStep("unsupported", target, null, tech.SourceFile, true, warnings[^1]));
            return;
        }

        if (!effective.TryGetValue(target, out var current) || current is null)
        {
            if (string.Equals(mode, "absolute", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "assign", StringComparison.OrdinalIgnoreCase))
            {
                effective[target] = raw;
                steps.Add(new EffectiveStatStep("tech-assign", target, raw, tech.InternalKey));
                return;
            }

            warnings.Add($"Unsupported relative effect '{target}' from {tech.InternalKey}: base missing.");
            steps.Add(new EffectiveStatStep("unsupported", target, raw, tech.InternalKey, true, warnings[^1]));
            return;
        }

        if (double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseVal)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
        {
            double next = mode.ToLowerInvariant() switch
            {
                "percent" or "pct" => baseVal * (1.0 + delta),
                "add" or "relative" => baseVal + delta,
                _ => delta
            };
            var formatted = next.ToString("0.0000", CultureInfo.InvariantCulture);
            effective[target] = formatted;
            steps.Add(new EffectiveStatStep($"tech-{mode}", target, formatted, tech.InternalKey));
        }
        else
        {
            effective[target] = raw;
            steps.Add(new EffectiveStatStep("tech-assign", target, raw, tech.InternalKey));
        }
    }

    private static string FieldKey(XElement child)
    {
        if (!child.HasAttributes) return child.Name.LocalName;
        var attrKey = string.Join(',', child.Attributes().Select(a => $"{a.Name.LocalName}={a.Value}"));
        return $"{child.Name.LocalName}[{attrKey}]";
    }
}
