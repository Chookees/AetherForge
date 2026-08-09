using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AomrModEditor.Api.Contracts.V1;
using AomrModEditor.GameData;
using AomrModEditor.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Api.Tests;

public class RestructureApiTests
{
    private static string FixtureRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "synthetic"));

    private static (WebApplicationFactory<Program> Factory, string PackPath) CreateFactory()
    {
        var packPath = Path.Combine(Path.GetTempPath(), "aomr-test-pack-" + Guid.NewGuid().ToString("N") + ".json.gz");
        var importer = new GameplayImporter(new SecureXmlLoader());
        var doc = GameplayPackBuilder.FromImporter(importer, FixtureRoot);
        GameplayPackBuilder.WriteGzip(doc, packPath);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AOMR_API_TOKEN", "test-token-not-for-production");
            builder.ConfigureServices(services =>
            {
                var existing = services.Where(d => d.ServiceType == typeof(ApiAuthOptions)).ToList();
                foreach (var d in existing) services.Remove(d);
                services.AddSingleton(new ApiAuthOptions("test-token-not-for-production"));

                var packs = services.Where(d => d.ServiceType == typeof(IEmbeddedGameplayPack)).ToList();
                foreach (var d in packs) services.Remove(d);
                services.AddSingleton<IEmbeddedGameplayPack>(new EmbeddedGameplayPack(() => File.OpenRead(packPath)));
            });
        });
        return (factory, packPath);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-not-for-production");
        return client;
    }

    [Fact]
    public async Task Entities_without_source_returns_409_problem()
    {
        var (factory, packPath) = CreateFactory();
        await using (factory)
        {
            var client = CreateClient(factory);
            var response = await client.GetAsync("/api/v1/entities");
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("urn:aomr:source-not-loaded");
        }

        File.Delete(packPath);
    }

    [Fact]
    public async Task Embedded_source_then_editor_schema_for_BerryBush()
    {
        var (factory, packPath) = CreateFactory();
        await using (factory)
        {
            var client = CreateClient(factory);
            var load = await client.PostAsync("/api/v1/sources/embedded", null);
            load.EnsureSuccessStatusCode();
            var snap = await load.Content.ReadFromJsonAsync<SourceSnapshotDto>();
            snap!.EntityCount.Should().BeGreaterThan(0);

            var schema = await client.GetFromJsonAsync<EntityEditorSchemaDto>("/api/v1/entities/BerryBush/editor-schema");
            schema!.Tabs.Select(t => t.Id).Should().Equal("overview", "initialResource", "rawXml");

            var entities = await client.GetFromJsonAsync<PagedEntitiesDto>("/api/v1/entities?take=10000");
            entities!.Items.Should().NotContain(e => e.Category == "Technology");
            entities.Items.Should().Contain(e => e.InternalKey == "BerryBush" && e.UnitTypes!.Contains("AbstractFarm"));
            entities.Items.Should().Contain(e => e.InternalKey == "Toxotes" && e.ExclusivePantheon == "Greek");
            entities.Items.Should().Contain(e => e.InternalKey == "House" && e.ExclusivePantheon == null);

            var withTech = await client.GetFromJsonAsync<PagedEntitiesDto>("/api/v1/entities?take=10000&includeTechnologies=true");
            withTech!.Items.Should().Contain(e => e.Category == "Technology");
        }

        File.Delete(packPath);
    }
}
