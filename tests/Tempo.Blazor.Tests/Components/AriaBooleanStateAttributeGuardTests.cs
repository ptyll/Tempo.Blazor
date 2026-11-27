using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Components;

/// <summary>
/// Fáze 4c (plan b75365fb): <c>aria-expanded</c> / <c>aria-pressed</c> / <c>aria-selected</c> /
/// <c>aria-checked</c> are ARIA STATE attributes — they must carry the strings <c>"true"</c> /
/// <c>"false"</c> (<c>"mixed"</c> is the third valid <c>aria-checked</c> value). Blazor renders a
/// <c>bool</c> attribute value as the attribute with an EMPTY value (true) or omits it entirely
/// (false), so <c>aria-expanded="@_isOpen"</c> produced the invalid <c>aria-expanded=""</c> on
/// open pickers (found on <c>TmDatePicker</c> / <c>TmDateTimePicker</c> in fáze 4, on
/// <c>TmDateRangePicker</c> / <c>TmMultiViewList</c> here).
/// The guard sweeps every library razor file and every library code-behind for these shapes:
/// <list type="bullet">
/// <item>bare identifier: <c>aria-X="@flag"</c> — passes only when the identifier resolves to a
/// string declaration in the razor/code-behind (a helper property like <c>AriaPressed</c>);</item>
/// <item>parenthesised expression: <c>aria-X="@(a == b)"</c> — passes only when it yields the
/// literal state strings (a <c>? "true" : "false"</c> ternary) or calls <c>.ToString()</c>;</item>
/// <item>dotted path / method call: <c>aria-X="@Model.Flag"</c> / <c>aria-X="@IsOpen(item)"</c> —
/// passes when the last segment resolves to a string declaration in the swept file (a string
/// property, field or method such as <c>string BoolText(…)</c>) or the expression calls
/// <c>.ToString()</c>. A method call whose file declares it <c>bool</c> is an offender; a dotted
/// path is an offender whenever its last segment is not proven string — the guard cannot see
/// types declared in OTHER files, so an unproven member is treated as a bool (a bool model
/// property is exactly the defect this guard exists for);</item>
/// <item>code-behind: <c>AddAttribute(seq, "aria-X", value)</c> — passes for a string literal,
/// an expression producing the state strings (<c>.ToString()</c>, <c>BoolText(…)</c>, a ternary
/// with the literals), a <c>var</c> initialized from those strings, or a string-typed
/// identifier; anything else is an offender.</item>
/// </list>
/// Known pre-existing offenders outside this phase's scope are FROZEN (the list may only
/// shrink — fixing a site removes its entry; adding a new site fails the guard).
/// Fáze 4h (plan b75365fb) extended the sweep to the demo surfaces
/// (<c>Tempo.Blazor.Demo.SharedUI</c>, <c>Tempo.ReportServer.Web.Client</c>) with zero tolerance —
/// no frozen entries there. Fáze 4i fixed the NotionEditor page and sidebar sites (the settings
/// menu, watch button, sidebar sections, page tree, comment panel, my tasks and page history)
/// through a string <c>BoolText</c> helper — except the watch toggle, whose <c>aria-pressed</c>
/// was removed entirely (its changing name announces the state, APG) — shrinking the frozen
/// list by 19.
/// </summary>
public class AriaBooleanStateAttributeGuardTests
{
    private const string Attr = "aria-(?:expanded|pressed|selected|checked)";

    // aria-X="@identifier" — identifier directly followed by the closing quote.
    private static readonly Regex Bare =
        new($"{Attr}=\"@(?<id>[A-Za-z_][A-Za-z0-9_]*)\"", RegexOptions.Compiled);

    // aria-X="@(...)" — lazy: the expression ends at the first ")" immediately followed by '"'.
    private static readonly Regex Parens =
        new($"{Attr}=\"@\\((?<expr>.*?)\\)\"", RegexOptions.Compiled);

    // aria-X="@Model.Flag" / aria-X="@IsOpen(item)" — a dotted member path or a method call.
    // A bare identifier (no dot, no call) also matches; those hits are covered by Bare and
    // skipped here.
    private static readonly Regex Dotted =
        new($"{Attr}=\"@(?<expr>[A-Za-z_]\\w*(?:\\.[A-Za-z_]\\w*)*(?:\\([^\"]*\\))?)\"",
            RegexOptions.Compiled);

