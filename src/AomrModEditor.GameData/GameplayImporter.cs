using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AomrModEditor.Application.Abstractions;
using AomrModEditor.Application.Services;
using AomrModEditor.Domain;

namespace AomrModEditor.GameData;

public sealed class GameplayImporter
{
    private readonly ISecureXmlLoader _xml;

    public GameplayImporter(ISecureXmlLoader xml) => _xml = xml;

    public (InMemoryEntityCatalog Catalog, SourceFingerprint Fingerprint) ImportDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);

        var gameplay = Directory.Exists(Path.Combine(directory, "gameplay"))
            ? Path.Combine(directory, "gameplay")
            : directory;

        var nodes = new List<SourceNode>();
        var hashes = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var proto = FindFile(gameplay, "proto.xml");
        if (proto is not null)
        {
            hashes[Rel(directory, proto)] = HashFile(proto);
            nodes.AddRange(ParseProto(proto));
        }

        var techtree = FindFile(gameplay, "techtree.xml");
        if (techtree is not null)
        {
            hashes[Rel(directory, techtree)] = HashFile(techtree);
            nodes.AddRange(ParseTechtree(techtree));
        }

        var gpDir = Path.Combine(gameplay, "god_powers");
        if (Directory.Exists(gpDir))
        {
            foreach (var file in Directory.EnumerateFiles(gpDir, "*.godpowers", SearchOption.AllDirectories)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                hashes[Rel(directory, file)] = HashFile(file);
                nodes.AddRange(ParseGodPowers(file));
            }
        }

        var fingerprint = new SourceFingerprint(HashAggregate(hashes), hashes);
        return (new InMemoryEntityCatalog(nodes), fingerprint);
    }

    private IEnumerable<SourceNode> ParseProto(string path)
    {
        var doc = _xml.LoadFile(path);
        var root = doc.Root ?? throw new InvalidOperationException("proto.xml missing root.");
        foreach (var unit in root.Elements().Where(e =>
                     e.Name.LocalName.Equals("Unit", StringComparison.OrdinalIgnoreCase)))
        {
            var name = unit.Attribute("name")?.Value
                       ?? throw new InvalidOperationException("Unit missing name attribute.");
            var types = unit.Elements()
                .Where(e => e.Name.LocalName.Equals("unittype", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Value)
                .ToList();
            var typeSet = types.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var xml = new XElement(unit);
            yield return new SourceNode
            {
                InternalKey = name,
                Category = ClassifyUnit(typeSet, name),
                SourceFile = path,
                Xml = xml,
                DisplayNameId = unit.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("displaynameid", StringComparison.OrdinalIgnoreCase))
                    ?.Value,
                Pantheon = PantheonInference.InferExclusive(xml, null),
                UnitTypes = types,
                IconPath = unit.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase))
                    ?.Value
            };
        }
    }

    private IEnumerable<SourceNode> ParseTechtree(string path)
    {
        var doc = _xml.LoadFile(path);
        var root = doc.Root ?? throw new InvalidOperationException("techtree.xml missing root.");
        foreach (var tech in root.Elements().Where(e => e.Name.LocalName.Equals("Tech", StringComparison.OrdinalIgnoreCase)))
        {
            var name = tech.Attribute("name")?.Value
                       ?? throw new InvalidOperationException("Tech missing name attribute.");
            yield return new SourceNode
            {
                InternalKey = name,
                Category = EntityCategory.Technology,
                SourceFile = path,
                Xml = new XElement(tech),
                DisplayNameId = tech.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("displaynameid", StringComparison.OrdinalIgnoreCase))
                    ?.Value
            };
        }
    }

    private IEnumerable<SourceNode> ParseGodPowers(string path)
    {
        var doc = _xml.LoadFile(path);
        var root = doc.Root ?? throw new InvalidOperationException("godpowers missing root.");
        IEnumerable<XElement> powers;
        if (root.Name.LocalName.Equals("GodPower", StringComparison.OrdinalIgnoreCase)
            || root.Name.LocalName.Equals("power", StringComparison.OrdinalIgnoreCase))
        {
            powers = new[] { root };
        }
        else
        {
            powers = root.Elements().Where(e =>
                e.Name.LocalName.Equals("GodPower", StringComparison.OrdinalIgnoreCase)
                || e.Name.LocalName.Equals("power", StringComparison.OrdinalIgnoreCase));
        }

        foreach (var gp in powers)
        {
            var name = gp.Attribute("name")?.Value ?? Path.GetFileNameWithoutExtension(path);
            var xml = new XElement(gp);
            var pantheonEl = gp.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals("pantheon", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            yield return new SourceNode
            {
                InternalKey = name,
                Category = EntityCategory.GodPower,
                SourceFile = path,
                Xml = xml,
                DisplayNameId = gp.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("displaynameid", StringComparison.OrdinalIgnoreCase))
                    ?.Value,
                Pantheon = PantheonInference.InferExclusive(xml, pantheonEl),
                IconPath = gp.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase))
                    ?.Value
            };
        }
    }

    private static EntityCategory ClassifyUnit(HashSet<string> types, string name)
    {
        if (types.Contains("AbstractVillager")) return EntityCategory.Worker;
        if (types.Contains("AbstractMythUnit") || types.Contains("MythUnit")) return EntityCategory.MythUnit;
        if (types.Contains("Hero") || name.Contains("Hero", StringComparison.OrdinalIgnoreCase)) return EntityCategory.Hero;
        if (types.Contains("AbstractFarm") || types.Contains("AbstractMine")
            || name.StartsWith("Mine", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Berry", StringComparison.OrdinalIgnoreCase))
            return EntityCategory.ResourceNode;
        if (types.Contains("Building") || types.Contains("AbstractTownCenter")) return EntityCategory.Building;
        return EntityCategory.Unit;
    }

    private static string? FindFile(string root, string fileName)
        => Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();

    private static string Rel(string root, string full)
        => Path.GetRelativePath(root, full).Replace('\\', '/');

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashAggregate(SortedDictionary<string, string> hashes)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in hashes)
            sb.Append(k).Append('=').Append(v).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}
