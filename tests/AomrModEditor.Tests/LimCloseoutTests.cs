using AomrModEditor.GameData;
using AomrModEditor.Persistence;
using FluentAssertions;
using Xunit;

namespace AomrModEditor.Tests;

public class LimCloseoutTests
{
    private static string ExtractRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "aomr_extract", "original"));

    private static string RetailProtoPath =>
        Path.Combine(ExtractRoot, "gameplay", "proto.xml");

    private static string PackPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "AomrModEditor.App", "Resources", "EmbeddedGameplayPack.json.gz"));

    private static bool RetailExtractPresent => File.Exists(RetailProtoPath);

    [SkippableFact]
    public void LIM_R02_embedded_pack_contains_thousands_of_entities_including_god_powers()
    {
        Skip.IfNot(RetailExtractPresent,
            "Retail extract (aomr_extract/original/gameplay/proto.xml) not present. Fixture packs from App CI builds are not the closeout target.");
        Skip.IfNot(File.Exists(PackPath),
            "EmbeddedGameplayPack.json.gz not present (gitignored / no retail pack in CI). Generate locally before App build to run this assertion.");
        using var stream = File.OpenRead(PackPath);
        var doc = GameplayPackBuilder.ReadGzip(stream);
        doc.Entities.Count.Should().BeGreaterThan(2000);
        doc.Entities.Count(e => e.Category.Equals("GodPower", StringComparison.OrdinalIgnoreCase))
            .Should().BeGreaterThan(100);
        doc.Entities.Should().Contain(e => e.Key == "BerryBush");
        doc.Entities.Should().Contain(e => e.Key == "Bolt");
        doc.Entities.Should().Contain(e => e.Key == "VillagerGreek");
    }

    [SkippableFact]
    public void LIM_R02_importer_parses_retail_power_elements()
    {
        Skip.IfNot(RetailExtractPresent,
            "aomr_extract/original/gameplay/proto.xml not present (retail extract is not committed). Run importer locally against a retail extract to assert this.");
        var importer = new GameplayImporter(new SecureXmlLoader());
        var (catalog, _) = importer.ImportDirectory(ExtractRoot);
        catalog.All.Count.Should().BeGreaterThan(2000);
        catalog.Search(null, Domain.EntityCategory.GodPower, take: 10_000).Count.Should().BeGreaterThan(100);
        catalog.GetSourceNode("Bolt").Should().NotBeNull();
        catalog.GetSourceNode("BerryBush")!.UnitTypes.Should().NotBeEmpty();
        catalog.GetSourceNode("BerryBush")!.IconPath.Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public void LIM_R03_icon_paths_are_indexed_on_units_and_powers()
    {
        Skip.IfNot(RetailExtractPresent,
            "Retail extract (aomr_extract/original/gameplay/proto.xml) not present. Fixture packs from App CI builds are not the closeout target.");
        Skip.IfNot(File.Exists(PackPath),
            "EmbeddedGameplayPack.json.gz not present (gitignored / no retail pack in CI). Generate locally before App build to run this assertion.");
        using var stream = File.OpenRead(PackPath);
        var doc = GameplayPackBuilder.ReadGzip(stream);
        doc.Entities.Count(e => !string.IsNullOrWhiteSpace(e.IconPath)).Should().BeGreaterThan(100);
    }
}
