using AomrModEditor.Application.Services;
using AomrModEditor.GameData;
using AomrModEditor.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Tests;

public class VerticalSliceTests
{
    private static string FixtureRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic"));

    private static WorkspaceService CreateWorkspace()
    {
        var services = new ServiceCollection();
        services.AddAomrModEditorCore();
        return services.BuildServiceProvider().GetRequiredService<WorkspaceService>();
    }

    [Fact]
    public async Task Import_synthetic_proto_indexes_VillagerGreek()
    {
        var ws = CreateWorkspace();
        var snap = await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        snap.EntityCount.Should().BeGreaterThan(0);
        var villager = ws.Catalog.Search("VillagerGreek").Should().ContainSingle().Subject;
        villager.InternalKey.Should().Be("VillagerGreek");
        var detail = ws.Catalog.GetByInternalKey("VillagerGreek");
        detail!.NormalizedFields["maxhitpoints"].Should().Be("55.0000");
    }

    [Fact]
    public async Task Edit_VillagerGreek_HP_preserves_unknown_fields_and_generates_proto_mods()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-tests", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("test", projectDir, CancellationToken.None);

        await ws.EditMaxHitpointsAsync("VillagerGreek", "100.0000", CancellationToken.None);

        var modified = ws.GetModifiedNode("VillagerGreek");
        modified.Xml.Elements().Any(e => e.Name.LocalName == "unknown_future_field" && e.Value == "keep-me")
            .Should().BeTrue();
        modified.Xml.Elements().Count(e => e.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase))
            .Should().Be(7);

        var diff = ws.DiffEntity("VillagerGreek");
        diff.Semantic.Should().Contain(s => s.Path.Contains("maxhitpoints", StringComparison.OrdinalIgnoreCase)
                                            && s.NewValue == "100.0000");

        var gen = ws.Generate();
        gen.ProtoModsXml.Should().Contain("<protomods>");
        gen.ProtoModsXml.Should().Contain("name=\"VillagerGreek\"");
        gen.ProtoModsXml.Should().Contain("mergeMode=\"replace\"");
        gen.ProtoModsXml.Should().Contain("<maxhitpoints mergeMode=\"replace\">100.0000</maxhitpoints>");
        gen.ProtoModsXml.Should().NotContain("Hoplite");
        File.Exists(Path.Combine(gen.StagingDirectory, "game", "data", "gameplay", "proto_mods.xml")).Should().BeTrue();

        var again = ws.Generate(Path.Combine(projectDir, "generated2"));
        again.ProtoModsXml.Should().Be(gen.ProtoModsXml);
    }

    [Fact]
    public async Task ReplaceActionValue_selects_by_action_name_not_index()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-tests", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("test", projectDir, CancellationToken.None);

        await ws.EditActionValueAsync("Hoplite", "HandAttack", "damage", "10.000000", CancellationToken.None,
            new Dictionary<string, string> { ["type"] = "Hack" });

        var node = ws.GetModifiedNode("Hoplite");
        var hand = node.Xml.Elements().First(e => e.Element("name")?.Value == "HandAttack");
        hand.Elements().First(e => e.Name.LocalName == "damage").Value.Should().Be("10.000000");
        var ranged = node.Xml.Elements().First(e => e.Element("name")?.Value == "RangedAttack");
        ranged.Elements().First(e => e.Name.LocalName == "damage").Value.Should().Be("5.000000");
    }

    [Fact]
    public async Task Edit_resource_and_myth_special_data()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-tests", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("test", projectDir, CancellationToken.None);

        await ws.EditScalarAsync("BerryBush", "initialresource", "300.0000", CancellationToken.None,
            new Dictionary<string, string> { ["resourcetype"] = "Food" });
        await ws.EditScalarAsync("Minotaur", "recharge", "15.0000", CancellationToken.None);

        ws.GetModifiedNode("BerryBush").Xml.Elements()
            .First(e => e.Name.LocalName == "initialresource").Value.Should().Be("300.0000");
        ws.GetModifiedNode("Minotaur").Xml.Elements()
            .First(e => e.Name.LocalName == "recharge").Value.Should().Be("15.0000");
    }

    [Fact]
    public async Task Provenance_visible_on_effective_detail()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-tests", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("test", projectDir, CancellationToken.None);
        await ws.EditMaxHitpointsAsync("VillagerGreek", "100.0000", CancellationToken.None);

        var detail = ws.GetEffectiveDetail("VillagerGreek");
        detail.Provenance["maxhitpoints"].Kind.Should().Be(Domain.ProvenanceKind.Override);
        detail.Provenance["maxhitpoints"].BaseValue.Should().Be("55.0000");
        detail.Provenance["maxhitpoints"].EffectiveValue.Should().Be("100.0000");
        detail.Provenance["maxhitpoints"].SourceFile.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Fingerprint_mismatch_blocks_generate()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        await ws.CreateProjectAsync("test2", Path.Combine(Path.GetTempPath(), "aomr-tests", Guid.NewGuid().ToString("N")), CancellationToken.None);
        await ws.EditMaxHitpointsAsync("VillagerGreek", "100.0000", CancellationToken.None);
        var projects = (FileProjectStore)typeof(WorkspaceService)
            .GetField("_projects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(ws)!;
        typeof(FileProjectStore).GetField("_current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(projects, projects.Current! with { SourceFingerprintHash = "0000" });
        var act = () => ws.Generate();
        act.Should().Throw<SourceFingerprintMismatchException>();
    }

    [Fact]
    public void Installer_copies_only_generated_mod_files()
    {
        var staging = Path.Combine(Path.GetTempPath(), "aomr-stage", Guid.NewGuid().ToString("N"));
        var gameplay = Path.Combine(staging, "game", "data", "gameplay");
        Directory.CreateDirectory(gameplay);
        File.WriteAllText(Path.Combine(gameplay, "proto_mods.xml"), "<protomods/>");
        File.WriteAllText(Path.Combine(gameplay, "techtree_mods.xml"), "<techtreemods/>");
        File.WriteAllText(Path.Combine(staging, "secret_retail.xml"), "<nope/>");
        var mods = Path.Combine(Path.GetTempPath(), "aomr-mods", Guid.NewGuid().ToString("N"));
        var installer = new ModGeneration.LocalModInstaller(new PathSafety());
        var result = installer.InstallGeneratedOnly(staging, mods, "MyMod");
        result.CopiedFiles.Should().HaveCount(2);
        File.Exists(Path.Combine(result.TargetDirectory, "game", "data", "gameplay", "proto_mods.xml")).Should().BeTrue();
        File.Exists(Path.Combine(result.TargetDirectory, "game", "data", "gameplay", "techtree_mods.xml")).Should().BeTrue();
        File.Exists(Path.Combine(result.TargetDirectory, "secret_retail.xml")).Should().BeFalse();
        File.Exists(Path.Combine(result.TargetDirectory, "proto_mods.xml")).Should().BeFalse();
    }

    [Fact]
    public async Task God_power_and_techtree_import_lossless_unknown()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var bolt = ws.Catalog.GetSourceNode("Bolt");
        bolt.Should().NotBeNull();
        bolt!.Category.Should().Be(Domain.EntityCategory.GodPower);
        bolt.Xml.Elements().Any(e => e.Name.LocalName == "unknown_gp_flag").Should().BeTrue();
        var archaic = ws.Catalog.GetSourceNode("ArchaicAgeGreek");
        archaic!.Xml.Elements().Any(e => e.Name.LocalName == "unknown_tech_meta").Should().BeTrue();
    }
}
