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
/// identifier; anything else is an offender;</item>
/// <item>code-behind <c>@attributes</c> dictionary: <c>attrs["aria-X"] = value</c> — the same
/// value rules as <c>AddAttribute</c> (fáze 4k review).</item>
/// </list>
/// Fáze 4h (plan b75365fb) extended the sweep to the demo surfaces
/// (<c>Tempo.Blazor.Demo.SharedUI</c>, <c>Tempo.ReportServer.Web.Client</c>) with zero tolerance —
/// no frozen entries there. Fáze 4i fixed the NotionEditor page and sidebar sites (the settings
/// menu, watch button, sidebar sections, page tree, comment panel, my tasks and page history)
/// through a string <c>BoolText</c> helper — except the watch toggle, whose <c>aria-pressed</c>
/// was removed entirely (its changing name announces the state, APG) — shrinking the frozen
/// list by 19. Fáze 4j fixed the NotionEditor menu and search surfaces (the mention menu tabs
/// and option lists, the comment mention input, the emoji picker categories, the token
/// dropdown, the block type switcher and the page search results) through the same string
/// <c>BoolText</c> helper — shrinking the frozen list by another 14. Fáze 4k fixed the last ten
/// sites (the NotionEditor blocks and database: the toggle block arrow, the table group toggle
/// and row expander, the view tabs, the import/export tabs, the group bar, the field editor
/// color dots and the work item display modes) and with the list empty REMOVED the frozen-list
/// mechanism — the guard now holds the library and the demo at ZERO tolerance: any boolean
/// ARIA state expression fails, with no exceptions list to hide behind.
/// The fáze 4k review widened the net twice: (1) the attribute set now covers the whole
/// true/false-token family — <c>aria-busy</c> / <c>aria-hidden</c> / <c>aria-disabled</c> /
/// <c>aria-invalid</c> / <c>aria-required</c> / <c>aria-modal</c> / <c>aria-readonly</c> /
/// <c>aria-multiselectable</c> / <c>aria-atomic</c> join the original four. Those tokens may
/// OMIT the attribute instead of rendering <c>"false"</c> (<c>? "true" : null</c> is a valid
/// string-producing expression), but a raw bool is the same defect — the one real offender the
/// widened sweep found was the <c>TmNotionSidebar</c> New page <c>aria-busy="@_isCreatingPage"</c>,
/// fixed in the same review round. (2) The declaration resolution now includes PARTIAL SIBLINGS
/// (<c>TmStatCard.Kpi.cs</c> declares <c>TmStatCard.razor</c>'s <c>BusyAttribute</c>) and the
/// <c>@attributes</c> dictionary form (<c>attrs["aria-invalid"] = InvalidText</c> in
/// <c>TmSigningChoiceStep.razor.cs</c>) is detected next to <c>AddAttribute</c>.
/// </summary>
public class AriaBooleanStateAttributeGuardTests
{
    // The true/false-token ARIA state family. expanded/pressed/selected/checked must render the
    // state in BOTH positions; the rest may omit the attribute instead of "false" — but none of
    // them may ever receive a raw bool.
    private const string Attr =
        "aria-(?:expanded|pressed|selected|checked|busy|hidden|disabled|invalid|required|modal|readonly|multiselectable|atomic)";

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

    // attrs["aria-X"] = value — an @attributes dictionary entry in a code-behind .cs file
    // (the value ends at the statement/initializer separator).
    private static readonly Regex CsAttrDictionary =
        new("\\[\\s*\"(?<attr>" + Attr + ")\"\\s*\\]\\s*=\\s*(?<val>[^;,}\\r\\n]+)",
            RegexOptions.Compiled);

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

            // Partial siblings (TmStatCard.Kpi.cs next to TmStatCard.razor) declare helpers the
            // razor binds — without them a legitimate string helper reads as an unproven bool.
            var directory = Path.GetDirectoryName(file)!;
            var siblingPattern = Path.GetFileNameWithoutExtension(file) + ".*.cs";
            foreach (var sibling in Directory.EnumerateFiles(directory, siblingPattern))
            {
                if (string.Equals(sibling, codeBehindPath, StringComparison.OrdinalIgnoreCase)) continue;
                code += "\n" + File.ReadAllText(sibling);
            }

