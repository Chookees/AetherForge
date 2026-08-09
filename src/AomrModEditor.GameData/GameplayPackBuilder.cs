using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using AomrModEditor.Application.Services;
using AomrModEditor.Domain;

namespace AomrModEditor.GameData;

public sealed class GameplayPackDocument
{
    public int FormatVersion { get; set; } = 1;
    public string GeneratedAt { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public List<GameplayPackEntity> Entities { get; set; } = new();
}

public sealed class GameplayPackEntity
{
    public string Key { get; set; } = "";
    public string Category { get; set; } = "";
    public List<string> UnitTypes { get; set; } = new();
    public string? DisplayNameId { get; set; }
    public string? Pantheon { get; set; }
    public string? IconPath { get; set; }
    public string SourceFile { get; set; } = "";
    public string Xml { get; set; } = "";
}

public static class GameplayPackBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static GameplayPackDocument FromImporter(GameplayImporter importer, string directory)
    {
        var (catalog, fingerprint) = importer.ImportDirectory(directory);
        var doc = new GameplayPackDocument
        {
            FormatVersion = 1,
            GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
            SourceFingerprint = fingerprint.AggregateHash
        };
        foreach (var summary in catalog.All)
        {
            var node = catalog.GetSourceNode(summary.InternalKey)!;
            doc.Entities.Add(new GameplayPackEntity
            {
                Key = node.InternalKey,
                Category = node.Category.ToString(),
                UnitTypes = node.UnitTypes.ToList(),
                DisplayNameId = node.DisplayNameId,
                Pantheon = node.Pantheon,
                IconPath = node.IconPath,
                SourceFile = Path.GetFileName(node.SourceFile),
                Xml = node.Xml.ToString(SaveOptions.DisableFormatting)
            });
        }

        doc.Entities = doc.Entities.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList();
        return doc;
    }

    public static void WriteGzip(GameplayPackDocument doc, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var json = JsonSerializer.Serialize(doc, JsonOptions);
        using var file = File.Create(outputPath);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        var bytes = Encoding.UTF8.GetBytes(json);
        gzip.Write(bytes);
    }

    public static GameplayPackDocument ReadGzip(Stream stream)
    {
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<GameplayPackDocument>(json, JsonOptions)
               ?? throw new InvalidOperationException("Invalid gameplay pack.");
    }

    public static (InMemoryEntityCatalog Catalog, SourceFingerprint Fingerprint) ToCatalog(GameplayPackDocument doc)
    {
        var nodes = new List<SourceNode>();
        foreach (var e in doc.Entities)
        {
            if (!Enum.TryParse<EntityCategory>(e.Category, true, out var cat))
                cat = EntityCategory.Unit;
            var xml = XElement.Parse(e.Xml);
            nodes.Add(new SourceNode
            {
                InternalKey = e.Key,
                Category = cat,
                SourceFile = e.SourceFile,
                Xml = xml,
                DisplayNameId = e.DisplayNameId,
                Pantheon = string.IsNullOrWhiteSpace(e.Pantheon)
                    ? PantheonInference.InferExclusive(xml, null)
                    : e.Pantheon,
                UnitTypes = e.UnitTypes,
                IconPath = e.IconPath
            });
        }

        var hash = string.IsNullOrWhiteSpace(doc.SourceFingerprint)
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(doc.Entities.Select(x => x.Key))))).ToLowerInvariant()
            : doc.SourceFingerprint;
        var fp = new SourceFingerprint(hash, new Dictionary<string, string> { ["embedded"] = hash });
        return (new InMemoryEntityCatalog(nodes), fp);
    }
}