    // A bare identifier is fine when it resolves to a STRING declaration (helper property such as
    // "private string? AriaPressed => ..." or a string-returning method such as
    // "private static string BoolText(bool value)"), either in the razor's @code block or its
    // code-behind.
    private static readonly Regex StringDecl =
        new(@"\bstring\??\s+(?<id>[A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

    // AddAttribute(seq, "aria-X", value) in a code-behind .cs file.
    private static readonly Regex CsAddAttribute =
        new("AddAttribute\\(\\s*[^,]+,\\s*\"(?<attr>" + Attr + ")\"\\s*,\\s*(?<val>[^;]+?)\\s*\\);",
            RegexOptions.Compiled);

    private static readonly string[] Frozen =
    [
        // ── bare identifier offenders (known debt, see plan_report_remaining) ────────────────
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionTokenDropdown.razor|aria-selected|@isSelected",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Lists/TmNotionToggleBlock.razor|aria-expanded|@_isOpen",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Database/TmNotionDbTableView.razor|aria-expanded|@isExpanded",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionBlockTypeSwitcher.razor|aria-selected|@isCurrent",
        // ── parenthesised bool-expression offenders ───────────────────────────────────────────
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionMentionMenu.razor|aria-selected|@(_tab == MentionTab.People)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionMentionMenu.razor|aria-selected|@(_tab == MentionTab.Pages)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionMentionMenu.razor|aria-selected|@(_tab == MentionTab.Date)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionMentionMenu.razor|aria-selected|@(pIdx == _selectedIndex)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionMentionMenu.razor|aria-selected|@(pgIdx == _selectedIndex)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionMentionMenu.razor|aria-selected|@(0 == _selectedIndex)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionMentionMenu.razor|aria-selected|@(1 == _selectedIndex)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionMentionMenu.razor|aria-selected|@(2 == _selectedIndex)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmCommentMentionInput.razor|aria-selected|@(idx == _selectedIndex)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionEmojiPicker.razor|aria-selected|@(_activeCategory == capturedTab.Category)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Database/TmNotionDbFieldEditor.razor|aria-pressed|@(o.Color == cc)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Database/TmNotionDatabaseBlock.razor|aria-selected|@(_activeViewId == v.Id)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Database/TmNotionDbTableView.razor|aria-expanded|@(!isCollapsed)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Database/TmNotionDbImportExport.razor|aria-selected|@(_tab == ActiveTab.Import)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Database/TmNotionDbImportExport.razor|aria-selected|@(_tab == ActiveTab.Export)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Database/TmNotionDbGroupBar.razor|aria-selected|@(_selectedFieldId is null)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Database/TmNotionDbGroupBar.razor|aria-selected|@(_selectedFieldId == f.Id)",
        // ── method calls declared bool in the same file ───────────────────────────────────────
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionPageSearch.razor|aria-selected|@IsPageSelected(pIdx)",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionPageSearch.razor|aria-selected|@IsBlockSelected(bIdx)",
        // ── code-behind AddAttribute with a bool expression ───────────────────────────────────
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Embed/TmNotionWorkItemBlock.razor.cs|aria-pressed|mode == CurrentDisplayMode",
    ];

    [Fact]
    public void NoAriaStateAttribute_ReceivesABooleanExpression()
    {
        var root = FindRepoRoot();
        var srcDir = Path.Combine(root.FullName, "src");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.razor", SearchOption.AllDirectories))
        {
            if (!IsLibrarySource(file)) continue;

            var relative = Path.GetRelativePath(root.FullName, file).Replace('\\', '/');
            var razor = File.ReadAllText(file);
            var code = razor;
            var codeBehindPath = file + ".cs";
            if (File.Exists(codeBehindPath))
            {
                code += "\n" + File.ReadAllText(codeBehindPath);
            }

            var stringIds = new HashSet<string>(
                StringDecl.Matches(code).Select(m => m.Groups["id"].Value),
                StringComparer.Ordinal);

            foreach (Match m in Bare.Matches(razor))
            {
                var id = m.Groups["id"].Value;
                if (stringIds.Contains(id)) continue; // a string helper property (e.g. AriaPressed)
                offenders.Add($"{relative}|{m.Value.Split('=')[0]}|@{id}");
            }

            foreach (Match m in Parens.Matches(razor))
            {
                var expr = m.Groups["expr"].Value;
                // String-producing: a ternary with the literal state strings ("mixed" is the
                // third valid aria-checked value), or a .ToString() call.
                if (expr.Contains("\"true\"", StringComparison.Ordinal)
                    || expr.Contains("\"false\"", StringComparison.Ordinal)
                    || expr.Contains("\"mixed\"", StringComparison.Ordinal)
                    || expr.Contains(".ToString(", StringComparison.Ordinal))
                {
                    continue;
                }

                offenders.Add($"{relative}|{m.Value.Split('=')[0]}|@({expr})");
            }

            foreach (Match m in Dotted.Matches(razor))
            {
                var expr = m.Groups["expr"].Value;
                if (!expr.Contains('.') && !expr.Contains('(')) continue; // covered by Bare

                var key = KeyOf(expr);
                if (stringIds.Contains(key)) continue; // a string member/method (e.g. BoolText)
                if (expr.Contains(".ToString(", StringComparison.Ordinal)) continue;

                if (expr.Contains('('))
                {
                    // A method call is an offender only when the swept file declares it bool —
                    // an unknown helper may legitimately return the state strings.
                    if (Regex.IsMatch(code, $@"\bbool\s+{Regex.Escape(key)}\s*\("))
                    {
                        offenders.Add($"{relative}|{m.Value.Split('=')[0]}|@{expr}");
                    }

                    continue;
                }

                // A dotted path is an offender unless its last segment is proven string above —
                // the guard cannot see the member's type across files, and a bool model
                // property is exactly the defect this guard exists for.
                offenders.Add($"{relative}|{m.Value.Split('=')[0]}|@{expr}");
            }
        }

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (!IsLibrarySource(file)) continue;

            var relative = Path.GetRelativePath(root.FullName, file).Replace('\\', '/');
            var code = File.ReadAllText(file);

            foreach (Match m in CsAddAttribute.Matches(code))
            {
                var val = m.Groups["val"].Value.Trim();
                if (IsStringValued(val, code)) continue;
                offenders.Add($"{relative}|{m.Groups["attr"].Value}|{val}");
            }
        }

        var offenderSet = offenders.ToHashSet(StringComparer.Ordinal);
        var frozenSet = Frozen.ToHashSet(StringComparer.Ordinal);

        offenderSet.Except(frozenSet).Should().BeEmpty(
            "no aria-expanded/aria-pressed/aria-selected/aria-checked may receive a boolean " +
            "expression (invalid empty ARIA value). New occurrences — fix them; the frozen list " +
            "may only shrink. Found:\n" + string.Join("\n", offenderSet.Except(frozenSet)));

        frozenSet.Except(offenderSet).Should().BeEmpty(
            "every frozen entry must still exist — remove entries when the underlying site is " +
            "fixed so the debt list can only shrink. No longer matching:\n" +
            string.Join("\n", frozenSet.Except(offenderSet)));
    }

