namespace AomrModEditor.Api.Client.Generated;

/// <summary>Partial hooks for Prefer: respond-async without hand-authored URL routing.</summary>
public partial class AomrGeneratedApiClient
{
    internal bool RequestPreferAsync;

    partial void PrepareRequest(System.Net.Http.HttpClient client, System.Net.Http.HttpRequestMessage request, string url)
    {
        if (RequestPreferAsync)
            request.Headers.TryAddWithoutValidation("Prefer", "respond-async");
    }
}
