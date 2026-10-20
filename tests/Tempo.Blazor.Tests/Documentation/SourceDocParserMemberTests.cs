using FluentAssertions;
using JsonDocumentationGenerator;
using System.Text.Json;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Documentation;

/// <summary>
/// Member extraction for the JSON documentation generator (cluster-A / A2, phase FZE-3).
/// <para>
/// <see cref="SourceDocParser.ExtractMembers"/> only recognised <c>{ get;</c> properties and
/// lines it could not classify defaulted into its field loop — which reads the text up to the
/// first <c>=</c>, so an expression-bodied property (<c>public bool IsMobile => X == 1;</c>) was
/// mis-kinded as a <c>Field</c>, and everything whose arrow or body wrapped to a second line
/// (<c>=> Resolved switch</c>, <c>{ get => …; init => …; }</c>) was dropped. Positional record
/// parameters — generated public properties — were never extracted at all.
/// </para>
/// <para>
/// The real victim is <c>TmLayoutContext</c>: the committed bundle lists only IsDesktop,
/// IsMobile and IsTablet, all as <c>"kind": "Field"</c>, while Mode, Resolved and CssModifier
/// are missing. These tests pin the full contract on synthetic sources first, then on the real
/// file and the real committed bundle.
/// </para>
/// </summary>
public sealed class SourceDocParserMemberTests
{
    [Fact]
    public void ExpressionBodiedProperty_IsAProperty_NotAField()
    {
        const string source = """
            namespace Demo;

            public sealed class Widget
            {
                /// <summary>True when narrow.</summary>
                public bool IsMobile => Resolved == 1;
            }
            """;

        var members = SourceDocParser.ExtractMembers(source, "Widget");

        var member = members.Should().ContainSingle(m => m.Name == "IsMobile").Subject;
        member.Kind.Should().Be("Property");
        member.Type.Should().Be("bool");
        member.Description.Should().Be("True when narrow.");
        members.Should().NotContain(
            m => m.Name == "IsMobile" && m.Kind == "Field",
            "the arrow of an expression body is not a field initializer");
    }

    [Fact]
    public void MultiLineExpressionBodiedProperty_IsListed()
    {
        const string source = """
            namespace Demo;

            public sealed class Widget
            {
                /// <summary>The BEM modifier.</summary>
                public string CssModifier => Resolved switch
                {
                    Mode.Mobile => "mobile",
                    Mode.Tablet => "tablet",
                    _ => "desktop",
                };
            }
            """;

        var members = SourceDocParser.ExtractMembers(source, "Widget");

        var member = members.Should().ContainSingle(m => m.Name == "CssModifier").Subject;
        member.Kind.Should().Be("Property");
        member.Type.Should().Be("string");
        member.Description.Should().Be("The BEM modifier.");
    }

    [Fact]
    public void AccessorBodiedProperty_IsListed()
    {
        const string source = """
            namespace Demo;

            public sealed class Widget
            {
                /// <summary>A resolved layout.</summary>
                public TmLayoutMode Resolved { get => _resolved; init => _resolved = Valid(value); }

                private TmLayoutMode _resolved;
            }
            """;

        var members = SourceDocParser.ExtractMembers(source, "Widget");

        var member = members.Should().ContainSingle(m => m.Name == "Resolved").Subject;
        member.Kind.Should().Be("Property");
        member.Type.Should().Be("TmLayoutMode");
        member.Description.Should().Be("A resolved layout.");
    }

    [Fact]
    public void PositionalRecordParameters_AreProperties_WithTheirParamDocs()
    {
        const string source = """
            namespace Demo;

            /// <summary>
            /// The layout a component has resolved.
            /// </summary>
            /// <param name="Mode">The mode the host asked for. Auto while the container is being measured.</param>
            /// <param name="Resolved">The mode actually rendered. Never Auto.</param>
            public sealed record TmLayoutContext(TmLayoutMode Mode, TmLayoutMode Resolved)
            {
            }
            """;

        var members = SourceDocParser.ExtractMembers(source, "TmLayoutContext");

        var mode = members.Should().ContainSingle(m => m.Name == "Mode").Subject;
        mode.Kind.Should().Be("Property", "a positional record parameter becomes a public property");
        mode.Description.Should().Be("The mode the host asked for. Auto while the container is being measured.");
        var resolved = members.Should().ContainSingle(m => m.Name == "Resolved").Subject;
        resolved.Kind.Should().Be("Property");
    }

    [Fact]
    public void PositionalRecord_MultiLine_GenericsDefaultsAndAttributes()
    {
        const string source = """
            namespace Demo;

            public sealed record Page<T>(
                IReadOnlyList<T> Items,
                Dictionary<string, int> Counts,
                int Total = 0,
                [property: JsonPropertyName("c")] string? Cursor = null);
            """;

        var members = SourceDocParser.ExtractMembers(source, "Page")
            .Where(m => m.Kind == "Property")
            .ToList();

        members.Select(m => (m.Name, m.Type)).Should().BeEquivalentTo(
        [
            ("Items", "IReadOnlyList<T>"),
            ("Counts", "Dictionary<string, int>"),
            ("Total", "int"),
            ("Cursor", "string?"),
        ]);
    }

