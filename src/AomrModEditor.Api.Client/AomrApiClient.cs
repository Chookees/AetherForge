using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AomrModEditor.Api.Contracts.V1;
using Gen = AomrModEditor.Api.Client.Generated;

namespace AomrModEditor.Api.Client;

/// <summary>
/// Thin Contracts DTO facade for Avalonia. All REST routing delegates to NSwag-generated
/// <see cref="Gen.AomrGeneratedApiClient"/> operations. Do not hand-edit Generated/*.g.cs.
/// </summary>
public sealed class AomrApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Gen.AomrGeneratedApiClient _generated;

    public AomrApiClient(HttpClient http)
    {
        var baseUrl = http.BaseAddress?.ToString() ?? "http://127.0.0.1/";
        _generated = new Gen.AomrGeneratedApiClient(baseUrl, http);
    }

    public Gen.AomrGeneratedApiClient Generated => _generated;

    public static AomrApiClient Create(string baseUrl, string bearerToken)
    {
        var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return new AomrApiClient(http);
    }

    public async Task<DiscoveryResultDto?> DiscoverInstallationsAsync(string? hintPath = null, CancellationToken ct = default)
    {
        var result = await _generated.DiscoverInstallationsAsync(
            new Gen.DiscoverInstallationsRequest { HintPath = hintPath ?? string.Empty }, ct).ConfigureAwait(false);
        return Map<DiscoveryResultDto>(result);
    }

    public async Task<InstallDto?> InstallAsync(string modsRoot, string modFolderName, CancellationToken ct = default)
    {
        var result = await _generated.InstallAsync(
            new Gen.InstallRequest { ModsRoot = modsRoot, ModFolderName = modFolderName }, ct).ConfigureAwait(false);
        return Map<InstallDto>(result);
    }

    public async Task<HttpResponseMessage> GetHealthAsync(CancellationToken ct = default)
    {
        await _generated.GetHealthAsync(ct).ConfigureAwait(false);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    public async Task<SourceSnapshotDto?> ImportSourceAsync(string directory, CancellationToken ct = default)
    {
        var result = await _generated.ImportSourceAsync(
            new Gen.ImportSourceRequest { DirectoryPath = directory }, ct).ConfigureAwait(false);
        return Map<SourceSnapshotDto>(result);
    }

    public async Task<SourceSnapshotDto?> LoadEmbeddedSourceAsync(CancellationToken ct = default)
    {
        var result = await _generated.LoadEmbeddedSourceAsync(ct).ConfigureAwait(false);
        return Map<SourceSnapshotDto>(result);
    }

    public async Task<OperationDto?> ImportSourceAsyncOperationAsync(string directory, CancellationToken ct = default)
    {
        _generated.RequestPreferAsync = true;
        try
        {
            await _generated.ImportSourceAsync(
                new Gen.ImportSourceRequest { DirectoryPath = directory }, ct).ConfigureAwait(false);
            return null;
        }
        catch (Gen.AomrApiException<Gen.OperationDto> ex) when (ex.StatusCode == 202)
        {
            return Map<OperationDto>(ex.Result);
        }
        finally
        {
            _generated.RequestPreferAsync = false;
        }
    }

    public async Task<PagedEntitiesDto?> GetEntitiesAsync(
        string? q = null,
        string? category = null,
        string? unitType = null,
        int? take = null,
        bool? includeTechnologies = null,
        CancellationToken ct = default)
    {
        var result = await _generated.GetEntitiesAsync(q, category, unitType, take, includeTechnologies, ct).ConfigureAwait(false);
        return Map<PagedEntitiesDto>(result);
    }

    public async Task<EntityDetailDto?> GetEntityAsync(string id, CancellationToken ct = default)
    {
        var result = await _generated.GetEntityAsync(id, ct).ConfigureAwait(false);
        return Map<EntityDetailDto>(result);
    }

    public async Task<EntityEditorSchemaDto?> GetEditorSchemaAsync(string id, CancellationToken ct = default)
    {
        var result = await _generated.GetEditorSchemaAsync(id, ct).ConfigureAwait(false);
        return Map<EntityEditorSchemaDto>(result);
    }

    public async Task<EffectiveStatsDto?> GetEffectiveStatsAsync(string id, CancellationToken ct = default)
    {
        var result = await _generated.GetEffectiveStatsAsync(id, ct).ConfigureAwait(false);
        return Map<EffectiveStatsDto>(result);
    }

    public async Task<GodPowerUnifiedDto?> GetGodPowerUnifiedAsync(string id, CancellationToken ct = default)
    {
        var result = await _generated.GetGodPowerUnifiedAsync(id, ct).ConfigureAwait(false);
        return Map<GodPowerUnifiedDto>(result);
    }

    public async Task<ProjectDto?> CreateProjectAsync(string name, string directory, CancellationToken ct = default)
    {
        var result = await _generated.CreateProjectAsync(
            new Gen.CreateProjectRequest { Name = name, Directory = directory }, ct).ConfigureAwait(false);
        return Map<ProjectDto>(result);
    }

    public async Task<ChangeDto?> ApplyChangeAsync(ApplyChangeRequest request, CancellationToken ct = default)
    {
        var body = new Gen.ApplyChangeRequest
        {
            EntityKey = request.EntityKey,
            OpType = request.OpType,
            ElementName = request.ElementName,
            ActionName = request.ActionName,
            ChildElementName = request.ChildElementName,
            NewValue = request.NewValue,
            AttributeName = request.AttributeName,
            Attributes = request.Attributes
        };
        var result = await _generated.ApplyChangeAsync(body, ct).ConfigureAwait(false);
        return Map<ChangeDto>(result);
    }

    public async Task<DiffDto?> DiffAsync(string entityKey, CancellationToken ct = default)
    {
        var result = await _generated.DiffEntityAsync(entityKey, ct).ConfigureAwait(false);
        return Map<DiffDto>(result);
    }

    public async Task<GenerationDto?> GenerateAsync(CancellationToken ct = default)
    {
        var result = await _generated.GenerateAsync(ct).ConfigureAwait(false);
        return Map<GenerationDto>(result);
    }

    public async Task<MigrationInspectDto?> InspectMigrationAsync(CancellationToken ct = default)
    {
        var result = await _generated.InspectMigrationAsync(ct).ConfigureAwait(false);
        return Map<MigrationInspectDto>(result);
    }

    public async Task<RebaseResultDto?> RebaseMigrationAsync(bool applyFuzzy = false, CancellationToken ct = default)
    {
        var result = await _generated.RebaseMigrationAsync(
            new Gen.RebaseRequest { ApplyFuzzyBinds = applyFuzzy }, ct).ConfigureAwait(false);
        return Map<RebaseResultDto>(result);
    }

    public async Task<OperationDto?> GetOperationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _generated.GetOperationAsync(id, ct).ConfigureAwait(false);
        return Map<OperationDto>(result);
    }

    private static T? Map<T>(object? source)
    {
        if (source is null) return default;
        var json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
