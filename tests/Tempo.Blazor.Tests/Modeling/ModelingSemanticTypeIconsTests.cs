using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// The semantic-type → icon mapping must resolve EVERY built-in vocabulary type
/// (BPMN / UML / ArchiMate 3.2 / ERD — see <c>BuiltInModelingProfiles.cs</c>) through the
/// exact-match table, must resolve the required special cases
/// (<c>EventBasedGateway</c> → the gateway icon, <c>DataObject</c>/<c>DataStore</c> → the
/// database icon), and must NOT be fooled by substring false positives
/// (<c>KnowledgeBase</c>, <c>Framework</c>, <c>Precondition</c> fall back to the box icon
/// because token rules match camel/Pascal word TOKENS, not substrings).
/// </summary>
public sealed class ModelingSemanticTypeIconsTests
{
    [Theory]
    [InlineData("Task", IconNames.Activity)]
    [InlineData("UserTask", IconNames.Activity)]
    [InlineData("SubProcess", IconNames.Activity)]
    [InlineData("StartEvent", IconNames.Message)]
    [InlineData("EndEvent", IconNames.Message)]
    [InlineData("IntermediateCatchEvent", IconNames.Message)]
    [InlineData("ExclusiveGateway", IconNames.GitCompare)]
    [InlineData("ParallelGateway", IconNames.GitCompare)]
    [InlineData("InclusiveGateway", IconNames.GitCompare)]
    [InlineData("ComplexGateway", IconNames.GitCompare)]
    [InlineData("EventBasedGateway", IconNames.GitCompare)]
    [InlineData("Pool", IconNames.Package)]
    [InlineData("Lane", IconNames.Package)]
    [InlineData("Group", IconNames.Package)]
    [InlineData("DataObject", IconNames.Database)]
    [InlineData("DataStore", IconNames.Database)]
    [InlineData("TextAnnotation", IconNames.FileText)]
    [InlineData("Class", IconNames.Box)]
    [InlineData("Interface", IconNames.Box)]
    [InlineData("Package", IconNames.Package)]
    [InlineData("Component", IconNames.Box)]
    [InlineData("UseCase", IconNames.Circle)]
    [InlineData("Actor", IconNames.User)]
    [InlineData("Activity", IconNames.Activity)]
    [InlineData("ObjectNode", IconNames.Box)]
    [InlineData("BusinessActor", IconNames.User)]
    [InlineData("BusinessRole", IconNames.User)]
    [InlineData("BusinessProcess", IconNames.Activity)]
    [InlineData("BusinessFunction", IconNames.Activity)]
    [InlineData("BusinessEvent", IconNames.Message)]
    [InlineData("ApplicationComponent", IconNames.Box)]
    [InlineData("ApplicationProcess", IconNames.Activity)]
    [InlineData("ApplicationFunction", IconNames.Activity)]
    [InlineData("ApplicationEvent", IconNames.Message)]
    [InlineData("ApplicationService", IconNames.Box)]
    [InlineData("TechnologyProcess", IconNames.Activity)]
    [InlineData("TechnologyFunction", IconNames.Activity)]
    [InlineData("TechnologyEvent", IconNames.Message)]
    [InlineData("Stakeholder", IconNames.User)]
    [InlineData("Requirement", IconNames.FileText)]
    [InlineData("WorkPackage", IconNames.Package)]
    [InlineData("Grouping", IconNames.Package)]
    [InlineData("ImplementationEvent", IconNames.Message)]
    [InlineData("Entity", IconNames.Table)]
    [InlineData("WeakEntity", IconNames.Table)]
    [InlineData("RelationshipSet", IconNames.Link2)]
    [InlineData("WeakRelationshipSet", IconNames.Link2)]
    public void Built_in_vocabulary_types_resolve_through_the_exact_table(string semanticType, string expectedIcon)
        => ModelingSemanticTypeIcons.ForSemanticType(semanticType).Should().Be(expectedIcon);

