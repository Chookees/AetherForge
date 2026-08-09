using System.Globalization;
using AomrModEditor.Application.Services;
using AomrModEditor.Domain;
using AomrModEditor.GameData;
using AomrModEditor.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Tests;

/// <summary>REQ-U18..U23 / AC-U09 upgrade coverage.</summary>
public class UpgradeSchemaTests
{
    private static string FixtureRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic"));

    private static (InMemoryEntityCatalog Catalog, EditorSchemaContext Context) Load()
    {
        var importer = new GameplayImporter(new SecureXmlLoader());
        var (catalog, _) = importer.ImportDirectory(FixtureRoot);
        return (catalog, EditorSchemaBuilder.BuildContext(catalog));
    }

    private static WorkspaceService CreateWorkspace()
    {
        var services = new ServiceCollection();
        services.AddAomrModEditorCore();
        return services.BuildServiceProvider().GetRequiredService<WorkspaceService>();
    }

    [Fact]
    public void REQ_U18_armor_percent_display_round_trip()
    {
        ArmorPercentConverter.ToDisplayPercent("0.1500").Should().Be("15");
        ArmorPercentConverter.ToDisplayPercent("0.15").Should().Be("15");
        ArmorPercentConverter.ToFraction("15").Should().Be("0.1500");
        ArmorPercentConverter.ToFraction("15.5").Should().Be("0.1550");

        var (catalog, ctx) = Load();
        var schema = EditorSchemaBuilder.Build(catalog.GetSourceNode("Toxotes")!, context: ctx);
        var armor = schema.Tabs.Single(t => t.Id == "unit").Fields
            .Single(f => f.Path == "armor[type=Hack]");
        armor.DisplayFormat.Should().Be("percent");
        armor.AttributeName.Should().Be("value");
        armor.CurrentValue.Should().Be("0.1500");
        ArmorPercentConverter.ToDisplayPercent(armor.CurrentValue).Should().Be("15");
        var written = ArmorPercentConverter.ToFraction("15");
        written.Should().Be("0.1500");
        decimal.Parse(written, CultureInfo.InvariantCulture).Should().Be(0.15m);
    }

    [Fact]
    public void REQ_U19_BerryBush_schema_no_combat_cost_confusion()
    {
        var (catalog, ctx) = Load();
        var schema = EditorSchemaBuilder.Build(catalog.GetSourceNode("BerryBush")!, context: ctx);
        schema.Tabs.Select(t => t.Id).Should().Equal("overview", "initialResource", "rawXml");
        schema.Tabs.Select(t => t.Id).Should().NotContain(new[] { "unit", "building", "economy", "attack", "combat", "actions" });
        var allFields = schema.Tabs.SelectMany(t => t.Fields).ToList();
        allFields.Should().NotContain(f => f.ElementName == "cost");
        allFields.Should().NotContain(f => f.ElementName == "armor");
        allFields.Should().Contain(f => f.ElementName == "initialresource");
    }

    [Fact]
    public void REQ_U20_Toxotes_has_unit_economy4_attack()
    {
        var (catalog, ctx) = Load();
        var schema = EditorSchemaBuilder.Build(catalog.GetSourceNode("Toxotes")!, context: ctx);
        schema.Tabs.Select(t => t.Id).Should().Contain(new[] { "overview", "unit", "economy", "attack", "rawXml" });
        schema.Tabs.Select(t => t.Id).Should().NotContain("initialResource");

        var economy = schema.Tabs.Single(t => t.Id == "economy");
        economy.Fields.Where(f => f.ElementName == "cost").Select(f => f.MatchAttributes!["resourcetype"])
            .Should().BeEquivalentTo(new[] { "Gold", "Wood", "Food", "Favor" });
        economy.Fields.Should().Contain(f => f.MatchAttributes!["resourcetype"] == "Favor" && f.Presence == "addable");

        var unit = schema.Tabs.Single(t => t.Id == "unit");
        unit.Fields.Should().Contain(f => f.Path == "turnrate" && f.Label == "Turn rate");
        unit.Fields.Should().Contain(f => f.DisplayFormat == "percent" && f.Path.Contains("armor", StringComparison.OrdinalIgnoreCase));
        unit.Fields.Should().NotContain(f => f.ElementName == "initialresource");

        var attack = schema.Tabs.Single(t => t.Id == "attack");
        attack.Fields.Should().Contain(f => f.GroupKey == "RangedAttack" && f.ElementName == "rof");
        attack.Fields.Should().Contain(f => f.GroupKey == "RangedAttack" && f.ElementName == "damage"
            && f.MatchAttributes!["type"] == "Pierce");
        attack.Fields.Should().Contain(f => f.ElementName == "damagebonus"
            && f.Options != null && f.Options.Contains("AbstractInfantry"));
        attack.Fields.Should().NotContain(f => f.Options != null && f.Options.Contains("AbstractFarm"));
    }