    [Fact]
    public void ClassPrimaryConstructorParameters_AreNotMembers()
    {
        const string source = """
            namespace Demo;

            public sealed class Svc(ILogger log)
            {
            }
            """;

        var members = SourceDocParser.ExtractMembers(source, "Svc");

        members.Should().NotContain(
            m => m.Name == "log",
            "a class primary constructor creates no properties — only record positional parameters do");
    }

    [Fact]
    public void ExpressionBodiedMethod_StaysAMethod_AndOperatorsAreIgnored()
    {
        const string source = """
            namespace Demo;

            public sealed class Widget
            {
                /// <summary>Doubles.</summary>
                public int Twice(int x) => x * 2;

                public static bool operator ==(Widget? a, Widget? b) => true;

                public static bool operator !=(Widget? a, Widget? b) => false;
            }
            """;

        var members = SourceDocParser.ExtractMembers(source, "Widget");

        var twice = members.Should().ContainSingle(m => m.Name == "Twice").Subject;
        twice.Kind.Should().Be("Method");
        members.Should().NotContain(
            m => m.Kind == "Property",
            "methods and operators must never leak into the property extraction");
    }

    [Fact]
    public void FieldWithLambdaInitializer_StaysAField()
    {
        const string source = """
            namespace Demo;

            public sealed class Widget
            {
                /// <summary>Doubles.</summary>
                public static readonly Func<int, int> Twice = x => x * 2;
            }
            """;

        var members = SourceDocParser.ExtractMembers(source, "Widget");

        var twice = members.Should().ContainSingle(m => m.Name == "Twice").Subject;
        twice.Kind.Should().Be("Field");
        twice.Type.Should().Be("Func<int, int>");
    }

    [Fact]
    public void TheRealTmLayoutContext_ListsAllSixMembersAsProperties()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Abstractions", "Layout", "TmLayoutContext.cs");
        var source = File.ReadAllText(path);

        var members = SourceDocParser.ExtractMembers(source, "TmLayoutContext");

        var names = new[] { "Mode", "Resolved", "CssModifier", "IsMobile", "IsTablet", "IsDesktop" };
        var documented = members.Where(m => names.Contains(m.Name, StringComparer.Ordinal)).ToList();
        documented.Select(m => m.Name).Should().BeEquivalentTo(names);
        documented.Should().OnlyContain(m => m.Kind == "Property");

        // The explicit property's richer body doc must beat the positional record's <param> doc
        // for the same generated property ("The mode actually rendered. Never Auto.").
        var resolved = documented.Should().ContainSingle(m => m.Name == "Resolved").Subject;
        resolved.Description.Should().StartWith(
            "A resolved layout is desktop, tablet or mobile",
            "the accessor-bodied declaration's summary wins over the record <param> doc");
    }

    [Fact]
    public void PositionalParameterModifiers_AreStrippedFromTheDocumentedType()
    {
        const string source = """
            namespace Demo;

            public sealed record Buffer(
                in int Width,
                ref readonly string Label,
                out double Ratio,
                params string[] Extras,
                scoped Span<byte> Scratch);
            """;

        var members = SourceDocParser.ExtractMembers(source, "Buffer")
            .Where(m => m.Kind == "Property")
            .ToDictionary(m => m.Name, m => m.Type, StringComparer.Ordinal);

        members.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Width"] = "int",
            ["Label"] = "readonly string",
            ["Ratio"] = "double",
            ["Extras"] = "string[]",
            ["Scratch"] = "Span<byte>",
        });
    }

    [Fact]
    public void TheCommittedAbstractionsBundle_DocumentsTmLayoutContextMembers()
    {
        var path = Path.Combine(ThemeCss.RepositoryRoot().FullName, "tempo-blazor-abstractions.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        JsonElement entry = default;
        var found = false;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("itemName", out var name)
                    && name.GetString() == "TmLayoutContext")
                {
                    entry = item.Clone();
                    found = true;
                    break;
                }
            }

            if (found)
            {
                break;
            }
        }

        found.Should().BeTrue("TmLayoutContext must be documented in the bundle");
        entry.TryGetProperty("members", out var members).Should().BeTrue();
        var wanted = new HashSet<string>(["Mode", "Resolved", "CssModifier"], StringComparer.Ordinal);
        var named = new Dictionary<string, string?>();
        foreach (var member in members.EnumerateArray())
        {
            if (member.TryGetProperty("name", out var name)
                && name.GetString() is { } memberName
                && wanted.Contains(memberName))
            {
                named[memberName] = member.GetProperty("kind").GetString();
            }
        }

        named.Keys.Should().BeEquivalentTo(["Mode", "Resolved", "CssModifier"]);
        named.Values.Should().OnlyContain(kind => kind == "Property");
    }
}