    /// <summary>Every type of every built-in profile resolves to a defined icon — never
    /// null/empty, never an unregistered name — no matter which notation profile emitted it.</summary>
    [Theory]
    [MemberData(nameof(BuiltInVocabulary))]
    public void Every_built_in_vocabulary_type_resolves_to_a_defined_icon(string semanticType)
    {
        var icon = ModelingSemanticTypeIcons.ForSemanticType(semanticType);

        icon.Should().NotBeNullOrEmpty($"'{semanticType}' must resolve to an icon");
        KnownIcons.Should().Contain(icon, $"'{semanticType}' resolved to the unknown icon '{icon}'");
    }

    [Theory]
    [InlineData("KnowledgeBase", IconNames.Box)]
    [InlineData("Framework", IconNames.Box)]
    [InlineData("Precondition", IconNames.Box)]
    public void Substring_false_positives_fall_back_to_the_box_icon(string semanticType, string expectedIcon)
    {
        // Token rules, not substrings: "base" is not in "KnowledgeBase", "frame" is not a
        // token of "Framework", "condition" is not a token of "Precondition".
        ModelingSemanticTypeIcons.ForSemanticType(semanticType).Should().Be(expectedIcon);
    }

    [Theory]
    [InlineData(null, IconNames.Box)]
    [InlineData("", IconNames.Box)]
    [InlineData("   ", IconNames.Box)]
    [InlineData("mysteryType", IconNames.Box)]
    public void Unknown_or_empty_types_fall_back_to_the_box_icon(string? semanticType, string expectedIcon)
        => ModelingSemanticTypeIcons.ForSemanticType(semanticType).Should().Be(expectedIcon);

    [Fact]
    public void Resolution_is_case_insensitive_and_trimmed()
    {
        ModelingSemanticTypeIcons.ForSemanticType("eventbasedgateway").Should().Be(IconNames.GitCompare);
        ModelingSemanticTypeIcons.ForSemanticType(" DATASTORE ").Should().Be(IconNames.Database);
        ModelingSemanticTypeIcons.ForSemanticType("userTask").Should().Be(ModelingSemanticTypeIcons.ForSemanticType("UserTask"));
    }

    [Fact]
    public void Token_rules_recognize_multiword_and_acronym_shapes()
    {
        // Token (not substring) matching still resolves the multiword phrase and acronym shapes.
        ModelingSemanticTypeIcons.ForSemanticType("BusinessRuleTask").Should().Be(IconNames.Activity);
        ModelingSemanticTypeIcons.ForSemanticType("EventBasedGateway").Should().Be(IconNames.GitCompare);
        ModelingSemanticTypeIcons.ForSemanticType("MultiValuedAttribute").Should().Be(IconNames.Box);
        ModelingSemanticTypeIcons.ForSemanticType("use case").Should().Be(IconNames.Circle);
        ModelingSemanticTypeIcons.ForSemanticType("XMLNode").Should().Be(IconNames.Box);
    }

    public static IEnumerable<object[]> BuiltInVocabulary()
        => BpmnModelingCatalog.ElementTypes
            .Concat(UmlModelingCatalog.ElementTypes)
            .Concat(Archimate32ModelingCatalog.ElementTypes)
            .Concat(ErdVocabulary)
            // The catalogs share types (and ErdVocabulary repeats a few) — xUnit refuses
            // duplicate theory rows, so de-duplicate case-insensitively (resolution is
            // case-insensitive too).
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(type => new object[] { type });

    private static readonly string[] ErdVocabulary =
    [
        "Entity",
        "WeakEntity",
        "Attribute",
        "MultiValuedAttribute",
        "DerivedAttribute",
        "KeyAttribute",
        "RelationshipSet",
        "WeakRelationshipSet"
    ];

    /// <summary>The icon set the mapping may ever return — guards against a typo in a rule.</summary>
    private static readonly IReadOnlyCollection<string> KnownIcons =
    [
        IconNames.Activity,
        IconNames.Table,
        IconNames.Box,
        IconNames.User,
        IconNames.Circle,
        IconNames.Message,
        IconNames.GitCompare,
        IconNames.Package,
        IconNames.Database,
        IconNames.ArrowRight,
        IconNames.FileText,
        IconNames.Link2
    ];
}
