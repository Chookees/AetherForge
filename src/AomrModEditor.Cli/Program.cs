using System.Text.Json;
using AomrModEditor.Application.Services;
using AomrModEditor.Persistence;
using Microsoft.Extensions.DependencyInjection;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0)
    {
        PrintHelp();
        return 1;
    }

    var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
    var command = args[0].ToLowerInvariant();
    try
    {
        switch (command)
        {
            case "import":
            {
                var path = RequirePositional(args, 1, "path");
                var ws = CreateWorkspace();
                var snap = await ws.ImportAsync(path, CancellationToken.None);
                Write(new { snap.Id, snap.EntityCount, hash = snap.Fingerprint.AggregateHash }, json);
                return 0;
            }
            case "inspect":
            {
                var id = RequirePositional(args, 2, "id");
                var source = RequireOption(args, "--source");
                var ws = CreateWorkspace();
                await ws.ImportAsync(source, CancellationToken.None);
                var detail = ws.GetEffectiveDetail(id);
                Write(new
                {
                    detail.Summary.InternalKey,
                    category = detail.Summary.Category.ToString(),
                    fields = detail.NormalizedFields,
                    provenance = detail.Provenance.ToDictionary(
                        k => k.Key,
                        v => new
                        {
                            kind = v.Value.Kind.ToString(),
                            v.Value.BaseValue,
                            v.Value.EffectiveValue,
                            v.Value.SourceFile
                        })
                }, json);
                return 0;
            }
            case "search":
            {
                var q = RequirePositional(args, 1, "query");
                var source = RequireOption(args, "--source");
                var ws = CreateWorkspace();
                await ws.ImportAsync(source, CancellationToken.None);
                var hits = ws.Catalog.Search(q);
                Write(hits.Select(h => new { h.InternalKey, category = h.Category.ToString(), h.SourceFile }), json);
                return 0;
            }
            case "generate":
            {
                var source = RequireOption(args, "--source");
                var project = RequireOption(args, "--project");
                var hp = GetOption(args, "--hp") ?? "100.0000";
                var ws = CreateWorkspace();
                await ws.ImportAsync(source, CancellationToken.None);
                await ws.CreateProjectAsync("cli", project, CancellationToken.None);
                await ws.EditMaxHitpointsAsync("VillagerGreek", hp, CancellationToken.None);
                var result = ws.Generate();
                Write(new
                {
                    result.StagingDirectory,
                    result.GeneratedFiles,
                    result.ProtoModsXml,
                    godPowerStrategies = result.GodPowerStrategies
                }, json);
                return 0;
            }
            case "validate":
            {
                var source = RequireOption(args, "--source");
                var project = RequireOption(args, "--project");
                var ws = CreateWorkspace();
                await ws.ImportAsync(source, CancellationToken.None);
                await ws.CreateProjectAsync("cli", project, CancellationToken.None);
                var issues = ws.Validate();
                Write(issues, json);
                return issues.Any(i => i.Severity is AomrModEditor.Domain.ValidationSeverity.Blocking
                    or AomrModEditor.Domain.ValidationSeverity.Error)
                    ? 2
                    : 0;
            }
            case "diff":
            {
                var entity = RequirePositional(args, 1, "entity");
                var source = RequireOption(args, "--source");
                var project = RequireOption(args, "--project");
                var hp = GetOption(args, "--hp") ?? "100.0000";
                var ws = CreateWorkspace();
                await ws.ImportAsync(source, CancellationToken.None);
                await ws.CreateProjectAsync("cli-diff", project, CancellationToken.None);
                await ws.EditMaxHitpointsAsync(entity, hp, CancellationToken.None);
                var diff = ws.DiffEntity(entity);
                Write(new
                {
                    semantic = diff.Semantic.Select(s => new { s.EntityKey, s.Path, s.OldValue, s.NewValue }),
                    diff.RawBefore,
                    diff.RawAfter
                }, json);
                return 0;
            }
            case "package":
            {
                var source = RequireOption(args, "--source");
                var project = RequireOption(args, "--project");
                var ws = CreateWorkspace();
                await ws.ImportAsync(source, CancellationToken.None);
                await ws.CreateProjectAsync("cli-pkg", project, CancellationToken.None);
                await ws.EditMaxHitpointsAsync("VillagerGreek", "100.0000", CancellationToken.None);
                ws.Generate();
                var zip = ws.PackageZip();
                Write(new { zip }, json);
                return 0;
            }
            case "migrate":
            {
                var source = RequireOption(args, "--source");
                var project = RequireOption(args, "--project");
                var ws = CreateWorkspace();
                await ws.ImportAsync(source, CancellationToken.None);
                await ws.CreateProjectAsync("cli-mig", project, CancellationToken.None);
                var inspect = ws.InspectMigration();
                if (!inspect.RequiresMigration)
                {
                    Write(new { requiresMigration = false, inspect.ExpectedFingerprint, inspect.ActualFingerprint }, json);
                    return 0;
                }

                var rebase = await ws.RebaseMigrationAsync(applyFuzzyBinds: false, CancellationToken.None);
                Write(new
                {
                    requiresMigration = true,
                    rebase.NewFingerprintHash,
                    rebased = rebase.RebasedChanges.Count,
                    dropped = rebase.DroppedChanges.Count,
                    fuzzyUnresolved = rebase.UnresolvedFuzzy.Count,
                    rebase.ReportMarkdown
                }, json);
                return 0;
            }
            case "export-docs":
            {
                var source = RequireOption(args, "--source");
                var project = RequireOption(args, "--project");
                var ws = CreateWorkspace();
                await ws.ImportAsync(source, CancellationToken.None);
                await ws.CreateProjectAsync("cli-docs", project, CancellationToken.None);
                var path = ws.ExportDocsMarkdown();
                Write(new { path }, json);
                return 0;
            }
            default:
                PrintHelp();
                return 1;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static WorkspaceService CreateWorkspace()
{
    var services = new ServiceCollection();
    services.AddAomrModEditorCore();
    return services.BuildServiceProvider().GetRequiredService<WorkspaceService>();
}

static void Write(object payload, bool asJson)
{
    if (asJson)
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    else
        Console.WriteLine(JsonSerializer.Serialize(payload));
}

static string RequirePositional(string[] args, int index, string name)
    => args.Length > index ? args[index] : throw new InvalidOperationException($"Missing argument '{name}'.");

static string RequireOption(string[] args, string name)
    => GetOption(args, name) ?? throw new InvalidOperationException($"Missing option '{name}'.");

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

static void PrintHelp()
{
    Console.WriteLine("""
        AomrModEditor.Cli
          import <path> [--json]
          inspect unit <id> --source <path> [--json]
          search <query> --source <path> [--json]
          generate --source <path> --project <dir> [--hp 100.0000] [--json]
          validate --source <path> --project <dir> [--json]
          diff <entity> --source <path> --project <dir> [--hp 100.0000] [--json]
          package --source <path> --project <dir> [--json]
          migrate --source <path> --project <dir> [--json]
          export-docs --source <path> --project <dir> [--json]
        """);
}
