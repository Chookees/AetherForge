using System.Xml.Linq;
using AomrModEditor.Application.Services;
using AomrModEditor.Domain;
using FluentAssertions;

namespace AomrModEditor.Tests;

public class ProtoActionClassifierTests
{
    [Theory]
    [InlineData("Build", ProtoActionKind.Utility)]
    [InlineData("Repair", ProtoActionKind.Utility)]
    [InlineData("Gather", ProtoActionKind.Utility)]
    [InlineData("Hunting", ProtoActionKind.Utility)]
    [InlineData("Pickup", ProtoActionKind.Utility)]
    [InlineData("DropOff", ProtoActionKind.Utility)]
    [InlineData("Heal", ProtoActionKind.Utility)]
    [InlineData("AutoGatherFood", ProtoActionKind.Utility)]
    public void Known_work_actions_are_utility(string name, ProtoActionKind expected)
    {
        var xml = XElement.Parse($"""
            <protoaction>
              <name>{name}</name>
              <rate type="Building">1.0</rate>
            </protoaction>
            """);
        ProtoActionClassifier.Classify(xml).Should().Be(expected);
    }

    [Fact]
    public void HandAttack_with_damage_is_combat()
    {
        var xml = XElement.Parse("""
            <protoaction>
              <name>HandAttack</name>
              <rof>1.0</rof>
              <damage type="Hack">8.0</damage>
            </protoaction>
            """);
        ProtoActionClassifier.Classify(xml).Should().Be(ProtoActionKind.Combat);
    }

    [Fact]
    public void FreezeAttack_without_damage_is_special_not_invented_combat()
    {
        var xml = XElement.Parse("""
            <protoaction>
              <name>FreezeAttack</name>
              <rof>1.7</rof>
              <maxrange>12</maxrange>
              <onhiteffect type="Freeze" duration="8.0"></onhiteffect>
            </protoaction>
            """);
        ProtoActionClassifier.Classify(xml).Should().Be(ProtoActionKind.Special);
    }

    [Fact]
    public void BuildAttack_schema_omits_damage_editors_for_utility_actions()
    {
        var unit = XElement.Parse("""
            <unit name="TestVillager">
              <maxhitpoints>55</maxhitpoints>
              <protoaction>
                <name>Build</name>
                <rate type="Building">1.0</rate>
              </protoaction>
              <protoaction>
                <name>Gather</name>
                <rate type="WoodResource">1.0</rate>
              </protoaction>
              <protoaction>
                <name>HandAttack</name>
                <rof>1.0</rof>
                <damage type="Hack">6.0</damage>
              </protoaction>
            </unit>
            """);

        var node = new SourceNode
        {
            InternalKey = "TestVillager",
            Category = EntityCategory.Worker,
            SourceFile = "proto.xml",
            Xml = unit
        };

        var schema = EditorSchemaBuilder.Build(node, context: new EditorSchemaContext(Array.Empty<string>(), Array.Empty<string>()));
        var attack = schema.Tabs.Should().ContainSingle(t => t.Id == "attack").Subject;
        var work = schema.Tabs.Should().ContainSingle(t => t.Id == "work").Subject;

        attack.Fields.Should().NotContain(f => f.GroupKey == "Build" || f.GroupKey == "Gather");
        var hand = attack.Fields.Where(f => f.GroupKey == "HandAttack").ToList();
        hand.Should().Contain(f => f.GroupKind == "combat");
        hand.Should().Contain(f => string.Equals(f.ElementName, "damage", StringComparison.OrdinalIgnoreCase));

        var byWork = work.Fields.GroupBy(f => f.GroupKey!).ToDictionary(g => g.Key, g => g.ToList());
        byWork["Build"].Should().OnlyContain(f => f.GroupKind == "utility");
        byWork["Build"].Should().Contain(f => string.Equals(f.ElementName, "rate", StringComparison.OrdinalIgnoreCase));
        byWork["Build"].Should().NotContain(f => string.Equals(f.ElementName, "damage", StringComparison.OrdinalIgnoreCase));
        byWork["Build"].Should().NotContain(f => f.Presence == "addable");

        byWork["Gather"].Should().Contain(f => string.Equals(f.ElementName, "rate", StringComparison.OrdinalIgnoreCase));
        byWork["Gather"].Should().NotContain(f => string.Equals(f.ElementName, "damage", StringComparison.OrdinalIgnoreCase));
    }
}
