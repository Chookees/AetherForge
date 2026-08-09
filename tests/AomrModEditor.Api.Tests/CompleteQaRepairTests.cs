using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AomrModEditor.Api.Client;
using AomrModEditor.Api.Contracts.V1;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Api.Tests;

/// <summary>Regression tests for QA-F001..QA-F004 (complete-run repair cycle 1).</summary>
public class CompleteQaRepairTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string FixtureRoot =>
        Path.GetFullPath(Path.Combine(RepoRoot, "fixtures", "synthetic"));

    public CompleteQaRepairTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AOMR_API_TOKEN", "repair-token");
            builder.ConfigureServices(services =>
            {
                var existing = services.Where(d => d.ServiceType == typeof(ApiAuthOptions)).ToList();
                foreach (var d in existing) services.Remove(d);
                services.AddSingleton(new ApiAuthOptions("repair-token"));
            });
        });
    }

    private HttpClient CreateHttp()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "repair-token");
        return client;
    }

    [Fact]
    public void QA_F001_facade_invokes_nswag_generated_operations()
    {
        var facadePath = Path.Combine(RepoRoot, "src", "AomrModEditor.Api.Client", "AomrApiClient.cs");
        var mainPath = Path.Combine(RepoRoot, "src", "AomrModEditor.App", "MainWindow.cs");
        File.Exists(facadePath).Should().BeTrue();
        var facade = File.ReadAllText(facadePath);
        var generatedCalls = Regex.Matches(facade, @"_generated\.\w+\(");
        generatedCalls.Count.Should().BeGreaterThan(
            5,
            "LIM-01 requires facade convenience methods to delegate to AomrGeneratedApiClient operations");
        facade.Should().NotContain("PostAsJsonAsync(", "hand-authored URL POST helpers must not remain as the final path");
        facade.Should().NotContain("GetFromJsonAsync<", "hand-authored URL GET helpers must not remain as the final path");

        var main = File.ReadAllText(mainPath);
        main.Should().NotContain("api/v1/", "App must not hard-code REST paths; use typed client only");
        main.Should().NotContain("new HttpClient", "App must not bypass typed client");
    }

    [Fact]
    public async Task QA_F001_typed_client_vertical_slice_uses_generated_path()
    {
        var http = CreateHttp();
        var client = new AomrApiClient(http);
        client.Generated.Should().NotBeNull();

        var snap = await client.ImportSourceAsync(FixtureRoot);
        snap.Should().NotBeNull();
        snap!.EntityCount.Should().BeGreaterThan(0);

        var entities = await client.GetEntitiesAsync("VillagerGreek");
        entities!.Items.Should().Contain(i => i.InternalKey == "VillagerGreek");

        var projectDir = Path.Combine(Path.GetTempPath(), "aomr-repair-f001", Guid.NewGuid().ToString("N"));
        await client.CreateProjectAsync("repair", projectDir);
        await client.ApplyChangeAsync(new ApplyChangeRequest(
            "VillagerGreek", "setscalar", "maxhitpoints", null, null, "100.0000", null));
        var gen = await client.GenerateAsync();
        gen!.ProtoModsXml.Should().Contain("100.0000");
    }

    [Fact]
    public async Task QA_F002_missing_import_directory_returns_problem_details_4xx()
    {
        var client = CreateHttp();
        var missing = Path.Combine(Path.GetTempPath(), "aomr-does-not-exist-" + Guid.NewGuid().ToString("N"));
        Directory.Exists(missing).Should().BeFalse();

        var response = await client.PostAsJsonAsync("/api/v1/sources/imports", new ImportSourceRequest(missing));
        ((int)response.StatusCode).Should().BeInRange(400, 499);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().MatchRegex("(?i)(title|detail|status|type)");
        body.Should().MatchRegex("(?i)(directory|not found|path)");
    }

    [Fact]
    public void QA_F003_unit_editor_shows_always_visible_provenance_strip()
    {
        var mainPath = Path.Combine(RepoRoot, "src", "AomrModEditor.App", "MainWindow.cs");
        var src = File.ReadAllText(mainPath);
        src.Should().Contain("ProvenanceStrip");
        src.Should().Contain("\"Base\"");
        src.Should().Contain("\"Override\"");
        src.Should().Contain("\"Effective\"");
        src.Should().Contain("BuildAdaptiveEditor");
        src.Should().Contain("BuildFieldEditor");
        var buildIdx = src.IndexOf("private Control BuildFieldEditor", StringComparison.Ordinal);
        buildIdx.Should().BeGreaterThan(0);
        var endIdx = src.IndexOf("private static Control ProvenanceStrip", buildIdx, StringComparison.Ordinal);
        endIdx.Should().BeGreaterThan(buildIdx);
        var fieldEditor = src[buildIdx..endIdx];
        fieldEditor.Should().Contain("ProvenanceStrip");
        fieldEditor.Should().Contain("provenanceHost");
    }

    [Fact]
    public void QA_F004_setup_requires_disclaimer_accept_before_import()
    {
        var mainPath = Path.Combine(RepoRoot, "src", "AomrModEditor.App", "MainWindow.cs");
        var src = File.ReadAllText(mainPath);
        src.Should().Contain("private Control BuildSetup");
        src.Should().MatchRegex("(?i)disclaimer|unofficial");
        src.Should().Contain("CheckBox");
        src.Should().MatchRegex("(?i)Decline|Exit");
        var setupIdx = src.IndexOf("private Control BuildSetup", StringComparison.Ordinal);
        var setup = src[setupIdx..Math.Min(src.Length, setupIdx + 2500)];
        setup.Should().MatchRegex(@"IsEnabled\s*=\s*false");
        setup.Should().MatchRegex("(?i)accept");
    }
}
