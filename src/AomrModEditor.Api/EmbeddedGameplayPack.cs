using AomrModEditor.Application.Abstractions;
using AomrModEditor.GameData;

namespace AomrModEditor.Api;

public interface IEmbeddedGameplayPack
{
    bool IsAvailable { get; }
    Stream OpenRead();
}

/// <summary>
/// Loads gzip pack from: env AOMR_EMBEDDED_PACK path, then AppBaseDirectory file,
/// then optional assembly resource named *.EmbeddedGameplayPack.json.gz.
/// </summary>
public sealed class EmbeddedGameplayPack : IEmbeddedGameplayPack
{
    private readonly Func<Stream?> _opener;

    public EmbeddedGameplayPack(Func<Stream?>? opener = null)
    {
        _opener = opener ?? DefaultOpen;
    }

    public bool IsAvailable
    {
        get
        {
            using var s = _opener();
            return s is not null;
        }
    }

    public Stream OpenRead()
        => _opener() ?? throw new FileNotFoundException("Embedded gameplay pack not found.");

    private static Stream? DefaultOpen()
    {
        var env = Environment.GetEnvironmentVariable("AOMR_EMBEDDED_PACK");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return File.OpenRead(env);

        var local = Path.Combine(AppContext.BaseDirectory, "EmbeddedGameplayPack.json.gz");
        if (File.Exists(local))
            return File.OpenRead(local);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            string? name;
            try { name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("EmbeddedGameplayPack.json.gz", StringComparison.OrdinalIgnoreCase)); }
            catch { continue; }
            if (name is null) continue;
            var stream = asm.GetManifestResourceStream(name);
            if (stream is not null) return stream;
        }

        return null;
    }
}
