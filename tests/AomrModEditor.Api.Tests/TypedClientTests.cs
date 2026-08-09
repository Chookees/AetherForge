using AomrModEditor.Api.Client;
using AomrModEditor.Api.Contracts.V1;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Api.Tests;

public class TypedClientTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static string FixtureRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "synthetic"));

    public TypedClientTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AOMR_API_TOKEN", "client-token");
            builder.ConfigureServices(services =>
            {
                var existing = services.Where(d => d.ServiceType == typeof(ApiAuthOptions)).ToList();
                foreach (var d in existing) services.Remove(d);
                services.AddSingleton(new ApiAuthOptions("client-token"));
            });
        });
    }

    [Fact]
    public async Task Avalonia_client_can_complete_vertical_slice()
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "client-token");
        var client = new AomrApiClient(http);

        await client.ImportSourceAsync(FixtureRoot);
        var entities = await client.GetEntitiesAsync("VillagerGreek");
        entities!.Items.Should().Contain(i => i.InternalKey == "VillagerGreek");

        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-client", Guid.NewGuid().ToString("N"));
        await client.CreateProjectAsync("client", projectDir);
        await client.ApplyChangeAsync(new ApplyChangeRequest(
            "VillagerGreek", "setscalar", "maxhitpoints", null, null, "100.0000", null));
        var gen = await client.GenerateAsync();
        gen!.ProtoModsXml.Should().Contain("100.0000");
    }
}
