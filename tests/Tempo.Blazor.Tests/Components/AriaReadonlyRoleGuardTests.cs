using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Components;

/// <summary>
/// Fáze 4g (plan b75365fb): <c>aria-readonly</c> is supported by ARIA 1.2 ONLY on the roles
/// checkbox, combobox, grid, gridcell, listbox, radiogroup, slider, spinbutton, textbox,
/// columnheader, rowheader, menuitemcheckbox, menuitemradio and switch — and on the native
/// <c>&lt;input&gt;</c> / <c>&lt;textarea&gt;</c> / <c>&lt;select&gt;</c> elements those roles map
/// from. Everywhere else (a plain <c>&lt;div&gt;</c>, an <c>&lt;aside&gt;</c>/complementary, a
/// <c>role="group"</c> container…) the attribute is invalid: axe flags it critical
/// (aria-allowed-attr) and a screen reader announces nothing. The defect was found twice in this
/// plan — on the locked <c>TmTimePicker</c> input group (fáze 4c) and on the
/// <c>TmSigningFieldEditorPanel</c> root / <c>TmNotionPageHistory</c> preview (this phase) — so
/// the guard sweeps every library razor file: an <c>aria-readonly</c> attribute is legal only on
/// an element whose literal <c>role</c> is in the supported set, or on a native
/// input/textarea/select. No frozen list — the tree is clean and must stay clean.
/// The analyzer is fed synthetic fixtures below, so a regression of the guard itself is caught
/// without touching a library file (the mutant: aria-readonly on a &lt;div&gt; must be reported).
/// Scope is deliberate: the reverse-scan (attribute → enclosing tag → role) runs over
/// <c>*.razor</c> MARKUP only, because only there can the attribute be checked against its
/// element's role. The code-behind / dictionary surface (<c>AddAttribute("aria-readonly", …)</c>,
/// attribute dictionaries) is covered by the second fact, which flags EVERY
/// <c>"aria-readonly"</c> string literal followed by <c>,</c> or <c>]</c> in library
/// <c>.cs</c>/<c>.razor</c> code — the role cannot be proven there, so no occurrence is legal.
/// Single-quoted markup attributes (<c>role='switch'</c>) are NOT supported by the analyzer;
/// the repo uses single quotes only inside JS selector strings and prose, never as an
/// aria-readonly/role markup attribute.
/// </summary>
public class AriaReadonlyRoleGuardTests
{
    private static readonly HashSet<string> SupportedRoles = new(StringComparer.Ordinal)
    {
        "checkbox", "combobox", "grid", "gridcell", "listbox", "radiogroup", "slider",
        "spinbutton", "textbox", "columnheader", "rowheader", "menuitemcheckbox",
        "menuitemradio", "switch",
    };

