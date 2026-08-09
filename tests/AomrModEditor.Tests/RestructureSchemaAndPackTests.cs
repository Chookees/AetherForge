using AomrModEditor.Application.Services;
using AomrModEditor.Domain;
using AomrModEditor.GameData;
using AomrModEditor.Persistence;
using FluentAssertions;
using System.Xml.Linq;

namespace AomrModEditor.Tests;

public class RestructureSchemaAndPackTests
{
    private static string FixtureRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "synthetic"));

    [Fact]
    public void BerryBush_schema_has_overview_initial_resource_raw_only()
    {
        var importer = new GameplayImporter(new SecureXmlLoader());
        var (catalog, _) = importer.ImportDirectory(FixtureRoot);
        var node = catalog.GetSourceNode("BerryBush")!;
        var schema = EditorSchemaBuilder.Build(node);

        schema.Tabs.Select(t => t.Id).Should().Equal("overview", "initialResource", "rawXml");
        schema.Tabs.Select(t => t.Id).Should().NotContain("unit");
        schema.Tabs.Select(t => t.Id).Should().NotContain("economy");
        schema.Tabs.Select(t => t.Id).Should().NotContain("attack");
        schema.Tabs.Select(t => t.Id).Should().NotContain("combat");
        schema.Tabs.Select(t => t.Id).Should().NotContain("actions");
        var food = schema.Tabs.Single(t => t.Id == "initialResource").Fields.Single();
        food.Label.Should().Be("Food");
        food.CurrentValue.Should().Be("150.0000");
    }

    [Fact]
    public void VillagerGreek_schema_includes_unit_economy_attack()
    {
        var importer = new GameplayImporter(new SecureXmlLoader());
        var (catalog, _) = importer.ImportDirectory(FixtureRoot);
        var node = catalog.GetSourceNode("VillagerGreek")!;
        var schema = EditorSchemaBuilder.Build(node, context: EditorSchemaBuilder.BuildContext(catalog));

        schema.Tabs.Select(t => t.Id).Should().Contain(new[] { "overview", "unit", "economy", "attack", "rawXml" });
        schema.Tabs.Select(t => t.Id).Should().NotContain("initialResource");
        schema.Tabs.Select(t => t.Id).Should().NotContain("combat");
        schema.Tabs.Select(t => t.Id).Should().NotContain("actions");
    }

    [Fact]
    public void Gameplay_pack_roundtrip_preserves_all_fixture_entities()
    {
        var importer = new GameplayImporter(new SecureXmlLoader());
        var doc = GameplayPackBuilder.FromImporter(importer, FixtureRoot);
        var tmp = Path.Combine(Path.GetTempPath(), "aomr-pack-" + Guid.NewGuid().ToString("N") + ".json.gz");
        GameplayPackBuilder.WriteGzip(doc, tmp);
        try
        {
            using var stream = File.OpenRead(tmp);
            var (catalog, _) = GameplayPackBuilder.ToCatalog(GameplayPackBuilder.ReadGzip(stream));
            catalog.All.Should().HaveCount(doc.Entities.Count);
            catalog.GetSourceNode("BerryBush").Should().NotBeNull();
            catalog.GetSourceNode("Bolt").Should().NotBeNull();
            catalog.Search(null, take: 10_000).Should().HaveCount(doc.Entities.Count);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Search_filters_by_unittype_and_empty_query_returns_all()
    {
        var importer = new GameplayImporter(new SecureXmlLoader());
        var (memory, _) = importer.ImportDirectory(FixtureRoot);
        var catalog = SqliteFtsEntityCatalog.From(memory);
        catalog.Search(null, take: 10_000).Should().HaveCount(catalog.All.Count);
        catalog.Search("Berry", unitType: "AbstractFarm", take: 10_000)
            .Should().ContainSingle(e => e.InternalKey == "BerryBush");
    }
}
