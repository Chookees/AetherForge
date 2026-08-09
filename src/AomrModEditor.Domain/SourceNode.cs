using System.Xml.Linq;

namespace AomrModEditor.Domain;

/// <summary>Lossless hybrid: retains original XML clone plus identity metadata.</summary>
public sealed class SourceNode
{
    public required string InternalKey { get; init; }
    public required EntityCategory Category { get; init; }
    public required string SourceFile { get; init; }
    public required XElement Xml { get; init; }
    public string? DisplayNameId { get; init; }
    public string? Pantheon { get; init; }
    public string? DlcTag { get; init; }
    public IReadOnlyList<string> UnitTypes { get; init; } = Array.Empty<string>();
    public string? IconPath { get; init; }

    public SourceNode CloneXml() => new()
    {
        InternalKey = InternalKey,
        Category = Category,
        SourceFile = SourceFile,
        Xml = new XElement(Xml),
        DisplayNameId = DisplayNameId,
        Pantheon = Pantheon,
        DlcTag = DlcTag,
        UnitTypes = UnitTypes,
        IconPath = IconPath
    };
}