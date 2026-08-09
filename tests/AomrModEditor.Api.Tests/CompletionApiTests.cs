using System.Net.Http.Headers;
using System.Net.Http.Json;
using AomrModEditor.Api.Client;
using AomrModEditor.Api.Contracts.V1;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Api.Tests;

public class CompletionApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static string FixtureRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "synthetic"));

    public CompletionApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AOMR_API_TOKEN", "complete-token");
            builder.ConfigureServices(services =>
            {
                var existing = services.Where(d => d.ServiceType == typeof(ApiAuthOptions)).ToList();
                foreach (var d in existing) services.Remove(d);
                services.AddSingleton(new ApiAuthOptions("complete-token"));
            });
        });
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "complete-token");
        return client;
    }

    [Fact]
    public async Task LIM05_async_import_returns_202_and_operation()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/sources/imports?async=1", new ImportSourceRequest(FixtureRoot));
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
        var op = await response.Content.ReadFromJsonAsync<OperationDto>();
        op.Should().NotBeNull();
        op!.Kind.Should().Be("import");

        OperationDto? polled = null;
        for (var i = 0; i < 50; i++)
        {
            polled = await client.GetFromJsonAsync<OperationDto>($"/api/v1/operations/{op.Id}");
            if (polled?.State is "Succeeded" or "Failed") break;
            await Task.Delay(50);
        }

        polled!.State.Should().Be("Succeeded");
    }

    [Fact]
    public async Task LIM07_effective_stats_endpoint()
    {
        var client = CreateClient();
        await client.PostAsJsonAsync("/api/v1/sources/imports", new ImportSourceRequest(FixtureRoot));
        var stats = await client.GetFromJsonAsync<EffectiveStatsDto>("/api/v1/entities/VillagerGreek/effective-stats");
        stats!.Effective.Should().ContainKey("maxhitpoints");
        stats.Steps.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LIM02_embedded_host_starts_loopback_without_logging_token()
    {
        var (app, baseUrl, token) = await ApiHostBootstrap.StartEmbeddedAsync();
        await using (app)
        {
            baseUrl.Should().Contain("127.0.0.1");
            token.Should().NotBeNullOrWhiteSpace();
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var health = await http.GetAsync("health");
            health.IsSuccessStatusCode.Should().BeTrue();
            using var anon = new HttpClient { BaseAddress = http.BaseAddress };
            var unauthorized = await anon.GetAsync("api/v1/sources");
            unauthorized.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
            var typed = AomrApiClient.Create(baseUrl, token);
            typed.Generated.Should().NotBeNull();
            await app.StopAsync();
        }
    }
}
