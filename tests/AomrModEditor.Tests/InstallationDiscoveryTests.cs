using AomrModEditor.GameData;
using FluentAssertions;

namespace AomrModEditor.Tests;

public class InstallationDiscoveryTests
{
    [Fact]
    public void Discover_finds_local_mods_under_user_games_when_present()
    {
        var discovery = new InstallationDiscovery();
        var result = discovery.Discover();

        result.Should().NotBeNull();
        result.Summary.Should().NotBeNullOrWhiteSpace();

        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Games",
            "Age of Mythology Retold");

        if (!Directory.Exists(expectedRoot))
        {
            result.LocalModsRoots.Should().BeEmpty();
            return;
        }

        // If the machine has Retold Games data, every discovered mods\local must sit under it.
        foreach (var mods in result.LocalModsRoots)
        {
            Path.GetFullPath(mods.Path)
                .StartsWith(Path.GetFullPath(expectedRoot), StringComparison.OrdinalIgnoreCase)
                .Should().BeTrue();
            Directory.Exists(mods.Path).Should().BeTrue();
            Path.GetFileName(mods.Path).Should().Be("local");
        }

        if (result.PreferredLocalModsPath is not null)
            Directory.Exists(result.PreferredLocalModsPath).Should().BeTrue();
    }

    [Fact]
    public void Discover_hint_path_is_reported()
    {
        var hint = Path.Combine(Path.GetTempPath(), "aomr-discover-hint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(hint);
        try
        {
            var result = new InstallationDiscovery().Discover(hint);
            result.Installations.Should().Contain(i =>
                string.Equals(Path.GetFullPath(i.Path), Path.GetFullPath(hint), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(hint, recursive: true);
        }
    }
}
