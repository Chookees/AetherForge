using AomrModEditor.Application.Services;
using AomrModEditor.Domain;
using AomrModEditor.GameData;
using AomrModEditor.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Tests;

/// <summary>QA deep-depth independent probes (qa-upgrade-001). Not a substitute for UpgradeSchemaTests.</summary>
public class QaDeepVerificationTests
{
    private static string FixtureRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic"));

    private static (InMemoryEntityCatalog Catalog, EditorSchemaContext Context) Load()
    {
        var importer = new GameplayImporter(new SecureXmlLoader());
        var (catalog, _) = importer.ImportDirectory(FixtureRoot);
        return (catalog, EditorSchemaBuilder.BuildContext(catalog));
    }

    [Fact]
    public void Deep_armor_boundaries_and_invalid()
    {
        ArmorPercentConverter.ToFraction("0").Should().Be("0.0000");
        ArmorPercentConverter.ToFraction("100").Should().Be("1.0000");
        // Trailing zeros trimmed for display: 0.0001 * 100 → "0.01"
        ArmorPercentConverter.ToDisplayPercent("0.0001").Should().Be("0.01");
        ArmorPercentConverter.ToDisplayPercent(ArmorPercentConverter.ToFraction("15")).Should().Be("15");
        var act = () => ArmorPercentConverter.ToFraction("not-a-number");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Deep_MythUnit_Worker_Building_ResourceNode_isolation()
    {
        var (catalog, ctx) = Load();

        catalog.GetSourceNode("Minotaur")!.Category.Should().Be(EntityCategory.MythUnit);
        var mino = EditorSchemaBuilder.Build(catalog.GetSourceNode("Minotaur")!, context: ctx);
        mino.Tabs.Select(t => t.Id).Should().Contain(new[] { "overview", "unit", "economy", "attack", "rawXml" });
        mino.Tabs.Should().NotContain(t => t.Id == "initialResource");

        catalog.GetSourceNode("VillagerGreek")!.Category.Should().Be(EntityCategory.Worker);
        var vill = EditorSchemaBuilder.Build(catalog.GetSourceNode("VillagerGreek")!, context: ctx);
        vill.Tabs.Select(t => t.Id).Should().Contain(new[] { "unit", "economy", "attack" });

        var berry = EditorSchemaBuilder.Build(catalog.GetSourceNode("BerryBush")!, context: ctx);
        berry.Tabs.Select(t => t.Id).Should().Equal("overview", "initialResource", "rawXml");
        berry.Tabs.SelectMany(t => t.Fields).Select(f => f.ElementName)
            .Should().NotContain(new[] { "cost", "armor", "damage" });

        var house = EditorSchemaBuilder.Build(catalog.GetSourceNode("House")!, context: ctx);
        house.Tabs.Should().Contain(t => t.Id == "building");
        house.Tabs.Should().NotContain(t => t.Id == "unit");
        var partisan = house.Tabs.Single(t => t.Id == "building").Fields.Single(f => f.Path == "partisantype");
        partisan.Options.Should().Contain("Militia");
        partisan.Options.Should().NotContain("House");
        partisan.Options.Should().NotContain("BerryBush");
    }

    [Fact]
    public void Deep_damagebonus_options_exclude_resource_food_types_and_existing_on_card()
    {
        var (catalog, ctx) = Load();
        var attack = EditorSchemaBuilder.Build(catalog.GetSourceNode("Toxotes")!, context: ctx)
            .Tabs.Single(t => t.Id == "attack");
        var add = attack.Fields.Single(f => f.Path.EndsWith("damagebonus[+]", StringComparison.Ordinal));
        add.Options.Should().NotContain("AbstractInfantry");
        add.Options.Should().NotContain("AbstractFarm");
        add.Options.Should().NotContain("AbstractMine");
    }

    [Fact]
    public async Task Deep_add_missing_cost_and_armor_persist()
    {
        var services = new ServiceCollection();
        services.AddAomrModEditorCore();
        var ws = services.BuildServiceProvider().GetRequiredService<WorkspaceService>();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-qa-deep", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("qa-deep", projectDir, CancellationToken.None);

        await ws.EditScalarAsync(
            "Toxotes", "cost", "7.0000", CancellationToken.None,
            new Dictionary<string, string> { ["resourcetype"] = "Food" });
        var food = ws.GetModifiedNode("Toxotes").Xml.Elements().First(e =>
            e.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase)
            && e.Attribute("resourcetype")?.Value == "Food");
        food.Value.Should().Be("7.0000");

        await ws.EditAttributeAsync(
            "Militia", "armor", "value", "0.3300", CancellationToken.None,
            new Dictionary<string, string> { ["type"] = "Pierce" });
        var armor = ws.GetModifiedNode("Militia").Xml.Elements().First(e =>
            e.Name.LocalName.Equals("armor", StringComparison.OrdinalIgnoreCase)
            && e.Attribute("type")?.Value == "Pierce");
        armor.Attribute("value")!.Value.Should().Be("0.3300");
    }

    [Fact]
    public void Deep_attack_owns_protoaction_fields_not_unit_tab()
    {
        var (catalog, ctx) = Load();
        var schema = EditorSchemaBuilder.Build(catalog.GetSourceNode("Toxotes")!, context: ctx);
        var unit = schema.Tabs.Single(t => t.Id == "unit");
        unit.Fields.Select(f => f.ElementName)
            .Should().NotContain(new[] { "damage", "damagebonus", "rof" });
        var attack = schema.Tabs.Single(t => t.Id == "attack");
        attack.Fields.Should().Contain(f => f.GroupKey == "RangedAttack" && f.ElementName == "rof");
        attack.Fields.Should().Contain(f => f.GroupKey == "RangedAttack" && f.ElementName == "damage");
        attack.Fields.Should().Contain(f => f.GroupKey == "RangedAttack" && f.ElementName == "damagebonus");
    }

    [Fact]
    public void Deep_militia_without_protoaction_has_no_attack_tab()
    {
        var (catalog, ctx) = Load();
        var schema = EditorSchemaBuilder.Build(catalog.GetSourceNode("Militia")!, context: ctx);
        schema.Tabs.Select(t => t.Id).Should().Equal("overview", "unit", "economy", "rawXml");
        schema.Tabs.Should().NotContain(t => t.Id == "attack");
        schema.Tabs.Single(t => t.Id == "unit").Fields
            .Where(f => f.ElementName == "armor").Should().OnlyContain(f => f.Presence == "addable");
    }
}
