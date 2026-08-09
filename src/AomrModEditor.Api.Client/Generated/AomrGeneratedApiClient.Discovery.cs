using System.Text.Json;

namespace AomrModEditor.Api.Client.Generated;

public partial class AomrGeneratedApiClient
{
    private static readonly JsonSerializerOptions DiscoveryJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
    /// <returns>OK</returns>
    /// <exception cref="AomrApiException">A server side error occurred.</exception>
    public virtual async Task<DiscoveryResultDto> DiscoverInstallationsAsync(
        DiscoverInstallationsRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        var client_ = _httpClient;
        using var request_ = new HttpRequestMessage();
        var json_ = JsonSerializer.Serialize(body ?? new DiscoverInstallationsRequest(), DiscoveryJson);
        request_.Content = new StringContent(json_, System.Text.Encoding.UTF8, "application/json");
        request_.Method = new HttpMethod("POST");
        request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

        var urlBuilder_ = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(_baseUrl)) urlBuilder_.Append(_baseUrl);
        urlBuilder_.Append("api/v1/installations/discover");

        PrepareRequest(client_, request_, urlBuilder_);
        var url_ = urlBuilder_.ToString();
        request_.RequestUri = new Uri(url_, UriKind.RelativeOrAbsolute);
        PrepareRequest(client_, request_, url_);

        using var response_ = await client_.SendAsync(request_, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var headers_ = response_.Headers.ToDictionary(h => h.Key, h => h.Value);
        if (response_.Content?.Headers != null)
        {
            foreach (var item_ in response_.Content.Headers)
                headers_[item_.Key] = item_.Value;
        }

        ProcessResponse(client_, response_);
        var status_ = (int)response_.StatusCode;
        if (status_ == 200)
        {
            var objectResponse_ = await ReadObjectResponseAsync<DiscoveryResultDto>(response_, headers_, cancellationToken).ConfigureAwait(false);
            if (objectResponse_.Object == null)
                throw new AomrApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
            return objectResponse_.Object;
        }

        var responseData_ = response_.Content == null ? null : await ReadAsStringAsync(response_.Content, cancellationToken).ConfigureAwait(false);
        throw new AomrApiException("The HTTP status code of the response was not expected (" + status_ + ").", status_, responseData_, headers_, null);
    }

    /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
    /// <returns>OK</returns>
    /// <exception cref="AomrApiException">A server side error occurred.</exception>
    public virtual async Task<InstallDto> InstallAsync(
        InstallRequest body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        var client_ = _httpClient;
        using var request_ = new HttpRequestMessage();
        var json_ = JsonSerializer.Serialize(body, DiscoveryJson);
        request_.Content = new StringContent(json_, System.Text.Encoding.UTF8, "application/json");
        request_.Method = new HttpMethod("POST");
        request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

        var urlBuilder_ = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(_baseUrl)) urlBuilder_.Append(_baseUrl);
        urlBuilder_.Append("api/v1/projects/current/installs");

        PrepareRequest(client_, request_, urlBuilder_);
        var url_ = urlBuilder_.ToString();
        request_.RequestUri = new Uri(url_, UriKind.RelativeOrAbsolute);
        PrepareRequest(client_, request_, url_);

        using var response_ = await client_.SendAsync(request_, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var headers_ = response_.Headers.ToDictionary(h => h.Key, h => h.Value);
        if (response_.Content?.Headers != null)
        {
            foreach (var item_ in response_.Content.Headers)
                headers_[item_.Key] = item_.Value;
        }

        ProcessResponse(client_, response_);
        var status_ = (int)response_.StatusCode;
        if (status_ == 200)
        {
            var objectResponse_ = await ReadObjectResponseAsync<InstallDto>(response_, headers_, cancellationToken).ConfigureAwait(false);
            if (objectResponse_.Object == null)
                throw new AomrApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
            return objectResponse_.Object;
        }

        var responseData_ = response_.Content == null ? null : await ReadAsStringAsync(response_.Content, cancellationToken).ConfigureAwait(false);
        throw new AomrApiException("The HTTP status code of the response was not expected (" + status_ + ").", status_, responseData_, headers_, null);
    }
}

public partial class DiscoverInstallationsRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("hintPath")]
    public string? HintPath { get; set; }
}

public partial class InstallationDto
{
    [System.Text.Json.Serialization.JsonPropertyName("path")]
    public string? Path { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string? Status { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("isConfirmed")]
    public bool IsConfirmed { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("kind")]
    public string? Kind { get; set; }
}

public partial class LocalModsRootDto
{
    [System.Text.Json.Serialization.JsonPropertyName("path")]
    public string? Path { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("accountFolderName")]
    public string? AccountFolderName { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("existingModCount")]
    public int ExistingModCount { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("lastWriteUtc")]
    public System.DateTimeOffset? LastWriteUtc { get; set; }
}

public partial class DiscoveryResultDto
{
    [System.Text.Json.Serialization.JsonPropertyName("installations")]
    public System.Collections.Generic.ICollection<InstallationDto>? Installations { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("localModsRoots")]
    public System.Collections.Generic.ICollection<LocalModsRootDto>? LocalModsRoots { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("preferredLocalModsPath")]
    public string? PreferredLocalModsPath { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("preferredInstallPath")]
    public string? PreferredInstallPath { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("summary")]
    public string? Summary { get; set; }
}

public partial class InstallRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("modsRoot")]
    public string? ModsRoot { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("modFolderName")]
    public string? ModFolderName { get; set; }
}

public partial class InstallDto
{
    [System.Text.Json.Serialization.JsonPropertyName("targetDirectory")]
    public string? TargetDirectory { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("copiedFiles")]
    public System.Collections.Generic.ICollection<string>? CopiedFiles { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("backupDirectory")]
    public string? BackupDirectory { get; set; }
}
