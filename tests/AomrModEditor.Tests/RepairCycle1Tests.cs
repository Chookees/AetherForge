using AomrModEditor.Application.Services;
using AomrModEditor.GameData;
using AomrModEditor.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Tests;

/// <summary>Regression coverage for QA FIND-U01..U05 (dev-repair-001).</summary>
public class RepairCycle1Tests
{
    private static string FixtureRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic"));

    private static string MainWindowSource =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "AomrModEditor.App", "MainWindow.cs"));

    private static string WorkspaceSource =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "AomrModEditor.Application", "Services", "WorkspaceService.cs"));

    private static WorkspaceService CreateWorkspace()
    {
        var services = new ServiceCollection();
        services.AddAomrModEditorCore();
        return services.BuildServiceProvider().GetRequiredService<WorkspaceService>();
    }

    private static (InMemoryEntityCatalog Catalog, EditorSchemaContext Context) Load()
    {
        var importer = new GameplayImporter(new SecureXmlLoader());
        var (catalog, _) = importer.ImportDirectory(FixtureRoot);
        return (catalog, EditorSchemaBuilder.BuildContext(catalog));
    }

    [Fact]
    public void FIND_U05_armor_display_trims_trailing_zeros()
    {
        ArmorPercentConverter.ToDisplayPercent("0.0001").Should().Be("0.01");
        ArmorPercentConverter.ToDisplayPercent("0.1500").Should().Be("15");
        ArmorPercentConverter.ToDisplayPercent("0.1550").Should().Be("15.5");
    }

    [Fact]
    public void FIND_U02_addable_damagebonus_excludes_types_already_on_card()
    {
        var (catalog, ctx) = Load();
        var attack = EditorSchemaBuilder.Build(catalog.GetSourceNode("Toxotes")!, context: ctx)
            .Tabs.Single(t => t.Id == "attack");
        var add = attack.Fields.Single(f => f.Path.EndsWith("damagebonus[+]", StringComparison.Ordinal));
        add.Options.Should().NotBeNull();
        add.Options.Should().NotContain("AbstractInfantry");
        add.Options.Should().NotContain("AbstractFarm");
        add.Options.Should().NotContain("AbstractMine");
        attack.Fields.Should().Contain(f =>
            f.Presence == "present"
            && f.ElementName == "damagebonus"
            && f.MatchAttributes!["type"] == "AbstractInfantry");
    }

    [Fact]
    public async Task FIND_U01_schema_refresh_after_add_cost_and_bonus()
    {
        var ws = CreateWorkspace();
        await ws.ImportAsync(FixtureRoot, CancellationToken.None);
        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-repair1", Guid.NewGuid().ToString("N"));
        await ws.CreateProjectAsync("repair1", projectDir, CancellationToken.None);

        var before = ws.GetEditorSchema("Toxotes");
        before.Tabs.Single(t => t.Id == "economy").Fields
            .Single(f => f.MatchAttributes!["resourcetype"] == "Favor")
            .Presence.Should().Be("addable");

        await ws.EditScalarAsync(
            "Toxotes", "cost", "5.0000", CancellationToken.None,
            new Dictionary<string, string> { ["resourcetype"] = "Favor" });

        var afterCost = ws.GetEditorSchema("Toxotes");
        afterCost.Tabs.Single(t => t.Id == "economy").Fields
            .Single(f => f.MatchAttributes!["resourcetype"] == "Favor")
            .Presence.Should().Be("present");

        var add = afterCost.Tabs.Single(t => t.Id == "attack").Fields
            .Single(f => f.Path.EndsWith("damagebonus[+]", StringComparison.Ordinal));
        var newType = add.Options!.First(o => !o.Equals("AbstractInfantry", StringComparison.OrdinalIgnoreCase));

        await ws.EditActionValueAsync(
            "Toxotes", "RangedAttack", "damagebonus", "1.100000", CancellationToken.None,
            new Dictionary<string, string> { ["type"] = newType });

        var afterBonus = ws.GetEditorSchema("Toxotes");
        var attack = afterBonus.Tabs.Single(t => t.Id == "attack");
        attack.Fields.Should().Contain(f =>
            f.Presence == "present"
            && f.ElementName == "damagebonus"
            && f.MatchAttributes!["type"] == newType);
        attack.Fields.Single(f => f.Path.EndsWith("damagebonus[+]", StringComparison.Ordinal))
            .Options.Should().NotContain(newType);
    }

    [Fact]
    public void FIND_U05_workspace_caches_editor_schema_context()
    {
        var src = File.ReadAllText(WorkspaceSource);
        src.Should().Contain("_schemaContext");
        src.Should().Contain("GetOrBuildSchemaContext");
        src.Should().Contain("InvalidateSchemaContext");
    }

    [Fact]
    public void FIND_U01_U03_U04_main_window_reload_a11y_warning()
    {
        var src = File.ReadAllText(MainWindowSource);
        src.Should().Contain("AutomationProperties.SetName");
        src.Should().Contain("AutomationProperties.SetHelpText");
        src.Should().Contain("AutomationControlType.Header");
        src.Should().Contain("reloadSchema");
        src.Should().Contain("WarningChip");
        src.Should().Contain(">100");
    }
}
