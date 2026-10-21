using System.Text.RegularExpressions;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7A DoD guard (rule 21): the email editor's scoped stylesheets may only use <c>--tm-*</c> design
/// tokens — no <c>var(--tm-token, #fallback)</c> hardcoded fallbacks and no literal colours, so a token
/// change (or a theme) is never masked by a value baked into the package.
/// </summary>
public class EmailEditorCssTokenTests
{
    private static readonly Lazy<IReadOnlyList<(string Name, string Text)>> Stylesheets = new(LoadStylesheets);

    [Fact]
    public void No_stylesheet_uses_a_hardcoded_fallback_for_a_design_token()
    {
        var offenders = new List<string>();
        var pattern = new Regex(@"var\(\s*(--tm-[a-z0-9-]+)\s*,", RegexOptions.IgnoreCase);

        foreach (var (name, text) in Stylesheets.Value)
        {
            foreach (Match match in pattern.Matches(text))
            {
                // A component-local custom property (--tm-email-preview-*) is set by the component itself,
                // so its fallback is part of the contract. A design token must come from the token sheet.
                if (match.Groups[1].Value.StartsWith("--tm-email-", StringComparison.Ordinal)) continue;
                offenders.Add($"{name}: {match.Value}");
            }
        }

        offenders.Should().BeEmpty("design tokens must come from the design system, never from a local fallback");
    }

    [Fact]
    public void No_stylesheet_contains_a_literal_colour()
    {
        var offenders = new List<string>();
        var hex = new Regex(@"#[0-9a-fA-F]{3,8}\b");
        var function = new Regex(@"\b(rgb|rgba|hsl|hsla)\(", RegexOptions.IgnoreCase);

        foreach (var (name, text) in Stylesheets.Value)
        {
            offenders.AddRange(hex.Matches(text).Select(m => $"{name}: {m.Value}"));
            offenders.AddRange(function.Matches(text).Select(m => $"{name}: {m.Value}"));
        }

        offenders.Should().BeEmpty("colours come from --tm-color-* tokens only");
    }

    [Fact]
    public void The_editor_panels_and_the_canvas_use_tokens_only()
    {
        // Fáze 8: the panels are the shell's docked columns, so the SHELL owns the card chrome (its own
        // surface + the separator) and the editor keeps only the inner column + typography.
        var editor = Stylesheets.Value.Single(s => s.Name == "TmEmailTemplateEditor.razor.css").Text;

        var panel = Rule(editor, ".tm-email-editor__panel");
        panel.Should().Contain("--tm-color-surface");
        panel.Should().Contain("min-block-size: 0", "a docked column must be able to shrink");

        Rule(editor, ".tm-email-editor__canvas").Should().Contain("--tm-space-4",
            "the paper keeps the workspace gutter around it");

        var header = Rule(editor, ".tm-email-editor__panel-header");
        header.Should().Contain("--tm-font-size-sm");
        header.Should().NotContain("text-transform: uppercase",
            "panel headers use normal typography, not uppercase micro-labels");
        header.Should().NotContain("--tm-font-size-xs");
    }

    [Fact]
    public void The_toolbox_never_crops_a_tile_label()
    {
        var toolbox = Stylesheets.Value.Single(s => s.Name == "TmEmailTemplateToolbox.razor.css").Text;

        var label = Rule(toolbox, ".tm-email-toolbox__label");
        label.Should().Contain("overflow-wrap", "a long block name wraps instead of being cut („Tabulk“, „Naviga“)");
        label.Should().NotContain("white-space: nowrap");
        label.Should().NotContain("text-overflow: ellipsis");
    }

    [Fact]
    public void Every_inspector_field_selector_survives_the_css_isolation_scope()
    {
        // The field markup is built with RenderTreeBuilder in @code, so those elements NEVER receive the
        // component's scope attribute. A bare `.tm-email-fields__field` rule therefore matched nothing (the
        // measured symptom: the label typography and the number+unit layout silently did nothing).
        var fields = Stylesheets.Value.Single(s => s.Name == "TmEmailObjectFields.razor.css").Text;

        var unscoped = fields.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(".tm-email-fields__", StringComparison.Ordinal))
            .ToList();

        unscoped.Should().BeEmpty(
            "a builder-rendered element carries no scope attribute — prefix the rules with `.tm-email-fields ::deep`");

        fields.Should().Contain(".tm-email-fields ::deep .tm-email-fields__length .tm-input-wrapper",
            "the length row pins the number input's flex basis through ::deep (the input is a child component)");
        fields.Should().Contain(".tm-email-fields ::deep .tm-email-fields__length .tm-select-wrapper");
    }

    [Fact]
    public void Inspector_labels_share_one_typography()
    {
        var fields = Stylesheets.Value.Single(s => s.Name == "TmEmailObjectFields.razor.css").Text;

        var label = Rule(fields, ".tm-email-fields ::deep .tm-email-fields__label");
        label.Should().Contain("--tm-font-size-sm");
        label.Should().Contain("--tm-font-weight-medium");
        label.Should().Contain("--tm-text-primary");
    }

    [Fact]
    public void Validation_icons_and_strips_use_the_strong_colour_tokens()
    {
        var validation = Stylesheets.Value.Single(s => s.Name == "TmEmailValidationPanel.razor.css").Text;

        Rule(validation, ".tm-email-validation__item--info").Should().Contain("--tm-color-info-strong");
        Rule(validation, ".tm-email-validation__item--warning").Should().Contain("--tm-color-warning-strong");
        Rule(validation, ".tm-email-validation__item--info .tm-email-validation__icon")
            .Should().Contain("--tm-color-info-strong");
        Rule(validation, ".tm-email-validation__item--warning .tm-email-validation__icon")
            .Should().Contain("--tm-color-warning-strong");
    }

    [Fact]
    public void The_variable_picker_row_is_a_code_badge_grid_with_a_wrapping_token()
    {
        var picker = Stylesheets.Value.Single(s => s.Name == "TmEmailVariablePicker.razor.css").Text;

        Rule(picker, ".tm-email-variables").Replace(" ", "").Should().Contain("inline-size:min(26rem,90vw)",
            "the picker is wider than the old 18rem chip column, but never wider than the viewport");

        var row = Rule(picker, ".tm-email-variables__item").Replace(" ", "");
        row.Should().Contain("grid-template-areas")
            .And.Contain("\"codebadge\"")
            .And.Contain("\"descdesc\"");

        var code = Rule(picker, ".tm-email-variables__item code").Replace(" ", "");
        code.Should().Contain("white-space:normal", "a long path wraps instead of truncating mid-token");
        code.Should().Contain("overflow-wrap:anywhere");
        code.Should().Contain("color:var(--tm-color-text)");
    }

    [Fact]
    public void The_toolbox_header_pairs_title_and_toggle_on_one_row()
    {
        var toolbox = Stylesheets.Value.Single(s => s.Name == "TmEmailTemplateToolbox.razor.css").Text;

        Rule(toolbox, ".tm-email-toolbox__header").Replace(" ", "").Should().Contain("justify-content:space-between",
            "the title sits left, the collapse arrow right");
    }

    private static string Rule(string css, string selector)
    {
        var index = css.IndexOf(selector + " ", StringComparison.Ordinal);
        if (index < 0) index = css.IndexOf(selector + "{", StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"{selector} must exist");
        var open = css.IndexOf('{', index);
        var close = css.IndexOf('}', open);
        return css[open..close];
    }

    private static IReadOnlyList<(string Name, string Text)> LoadStylesheets()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor.EmailTemplates", "Components");
        return Directory.GetFiles(directory, "*.razor.css")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)))
            .ToList();
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
