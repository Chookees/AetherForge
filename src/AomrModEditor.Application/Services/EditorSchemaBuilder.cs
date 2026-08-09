using System.Xml.Linq;
using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;

namespace AomrModEditor.Application.Services;

public sealed record EditorField(
    string Path,
    string Label,
    string ValueKind,
    string? CurrentValue,
    IReadOnlyDictionary<string, string>? MatchAttributes = null,
    string? ElementName = null,
    string? AttributeName = null,
    string DisplayFormat = "raw",
    string Presence = "present",
    string? GroupKey = null,
    IReadOnlyList<string>? Options = null,
    string? PersistHint = null,
    string? GroupKind = null);

public sealed record EditorTab(string Id, string Title, IReadOnlyList<EditorField> Fields);

public sealed record EntityEditorSchema(
    string EntityKey,
    EntityCategory Category,
    IReadOnlyList<string> UnitTypes,
    string? IconPath,
    IReadOnlyList<EditorTab> Tabs);

public sealed record EditorSchemaContext(
    IReadOnlyList<string> PartisanUnitKeys,
    IReadOnlyList<string> DamageBonusTypes);

/// <summary>Derives adaptive editor tabs/fields from entity category + XML (REQ-U01..U12).</summary>
public static class EditorSchemaBuilder
{
    private static readonly string[] CostTypes = ["Gold", "Wood", "Food", "Favor"];
    private static readonly string[] ArmorTypes = ["Hack", "Pierce", "Crush"];
    private static readonly string[] DamageTypes = ["Pierce", "Hack", "Crush"];

    public static EditorSchemaContext BuildContext(IEntityCatalog catalog)
    {
        var partisan = catalog.All
            .Where(e => e.Category == EntityCategory.Unit)
            .Select(e => e.InternalKey)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var damageBonus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var summary in catalog.All)
        {
            if (summary.Category is EntityCategory.ResourceNode or EntityCategory.Technology or EntityCategory.GodPower)
                continue;
            var node = catalog.GetSourceNode(summary.InternalKey);
            if (node is null) continue;
            if (!HasHitpoints(node.Xml)) continue;
            foreach (var ut in node.UnitTypes)
            {
                if (string.IsNullOrWhiteSpace(ut)) continue;
                // Exclude pure resource/farm markers from damage-bonus dropdown (A7)
                if (ut.Equals("AbstractFarm", StringComparison.OrdinalIgnoreCase)
                    || ut.Equals("AbstractMine", StringComparison.OrdinalIgnoreCase))
                    continue;
                damageBonus.Add(ut);
            }
        }

