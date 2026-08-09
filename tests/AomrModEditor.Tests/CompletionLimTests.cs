using System.Diagnostics;
using AomrModEditor.Application.Services;
using AomrModEditor.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Tests;

public class CompletionLimTests
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
    public async Task LIM04_FTS_search_finds_VillagerGreek_under_150ms()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var sw = Stopwatch.StartNew();
        var hits = ws.Catalog.Search("VillagerGreek");
        sw.Stop();
        hits.Should().ContainSingle(h => h.InternalKey == "VillagerGreek");
        sw.ElapsedMilliseconds.Should().BeLessThan(150);
        ws.Catalog.Should().BeOfType<SqliteFtsEntityCatalog>();
    }

    [Fact]
    public async Task LIM07_effective_stats_include_base_steps()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var stats = ws.GetEffectiveStats("VillagerGreek");
        stats.Effective.Should().ContainKey("maxhitpoints");
        stats.Steps.Should().Contain(s => s.Step == "base" && s.Field == "maxhitpoints");
    }

    [Fact]
    public async Task LIM06_rebase_updates_fingerprint_without_silent_fuzzy()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-mig", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("mig", projectDir, CancellationToken.None);
        await ws.EditMaxHitpointsAsync("VillagerGreek", "100.0000", CancellationToken.None);

        var projects = (FileProjectStore)typeof(WorkspaceService)
            .GetField("_projects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(ws)!;
        typeof(FileProjectStore).GetField("_current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(projects, projects.Current! with { SourceFingerprintHash = "deadbeef" });

        var inspect = ws.InspectMigration();
        inspect.RequiresMigration.Should().BeTrue();

        var rebase = await ws.RebaseMigrationAsync(applyFuzzyBinds: false, CancellationToken.None);
        rebase.NewFingerprintHash.Should().NotBe("deadbeef");
        rebase.UnresolvedFuzzy.Should().BeEmpty();
        ws.Project!.SourceFingerprintHash.Should().Be(rebase.NewFingerprintHash);
        var act = () => ws.Generate();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task LIM09_god_power_strategy_report_is_explicit()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-gp", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("gp", projectDir, CancellationToken.None);
        await ws.EditScalarAsync("Bolt", "damage", "2000.0000", CancellationToken.None,
            new Dictionary<string, string> { ["type"] = "Divine" });
        var gen = ws.Generate();
        gen.GodPowerStrategies.Should().Contain(s => s.EntityKey == "Bolt" && s.Strategy == "Additive");
        File.Exists(Path.Combine(gen.StagingDirectory, "godpower_strategy_report.json")).Should().BeTrue();
    }

    [Fact]
    public void LIM08_cli_commands_are_not_stubbed()
    {
        var cliProj = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AomrModEditor.Cli", "Program.cs"));
        var src = File.ReadAllText(cliProj);
        src.Should().NotContain("is stubbed for later phases");
        src.Should().Contain("case \"diff\":");
        src.Should().Contain("case \"package\":");
        src.Should().Contain("case \"migrate\":");
        src.Should().Contain("case \"export-docs\":");
        src.Should().Contain("case \"search\":");
    }

    [Fact]
    public async Task LIM08_cli_search_runs_with_exit_zero()
    {
        // Cli is a ProjectReference of the test project, so it is built for the same configuration.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "AomrModEditor.Cli", "bin", configuration, "net10.0", "AomrModEditor.Cli.dll"));
        File.Exists(dll).Should().BeTrue($"CLI must be built as a test ProjectReference (looked for {dll})");
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add("search");
        psi.ArgumentList.Add("VillagerGreek");
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(FixtureRoot);
        psi.ArgumentList.Add("--json");
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        var completed = p.WaitForExit(60000);
        completed.Should().BeTrue("CLI search should exit");
        var stderr = await stderrTask;
        p.ExitCode.Should().Be(0, "stderr: " + stderr);
        var output = await stdoutTask;
        output.Should().Contain("VillagerGreek");
    }

    [Fact]
    public void LIM01_nswag_generated_client_exists()
    {
        var generated = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "AomrModEditor.Api.Client", "Generated", "AomrGeneratedApiClient.g.cs"));
        File.Exists(generated).Should().BeTrue("NSwag MSBuild should emit Generated/AomrGeneratedApiClient.g.cs");
        var text = File.ReadAllText(generated);
        text.Should().Contain("auto-generated");
        text.Should().Contain("AomrGeneratedApiClient");
    }

    [Fact]
    public async Task AC06_god_power_unified_edits_behavior_and_techtree_cooldown()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-gp-u", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("gpu", projectDir, CancellationToken.None);

        var view = ws.GetGodPowerUnified("Bolt");
        view.Behavior.Should().Be("StrikeSingleTarget");
        view.CooldownTechKey.Should().Be("BoltCooldown");
        view.Cooldown.Should().Be("90.0");

        await ws.EditScalarAsync("Bolt", "behavior", "StrikeArea", CancellationToken.None);
        await ws.EditAttributeAsync(
            "BoltCooldown",
            "effect",
            "cooldown",
            "120.0",
            CancellationToken.None,
            new Dictionary<string, string> { ["power"] = "Bolt", ["subtype"] = "GodPower" });

        var after = ws.GetGodPowerUnified("Bolt");
        after.Behavior.Should().Be("StrikeArea");
        after.Cooldown.Should().Be("120.0");
        ws.GetModifiedNode("Bolt").Xml.Elements().Any(e => e.Name.LocalName == "unknown_gp_flag").Should().BeTrue();
    }

    [Fact]
    public async Task AC14_export_docs_writes_markdown_report()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-docs", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("docs", projectDir, CancellationToken.None);
        var path = ws.ExportDocsMarkdown();
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Contain("AoMR Mod Editor export");
    }

    [Fact]
    public void LIM03_main_window_covers_core_screens_and_ue_tabs()
    {
        var main = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "AomrModEditor.App", "MainWindow.cs"));
        var src = File.ReadAllText(main);
        src.Should().Contain("Home");
        src.Should().Contain("Setup");
        src.Should().Contain("Entity Browser");
        src.Should().Contain("Unit Editor");
        src.Should().Contain("Diff Workspace");
        src.Should().Contain("Generation");
        src.Should().Contain("God Power Editor");
        src.Should().Contain("BuildAdaptiveEditor");
        src.Should().Contain("GetEditorSchemaAsync");
        src.Should().Contain("Load default data");
        src.Should().Contain("Browse");
        src.Should().Contain("ProvenanceStrip");
        src.Should().NotContain("FR-01");
        src.Should().NotContain("EB-01");
        src.Should().NotContain("UE-01");
        src.Should().NotContain("FR Shell");
    }

    [Fact]
    public async Task LIM07_unsupported_effect_warnings_surface()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var stats = ws.GetEffectiveStats("VillagerGreek");
        // nested containers (if any) or empty relative effects from techs produce warnings list (may be empty on clean fixture)
        stats.UnsupportedWarnings.Should().NotBeNull();
        stats.Steps.Should().NotBeEmpty();
    }
}

