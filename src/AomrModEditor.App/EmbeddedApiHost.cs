using AomrModEditor.Api;
using AomrModEditor.Api.Client;
using Microsoft.AspNetCore.Builder;

namespace AomrModEditor.App;

/// <summary>Embeds Kestrel loopback API in-process (LIM-02). Token never logged.</summary>
public sealed class EmbeddedApiHost : IAsyncDisposable
{
    private WebApplication? _app;

    public string BaseUrl { get; private set; } = "";
    public AomrApiClient? Client { get; private set; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var (app, baseUrl, token) = await ApiHostBootstrap.StartEmbeddedAsync(ct).ConfigureAwait(false);
        _app = app;
        BaseUrl = baseUrl;
        Client = AomrApiClient.Create(baseUrl, token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }

        Client = null;
    }
}