        return new EditorSchemaContext(
            partisan,
            damageBonus.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static EntityEditorSchema Build(
        SourceNode node,
        GodPowerUnifiedView? godPower = null,
        EditorSchemaContext? context = null)
    {
        context ??= new EditorSchemaContext(Array.Empty<string>(), Array.Empty<string>());
        var xml = node.Xml;
        var tabs = new List<EditorTab> { BuildOverview(node) };

        if (node.Category == EntityCategory.GodPower)
        {
            tabs.AddRange(BuildGodPowerTabs(node, godPower));
        }
        else if (node.Category == EntityCategory.ResourceNode)
        {
            if (HasInitialResource(xml))
                tabs.Add(BuildInitialResource(xml));
        }
        else if (node.Category == EntityCategory.Building)
        {
            tabs.Add(BuildBuildingTab(xml, context));
            tabs.Add(BuildBuildingEconomy(xml));
            AddActionTabs(tabs, xml, context);
        }
        else if (IsUnitFamily(node.Category))
        {
            tabs.Add(BuildUnitTab(xml));
            tabs.Add(BuildUnitEconomy(xml));
            AddActionTabs(tabs, xml, context);
            if (HasSpawns(xml))
                tabs.Add(BuildSpawns(xml));
        }
        else
        {
            // Other / Technology: minimal adaptive fallback
            if (HasInitialResource(xml))
                tabs.Add(BuildInitialResource(xml));
        }

        tabs.Add(new EditorTab("rawXml", "Raw XML", Array.Empty<EditorField>()));
        return new EntityEditorSchema(node.InternalKey, node.Category, node.UnitTypes, node.IconPath, tabs);
    }

    private static bool IsUnitFamily(EntityCategory category)
        => category is EntityCategory.Unit or EntityCategory.MythUnit or EntityCategory.Hero or EntityCategory.Worker;

    private static EditorTab BuildOverview(SourceNode node)
    {
        var fields = new List<EditorField>
        {
            new("name", "Id", "string", node.InternalKey),
            new("displaynameid", "Display name id", "string", node.DisplayNameId),
            new("unittypes", "Unit types", "chips", string.Join(", ", node.UnitTypes))
        };
        return new EditorTab("overview", "Overview", fields);
    }

    private static EditorTab BuildUnitTab(XElement xml)
    {
        var fields = new List<EditorField>();
        AddScalarOrAddable(fields, xml, "turnrate", "Turn rate");
        AddScalarOrAddable(fields, xml, "initialhitpoints", "Starting hitpoints");
        AddScalarOrAddable(fields, xml, "maxhitpoints", "Max hitpoints");
        AddScalarOrAddable(fields, xml, "trainpoints", "Train time");
        AddScalarOrAddable(fields, xml, "populationcount", "Population");
        AddScalarOrAddable(fields, xml, "maxvelocity", "Movement speed");
        AddScalarOrAddable(fields, xml, "los", "Line of Sight");
        AddArmorFields(fields, xml);
        return new EditorTab("unit", "Unit", fields);
    }

    private static EditorTab BuildBuildingTab(XElement xml, EditorSchemaContext context)
    {
        var fields = new List<EditorField>();
        AddEnumOrAddable(fields, xml, "partisantype", "Partisan type", context.PartisanUnitKeys);
        AddScalarOrAddable(fields, xml, "partisancount", "Partisan count");
        AddScalarOrAddable(fields, xml, "initialhitpoints", "Starting hitpoints");
        AddScalarOrAddable(fields, xml, "maxhitpoints", "Max hitpoints");
        AddScalarOrAddable(fields, xml, "buildpoints", "Time to build");
        AddScalarOrAddable(fields, xml, "los", "Line of Sight");
        AddArmorFields(fields, xml);
        return new EditorTab("building", "Building", fields);
    }

    private static EditorTab BuildUnitEconomy(XElement xml)
    {
        var fields = new List<EditorField>();
        AddCostFields(fields, xml);
        return new EditorTab("economy", "Economy", fields);
    }

    private static EditorTab BuildBuildingEconomy(XElement xml)
    {
        var fields = new List<EditorField>();
        AddScalarOrAddable(fields, xml, "buildlimit", "Build limit");
        AddCostFields(fields, xml);
        return new EditorTab("economy", "Economy", fields);
    }

    private static void AddActionTabs(List<EditorTab> tabs, XElement xml, EditorSchemaContext context)
    {
        if (!HasActions(xml)) return;
        var attack = BuildAttack(xml, context);
        if (attack.Fields.Count > 0)
            tabs.Add(attack);
        var work = BuildWork(xml);
        if (work.Fields.Count > 0)
            tabs.Add(work);
    }

    private static EditorTab BuildAttack(XElement xml, EditorSchemaContext context)
    {
        var fields = new List<EditorField>();
        foreach (var action in xml.Elements().Where(e => e.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase)))
        {
            var name = action.Element("name")?.Value ?? "action";
            var kind = ProtoActionClassifier.Classify(action);
            if (kind == ProtoActionKind.Utility)
                continue;

            var kindKey = kind.ToString().ToLowerInvariant();
            fields.Add(new EditorField(
                $"protoaction/{name}/name",
                "Name",
                "string",
                name,
                null,
                "name",
                Presence: "present",
                GroupKey: name,
                PersistHint: null,
                GroupKind: kindKey));

            if (kind == ProtoActionKind.Special)
            {
                fields.Add(new EditorField(
                    $"protoaction/{name}/summary",
                    "Special ability",
                    "summary",
                    ProtoActionClassifier.Summarize(action),
                    null,
                    "summary",
                    Presence: "readonly",
                    GroupKey: name,
                    GroupKind: kindKey));
                // Present scalar stats only — never invent damage.
                AddPresentWorkScalars(fields, action, name, kindKey);
                continue;
            }

            AddActionScalar(fields, action, name, "rof", "Rate of Fire", kindKey);

            foreach (var dtype in DamageTypes)
                AddActionDamage(fields, action, name, dtype, kindKey);

            foreach (var dmg in action.Elements().Where(e => e.Name.LocalName.Equals("damage", StringComparison.OrdinalIgnoreCase)))
            {
                var type = dmg.Attribute("type")?.Value;
                if (string.IsNullOrWhiteSpace(type)) continue;
                if (DamageTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) continue;
                fields.Add(MakeActionField(
                    name,
                    "damage",
                    $"Damage {type}",
                    dmg.Value,
                    new Dictionary<string, string> { ["type"] = type },
                    "present",
                    kindKey));
            }

            var existingBonusTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bonus in action.Elements().Where(e => e.Name.LocalName.Equals("damagebonus", StringComparison.OrdinalIgnoreCase)))
            {
                var type = bonus.Attribute("type")?.Value ?? "Bonus";
                existingBonusTypes.Add(type);
                fields.Add(new EditorField(
                    $"protoaction/{name}/damagebonus[type={type}]",
                    $"Damage bonus · {type}",
                    "number",
                    bonus.Value,
                    new Dictionary<string, string> { ["type"] = type },
                    "damagebonus",
                    Presence: "present",
                    GroupKey: name,
                    Options: context.DamageBonusTypes,
                    PersistHint: "replaceactionvalue",
                    GroupKind: kindKey));
            }

            var addableBonusTypes = context.DamageBonusTypes
                .Where(t => !existingBonusTypes.Contains(t))
                .ToList();
            fields.Add(new EditorField(
                $"protoaction/{name}/damagebonus[+]",
                "Add damage bonus",
                "enum",
                null,
                null,
                "damagebonus",
                Presence: "addable",
                GroupKey: name,
                Options: addableBonusTypes,
                PersistHint: "replaceactionvalue",
                GroupKind: kindKey));
            AddActionScalar(fields, action, name, "minrange", "Min range", kindKey, addable: true);
            AddActionScalar(fields, action, name, "maxrange", "Max range", kindKey, addable: true);
        }

