using System.Net;
using System.Security.Cryptography;
using AomrModEditor.Api.Contracts.V1;
using AomrModEditor.Application.Abstractions;
using AomrModEditor.Application.Services;
using AomrModEditor.Domain;
using AomrModEditor.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AomrModEditor.Api;

public sealed record ApiAuthOptions(string Token);

public static class ApiHostBootstrap
{
    public static WebApplicationBuilder CreateBuilder(string[]? args = null, string? token = null, bool embedded = false)
    {
        args ??= Array.Empty<string>();
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog((ctx, cfg) => cfg.WriteTo.Console().MinimumLevel.Information());

        var resolved = token
                       ?? builder.Configuration["AOMR_API_TOKEN"]
                       ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        builder.Services.AddSingleton(new ApiAuthOptions(resolved));
        builder.Services.AddSingleton<IEmbeddedGameplayPack, EmbeddedGameplayPack>();
        builder.Services.AddAomrModEditorCore();
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddCors(o =>
        {
            o.AddPolicy("loopback", p => p
                .SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
                .AllowAnyHeader()
                .AllowAnyMethod());
        });

        if (embedded
            || string.Equals(Environment.GetEnvironmentVariable("AOMR_EMBEDDED"), "1", StringComparison.Ordinal)
            || args.Contains("--embedded", StringComparer.OrdinalIgnoreCase))
        {
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0);
                options.Listen(IPAddress.IPv6Loopback, 0);
            });
        }

        return builder;
    }

    public static WebApplication BuildAndMap(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        app.UseCors("loopback");
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/health")
                || ctx.Request.Path.StartsWithSegments("/openapi"))
            {
                await next();
                return;
            }

            var opts = ctx.RequestServices.GetRequiredService<ApiAuthOptions>();
            var header = ctx.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(header["Bearer ".Length..].Trim()),
                    System.Text.Encoding.UTF8.GetBytes(opts.Token)))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Title = "Unauthorized",
                    Status = 401,
                    Detail = "Missing or invalid bearer token."
                });
                return;
            }

            await next();
        });

        app.MapOpenApi();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/ready", (SourceSnapshotStore store) =>
            store.Current is null
                ? Results.Json(new { status = "not_ready", sourceLoaded = false }, statusCode: 503)
                : Results.Ok(new { status = "ready", sourceLoaded = true, entities = store.Current.EntityCount }));

        MapV1(app);
        return app;
    }

    private static IResult SourceNotLoaded()
        => Results.Problem(
            title: "Source not loaded",
            detail: "Select and successfully load a data source in Setup before using this resource.",
            statusCode: StatusCodes.Status409Conflict,
            type: "urn:aomr:source-not-loaded");

    private static IResult? RequireSource(WorkspaceService ws)
        => ws.IsSourceLoaded ? null : SourceNotLoaded();

    public static async Task<(WebApplication App, string BaseUrl, string Token)> StartEmbeddedAsync(CancellationToken ct = default)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Environment.SetEnvironmentVariable("AOMR_EMBEDDED", "1");
        var builder = CreateBuilder(new[] { "--embedded" }, token, embedded: true);
        var app = BuildAndMap(builder);
        await app.StartAsync(ct).ConfigureAwait(false);
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
                        ?? throw new InvalidOperationException("No server addresses.");
        var baseUrl = addresses.FirstOrDefault(a => a.Contains("127.0.0.1", StringComparison.Ordinal))
                      ?? addresses.First();
        // Token intentionally never written to logs.
        return (app, baseUrl, token);
    }

    private static void MapV1(WebApplication app)
    {
        var v1 = app.MapGroup("/api/v1");

        v1.MapPost("/installations/discover", (DiscoverInstallationsRequest? req, IInstallationDiscovery discovery) =>
        {
            var result = discovery.Discover(req?.HintPath);
            var dto = new DiscoveryResultDto(
                result.Installations.Select(i => new InstallationDto(i.Path, i.Status, false, i.Kind)).ToList(),
                result.LocalModsRoots.Select(m => new LocalModsRootDto(m.Path, m.AccountFolderName, m.ExistingModCount, m.LastWriteUtc)).ToList(),
                result.PreferredLocalModsPath,
                result.PreferredInstallPath,
                result.Summary);
            return Results.Ok(dto);
        });

        v1.MapPost("/sources/imports", async (ImportSourceRequest req, WorkspaceService ws, IOperationRunner ops, HttpRequest http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.DirectoryPath) || !Directory.Exists(req.DirectoryPath))
            {
                return Results.Problem(
                    title: "Import directory not found",
                    detail: $"Directory path does not exist: {req.DirectoryPath}",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (http.Query.ContainsKey("async") || string.Equals(http.Headers["Prefer"], "respond-async", StringComparison.OrdinalIgnoreCase))
            {
                var id = ops.Enqueue("import", async (progress, token) =>
                {
                    progress.Report(10, "importing");
                    var snap = await ws.ImportAsync(req.DirectoryPath, token).ConfigureAwait(false);
                    progress.Report(100, "done");
                    return ApiMapping.ToSnapshotDto(snap);
                });
                return Results.Accepted($"/api/v1/operations/{id}", ApiMapping.ToOperationDto(ops.Get(id)!));
            }

            try
            {
                var sync = await ws.ImportAsync(req.DirectoryPath, ct);
                return Results.Ok(ApiMapping.ToSnapshotDto(sync));
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.Problem(
                    title: "Import directory not found",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        v1.MapPost("/sources/embedded", async (WorkspaceService ws, IEmbeddedGameplayPack pack, CancellationToken ct) =>
        {
            if (!pack.IsAvailable)
            {
                return Results.Problem(
                    title: "Embedded pack unavailable",
                    detail: "No embedded gameplay pack was found. Browse to an import folder in Setup instead.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            using var stream = pack.OpenRead();
            var snap = await ws.ImportEmbeddedPackAsync(stream, "embedded://default", ct);
            return Results.Ok(ApiMapping.ToSnapshotDto(snap));
        });

        v1.MapGet("/sources", (SourceSnapshotStore store) =>
        {
            if (store.Current is null) return Results.Ok(Array.Empty<SourceSnapshotDto>());
            return Results.Ok(new[] { ApiMapping.ToSnapshotDto(store.Current) });
        });

        v1.MapGet("/entities", (WorkspaceService ws, string? q, string? category, string? unitType, int? take, bool? includeTechnologies) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            EntityCategory? cat = null;
            if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<EntityCategory>(category, true, out var parsed))
                cat = parsed;
            var items = ws.Catalog.Search(q, cat, take ?? 10_000, unitType)
                .Where(e => includeTechnologies == true || e.Category != EntityCategory.Technology)
                .Select(ApiMapping.ToEntitySummary)
                .ToList();
            return Results.Ok(new PagedEntitiesDto(items, null));
        });

        v1.MapGet("/entities/{id}", (string id, WorkspaceService ws) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            try
            {
                return Results.Ok(ApiMapping.ToEntityDetail(ws.GetEffectiveDetail(id)));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new ProblemDetails { Title = "Not found", Status = 404, Detail = id });
            }
        });

        v1.MapGet("/entities/{id}/editor-schema", (string id, WorkspaceService ws) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            try
            {
                return Results.Ok(ApiMapping.ToEditorSchema(ws.GetEditorSchema(id)));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new ProblemDetails { Title = "Not found", Status = 404, Detail = id });
            }
        });

        v1.MapGet("/entities/{id}/effective-stats", (string id, WorkspaceService ws) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            try
            {
                var stats = ws.GetEffectiveStats(id);
                return Results.Ok(new EffectiveStatsDto(
                    stats.EntityKey,
                    stats.Effective,
                    stats.Steps.Select(s => new EffectiveStatStepDto(s.Step, s.Field, s.Value, s.Source, s.Unsupported, s.Warning)).ToList(),
                    stats.UnsupportedWarnings));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        v1.MapGet("/god-powers/{id}/unified", (string id, WorkspaceService ws) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            try
            {
                var view = ws.GetGodPowerUnified(id);
                return Results.Ok(new GodPowerUnifiedDto(
                    view.PowerKey,
                    view.Behavior,
                    view.Damage,
                    view.CooldownTechKey,
                    view.Cooldown,
                    view.CostFavor,
                    view.BehaviorSourceFile,
                    view.CooldownSourceFile));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ProblemDetails { Title = "Not a god power", Detail = ex.Message, Status = 400 });
            }
        });

        v1.MapPost("/projects", async (CreateProjectRequest req, WorkspaceService ws, CancellationToken ct) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            var project = await ws.CreateProjectAsync(req.Name, req.Directory, ct);
            return Results.Ok(ApiMapping.ToProjectDto(project));
        });

        v1.MapGet("/projects/current", (WorkspaceService ws) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            return ws.Project is null ? Results.NotFound() : Results.Ok(ApiMapping.ToProjectDto(ws.Project));
        });

        v1.MapPost("/projects/current/changes", async (ApplyChangeRequest req, WorkspaceService ws, CancellationToken ct) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            ChangeOp op = req.OpType.ToLowerInvariant() switch
            {
                "setscalar" or "maxhitpoints" => await ws.EditScalarAsync(req.EntityKey, req.ElementName ?? "maxhitpoints", req.NewValue, ct, req.Attributes),
                "replaceactionvalue" => await ws.EditActionValueAsync(req.EntityKey, req.ActionName!, req.ChildElementName!, req.NewValue, ct, req.Attributes),
                "setattribute" => await ws.EditAttributeAsync(
                    req.EntityKey,
                    req.ElementName!,
                    req.AttributeName ?? req.ChildElementName!,
                    req.NewValue,
                    ct,
                    req.Attributes),
                _ => throw new BadHttpRequestException($"Unsupported op '{req.OpType}'")
            };
            return Results.Ok(new ChangeDto(op.Id, op.EntityKey, op.GetType().Name, op.CreatedAtUtc));
        });

        v1.MapPost("/projects/current/diffs", (WorkspaceService ws, string entityKey) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            try
            {
                var diff = ws.DiffEntity(entityKey);
                return Results.Ok(new DiffDto(
                    diff.Semantic.Select(s => new SemanticDiffEntryDto(s.EntityKey, s.Path, s.OldValue, s.NewValue)).ToList(),
                    diff.RawBefore,
                    diff.RawAfter));
            }
            catch (SourceFingerprintMismatchException ex)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Title = "Source fingerprint mismatch",
                    Status = 409,
                    Detail = ex.Message,
                    Extensions = { ["expected"] = ex.Expected, ["actual"] = ex.Actual }
                });
            }
        });

        v1.MapPost("/projects/current/validations", (WorkspaceService ws) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            var issues = ws.Validate()
                .Select(i => new ValidationIssueDto(i.Severity.ToString(), i.Code, i.Message, i.EntityKey))
                .ToList();
            return Results.Ok(issues);
        });

        v1.MapPost("/projects/current/generations", (WorkspaceService ws, IOperationRunner ops, HttpRequest http) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            if (http.Query.ContainsKey("async") || string.Equals(http.Headers["Prefer"], "respond-async", StringComparison.OrdinalIgnoreCase))
            {
                var id = ops.Enqueue("generate", (progress, _) =>
                {
                    progress.Report(20, "generating");
                    var result = ws.Generate();
                    progress.Report(100, "done");
                    return Task.FromResult<object?>(ApiMapping.ToGenerationDto(result));
                });
                return Results.Accepted($"/api/v1/operations/{id}", ApiMapping.ToOperationDto(ops.Get(id)!));
            }

            try
            {
                return Results.Ok(ApiMapping.ToGenerationDto(ws.Generate()));
            }
            catch (SourceFingerprintMismatchException ex)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Title = "Source fingerprint mismatch",
                    Status = 409,
                    Detail = ex.Message
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("blocked", StringComparison.OrdinalIgnoreCase))
            {
                return Results.UnprocessableEntity(new ProblemDetails
                {
                    Title = "Validation blocked generation",
                    Status = 422,
                    Detail = ex.Message
                });
            }
        });

        v1.MapGet("/projects/current/migrations/inspect", (WorkspaceService ws) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            var report = ws.InspectMigration();
            return Results.Ok(new MigrationInspectDto(
                report.ExpectedFingerprint,
                report.ActualFingerprint,
                report.RequiresMigration,
                report.MatchedEntityKeys,
                report.MissingInNewSource,
                report.AddedInNewSource,
                report.FuzzySuggestions.Select(f => new FuzzySuggestionDto(f.ChangeEntityKey, f.SuggestedKey, f.Score)).ToList()));
        });

        v1.MapPost("/projects/current/migrations/rebase", async (RebaseRequest? req, WorkspaceService ws, CancellationToken ct) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            var result = await ws.RebaseMigrationAsync(req?.ApplyFuzzyBinds ?? false, ct);
            return Results.Ok(new RebaseResultDto(
                result.RebasedChanges.Count,
                result.DroppedChanges.Count,
                result.UnresolvedFuzzy.Count,
                result.NewFingerprintHash,
                result.ReportMarkdown));
        });

        v1.MapPost("/projects/current/packages", (WorkspaceService ws) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            var zip = ws.PackageZip();
            return Results.Ok(new PackageDto(zip));
        });

        v1.MapPost("/projects/current/installs", (InstallRequest req, WorkspaceService ws) =>
        {
            if (RequireSource(ws) is { } blocked) return blocked;
            var result = ws.Install(req.ModsRoot, req.ModFolderName);
            return Results.Ok(new InstallDto(result.TargetDirectory, result.CopiedFiles, result.BackupDirectory));
        });

        v1.MapGet("/operations/{id:guid}", (Guid id, IOperationRunner ops) =>
        {
            var op = ops.Get(id);
            return op is null ? Results.NotFound() : Results.Ok(ApiMapping.ToOperationDto(op));
        });

        v1.MapGet("/operations", (IOperationRunner ops) =>
            Results.Ok(ops.List().Select(ApiMapping.ToOperationDto).ToList()));

        v1.MapPost("/cache/purge-retail", () =>
        {
            var cache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AomrModEditor", "retail-cache");
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
            return Results.Ok(new { purged = true, path = cache });
        });
    }
}

