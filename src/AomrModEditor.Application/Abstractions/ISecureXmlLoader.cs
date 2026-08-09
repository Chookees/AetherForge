using System.Xml.Linq;

namespace AomrModEditor.Application.Abstractions;

public interface ISecureXmlLoader
{
    XDocument LoadFile(string path);
    XDocument Load(Stream stream, string? nameHint = null);
}