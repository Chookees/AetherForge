using System.Xml;
using System.Xml.Linq;
using AomrModEditor.Application.Abstractions;

namespace AomrModEditor.GameData;

public sealed class SecureXmlLoader : ISecureXmlLoader
{
    public const long DefaultMaxCharacters = 50_000_000;

    private readonly long _maxCharacters;

    public SecureXmlLoader(long maxCharacters = DefaultMaxCharacters) => _maxCharacters = maxCharacters;

    public XDocument LoadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream, path);
    }

    public XDocument Load(Stream stream, string? nameHint = null)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = _maxCharacters,
            MaxCharactersFromEntities = 0,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false
        };

        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    }
}