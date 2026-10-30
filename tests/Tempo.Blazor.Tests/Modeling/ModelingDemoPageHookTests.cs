using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 15b carry-forward (F5): the demo <c>/modeling-editor</c> page collapses the demo shell
/// through a STABLE hook — <c>data-demo-shell="…"</c> attributes on the layout's
/// sidebar/main/page-body — instead of keying CSS off Tailwind utility class names
/// (<c>aside.fixed.inset-y-0.left-0.z-30</c>, <c>.max-w-7xl</c>) that churn with every styling
/// pass, and its index link rides <c>--tm-text-link</c> instead of hex fallbacks. The demo page
/// is demo-only code, but its &lt;style&gt; is shipped source: it must stay free of Tailwind
/// class selectors and hardcoded colors.
/// </summary>
public sealed class ModelingDemoPageHookTests
{
    private const string PagePath = "src/Tempo.Blazor.Demo.SharedUI/Pages/ModelingEditorPage.razor";
    private const string LayoutPath = "src/Tempo.Blazor.Demo.SharedUI/Layout/MainLayout.razor";

    private static string PageSource() => File.ReadAllText(
        Path.Combine(Theme.ThemeCss.RepositoryRoot().FullName, PagePath));

    private static string LayoutSource() => File.ReadAllText(
        Path.Combine(Theme.ThemeCss.RepositoryRoot().FullName, LayoutPath));

    private static string StyleBlock(string source)
    {
        var match = Regex.Match(source, @"<style>(.*?)</style>", RegexOptions.Singleline);
        match.Success.Should().BeTrue("the demo page styles its full-bleed layout in one <style> block");
        return match.Groups[1].Value;
    }

    [Fact]
    public void ModelingEditorPage_css_has_no_tailwind_utility_selectors_or_hex()
    {
        var style = StyleBlock(PageSource());

        Regex.Matches(style, @"\.(fixed|inset-y-0|left-0|z-30|max-w-7xl)\b")
            .Should().BeEmpty(
                "the full-bleed hook must target the stable data-demo-shell attributes, " +
                "never Tailwind utility class names that churn");

        // No hex fallbacks: colors ride the core tokens (the indigo/dark themes recolour them).
        Regex.Matches(style, @"#[0-9a-fA-F]{3,8}\b")
            .Should().BeEmpty("the demo page paints through --tm-* tokens only");
    }

    [Fact]
    public void ModelingEditorPage_hides_the_shell_through_the_data_hook()
    {
        var style = StyleBlock(PageSource());

        style.Should().Contain("[data-demo-shell=\"sidebar\"]",
            "the sidebar hides through the stable hook");
        style.Should().Contain("[data-demo-shell=\"main\"]",
            "the main column re-widens through the stable hook");
        style.Should().Contain("[data-demo-shell=\"page-body\"]",
            "the content wrapper's max-width lifts through the stable hook");
    }

    [Fact]
    public void Demo_layout_carries_the_stable_shell_hooks()
    {
        var layout = LayoutSource();

        layout.Should().Contain("data-demo-shell=\"sidebar\"", "the aside carries the hook");
        layout.Should().Contain("data-demo-shell=\"main\"", "the main element carries the hook");
        layout.Should().Contain("data-demo-shell=\"page-body\"", "the page-body wrapper carries the hook");
    }

    [Fact]
    public void Index_link_rides_the_theme_aware_link_token()
    {
        var style = StyleBlock(PageSource());

        style.Should().Contain("var(--tm-text-link)",
            "the index link follows the theme through --tm-text-link (light + dark + indigo)");
        style.Should().NotContain("--tm-color-primary-700,",
            "no hex fallback may shadow the token");
    }
}
