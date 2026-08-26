using System.Text.Json;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Keeps the published token table in <c>JsonDocumentation/gettingStarted.json</c> honest against
/// <c>tokens.css</c>.
/// <para>
/// That JSON is not prose: it is the machine-readable contract an MCP client and the package documentation
/// hand to a consumer who is theming Tempo, and it repeats every token's VALUE. Nothing regenerated it and
/// nothing compared it, so it drifted silently — when this guard was written it still claimed
/// <c>--tm-color-primary: var(--tm-color-primary-500)</c> while the stylesheet had moved to
/// <c>-600</c>, and the same for the hover and active aliases and the sans stack. A consumer following the
/// documentation would have computed contrast against the wrong shade.
/// </para>
/// </summary>
public class DesignTokenDocumentationTests
{
    /// <summary>
    /// The documented table is a curated SUBSET — the compatibility aliases and internal knobs are not part
    /// of the public theming contract. A subset is exactly what makes a drift guard vacuous if entries can
    /// simply disappear, so the size is ratcheted: entries may be added, never silently dropped.
    /// </summary>
    private const int DocumentedTokenFloor = 115;

    private static string DocumentationPath() =>
        Path.Combine(ThemeCss.RepositoryRoot().FullName, "JsonDocumentation", "gettingStarted.json");

    /// <summary>The token → value table the documentation publishes, flattened across its categories.</summary>
    private static Dictionary<string, (string Value, string Category)> DocumentedTokens()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(DocumentationPath()));
        var categories = document.RootElement
            .GetProperty("cssFramework").GetProperty("designTokens").GetProperty("categories");

        var documented = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var category in categories.EnumerateObject())
        {
            if (!category.Value.TryGetProperty("tokens", out var tokens))
            {
                continue;
            }

            foreach (var token in tokens.EnumerateObject())
            {
                documented[token.Name] = (ThemeCss.Normalise(token.Value.GetString() ?? string.Empty), category.Name);
            }
        }

        return documented;
    }

    [Fact]
    public void EveryDocumentedToken_IsActuallyDeclared_InTokensCss()
    {
        var declared = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));

        var phantom = DocumentedTokens().Keys.Where(token => !declared.ContainsKey(token)).ToList();

        phantom.Should().BeEmpty(
            "a documented token nobody declares is a theming instruction that silently does nothing");
    }

    [Fact]
    public void EveryDocumentedToken_CarriesTheValueTheStylesheetDeclares()
    {
        var declared = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));

        var drifted = DocumentedTokens()
            .Where(entry => declared.TryGetValue(entry.Key, out var value)
                            && !string.Equals(value, entry.Value.Value, StringComparison.Ordinal))
            .Select(entry => $"[{entry.Value.Category}] {entry.Key}: documented '{entry.Value.Value}', "
                             + $"tokens.css declares '{declared[entry.Key]}'")
            .ToList();

        drifted.Should().BeEmpty(
            "tokens.css is the source of truth — the documentation has to be corrected to match it, never "
            + "the other way round");
    }

    [Fact]
    public void TheDocumentedTable_DoesNotShrink()
    {
        DocumentedTokens().Should().HaveCountGreaterThanOrEqualTo(DocumentedTokenFloor,
            "deleting a row is the one way to make a drift guard pass without fixing the drift");
    }

    /// <summary>
    /// The tokens that carry an accessibility obligation must be documented, not merely declared: a consumer
    /// who repoints the primary scale has to know that these exist and what they are for, or the 3:1 fixes
    /// they encode are lost the moment the palette is changed.
    /// </summary>
    [Theory]
    [InlineData("--tm-border-color-control")]
    [InlineData("--tm-control-glyph-color")]
    [InlineData("--tm-control-hover-fill")]
    [InlineData("--tm-sort-indicator-idle")]
    [InlineData("--tm-sort-indicator-active")]
    public void AccessibilityTokens_AreDocumented(string token)
        => DocumentedTokens().Should().ContainKey(token,
            "a consumer repointing the palette cannot honour a contract that is not published");
}