        return new EditorTab("attack", "Attack", fields);
    }

    /// <summary>
    /// Work abilities (Build/Gather/Repair/…). Own tab; only already-present values are editable — nothing invented.
    /// </summary>
    private static EditorTab BuildWork(XElement xml)
    {
        var fields = new List<EditorField>();
        foreach (var action in xml.Elements().Where(e => e.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase)))
        {
            var name = action.Element("name")?.Value ?? "action";
            if (ProtoActionClassifier.Classify(action) != ProtoActionKind.Utility)
                continue;

            const string kindKey = "utility";
            fields.Add(new EditorField(
                $"protoaction/{name}/name",
                "Name",
                "string",
                name,
                null,
                "name",
                Presence: "present",
                GroupKey: name,
                GroupKind: kindKey));

            AddPresentWorkScalars(fields, action, name, kindKey);
        }

        return new EditorTab("work", "Work", fields);
    }

    private static void AddPresentWorkScalars(List<EditorField> fields, XElement action, string actionName, string kindKey)
    {
        // Rates already on the action (e.g. Building×1.0, WoodResource×1.0) — edit value only.
        foreach (var rate in action.Elements().Where(e => e.Name.LocalName.Equals("rate", StringComparison.OrdinalIgnoreCase)))
        {
            var type = rate.Attribute("type")?.Value ?? "Rate";
            var match = new Dictionary<string, string> { ["type"] = type };
            var resource = rate.Attribute("resource")?.Value;
            if (!string.IsNullOrWhiteSpace(resource))
                match["resource"] = resource;

            var label = string.IsNullOrWhiteSpace(resource) ? $"Rate · {type}" : $"Rate · {type} ({resource})";
            fields.Add(new EditorField(
                $"protoaction/{actionName}/rate[type={type}" + (resource is null ? "]" : $",resource={resource}]"),
                label,
                "number",
                rate.Value,
                match,
                "rate",
                Presence: "present",
                GroupKey: actionName,
                PersistHint: "replaceactionvalue",
                GroupKind: kindKey));
        }

        // Other present leaf values we already surface in summaries — never invent missing ones.
        foreach (var (element, label) in new (string, string)[]
                 {
                     ("dropsitegathering", "Drop-site gathering"),
                     ("slowhealmultiplier", "Slow heal multiplier"),
                     ("type", "Type"),
                     ("rof", "Rate of Fire"),
                     ("maxrange", "Max range"),
                     ("minrange", "Min range")
                 })
        {
            var el = action.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(element, StringComparison.OrdinalIgnoreCase));
            if (el is null) continue;
            fields.Add(MakeActionField(
                actionName,
                element,
                label,
                el.Value,
                null,
                "present",
                kindKey));
        }
    }

    private static void AddActionScalar(
        List<EditorField> fields,
        XElement action,
        string actionName,
        string element,
        string label,
        string groupKind,
        bool addable = false)
    {
        var el = action.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(element, StringComparison.OrdinalIgnoreCase));
        if (el is null && !addable) return;
        fields.Add(MakeActionField(
            actionName,
            element,
            label,
            el?.Value,
            null,
            el is null ? "addable" : "present",
            groupKind));
    }

    private static void AddActionDamage(
        List<EditorField> fields,
        XElement action,
        string actionName,
        string type,
        string groupKind)
    {
        var el = action.Elements().FirstOrDefault(e =>
            e.Name.LocalName.Equals("damage", StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Attribute("type")?.Value, type, StringComparison.OrdinalIgnoreCase));
        fields.Add(MakeActionField(
            actionName,
            "damage",
            $"Damage {type}",
            el?.Value,
            new Dictionary<string, string> { ["type"] = type },
            el is null ? "addable" : "present",
            groupKind));
    }

    private static EditorField MakeActionField(
        string actionName,
        string element,
        string label,
        string? value,
        IReadOnlyDictionary<string, string>? match,
        string presence,
        string groupKind)
    {
        var path = match is null
            ? $"protoaction/{actionName}/{element}"
            : $"protoaction/{actionName}/{element}[{string.Join(',', match.Select(kv => $"{kv.Key}={kv.Value}"))}]";
        return new EditorField(
            path,
            label,
            "number",
            value,
            match,
            element,
            Presence: presence,
            GroupKey: actionName,
            PersistHint: "replaceactionvalue",
            GroupKind: groupKind);
    }

    private static void AddCostFields(List<EditorField> fields, XElement xml)
    {
        foreach (var rt in CostTypes)
        {
            var cost = xml.Elements().FirstOrDefault(e =>
                e.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Attribute("resourcetype")?.Value, rt, StringComparison.OrdinalIgnoreCase));
            fields.Add(new EditorField(
                $"cost[resourcetype={rt}]",
                rt,
                "number",
                cost?.Value,
                new Dictionary<string, string> { ["resourcetype"] = rt },
                "cost",
                Presence: cost is null ? "addable" : "present",
                PersistHint: "setscalar"));
        }
    }

    private static void AddArmorFields(List<EditorField> fields, XElement xml)
    {
        foreach (var type in ArmorTypes)
        {
            var armor = xml.Elements().FirstOrDefault(e =>
                e.Name.LocalName.Equals("armor", StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Attribute("type")?.Value, type, StringComparison.OrdinalIgnoreCase));
            string? value = null;
            if (armor is not null)
                value = armor.Attribute("value")?.Value ?? (string.IsNullOrWhiteSpace(armor.Value) ? null : armor.Value);

            fields.Add(new EditorField(
                $"armor[type={type}]",
                $"Armor {type}",
                "number",
                value,
                new Dictionary<string, string> { ["type"] = type },
                "armor",
                "value",
                DisplayFormat: "percent",
                Presence: armor is null ? "addable" : "present",
                PersistHint: "setattribute"));
        }
    }

    private static void AddScalarOrAddable(List<EditorField> fields, XElement xml, string elementName, string label)
    {
        var el = xml.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase));
        fields.Add(new EditorField(
            elementName,
            label,
            "number",
            el?.Value,
            null,
            elementName,
            Presence: el is null ? "addable" : "present",
            PersistHint: "setscalar"));
    }

    private static void AddEnumOrAddable(
        List<EditorField> fields,
        XElement xml,
        string elementName,
        string label,
        IReadOnlyList<string> options)
    {
        var el = xml.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase));
        fields.Add(new EditorField(
            elementName,
            label,
            "enum",
            el?.Value,
            null,
            elementName,
            Presence: el is null ? "addable" : "present",
            Options: options,
            PersistHint: "setscalar"));
    }

    private static bool HasActions(XElement xml) =>
        xml.Elements().Any(e => e.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase));

    private static bool HasInitialResource(XElement xml) =>
        HasElement(xml, "initialresource");

    private static bool HasSpawns(XElement xml) =>
        HasElement(xml, "spawn") || HasElement(xml, "spawnnunit") || HasElement(xml, "train");

    private static bool HasHitpoints(XElement xml) =>
        HasElement(xml, "maxhitpoints") || HasElement(xml, "initialhitpoints");

    private static EditorTab BuildInitialResource(XElement xml)
    {
        var fields = new List<EditorField>();
        foreach (var res in xml.Elements().Where(e => e.Name.LocalName.Equals("initialresource", StringComparison.OrdinalIgnoreCase)))
        {
            var rt = res.Attribute("resourcetype")?.Value ?? "Resource";
            fields.Add(new EditorField(
                $"initialresource[resourcetype={rt}]",
                rt,
                "number",
                res.Value,
                new Dictionary<string, string> { ["resourcetype"] = rt },
                "initialresource"));
        }

        return new EditorTab("initialResource", "Initial Resource", fields);
    }

    private static EditorTab BuildSpawns(XElement xml)
    {
        var fields = new List<EditorField>();
        foreach (var name in new[] { "spawn", "spawnnunit", "train" })
        {
            foreach (var el in xml.Elements().Where(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                fields.Add(new EditorField(name, name, "string", el.Value, null, name));
        }

        return new EditorTab("spawns", "Spawns", fields);
    }

    private static IEnumerable<EditorTab> BuildGodPowerTabs(SourceNode node, GodPowerUnifiedView? unified)
    {
        var behaviorFields = new List<EditorField>();
        AddScalarPresentOnly(behaviorFields, node.Xml, "behavior", "Behavior");
        AddScalarPresentOnly(behaviorFields, node.Xml, "damage", "Damage");
        if (behaviorFields.Count > 0)
            yield return new EditorTab("behavior", "Behavior", behaviorFields);

        var powerFields = new List<EditorField>();
        foreach (var name in new[] { "cost", "repeatcost", "activetime", "radius", "builduptime", "powerplayerrelation" })
            AddScalarPresentOnly(powerFields, node.Xml, name, name);
        if (node.Xml.Attribute("type") is { } typeAttr)
            powerFields.Insert(0, new EditorField("@type", "Type", "string", typeAttr.Value));
        if (powerFields.Count > 0)
            yield return new EditorTab("power", "Power", powerFields);

        if (unified is not null)
        {
            var techFields = new List<EditorField>
            {
                new("cooldownTechKey", "Cooldown tech", "string", unified.CooldownTechKey),
                new("cooldown", "Cooldown", "number", unified.Cooldown),
                new("costFavor", "Favor cost", "number", unified.CostFavor)
            };
            yield return new EditorTab("techtree", "Techtree", techFields);
        }
    }

    private static void AddScalarPresentOnly(List<EditorField> fields, XElement xml, string elementName, string label)
    {
        var el = xml.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase));
        if (el is null) return;
        fields.Add(new EditorField(elementName, label, "number", el.Value, null, elementName));
    }

    private static bool HasElement(XElement xml, string localName)
        => xml.Elements().Any(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
}
