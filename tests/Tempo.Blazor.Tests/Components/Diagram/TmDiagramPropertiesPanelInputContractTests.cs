using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// The properties panel renders raw input/textarea elements carrying the .tm-text-input class.
/// Nothing else in the library styles that class (core ships .tm-input, the demo only sets
/// font-family), so the browser's UA default leaked through: in dark theme the page W/H/Scale
/// and the node X/Y/W/H fields painted as WHITE boxes on the dark panel with barely legible
/// text (final UX review, DiagramEditor phase 8, dark editor capture). Freeze the contract:
/// the package stylesheet itself must give the fields the tokenized control look that follows
/// the theme — surface background, primary text, a real border — instead of UA chrome.
/// </summary>
public class TmDiagramPropertiesPanelInputContractTests
{
    private static string PackageCss()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        return Path.Combine(root, "src", "Tempo.Blazor.DiagramEditor", "wwwroot", "css",
            "components", "_diagram-editor.css");
    }

    private static string? Property(string selector, string property)
    {
        var css = ThemeCss.StripComments(File.ReadAllText(PackageCss()));
        foreach (Match rule in Regex.Matches(css, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}"))
        {
            var parts = ThemeCss.Normalise(rule.Groups["selector"].Value)
                .Split(',')
                .Select(part => part.Trim())
                .ToList();
            if (!parts.Contains(selector, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var declaration in rule.Groups["body"].Value.Split(';'))
            {
                var separator = declaration.IndexOf(':', StringComparison.Ordinal);
                if (separator > 0 &&
                    string.Equals(declaration[..separator].Trim(), property, StringComparison.Ordinal))
                {
                    return ThemeCss.Normalise(declaration[(separator + 1)..]);
                }
            }
        }

        return null;
    }

    [Fact]
    public void TextInput_Fields_AreStyledWithThemeTokens()
    {
        const string selector = ".tm-diagram-properties__field .tm-text-input";

        Property(selector, "background-color")
            .Should().Be("var(--tm-bg-surface)",
                "the UA white default must not leak onto the dark panel");

        Property(selector, "color")
            .Should().Be("var(--tm-text-primary)",
                "the value text must follow the theme, not the UA default");

        Property(selector, "border")
            .Should().Contain("var(--tm-border-color)",
                "a visible border keeps the field readable on both themes");

        Property(selector, "border-radius")
            .Should().NotBeNull("the fields should match the control radius convention");
    }

    [Fact]
    public void TextInput_Fields_StayFullWidth_AndTextareaKeepsRoomToBreathe()
    {
        const string selector = ".tm-diagram-properties__field .tm-text-input";

        Property(selector, "width").Should().Be("100%",
            "the regression guard from the original rule must survive");
    }
}