            foreach (var offender in RazorAttributeOffenders(razor, code))
            {
                offenders.Add($"{relative}|{offender}");
            }
        }

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (!IsLibrarySource(file)) continue;

            var relative = Path.GetRelativePath(root.FullName, file).Replace('\\', '/');
            var code = File.ReadAllText(file);

            foreach (var offender in CsAttributeOffenders(code))
            {
                offenders.Add($"{relative}|{offender}");
            }
        }

        // Fáze 4k (plan b75365fb): the frozen list is gone — zero tolerance. Every boolean ARIA
        // state expression in the library or the demo surfaces fails the guard; there is no
        // exceptions list to add a new site to.
        offenders.Should().BeEmpty(
            "no aria-expanded/aria-pressed/aria-selected/aria-checked/aria-busy/aria-hidden/" +
            "aria-disabled/aria-invalid/aria-required/aria-modal/aria-readonly/" +
            "aria-multiselectable/aria-atomic may receive a boolean expression (invalid empty " +
            "ARIA value) — pass the strings ? \"true\" : \"false\" (or a BoolText helper); the " +
            "true/false-token attributes (busy/hidden/disabled/…) may omit the attribute " +
            "instead of \"false\": ? \"true\" : null. Found:\n" + string.Join("\n", offenders));
    }

    // Every boolean ARIA state expression in one razor file, given the file's combined
    // declaration text (razor + code-behind + partial siblings). Entries are "attr|expr".
    internal static List<string> RazorAttributeOffenders(string razor, string code)
    {
        var offenders = new List<string>();

        var stringIds = new HashSet<string>(
            StringDecl.Matches(code).Select(m => m.Groups["id"].Value),
            StringComparer.Ordinal);

        foreach (Match m in Bare.Matches(razor))
        {
            var id = m.Groups["id"].Value;
            if (stringIds.Contains(id)) continue; // a string helper property (e.g. AriaPressed)
            offenders.Add($"{m.Value.Split('=')[0]}|@{id}");
        }

        foreach (Match m in Parens.Matches(razor))
        {
            var expr = m.Groups["expr"].Value;
            // String-producing: a ternary with the literal state strings ("mixed" is the
            // third valid aria-checked value; "true" : null is the valid omission form of the
            // true/false-token attributes), or a .ToString() call.
            if (ProducesStateString(expr)) continue;

            offenders.Add($"{m.Value.Split('=')[0]}|@({expr})");
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
                    offenders.Add($"{m.Value.Split('=')[0]}|@{expr}");
                }

                continue;
            }

            // A dotted path is an offender unless its last segment is proven string above —
            // the guard cannot see the member's type across files, and a bool model
            // property is exactly the defect this guard exists for.
            offenders.Add($"{m.Value.Split('=')[0]}|@{expr}");
        }

        return offenders;
    }

    // Every boolean ARIA state value assigned in one code-behind file — through
    // AddAttribute(seq, "aria-X", value) or an @attributes dictionary entry
    // attrs["aria-X"] = value. Entries are "attr|value".
    internal static List<string> CsAttributeOffenders(string code)
    {
        var offenders = new List<string>();

        foreach (Match m in CsAddAttribute.Matches(code))
        {
            var val = m.Groups["val"].Value.Trim();
            if (IsStringValued(val, code)) continue;
            offenders.Add($"{m.Groups["attr"].Value}|{val}");
        }

        foreach (Match m in CsAttrDictionary.Matches(code))
        {
            var val = m.Groups["val"].Value.Trim();
            if (IsStringValued(val, code)) continue;
            offenders.Add($"{m.Groups["attr"].Value}|{val}");
        }

        return offenders;
    }

    // The expression yields the state strings: a ternary carrying a "true"/"false"/"mixed"
    // literal (the other branch may be null — the omission form of the true/false-token
    // attributes) or a .ToString() call.
    private static bool ProducesStateString(string expr)
        => expr.Contains("\"true\"", StringComparison.Ordinal)
            || expr.Contains("\"false\"", StringComparison.Ordinal)
            || expr.Contains("\"mixed\"", StringComparison.Ordinal)
            || expr.Contains(".ToString(", StringComparison.Ordinal);

    // The member the attribute actually reads: the last path segment before the call's "(".
    private static string KeyOf(string expr)
    {
        var head = expr.Split('(')[0];
        var segments = head.Split('.');
        return segments[^1];
    }

    // An AddAttribute / dictionary value is string-valued when it is a string literal, an
    // expression that produces the state strings, or a bare identifier the file declares as
    // string (or as a var initialized from the state strings — DocumentToolbarToggleRenderer's
    // "pressed" switch).
    private static bool IsStringValued(string val, string code)
    {
        if (val.StartsWith('"')) return true;
        if (ProducesStateString(val) || val.Contains("BoolText(", StringComparison.Ordinal))
        {
            return true;
        }

        if (!Regex.IsMatch(val, @"^[A-Za-z_]\w*$")) return false;

        if (Regex.IsMatch(code, $@"\bstring\??\s+{Regex.Escape(val)}\b")) return true;

        var varDecl = Regex.Match(code, $@"\bvar\s+{Regex.Escape(val)}\s*=\s*(?<init>[^;]+);");
        if (!varDecl.Success) return false;

        var init = varDecl.Groups["init"].Value;
        return ProducesStateString(init) || init.Contains("BoolText(", StringComparison.Ordinal);
    }

    // The true/false-token attributes may omit the attribute instead of rendering "false" —
    // ? "true" : null is a valid string-producing expression, never an offender. A raw bool
    // identifier is the same defect on every attribute of the family.
    [Theory]
    [InlineData("aria-busy=\"@(_isCreatingPage ? \"true\" : null)\"", false)]
    [InlineData("aria-hidden=\"@(Compact ? \"true\" : \"false\")\"", false)]
    [InlineData("aria-disabled=\"@(ReadOnly ? \"true\" : null)\"", false)]
    [InlineData("aria-busy=\"@_isCreatingPage\"", true)]
    [InlineData("aria-pressed=\"@IsOn\"", true)]
    [InlineData("aria-modal=\"@IsModal\"", true)]
    public void RazorAttribute_TrueTokenExpressions_Pass_BoolExpressionsFail(string markup, bool expectedOffender)
    {
        var offenders = RazorAttributeOffenders(markup, markup);
        offenders.Should().HaveCount(expectedOffender ? 1 : 0,
            expectedOffender
                ? $"a raw bool must be reported: {markup}"
                : $"a string-producing expression is valid: {markup}");
    }

    // The @attributes dictionary form in code-behind: a bool value is the same defect as a bool
    // AddAttribute value; the state strings (including the null-omitting ternary) pass.
    [Theory]
    [InlineData("attrs[\"aria-pressed\"] = isOn;", "aria-pressed|isOn")]
    [InlineData("attrs[\"aria-busy\"] = _loading;", "aria-busy|_loading")]
    [InlineData("attrs[\"aria-expanded\"] = isOn ? \"true\" : \"false\";", null)]
    [InlineData("attrs[\"aria-busy\"] = _loading ? \"true\" : null;", null)]
    [InlineData("private string? BusyText => _loading ? \"true\" : null;\nattrs[\"aria-busy\"] = BusyText;", null)]
    public void CsAttributeDictionary_BoolValue_IsAnOffender(string snippet, string? expectedOffender)
    {
        var offenders = CsAttributeOffenders(snippet);
        if (expectedOffender is null)
        {
            offenders.Should().BeEmpty($"a string-valued dictionary entry is valid: {snippet}");
        }
        else
        {
            offenders.Should().Equal(expectedOffender);
        }
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
        // Fáze 4h (plan b75365fb): the demo surfaces joined the sweep — the Demo.SharedUI pages
        // and the Report Server web client render the same attributes hosts copy, so a bool
        // state there is the same defect. The whole guard is zero-tolerance since fáze 4k.
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