public static class ApiMapping
{
    public static SourceSnapshotDto ToSnapshotDto(Application.Abstractions.SourceSnapshot snap)
        => new(snap.Id, snap.RootPath, snap.Fingerprint.AggregateHash, snap.EntityCount, snap.ImportedAtUtc);

    public static ProjectDto ToProjectDto(Application.Abstractions.ProjectInfo p)
        => new(p.Id, p.Name, p.Directory, p.SourceSnapshotId, p.SourceFingerprintHash, p.Version);

    public static GenerationDto ToGenerationDto(Application.Abstractions.GenerationResult result)
        => new(
            result.StagingDirectory,
            result.GeneratedFiles,
            result.ProtoModsXml,
            result.TechtreeModsXml,
            result.Issues.Select(i => new ValidationIssueDto(i.Severity.ToString(), i.Code, i.Message, i.EntityKey)).ToList(),
            result.GodPowerStrategies.Select(g => new GodPowerStrategyDto(g.EntityKey, g.Strategy, g.Reason, g.OutputFile)).ToList());

    public static OperationDto ToOperationDto(Application.Abstractions.OperationInfo op)
        => new(op.Id, op.Kind, op.State, op.ProgressPercent, op.Phase, op.Warnings, op.Errors, op.CreatedAtUtc, op.CompletedAtUtc, op.ResultLink);

