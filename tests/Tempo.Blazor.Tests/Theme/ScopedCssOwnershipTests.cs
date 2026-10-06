using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// P21: a scoped <c>.razor.css</c> rewrites every selector to <c>.class[b-xxxx]</c> where
/// <c>b-xxxx</c> is the scope of the OWNING component only. A rule whose key class is rendered
/// by a child component never applies — Blazor isolation strips it silently, and neither the
/// token audit nor bUnit notices. The rule is: the key class of every selector must be rendered
/// by the owning component, or the selector must opt into <c>::deep</c> / <c>:deep</c>.
/// </summary>
/// <remarks>
/// The fixture half proves the heuristic can fail (a parent stylesheet styling a child's class
/// is reported; <c>::deep</c> and a class the owner renders itself are not). The repository half
/// freezes today's findings — Gantt, Notion table/editor/column, Modeling — and may only shrink.
/// Moving those rules is the Gantt and Notion plans' work, not this one's.
/// </remarks>
public sealed class ScopedCssOwnershipTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void KeySelectorClass_IsRenderedByOwningComponent()
    {
        const string parentCss = """
            .tm-parent { color: red; }
            .tm-parent__child-only { padding: 4px; }
            ::deep .tm-parent__child-only { margin: 0; }
            """;
        const string parentRazor = """<div class="tm-parent"><Child /></div>""";
        const string childRazor = """<div class="tm-parent__child-only"></div>""";

        var findings = ScopedCssOwnership
            .FindForeignClasses(parentCss, parentRazor, [childRazor])
            .Select(finding => finding.ClassName)
            .ToList();

        findings.Should().Equal(["tm-parent__child-only"]);
    }

    [Fact]
    public void CommaGroup_DoesNotLetADeepItemHideAPlainSibling()
    {
        const string parentCss = """.tm-parent__child-only, .tm-parent {}""";
        const string parentRazor = """<div class="tm-parent"><Child /></div>""";
        const string childRazor = """<div class="tm-parent__child-only"></div>""";

        var findings = ScopedCssOwnership
            .FindForeignClasses(parentCss, parentRazor, [childRazor])
            .Select(finding => finding.ClassName)
            .ToList();

        findings.Should().Equal(["tm-parent__child-only"],
            "a comma group is several selectors, and a ::deep-free item must be judged on its own");
    }

    [Fact]
    public void Repository_ForeignClasses_DoNotExceedTheBaseline()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var findings = ScopedCssOwnership.Scan(root);
        var baseline = ScopedCssOwnership.Baseline(root);

        findings.Count.Should().BeLessThanOrEqualTo(77,
            "growing past the hard ceiling is a code change, not a baseline edit");

        findings.Should().NotBeEmpty(
            "the baseline exists because Gantt/Notion/Modeling already have foreign classes; "
            + "an empty scan means the heuristic stopped seeing them");

        var regressions = findings
            .Where(finding => !baseline.Contains(finding.Key))
            .Select(finding => finding.Key)
            .ToList();

        regressions.Should().BeEmpty(
            "a NEW foreign class in a scoped stylesheet is a rule that will not apply. "
            + "Use ::deep, or move the rule into the component that renders the class. New: "
            + string.Join(", ", regressions));

        var stale = baseline.Except(findings.Select(finding => finding.Key)).ToList();
        stale.Should().BeEmpty(
            "the baseline may only shrink — remove the fixed entries from "
            + "tests/Tempo.Blazor.Tests/Theme/scoped-css-ownership-baseline.txt: "
            + string.Join(", ", stale));
    }
}

