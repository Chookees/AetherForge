using System.Xml.Linq;

namespace AomrModEditor.Application.Services;

public enum ProtoActionKind
{
    /// <summary>Has combat damage (or equivalent). Full Attack editor.</summary>
    Combat,

    /// <summary>Work/economy ability (Build, Gather, Repair, …). Own Work tab; present values only.</summary>
    Utility,

    /// <summary>Special ability (freeze/chaos/aura/transform). Summary + present scalars; no inventing damage.</summary>
    Special
}

/// <summary>
/// Classifies &lt;protoaction&gt; nodes from Retold proto.xml.
/// Combat ≈ presence of &lt;damage&gt; (or damage-modify combat payloads).
/// Known work actions never become Combat even if similarly named.
/// </summary>
public static class ProtoActionClassifier
{
    private static readonly HashSet<string> UtilityExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "Build", "Repair", "Gather", "GatherBonus", "GatherShrine", "Hunting",
        "Pickup", "DropOff", "RelicNoPickup", "Trade", "Eat", "Drone", "Pagoda", "Trail",
        "Convert", "AutoConvert", "DevoteMajor", "DevoteMinor",
        "Heal", "AreaHeal", "BlessingAreaHeal", "HealUnitArea", "BurstHeal", "ResourceTiedHeal",
        "AutoGather", "AutoGatherFood", "AutoGatherGold", "AutoGatherFavor",
        "MaintainTrain", "MaintainTrainClassical", "MaintainTrainHeroic", "MaintainTrainMythic",
        "FarmGatherAura", "FoodGatherAura", "ShrineGatherRateBoost", "TempleFavorBonus",
        "ResourceInventoryAura", "ResourceInventoryCapAura",
        "CherrySpawn", "WolfSpawn", "DeerSpawn", "ScorpionSpawn", "ChargedSpawn",
        "ReleaseSkyLantern", "AbductDrop"
    };

    private static readonly string[] UtilityPrefixes =
    [
        "AutoGather", "MaintainTrain", "BlessingSlowDown", "LightWind", "ModerateWind", "StrongWind",
        "DelayedTransform", "ConditionalTransform"
    ];

    public static ProtoActionKind Classify(XElement action)
    {
        var name = action.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim() ?? "";

        if (IsUtilityName(name))
            return ProtoActionKind.Utility;

        if (HasDirectDamage(action))
            return ProtoActionKind.Combat;

        // AreaDamage / modifydamagetype combat payloads without a <damage> child.
        if (HasCombatModifyDamage(action))
            return ProtoActionKind.Special;

        if (HasOnHitEffect(action) || LooksLikeAttackName(name))
            return ProtoActionKind.Special;

        // Rate-only / aura / buff leftovers → utility-style info.
        return ProtoActionKind.Utility;
    }

    public static string Summarize(XElement action)
    {
        var parts = new List<string>();
        foreach (var rate in action.Elements().Where(e => e.Name.LocalName.Equals("rate", StringComparison.OrdinalIgnoreCase)))
        {
            var type = rate.Attribute("type")?.Value ?? "?";
            parts.Add($"{type}×{rate.Value}");
        }

        foreach (var dmg in action.Elements().Where(e => e.Name.LocalName.Equals("damage", StringComparison.OrdinalIgnoreCase)))
        {
            var type = dmg.Attribute("type")?.Value ?? "Damage";
            parts.Add($"{type} {dmg.Value}");
        }

        var rof = action.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("rof", StringComparison.OrdinalIgnoreCase));
        if (rof is not null)
            parts.Add($"RoF {rof.Value}");

        var max = action.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("maxrange", StringComparison.OrdinalIgnoreCase));
        if (max is not null)
            parts.Add($"range {max.Value}");

        if (HasOnHitEffect(action))
            parts.Add("on-hit effect");

        if (HasCombatModifyDamage(action))
        {
            var amt = action.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("modifyamount", StringComparison.OrdinalIgnoreCase))?.Value;
            var dtype = action.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("modifydamagetype", StringComparison.OrdinalIgnoreCase))?.Value;
            if (amt is not null || dtype is not null)
                parts.Add($"modify {dtype} {amt}".Trim());
        }

        return parts.Count == 0 ? "No editable combat stats" : string.Join(" · ", parts.Take(6));
    }

    private static bool IsUtilityName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (UtilityExact.Contains(name)) return true;
        return UtilityPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasDirectDamage(XElement action)
        => action.Elements().Any(e => e.Name.LocalName.Equals("damage", StringComparison.OrdinalIgnoreCase));

    private static bool HasOnHitEffect(XElement action)
        => action.Elements().Any(e => e.Name.LocalName.Equals("onhiteffect", StringComparison.OrdinalIgnoreCase));

    private static bool HasCombatModifyDamage(XElement action)
        => action.Elements().Any(e => e.Name.LocalName.Equals("modifydamagetype", StringComparison.OrdinalIgnoreCase)
                                      || e.Name.LocalName.Equals("modifyamount", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeAttackName(string name)
        => name.Contains("Attack", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Strike", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Throw", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Beam", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Gore", StringComparison.OrdinalIgnoreCase);
}