    // The member the attribute actually reads: the last path segment before the call's "(".
    private static string KeyOf(string expr)
    {
        var head = expr.Split('(')[0];
        var segments = head.Split('.');
        return segments[^1];
    }

    // An AddAttribute value is string-valued when it is a string literal, an expression that
    // produces the state strings, or a bare identifier the file declares as string (or as a var
    // initialized from the state strings — DocumentToolbarToggleRenderer's "pressed" switch).
    private static bool IsStringValued(string val, string code)
    {
        if (val.StartsWith('"')) return true;
        if (val.Contains("\"true\"", StringComparison.Ordinal)
            || val.Contains("\"false\"", StringComparison.Ordinal)
            || val.Contains("\"mixed\"", StringComparison.Ordinal)
            || val.Contains(".ToString(", StringComparison.Ordinal)
            || val.Contains("BoolText(", StringComparison.Ordinal))
        {
            return true;
        }

        if (!Regex.IsMatch(val, @"^[A-Za-z_]\w*$")) return false;

        if (Regex.IsMatch(code, $@"\bstring\??\s+{Regex.Escape(val)}\b")) return true;

        var varDecl = Regex.Match(code, $@"\bvar\s+{Regex.Escape(val)}\s*=\s*(?<init>[^;]+);");
        if (!varDecl.Success) return false;

        var init = varDecl.Groups["init"].Value;
        return init.Contains("\"true\"", StringComparison.Ordinal)
            || init.Contains("\"false\"", StringComparison.Ordinal)
            || init.Contains("\"mixed\"", StringComparison.Ordinal)
            || init.Contains(".ToString(", StringComparison.Ordinal)
            || init.Contains("BoolText(", StringComparison.Ordinal);
    }

    // Pinned by the theory below: the demo sweep must include exactly the surfaces hosts copy
    // (Demo.SharedUI pages, the Report Server web client) and exclude everything else demo-ish
    // (the WASM/Server demo hosts, the Report Server host, build output).
    [Theory]
    [InlineData("C:/repo/src/Tempo.Blazor.Demo.SharedUI/Pages/ReportEmbeddingPage.razor", true)]
    [InlineData("C:/repo/src/Tempo.Blazor.Demo.SharedUI/Pages/TestSuiteManagerPage.razor.cs", true)]
    [InlineData("C:/repo/src/Tempo.ReportServer.Web.Client/Pages/ReportDesignerPage.razor", true)]
    [InlineData("C:/repo/src/Tempo.Blazor.Demo/Pages/Index.razor", false)]
    [InlineData("C:/repo/src/Tempo.ReportServer.Web/App.razor", false)]
    [InlineData("C:/repo/src/Tempo.Blazor.Demo.SharedUI/obj/Debug/net10.0/Generated.razor", false)]
    [InlineData("C:/repo/src/Tempo.Blazor/Components/Buttons/TmButton.razor", true)]
    public void IsLibrarySource_PinsTheDemoSweepBoundaries(string path, bool expected)
    {
        IsLibrarySource(path).Should().Be(expected);
    }

    internal static bool IsLibrarySource(string path)
    {
        var p = path.Replace('\\', '/');
        if (!p.Contains("/src/")) return false;
        if (p.Contains("/bin/") || p.Contains("/obj/")) return false;
        // Fáze 4h (plan b75365fb): the demo surfaces joined the sweep with ZERO tolerance (no
        // frozen entries) — the Demo.SharedUI pages and the Report Server web client render the
        // same attributes hosts copy, so a bool state there is the same defect.
        if (p.Contains("/Tempo.Blazor.Demo.SharedUI/") || p.Contains("/Tempo.ReportServer.Web.Client/"))
        {
            return p.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        }

        if (p.Contains(".Demo") || p.Contains("ReportServer")) return false;
        return p.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx"))) return directory;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
