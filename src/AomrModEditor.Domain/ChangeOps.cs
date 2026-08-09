namespace AomrModEditor.Domain;

public abstract record ChangeOp(
    Guid Id,
    string EntityKey,
    DateTimeOffset CreatedAtUtc);

public sealed record SetScalar(
    Guid Id,
    string EntityKey,
    DateTimeOffset CreatedAtUtc,
    string ElementName,
    string? OldValue,
    string NewValue,
    IReadOnlyDictionary<string, string>? Attributes = null) : ChangeOp(Id, EntityKey, CreatedAtUtc);

public sealed record SetAttribute(
    Guid Id,
    string EntityKey,
    DateTimeOffset CreatedAtUtc,
    string ElementName,
    string AttributeName,
    string? OldValue,
    string NewValue,
    IReadOnlyDictionary<string, string>? MatchAttributes = null) : ChangeOp(Id, EntityKey, CreatedAtUtc);

public sealed record ReplaceActionValue(
    Guid Id,
    string EntityKey,
    DateTimeOffset CreatedAtUtc,
    string ActionName,
    string ChildElementName,
    string? OldValue,
    string NewValue,
    IReadOnlyDictionary<string, string>? ChildAttributes = null) : ChangeOp(Id, EntityKey, CreatedAtUtc);

public sealed record AddEntity(
    Guid Id,
    string EntityKey,
    DateTimeOffset CreatedAtUtc,
    string XmlFragment) : ChangeOp(Id, EntityKey, CreatedAtUtc);

public sealed record CloneEntity(
    Guid Id,
    string EntityKey,
    DateTimeOffset CreatedAtUtc,
    string SourceEntityKey,
    string NewEntityKey) : ChangeOp(Id, EntityKey, CreatedAtUtc);

public sealed record SetReference(
    Guid Id,
    string EntityKey,
    DateTimeOffset CreatedAtUtc,
    string Path,
    string? OldValue,
    string NewValue) : ChangeOp(Id, EntityKey, CreatedAtUtc);

public sealed record AddString(
    Guid Id,
    string EntityKey,
    DateTimeOffset CreatedAtUtc,
    string StringId,
    string Locale,
    string Value) : ChangeOp(Id, EntityKey, CreatedAtUtc);

public sealed record RawXmlPatch(
    Guid Id,
    string EntityKey,
    DateTimeOffset CreatedAtUtc,
    string OldXml,
    string NewXml) : ChangeOp(Id, EntityKey, CreatedAtUtc);

public sealed record AddRepeatedElement(
    Guid Id,
    string EntityKey,
    DateTimeOffset CreatedAtUtc,
    string ElementName,
    string XmlFragment) : ChangeOp(Id, EntityKey, CreatedAtUtc);

public sealed record RemoveElement(
    Guid Id,
    string EntityKey,
    DateTimeOffset CreatedAtUtc,
    string ElementName,
    IReadOnlyDictionary<string, string>? MatchAttributes = null) : ChangeOp(Id, EntityKey, CreatedAtUtc);