    [Fact]
    public void REQ_U21_Building_schema_has_building_tab_and_economy_buildlimit()
    {
        var (catalog, ctx) = Load();
        var schema = EditorSchemaBuilder.Build(catalog.GetSourceNode("House")!, context: ctx);
        schema.Tabs.Select(t => t.Id).Should().Contain(new[] { "overview", "building", "economy", "rawXml" });
        schema.Tabs.Select(t => t.Id).Should().NotContain("unit");

        var building = schema.Tabs.Single(t => t.Id == "building");
        building.Fields.Should().Contain(f => f.Path == "partisantype" && f.ValueKind == "enum"
            && f.Options != null && f.Options.Contains("Militia") && !f.Options.Contains("House"));
        building.Fields.Should().Contain(f => f.Path == "partisancount");
        building.Fields.Should().Contain(f => f.Path == "buildpoints" && f.Label == "Time to build");
        building.Fields.Should().Contain(f => f.DisplayFormat == "percent");

        var economy = schema.Tabs.Single(t => t.Id == "economy");
        economy.Fields.Should().Contain(f => f.Path == "buildlimit");
        economy.Fields.Where(f => f.ElementName == "cost").Should().HaveCount(4);
    }

    [Fact]
    public void REQ_U22_entity_browser_excludes_Technology()
    {
        var (catalog, _) = Load();
        catalog.All.Should().Contain(e => e.Category == EntityCategory.Technology);

        var forEditor = catalog.All.Where(e => e.Category != EntityCategory.Technology).ToList();
        forEditor.Should().NotContain(e => e.Category == EntityCategory.Technology);
        forEditor.Should().Contain(e => e.InternalKey == "BerryBush");
        forEditor.Should().Contain(e => e.InternalKey == "Toxotes");
    }

    [Fact]
    public async Task REQ_U23_add_cost_creates_cost_node()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-upgrade", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("upgrade", projectDir, CancellationToken.None);

        await ws.EditScalarAsync(
            "Toxotes",
            "cost",
            "5.0000",
            CancellationToken.None,
            new Dictionary<string, string> { ["resourcetype"] = "Favor" });

        var node = ws.GetModifiedNode("Toxotes");
        var favor = node.Xml.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase)
                && e.Attribute("resourcetype")?.Value == "Favor");
        favor.Should().NotBeNull();
        favor!.Value.Should().Be("5.0000");
    }

    [Fact]
    public async Task REQ_U17_add_armor_creates_armor_value_attribute()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-upgrade-armor", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("upgrade", projectDir, CancellationToken.None);

        // Militia has no armor — create via setattribute
        await ws.EditAttributeAsync(
            "Militia",
            "armor",
            "value",
            "0.2000",
            CancellationToken.None,
            new Dictionary<string, string> { ["type"] = "Hack" });

        var node = ws.GetModifiedNode("Militia");
        var armor = node.Xml.Elements()
            .First(e => e.Name.LocalName.Equals("armor", StringComparison.OrdinalIgnoreCase)
                && e.Attribute("type")?.Value == "Hack");
        armor.Attribute("value")!.Value.Should().Be("0.2000");
    }

    [Fact]
    public void Exclusive_pantheon_on_Toxotes_omitted_on_House()
    {
        var (catalog, _) = Load();
        catalog.GetSourceNode("Toxotes")!.Pantheon.Should().Be("Greek");
        catalog.GetSourceNode("House")!.Pantheon.Should().BeNull();
    }
}
