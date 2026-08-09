using System.Net.Http.Headers;
using System.Net.Http.Json;
using AomrModEditor.Api.Contracts.V1;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Api.Tests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static string FixtureRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "synthetic"));

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AOMR_API_TOKEN", "test-token-not-for-production");
            builder.ConfigureServices(services =>
            {
                // ensure token option matches
                var existing = services.Where(d => d.ServiceType == typeof(ApiAuthOptions)).ToList();
                foreach (var d in existing) services.Remove(d);
                services.AddSingleton(new ApiAuthOptions("test-token-not-for-production"));
            });
        });
    }

    private HttpClient CreateClient(bool auth = true)
    {
        var client = _factory.CreateClient();
        if (auth)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-not-for-production");
        return client;
    }

    [Fact]
    public async Task Missing_token_returns_401()
    {
        var client = CreateClient(auth: false);
        var response = await client.GetAsync("/api/v1/sources");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_is_public()
    {
        var client = CreateClient(auth: false);
        var response = await client.GetAsync("/health");
        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Vertical_slice_via_rest()
    {
        var client = CreateClient();
        var import = await client.PostAsJsonAsync("/api/v1/sources/imports", new ImportSourceRequest(FixtureRoot));
        import.EnsureSuccessStatusCode();

        var entities = await client.GetFromJsonAsync<PagedEntitiesDto>("/api/v1/entities?q=VillagerGreek");
        entities!.Items.Should().Contain(e => e.InternalKey == "VillagerGreek");

        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-api", Guid.NewGuid().ToString("N"));
        var project = await client.PostAsJsonAsync("/api/v1/projects", new CreateProjectRequest("api", projectDir));
        project.EnsureSuccessStatusCode();

        var change = await client.PostAsJsonAsync("/api/v1/projects/current/changes", new ApplyChangeRequest(
            "VillagerGreek", "setscalar", "maxhitpoints", null, null, "100.0000", null));
        change.EnsureSuccessStatusCode();

        var gen = await client.PostAsync("/api/v1/projects/current/generations", null);
        gen.EnsureSuccessStatusCode();
        var body = await gen.Content.ReadFromJsonAsync<GenerationDto>();
        body!.ProtoModsXml.Should().Contain("<maxhitpoints mergeMode=\"replace\">100.0000</maxhitpoints>");
    }

    [Fact]
    public async Task OpenApi_document_is_available()
    {
        var client = CreateClient(auth: false);
        var response = await client.GetAsync("/openapi/v1.json");
        response.IsSuccessStatusCode.Should().BeTrue();
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("openapi");
    }
}