    private static readonly HashSet<string> NativeTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "input", "textarea", "select",
    };

    [Fact]
    public void NoAriaReadonly_OnAnUnsupportedRole_InLibraryRazor()
    {
        var root = FindRepoRoot();
        var srcDir = Path.Combine(root.FullName, "src");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.razor", SearchOption.AllDirectories))
        {
            if (!IsLibrarySource(file)) continue;

            var relative = Path.GetRelativePath(root.FullName, file).Replace('\\', '/');
            offenders.AddRange(FindOffenders(relative, File.ReadAllText(file)));
        }

        offenders.Should().BeEmpty(
            "aria-readonly is supported only on roles "
            + string.Join(", ", SupportedRoles.OrderBy(role => role, StringComparer.Ordinal))
            + " and on native input/textarea/select — everywhere else it is invalid ARIA "
            + "(axe aria-allowed-attr, critical) and announces nothing. Offenders:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void NoAriaReadonly_InCodeBehindOrAttributeDictionaries()
    {
        var root = FindRepoRoot();
        var srcDir = Path.Combine(root.FullName, "src");

        var offenders = new List<string>();
        foreach (var pattern in new[] { "*.razor", "*.cs" })
        {
            foreach (var file in Directory.EnumerateFiles(srcDir, pattern, SearchOption.AllDirectories))
            {
                if (!IsLibrarySource(file)) continue;

                var relative = Path.GetRelativePath(root.FullName, file).Replace('\\', '/');
                var text = StripComments(File.ReadAllText(file));
                foreach (Match match in Regex.Matches(text, "\"aria-readonly\"\\s*[,\\]]", RegexOptions.IgnoreCase))
                {
                    offenders.Add($"{relative}|\"aria-readonly\" in code — the role cannot be proven");
                }
            }
        }

        offenders.Should().BeEmpty(
            "aria-readonly set from code (AddAttribute, attribute dictionaries) cannot be checked "
            + "against the element's role, so no \"aria-readonly\" literal followed by ',' or ']' is "
            + "allowed in library code — render the attribute in markup on a supported role instead. "
            + "Offenders:\n" + string.Join("\n", offenders));
    }

    [Theory]
    // The mutant this guard exists for: the attribute on a plain div / complementary / group.
    [InlineData("""<div class="tm-x" aria-readonly="true">x</div>""", "div")]
    [InlineData("""<aside class="tm-x" aria-readonly="@BoolText(ReadOnly)">x</aside>""", "aside")]
    [InlineData("""<div role="group" aria-readonly="true">x</div>""", "div")]
    [InlineData("""<div role="region" aria-label="Preview" aria-readonly="true">x</div>""", "div")]
    // data-role is NOT a role: the checkbox in the attribute name must not legitimise the div.
    [InlineData("""<div data-role="checkbox" aria-readonly="true">x</div>""", "div")]
    // A quoted string containing /* (an accept filter) must not swallow the markup after it.
    [InlineData("""<input accept="image/*" /><div aria-readonly="true">x</div>@code { /* note */ }""", "div")]
    // A dynamic role cannot be proven supported — treated as an offender (strict by design).
    [InlineData("""<span role="@SegmentRole" aria-readonly="true">x</span>""", "span")]
    public void Analyzer_ReportsAriaReadonly_OnUnsupportedElements(string markup, string tag)
    {
        var offenders = FindOffenders("fixture.razor", markup);

        offenders.Should().ContainSingle(offender => offender.Contains($"<{tag}>", StringComparison.Ordinal),
            "the guard must flag aria-readonly on an element whose role ARIA does not support");
    }

    [Theory]
    // Every supported role and the native elements stay legal.
    [InlineData("""<select aria-readonly="@(ReadOnly ? "true" : null)">x</select>""")]
    [InlineData("""<input type="text" aria-readonly="true" />""")]
    [InlineData("""<textarea aria-readonly="true"></textarea>""")]
    [InlineData("""<span role="spinbutton" aria-readonly="true">x</span>""")]
    [InlineData("""<div role="combobox" aria-readonly="@(ReadOnly ? "true" : null)">x</div>""")]
    [InlineData("""<div role="listbox" aria-readonly="true">x</div>""")]
    [InlineData("""<div role="textbox" aria-readonly="true">x</div>""")]
    [InlineData("""<div role="gridcell" aria-readonly="true">x</div>""")]
    [InlineData("""<div role="switch" aria-readonly="true">x</div>""")]
    // data-role carries no role semantics; the real role attribute decides.
    [InlineData("""<div data-role="x" role="switch" aria-readonly="true">x</div>""")]
    // Comment occurrences are prose, not markup — never reported.
    [InlineData("""@* aria-readonly is not valid on role=group *@<div role="group">x</div>""")]
    [InlineData("""<!-- no aria-readonly here: ARIA 1.2 does not support it --><div>x</div>""")]
    [InlineData("""
        <div class="tm-x">
            @code {
                /// <summary>The segments carry the lock (<c>aria-readonly</c>).</summary>
                private bool ReadOnly => true;
            }
        </div>
        """)]
    public void Analyzer_AcceptsSupportedRolesNativeTagsAndComments(string markup)
    {
        FindOffenders("fixture.razor", markup).Should().BeEmpty();
    }

    /// <summary>
    /// Every <c>aria-readonly</c> attribute in the markup (comments stripped) whose enclosing
    /// element is neither a native input/textarea/select nor carries a literal supported
    /// <c>role</c>. Offender format: <c>path|&lt;tag&gt;|role=…</c>.
    /// </summary>
    internal static IReadOnlyList<string> FindOffenders(string relativePath, string markup)
    {
        var text = StripComments(markup);
        var offenders = new List<string>();

        foreach (Match match in Regex.Matches(text, @"aria-readonly\s*=", RegexOptions.IgnoreCase))
        {
            var tag = FindEnclosingTag(text, match.Index);
            if (tag is null)
            {
                offenders.Add($"{relativePath}|<unknown>|no enclosing tag found");
                continue;
            }

            var (name, attributes) = tag.Value;
            if (NativeTags.Contains(name)) continue;

            // The lookbehind keeps data-role (or any *-role attribute) from posing as the role.
            var role = Regex.Match(attributes, "(?<![\\w-])role\\s*=\\s*\"(?<role>[^\"]*)\"", RegexOptions.IgnoreCase);
            if (role.Success && SupportedRoles.Contains(role.Groups["role"].Value.Trim()))
            {
                continue;
            }

            offenders.Add(role.Success
                ? $"{relativePath}|<{name}>|role={role.Groups["role"].Value}"
                : $"{relativePath}|<{name}>|no literal role");
        }

        return offenders;
    }

    /// <summary>
    /// Razor (<c>@* *@</c>), HTML (<c>&lt;!-- --&gt;</c>) and C# (<c>//</c> doc/line and
    /// <c>/* */</c>) comments removed, so prose mentioning the attribute is never scanned.
    /// Block and line comments are stripped only OUTSIDE quoted strings — a quoted value that
    /// contains a comment opener (e.g. <c>accept="image/*"</c>) must not swallow the markup
    /// after it; the quoted-string alternation wins the match and is kept verbatim.
    /// </summary>
    private static string StripComments(string markup)
    {
        var text = Regex.Replace(markup, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"<!--.*?-->", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, "\"[^\"\\n]*\"|/\\*.*?\\*/",
            match => match.Value.StartsWith('"') ? match.Value : " ", RegexOptions.Singleline);
        // Line comments (incl. /// XML doc lines): a // that is not part of a URL scheme (://).
        text = Regex.Replace(text, "\"[^\"\\n]*\"|(?<!:)//[^\\n]*",
            match => match.Value.StartsWith('"') ? match.Value : " ");
        return text;
    }

    /// <summary>
    /// The tag the attribute at <paramref name="index"/> belongs to: walking backwards over
    /// quoted strings, the first <c>&lt;tag</c> opens the element the attribute sits in (a tag's
    /// attributes never contain an unquoted <c>&gt;</c>). Returns the tag name and the full
    /// attribute text up to the tag's closing <c>&gt;</c>.
    /// </summary>
    private static (string Name, string Attributes)? FindEnclosingTag(string text, int index)
    {
        var inQuote = false;
        for (var i = index - 1; i >= 0; i--)
        {
            var c = text[i];
            if (inQuote)
            {
                if (c == '"') inQuote = false;
                continue;
            }

            if (c == '"')
            {
                inQuote = true;
                continue;
            }

            if (c == '<' && i + 1 < text.Length && char.IsLetter(text[i + 1]))
            {
                var end = FindTagEnd(text, i);
                if (end < 0 || end < index) return null; // the tag closed before the attribute — not ours
                var nameMatch = Regex.Match(text[i..], @"<(?<name>[A-Za-z][\w-]*)");
                return (nameMatch.Groups["name"].Value, text[i..end]);
            }
        }

        return null;
    }

    private static int FindTagEnd(string text, int tagStart)
    {
        var inQuote = false;
        for (var i = tagStart; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuote)
            {
                if (c == '"') inQuote = false;
                continue;
            }

            if (c == '"') inQuote = true;
            else if (c == '>') return i;
        }

        return -1;
    }

    private static bool IsLibrarySource(string path)
    {
        var p = path.Replace('\\', '/');
        if (!p.Contains("/src/")) return false;
        if (p.Contains("/bin/") || p.Contains("/obj/")) return false;
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
