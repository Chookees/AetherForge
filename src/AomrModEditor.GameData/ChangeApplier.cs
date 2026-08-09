using System.Xml.Linq;
using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;

namespace AomrModEditor.GameData;

public sealed class ChangeApplier : IChangeApplier
{
    public SourceNode Apply(SourceNode source, IEnumerable<ChangeOp> ops)
    {
        var clone = source.CloneXml();
        foreach (var op in ops)
            ApplyOne(clone.Xml, op);
        return clone;
    }

    private static void ApplyOne(XElement xml, ChangeOp op)
    {
        switch (op)
        {
            case SetScalar set:
            {
                var el = FindOrCreate(xml, set.ElementName, set.Attributes);
                el.Value = set.NewValue;
                break;
            }
            case SetAttribute sa:
            {
                var el = FindOrCreate(xml, sa.ElementName, sa.MatchAttributes);
                el.SetAttributeValue(sa.AttributeName, sa.NewValue);
                break;
            }
            case ReplaceActionValue ra:
            {
                var action = xml.Elements()
                                 .FirstOrDefault(e => e.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase)
                                                      && string.Equals(e.Element("name")?.Value, ra.ActionName, StringComparison.OrdinalIgnoreCase))
                             ?? throw new InvalidOperationException($"protoaction '{ra.ActionName}' not found.");
                var child = action.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals(ra.ChildElementName, StringComparison.OrdinalIgnoreCase)
                                         && AttrMatch(e, ra.ChildAttributes));
                if (child is null)
                {
                    child = new XElement(ra.ChildElementName);
                    if (ra.ChildAttributes is not null)
                        foreach (var (k, v) in ra.ChildAttributes)
                            child.SetAttributeValue(k, v);
                    action.Add(child);
                }

                child.Value = ra.NewValue;
                break;
            }
            case RawXmlPatch raw:
            {
                var replacement = XElement.Parse(raw.NewXml);
                xml.ReplaceNodes(replacement.Nodes());
                foreach (var a in replacement.Attributes())
                    xml.SetAttributeValue(a.Name, a.Value);
                break;
            }
            case AddRepeatedElement add:
                xml.Add(XElement.Parse(add.XmlFragment));
                break;
            case RemoveElement rem:
            {
                var match = xml.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals(rem.ElementName, StringComparison.OrdinalIgnoreCase)
                                         && AttrMatch(e, rem.MatchAttributes));
                match?.Remove();
                break;
            }
        }
    }

    private static XElement FindOrCreate(XElement parent, string name, IReadOnlyDictionary<string, string>? attrs)
    {
        var existing = parent.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase) && AttrMatch(e, attrs));
        if (existing is not null) return existing;
        var created = new XElement(name);
        if (attrs is not null)
            foreach (var (k, v) in attrs)
                created.SetAttributeValue(k, v);
        parent.Add(created);
        return created;
    }

    private static bool AttrMatch(XElement e, IReadOnlyDictionary<string, string>? attrs)
    {
        if (attrs is null || attrs.Count == 0) return true;
        foreach (var (k, v) in attrs)
        {
            var a = e.Attribute(k);
            if (a is null || !string.Equals(a.Value, v, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
