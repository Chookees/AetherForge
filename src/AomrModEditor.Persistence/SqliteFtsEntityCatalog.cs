using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;
using AomrModEditor.GameData;
using Microsoft.Data.Sqlite;

namespace AomrModEditor.Persistence;

/// <summary>
/// Hot-path entity search via SQLite FTS5; entity XML retained in memory for open latency.
/// </summary>
public sealed class SqliteFtsEntityCatalog : IEntityCatalog, IDisposable
{
    private readonly Dictionary<string, SourceNode> _nodes;
    private readonly List<EntitySummary> _all;
    private readonly SqliteConnection _connection;

    public SqliteFtsEntityCatalog(IEnumerable<SourceNode> nodes, string? databasePath = null)
    {
        var deduped = EntityKeyDeduper.Deduplicate(nodes);
        _nodes = deduped.ToDictionary(n => n.InternalKey, n => n, StringComparer.OrdinalIgnoreCase);
        _all = _nodes.Values
            .Select(ToSummary)
            .OrderBy(e => e.InternalKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Prefer isolated in-memory FTS (avoids cross-run file locks / duplicate appends).
        // Optional file path kept for durable index scenarios.
        if (string.IsNullOrWhiteSpace(databasePath) || databasePath == ":memory:")
        {
            _connection = new SqliteConnection("Data Source=:memory:");
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            if (File.Exists(databasePath))
                File.Delete(databasePath);
            _connection = new SqliteConnection($"Data Source={databasePath}");
        }

        _connection.Open();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE VIRTUAL TABLE entities_fts USING fts5(
                  internal_key,
                  category,
                  display_name,
                  pantheon,
                  source_file,
                  unit_types,
                  tokenize = 'porter');
                """;
            cmd.ExecuteNonQuery();
        }

        using var tx = _connection.BeginTransaction();
        foreach (var e in _all)
        {
            using var insert = _connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO entities_fts(internal_key, category, display_name, pantheon, source_file, unit_types)
                VALUES ($k, $c, $d, $p, $s, $u);
                """;
            insert.Parameters.AddWithValue("$k", e.InternalKey);
            insert.Parameters.AddWithValue("$c", e.Category.ToString());
            insert.Parameters.AddWithValue("$d", (object?)e.DisplayNameId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$p", (object?)e.Pantheon ?? DBNull.Value);
            insert.Parameters.AddWithValue("$s", e.SourceFile);
            insert.Parameters.AddWithValue("$u", string.Join(' ', e.UnitTypes));
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public static SqliteFtsEntityCatalog From(IEntityCatalog source, string? databasePath = null)
    {
        var nodes = source.All
            .Select(s => source.GetSourceNode(s.InternalKey)!)
            .Where(n => n is not null);
        return new SqliteFtsEntityCatalog(nodes, databasePath);
    }

    public IReadOnlyList<EntitySummary> All => _all;

    public EntityDetail? GetByInternalKey(string internalKey)
    {
        if (!_nodes.TryGetValue(internalKey, out var node)) return null;
        var fields = ExtractFields(node);
        var provenance = fields.ToDictionary(
            kv => kv.Key,
            kv => new FieldProvenance(ProvenanceKind.Base, node.SourceFile, kv.Key, kv.Value, null, kv.Value),
            StringComparer.OrdinalIgnoreCase);
        return new EntityDetail(ToSummary(node), node.Xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting), provenance, fields);
    }

    public SourceNode? GetSourceNode(string internalKey)
        => _nodes.TryGetValue(internalKey, out var n) ? n : null;

    public IReadOnlyList<EntitySummary> Search(string? query, EntityCategory? category = null, int take = 100, string? unitType = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            IEnumerable<EntitySummary> q = _all;
            if (category is not null) q = q.Where(e => e.Category == category);
            if (!string.IsNullOrWhiteSpace(unitType))
                q = q.Where(e => e.UnitTypes.Any(t => t.Equals(unitType, StringComparison.OrdinalIgnoreCase)));
            return q.Take(take).ToList();
        }

        using var cmd = _connection.CreateCommand();
        var token = EscapeFts(query.Trim());
        cmd.CommandText = """
            SELECT internal_key FROM entities_fts
            WHERE entities_fts MATCH $q
            LIMIT $take;
            """;
        cmd.Parameters.AddWithValue("$q", $"{token}*");
        cmd.Parameters.AddWithValue("$take", take);

        var keys = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                keys.Add(reader.GetString(0));
        }

        IEnumerable<EntitySummary> results = keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(k => _all.FirstOrDefault(e => e.InternalKey.Equals(k, StringComparison.OrdinalIgnoreCase)))
            .Where(e => e is not null)
            .Cast<EntitySummary>();

        if (category is not null)
            results = results.Where(e => e.Category == category);
        if (!string.IsNullOrWhiteSpace(unitType))
            results = results.Where(e => e.UnitTypes.Any(t => t.Equals(unitType, StringComparison.OrdinalIgnoreCase)));

        var list = results.Take(take).ToList();
        if (list.Count > 0) return list;

        return _all
            .Where(e => (category is null || e.Category == category)
                        && (string.IsNullOrWhiteSpace(unitType)
                            || e.UnitTypes.Any(t => t.Equals(unitType, StringComparison.OrdinalIgnoreCase)))
                        && (e.InternalKey.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || (e.DisplayNameId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                            || (e.Pantheon?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                            || e.UnitTypes.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))))
            .Take(take)
            .ToList();
    }

    public void Dispose() => _connection.Dispose();

    private static EntitySummary ToSummary(SourceNode node)
        => new(node.InternalKey, node.Category, node.SourceFile, node.DisplayNameId, node.Pantheon, node.UnitTypes, node.IconPath);

    private static string EscapeFts(string input)
    {
        var cleaned = new string(input.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "\"\"" : $"\"{cleaned.Replace("\"", "\"\"")}\"";
    }

    private static Dictionary<string, string?> ExtractFields(SourceNode node)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in node.Xml.Elements())
        {
            if (child.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!child.HasAttributes)
                dict[child.Name.LocalName] = child.Value;
            else
            {
                var attrKey = string.Join(',', child.Attributes().Select(a => $"{a.Name.LocalName}={a.Value}"));
                dict[$"{child.Name.LocalName}[{attrKey}]"] = child.Value;
            }
        }

        return dict;
    }
}
