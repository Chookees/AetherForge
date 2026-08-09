using System.Text;
using System.Xml;
using AomrModEditor.GameData;
using FluentAssertions;

namespace AomrModEditor.Tests;

public class SecureXmlAndPathTests
{
    [Fact]
    public void SecureXmlLoader_rejects_dtd()
    {
        var xml = """<?xml version="1.0"?><!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><root>&xxe;</root>""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var loader = new SecureXmlLoader();
        var act = () => loader.Load(stream);
        act.Should().Throw<XmlException>();
    }

    [Fact]
    public void PathSafety_rejects_traversal()
    {
        var safety = new PathSafety();
        var root = Path.Combine(Path.GetTempPath(), "aomr-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var act = () => safety.NormalizeAndValidate(root, "..\\..\\Windows\\System32");
        act.Should().Throw<InvalidOperationException>().WithMessage("*traversal*");
    }
}