    public static EntitySummaryDto ToEntitySummary(Application.Abstractions.EntitySummary e)
        => new(e.InternalKey, e.Category.ToString(), e.SourceFile, e.DisplayNameId, e.Pantheon, e.UnitTypes, e.IconPath, e.Pantheon);

    public static EntityEditorSchemaDto ToEditorSchema(Application.Services.EntityEditorSchema schema)
        => new(
            schema.EntityKey,
            schema.Category.ToString(),
            schema.UnitTypes,
            schema.IconPath,
            schema.Tabs.Select(t => new EditorTabDto(
                t.Id,
                t.Title,
                t.Fields.Select(f => new EditorFieldDto(
                    f.Path,
                    f.Label,
                    f.ValueKind,
                    f.CurrentValue,
                    f.MatchAttributes,
                    f.ElementName,
                    f.AttributeName,
                    f.DisplayFormat,
                    f.Presence,
                    f.GroupKey,
                    f.Options,
                    f.PersistHint,
                    f.GroupKind)).ToList())).ToList());

    public static EntityDetailDto ToEntityDetail(Application.Abstractions.EntityDetail d)
        => new(
            ToEntitySummary(d.Summary),
            d.RawXml,
            d.Provenance.ToDictionary(
                kv => kv.Key,
                kv => new FieldProvenanceDto(
                    kv.Value.Kind.ToString(),
                    kv.Value.SourceFile,
                    kv.Value.FieldPath,
                    kv.Value.BaseValue,
                    kv.Value.OverrideValue,
                    kv.Value.EffectiveValue)),
            d.NormalizedFields);
}
