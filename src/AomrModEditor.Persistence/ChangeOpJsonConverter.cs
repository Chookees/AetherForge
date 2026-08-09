using System.Text.Json;
using System.Text.Json.Serialization;
using AomrModEditor.Domain;

namespace AomrModEditor.Persistence;

public sealed class ChangeOpJsonConverter : JsonConverter<ChangeOp>
{
    public override ChangeOp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();
        var id = root.GetProperty("id").GetGuid();
        var entityKey = root.GetProperty("entityKey").GetString()!;
        var created = root.GetProperty("createdAtUtc").GetDateTimeOffset();
        return type switch
        {
            nameof(SetScalar) => new SetScalar(id, entityKey, created,
                root.GetProperty("elementName").GetString()!,
                root.TryGetProperty("oldValue", out var ov) ? ov.GetString() : null,
                root.GetProperty("newValue").GetString()!,
                ReadStringMap(root, "attributes")),
            nameof(SetAttribute) => new SetAttribute(id, entityKey, created,
                root.GetProperty("elementName").GetString()!,
                root.GetProperty("attributeName").GetString()!,
                root.TryGetProperty("oldValue", out var ova) ? ova.GetString() : null,
                root.GetProperty("newValue").GetString()!,
                ReadStringMap(root, "matchAttributes")),
            nameof(ReplaceActionValue) => new ReplaceActionValue(id, entityKey, created,
                root.GetProperty("actionName").GetString()!,
                root.GetProperty("childElementName").GetString()!,
                root.TryGetProperty("oldValue", out var ovr) ? ovr.GetString() : null,
                root.GetProperty("newValue").GetString()!,
                ReadStringMap(root, "childAttributes")),
            _ => throw new NotSupportedException($"Unknown change type {type}")
        };
    }

    public override void Write(Utf8JsonWriter writer, ChangeOp value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.GetType().Name);
        writer.WriteString("id", value.Id);
        writer.WriteString("entityKey", value.EntityKey);
        writer.WriteString("createdAtUtc", value.CreatedAtUtc);
        switch (value)
        {
            case SetScalar s:
                writer.WriteString("elementName", s.ElementName);
                writer.WriteString("oldValue", s.OldValue);
                writer.WriteString("newValue", s.NewValue);
                WriteStringMap(writer, "attributes", s.Attributes);
                break;
            case SetAttribute sa:
                writer.WriteString("elementName", sa.ElementName);
                writer.WriteString("attributeName", sa.AttributeName);
                writer.WriteString("oldValue", sa.OldValue);
                writer.WriteString("newValue", sa.NewValue);
                WriteStringMap(writer, "matchAttributes", sa.MatchAttributes);
                break;
            case ReplaceActionValue r:
                writer.WriteString("actionName", r.ActionName);
                writer.WriteString("childElementName", r.ChildElementName);
                writer.WriteString("oldValue", r.OldValue);
                writer.WriteString("newValue", r.NewValue);
                WriteStringMap(writer, "childAttributes", r.ChildAttributes);
                break;
        }

        writer.WriteEndObject();
    }

    private static IReadOnlyDictionary<string, string>? ReadStringMap(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in el.EnumerateObject())
            map[prop.Name] = prop.Value.GetString() ?? "";
        return map.Count == 0 ? null : map;
    }

    private static void WriteStringMap(Utf8JsonWriter writer, string propertyName, IReadOnlyDictionary<string, string>? map)
    {
        if (map is null || map.Count == 0) return;
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        foreach (var (k, v) in map.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            writer.WriteString(k, v);
        writer.WriteEndObject();
    }
}
