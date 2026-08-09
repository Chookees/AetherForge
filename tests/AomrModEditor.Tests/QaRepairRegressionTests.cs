using System.Text.Json;
using AomrModEditor.Application.Services;
using AomrModEditor.Domain;
using AomrModEditor.GameData;
using AomrModEditor.ModGeneration;
using AomrModEditor.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Tests;

public class QaRepairRegressionTests
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
    public async Task QA_F001_HP_only_generate_excludes_unchanged_unittype_siblings()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-tests", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("f001-hp", projectDir, CancellationToken.None);
        await ws.EditMaxHitpointsAsync("VillagerGreek", "100.0000", CancellationToken.None);

        var gen = ws.Generate();
        gen.ProtoModsXml.Should().Contain("<maxhitpoints mergeMode=\"replace\">100.0000</maxhitpoints>");
        gen.ProtoModsXml.Should().NotContain("unittype");
        gen.ProtoModsXml.Should().NotContain("EconomicUnit");
        gen.ProtoModsXml.Should().NotContain("AbstractVillager");
    }

    [Fact]
    public async Task QA_F001_researchpoints_only_generate_excludes_unchanged_effects()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-tests", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("f001-rp", projectDir, CancellationToken.None);
        await ws.EditScalarAsync("ArchaicAgeGreek", "researchpoints", "1.0000", CancellationToken.None);

        var gen = ws.Generate();
        gen.TechtreeModsXml.Should().Contain("<researchpoints mergeMode=\"replace\">1.0000</researchpoints>");
        gen.TechtreeModsXml.Should().NotContain("<effect");
        gen.TechtreeModsXml.Should().NotContain("Hoplite");
        gen.TechtreeModsXml.Should().NotContain("VillagerGreek");
    }

    [Fact]
    public async Task QA_F002_attribute_selectors_persist_and_round_trip_apply_generate()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-tests", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("f002", projectDir, CancellationToken.None);

        await ws.EditActionValueAsync("Hoplite", "HandAttack", "damage", "99.000000", CancellationToken.None,
            new Dictionary<string, string> { ["type"] = "Hack" });
        await ws.EditScalarAsync("BerryBush", "initialresource", "300.0000", CancellationToken.None,
            new Dictionary<string, string> { ["resourcetype"] = "Food" });

        var projectJson = await File.ReadAllTextAsync(Path.Combine(projectDir, "aomrmod.project.json"));
        var changelogJson = await File.ReadAllTextAsync(Path.Combine(projectDir, "changes", "changelog.json"));
        projectJson.Should().Contain("Hack");
        projectJson.Should().Contain("resourcetype");
        projectJson.Should().Contain("Food");
        changelogJson.Should().Contain("Hack");
        changelogJson.Should().Contain("resourcetype");

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new ChangeOpJsonConverter() }
        };
        var loaded = JsonSerializer.Deserialize<List<ChangeOp>>(changelogJson, options)!;
        loaded.Should().HaveCount(2);
        loaded.OfType<ReplaceActionValue>().Single().ChildAttributes.Should()
            .ContainKey("type").WhoseValue.Should().Be("Hack");
        loaded.OfType<SetScalar>().Single().Attributes.Should()
            .ContainKey("resourcetype").WhoseValue.Should().Be("Food");

        var catalog = ws.Catalog;
        var originals = catalog.All.ToDictionary(
            e => e.InternalKey,
            e => catalog.GetSourceNode(e.InternalKey)!,
            StringComparer.OrdinalIgnoreCase);
        var applier = new ChangeApplier();
        var modified = originals.ToDictionary(
            kv => kv.Key,
            kv => applier.Apply(kv.Value, loaded.Where(c => c.EntityKey.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))),
            StringComparer.OrdinalIgnoreCase);

        var staging = Path.Combine(projectDir, "reload-gen");
        var gen = new AdditiveModGenerator().Generate(originals, modified, staging);
        gen.ProtoModsXml.Should().Contain("Hoplite");
        gen.ProtoModsXml.Should().Contain("99.000000");
        gen.ProtoModsXml.Should().Contain("BerryBush");
        gen.ProtoModsXml.Should().Contain("300.0000");
        gen.ProtoModsXml.Should().Contain("resourcetype=\"Food\"");
    }

    [Fact]
    public void QA_F003_install_rejects_relative_traversal_and_absolute_escape()
    {
        var staging = Path.Combine(Path.GetTempPath(), "aomr-stage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "proto_mods.xml"), "<protomods/>");
        var modsRoot = Path.Combine(Path.GetTempPath(), "aomr-mods", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(modsRoot);
        var installer = new LocalModInstaller(new PathSafety());

        var traversal = () => installer.InstallGeneratedOnly(staging, modsRoot, "..\\escaped-mod");
        traversal.Should().Throw<InvalidOperationException>();

        var absolute = Path.Combine(Path.GetTempPath(), "aomr-injected-" + Guid.NewGuid().ToString("N"), "InjectedMod");
        var absEscape = () => installer.InstallGeneratedOnly(staging, modsRoot, absolute);
        absEscape.Should().Throw<InvalidOperationException>();

        var ok = installer.InstallGeneratedOnly(staging, modsRoot, "SafeMod");
        Path.GetFullPath(ok.TargetDirectory)
            .StartsWith(Path.GetFullPath(modsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            .Should().BeTrue();
        File.Exists(Path.Combine(ok.TargetDirectory, "game", "data", "gameplay", "proto_mods.xml")).Should().BeTrue();
    }

    [Fact]
    public void QA_F004_ci_workflow_runs_dotnet_test_solution()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var workflow = Path.Combine(root, ".github", "workflows", "ci.yml");
        File.Exists(workflow).Should().BeTrue("AC-15 requires CI running automated tests");
        var text = File.ReadAllText(workflow);
        text.Should().Contain("dotnet test");
        text.Should().Contain("AomrModEditor.sln");
    }

    [Fact]
    public async Task QA_F005_concurrent_mutating_ops_do_not_corrupt_project()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-tests", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("f005", projectDir, CancellationToken.None);

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            if (i % 2 == 0)
                await ws.EditMaxHitpointsAsync("VillagerGreek", $"{100 + i}.0000", CancellationToken.None);
            else
                await ws.EditScalarAsync("BerryBush", "initialresource", $"{200 + i}.0000", CancellationToken.None,
                    new Dictionary<string, string> { ["resourcetype"] = "Food" });
        }));

        await Task.WhenAll(tasks);

        var changes = ws.Project!.Version;
        changes.Should().BeGreaterThan(1);
        var gen = ws.Generate();
        gen.ProtoModsXml.Should().Contain("VillagerGreek");
        gen.ProtoModsXml.Should().Contain("BerryBush");
        var projectJson = await File.ReadAllTextAsync(Path.Combine(projectDir, "aomrmod.project.json"));
        projectJson.Should().Contain("\"changes\"");
        JsonDocument.Parse(projectJson).RootElement.GetProperty("changes").GetArrayLength().Should().Be(20);
    }
}
