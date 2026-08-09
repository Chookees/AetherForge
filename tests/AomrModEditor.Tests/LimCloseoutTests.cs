using AomrModEditor.GameData;
using AomrModEditor.Persistence;
using FluentAssertions;

namespace AomrModEditor.Tests;

public class LimCloseoutTests
{
    private static string ExtractRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "aomr_extract", "original"));

    private static string PackPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "AomrModEditor.App", "Resources", "EmbeddedGameplayPack.json.gz"));

    [Fact]
    public void LIM_R02_embedded_pack_contains_thousands_of_entities_including_god_powers()
    {
        File.Exists(PackPath).Should().BeTrue("pack must be generated before App build");
        using var stream = File.OpenRead(PackPath);
        var doc = GameplayPackBuilder.ReadGzip(stream);
        doc.Entities.Count.Should().BeGreaterThan(2000);
        doc.Entities.Count(e => e.Category.Equals("GodPower", StringComparison.OrdinalIgnoreCase))
            .Should().BeGreaterThan(100);
        doc.Entities.Should().Contain(e => e.Key == "BerryBush");
        doc.Entities.Should().Contain(e => e.Key == "Bolt");
        doc.Entities.Should().Contain(e => e.Key == "VillagerGreek");
    }

    [Fact]
    public void LIM_R02_importer_parses_retail_power_elements()
    {
        Directory.Exists(ExtractRoot).Should().BeTrue();
        var importer = new GameplayImporter(new SecureXmlLoader());
        var (catalog, _) = importer.ImportDirectory(ExtractRoot);
        catalog.All.Count.Should().BeGreaterThan(2000);
        catalog.Search(null, Domain.EntityCategory.GodPower, take: 10_000).Count.Should().BeGreaterThan(100);
        catalog.GetSourceNode("Bolt").Should().NotBeNull();
        catalog.GetSourceNode("BerryBush")!.UnitTypes.Should().NotBeEmpty();
        catalog.GetSourceNode("BerryBush")!.IconPath.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void LIM_R03_icon_paths_are_indexed_on_units_and_powers()
    {
        using var stream = File.OpenRead(PackPath);
        var doc = GameplayPackBuilder.ReadGzip(stream);
        doc.Entities.Count(e => !string.IsNullOrWhiteSpace(e.IconPath)).Should().BeGreaterThan(100);
    }
}