/// <summary>
/// The heuristic from 00-cross-cutting §7.4: take the classes of the KEY (last) compound of each
/// selector that does not opt into <c>::deep</c>, and report those the owner does not render.
/// </summary>
internal static class ScopedCssOwnership
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ClassName =
        new(@"\.(-?[_a-zA-Z][_a-zA-Z0-9-]*)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex MarkupClass =
        new(@"class\s*=\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

    private static readonly Regex DeepMarker =
        new(@"::deep|:deep\(", RegexOptions.Compiled, RegexTimeout);

    public static IReadOnlyList<ForeignClass> FindForeignClasses(
        string scopedCss, string ownerMarkup, IReadOnlyList<string> otherMarkup)
    {
        var ownerClasses = ClassesIn(ownerMarkup);
        var renderedElsewhere = otherMarkup
            .SelectMany(ClassesIn)
            .ToHashSet(StringComparer.Ordinal);

        var findings = new List<ForeignClass>();
        foreach (var selector in Selectors(scopedCss))
        {
            // A comma group is several selectors. Judging the group as a whole lets a
            // ::deep item hide a plain sibling, so each item is judged on its own.
            foreach (var item in SelectorItems(selector))
            {
                if (DeepMarker.IsMatch(item))
                {
                    continue;
                }

                Judge(item);
            }

            void Judge(string item)
            {
            var key = KeyCompound(item);
            foreach (Match match in ClassName.Matches(key))
            {
                var className = match.Groups[1].Value;
                if (ownerClasses.Contains(className) || !renderedElsewhere.Contains(className))
                {
                    continue;
                }

                findings.Add(new ForeignClass(className, item.Trim()));
            }
            }
        }

        return findings
            .DistinctBy(finding => finding.ClassName + "|" + finding.Selector)
            .ToList();
    }

    public static IReadOnlyList<RepositoryFinding> Scan(string repositoryRoot)
    {
        var razorFiles = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.razor", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToList();
        var markupByComponent = razorFiles.ToDictionary(
            ComponentKey,
            path => File.ReadAllText(path) + CompanionCode(path),
            StringComparer.OrdinalIgnoreCase);

        var findings = new List<RepositoryFinding>();
        foreach (var cssPath in razorFiles.Select(path => path + ".css").Where(File.Exists))
        {
            var ownerKey = ComponentKey(cssPath[..^4]);
            if (!markupByComponent.TryGetValue(ownerKey, out var ownerMarkup))
            {
                continue;
            }

            var others = markupByComponent
                .Where(pair => !string.Equals(pair.Key, ownerKey, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Value)
                .ToList();

            foreach (var finding in FindForeignClasses(File.ReadAllText(cssPath), ownerMarkup, others))
            {
                findings.Add(new RepositoryFinding(
                    Path.GetRelativePath(repositoryRoot, cssPath).Replace('\\', '/'),
                    finding.ClassName));
            }
        }

        return findings
            .DistinctBy(finding => finding.Key)
            .OrderBy(finding => finding.Key, StringComparer.Ordinal)
            .ToList();
    }

    public static HashSet<string> Baseline(string repositoryRoot)
    {
        var path = Path.Combine(
            repositoryRoot, "tests", "Tempo.Blazor.Tests", "Theme", "scoped-css-ownership-baseline.txt");
        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string CompanionCode(string razorPath)
    {
        var codeBehind = razorPath + ".cs";
        return File.Exists(codeBehind) ? File.ReadAllText(codeBehind) : string.Empty;
    }

    private static string ComponentKey(string razorPath) =>
        razorPath[..^".razor".Length];

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>Splits a selector group on top-level commas, ignoring commas inside parentheses.</summary>
    private static IEnumerable<string> SelectorItems(string selector)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < selector.Length; i++)
        {
            switch (selector[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ',' when depth == 0:
                    yield return selector[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return selector[start..];
    }

    private static IEnumerable<string> Selectors(string css)
    {
        var stripped = Comment.Replace(css, " ");
        return RuleBlock.Matches(stripped).Select(match => match.Groups["selector"].Value);
    }

    private static readonly Regex FunctionCall =
        new(@":[a-zA-Z-]+\((?>[^()]+|(?<depth>\()|(?<-depth>\)))*\)(?(depth)(?!))", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex CompoundSeparator =
        new(@"\s*[>+~]\s*|\s+", RegexOptions.Compiled, RegexTimeout);

    /// <summary>The last compound of a selector — the element the declaration actually paints.</summary>
    private static string KeyCompound(string selector)
    {
        var compounds = CompoundSeparator.Split(FunctionCall.Replace(selector, " "))
            .Where(part => part.Contains('.'))
            .ToList();
        return compounds.Count == 0 ? selector : compounds[^1];
    }

    private static HashSet<string> ClassesIn(string markup)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in MarkupClass.Matches(markup))
        {
            foreach (var token in match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                classes.Add(token.TrimStart('.'));
            }
        }

        return classes;
    }
}

internal readonly record struct ForeignClass(string ClassName, string Selector);

internal readonly record struct RepositoryFinding(string File, string ClassName)
{
    public string Key => $"{File} :: {ClassName}";
}
