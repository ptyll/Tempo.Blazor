using FluentAssertions;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;

namespace Tempo.Blazor.Tests.Diff;

/// <summary>
/// Regression net for the two diff facades that were re-pointed at the shared
/// <c>Tempo.Blazor.Diff</c> engine.
/// <para>
/// <see cref="DocumentTextDiffHelper.Diff"/> (word level, document comparison) and
/// <see cref="TmTextLineDiff.Compute"/> (line level, file version history) are public API with
/// existing consumers and rendered output, so the extraction must be invisible: for the same input
/// the segment list must stay byte-identical. Two guards pin that:
/// </para>
/// <list type="bullet">
/// <item>explicit golden expectations for the inputs the existing suites use
/// (DocumentComparisonServiceTests, TmDocumentDiffHelperTests, TmFileUploadK4Tests), captured from
/// the implementation BEFORE the extraction;</item>
/// <item>a frozen copy of the pre-extraction algorithm inside this file, compared against the
/// façades over a generated corpus that includes punctuation, repeated whitespace, diacritics,
/// CRLF and the empty string — a hand-written golden list cannot cover the walk's tie-breaking,
/// and the tie-breaking is exactly what an extraction is most likely to change.</item>
/// </list>
/// <para>
/// The frozen copy below is deliberately not refactored, not deleted and not shared with the
/// product code: it is the reference the façades are measured against.
/// </para>
/// </summary>
public sealed class DiffFacadeRegressionTests
{
    // -------------------------------------------------------------------------------------------
    // Explicit goldens captured from the pre-extraction implementation
    // -------------------------------------------------------------------------------------------

    public static TheoryData<string, string, string> WordGoldens => new()
    {
        { "Smlouva je platná", "Smlouva je dnes platná", "Unchanged:Smlouva je|Added:dnes|Unchanged:platná" },
        { "Smlouva je stále platná", "Smlouva je platná", "Unchanged:Smlouva je|Removed:stále|Unchanged:platná" },
        { "Klient zaplatí 1000 Kč", "Klient zaplatí 1200 Kč", "Unchanged:Klient zaplatí|Removed:1000|Added:1200|Unchanged:Kč" },
        { "Příliš žluťoučký kůň úpěl", "Příliš žluťoučký kůň hlasitě úpěl", "Unchanged:Příliš žluťoučký kůň|Added:hlasitě|Unchanged:úpěl" },
        { "Price 1000", "Price 1200", "Unchanged:Price|Removed:1000|Added:1200" },
        { "Hello, world!", "Hello, brave world!", "Unchanged:Hello,|Added:brave|Unchanged:world!" },
        { "", "one two", "Added:one two" },
        { "one two", "", "Removed:one two" },
        { "", "", "" },
        { "   ", "a", "Added:a" },
        { "a", "   ", "Removed:a" },
        { "a b", "a b", "Unchanged:a b" },
        { "The quick brown fox", "The quick red fox jumps", "Unchanged:The quick|Removed:brown|Added:red|Unchanged:fox|Added:jumps" },
        { "abc", "axc", "Removed:abc|Added:axc" },
        { "1 2 3", "1 3", "Unchanged:1|Removed:2|Unchanged:3" },
        { "a  b   c", "a b c", "Unchanged:a b c" },
        { "a.b,c", "a.b,d", "Unchanged:a. b,|Removed:c|Added:d" },
        { "Žluťoučký kůň", "Žluťoučký kůň!", "Unchanged:Žluťoučký kůň|Added:!" },
        { "x y z", "x q r z", "Unchanged:x|Removed:y|Added:q r|Unchanged:z" },
        { "A B C D", "A C D", "Unchanged:A|Removed:B|Unchanged:C D" },
        { ".start", "start", "Removed:.|Unchanged:start" },
        { "foo(bar)", "foo(baz)", "Unchanged:foo (|Removed:bar|Added:baz|Unchanged:)" },
        { "smlouva-2024", "smlouva-2025", "Unchanged:smlouva-|Removed:2024|Added:2025" }
    };

    [Theory]
    [MemberData(nameof(WordGoldens))]
    public void DocumentTextDiffHelper_KeepsThePreExtractionOutput(string oldText, string newText, string expected)
    {
        var diff = DocumentTextDiffHelper.Diff(oldText, newText);

        Describe(diff).Should().Be(expected);
    }

    public static TheoryData<string, string, string> LineGoldens => new()
    {
        { "a\nb", "a\nb\nc", "Unchanged:a:1:1|Unchanged:b:2:2|Added:c::3" },
        { "a\nb\nc", "a\nB\nc", "Unchanged:a:1:1|Removed:b:2:|Added:B::2|Unchanged:c:3:3" },
        { "x\ny", "x\ny", "Unchanged:x:1:1|Unchanged:y:2:2" },
        { "", "one\ntwo", "Added:one::1|Added:two::2" },
        { "one\ntwo", "", "Removed:one:1:|Removed:two:2:" },
        { "", "", "" },
        { "a\r\nb\r\nc", "a\r\nB\r\nc", "Unchanged:a:1:1|Removed:b:2:|Added:B::2|Unchanged:c:3:3" },
        { "x\ny\nz", "x\nz", "Unchanged:x:1:1|Removed:y:2:|Unchanged:z:3:2" },
        { "line1\nline2\nline3\nline4", "line1\nlineX\nline3\nline4", "Unchanged:line1:1:1|Removed:line2:2:|Added:lineX::2|Unchanged:line3:3:3|Unchanged:line4:4:4" },
        { "a\nb", "b\na", "Removed:a:1:|Unchanged:b:2:1|Added:a::2" }
    };

    [Theory]
    [MemberData(nameof(LineGoldens))]
    public void TmTextLineDiff_KeepsThePreExtractionOutput(string oldText, string newText, string expected)
    {
        var lines = TmTextLineDiff.Compute(oldText, newText);

        string.Join("|", lines.Select(line =>
                $"{line.Kind}:{line.Text}:{line.OldLineNumber?.ToString() ?? string.Empty}:{line.NewLineNumber?.ToString() ?? string.Empty}"))
            .Should().Be(expected);
    }

    // -------------------------------------------------------------------------------------------
    // Degradation is visible through the façades
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void DocumentTextDiffHelper_DoesNotReportDegradation_ForANormalComparison()
    {
        var diff = DocumentTextDiffHelper.Diff("Smlouva je platná", "Smlouva je dnes platná");

        diff.IsDegraded.Should().BeFalse();
    }

    [Fact]
    public void DocumentTextDiffHelper_ReportsThatTheResultDegraded_AndKeepsTheDocumentedShape()
    {
        // 2 500 distinct tokens per side need a 2 501 × 2 501 word table (≈ 6.25 M cells), above
        // TextDiffOptions.DefaultMaxCells (4 M), so the engine degrades to line level. MaxCells is
        // used on purpose: an input above DefaultMaxTokens would degrade the same way, but re-joining
        // 20 000+ tokens in the façade would dominate this test's runtime.
        var oldText = TokenRun("a", 2500);
        var newText = TokenRun("b", 2500);

        var diff = DocumentTextDiffHelper.Diff(oldText, newText);

        diff.IsDegraded.Should().BeTrue();
        // The documented degradation above the limits: one block of removed lines, then one block of
        // added lines — the whole text on each side, because the input is a single line.
        diff.Segments.Select(segment => segment.Kind).Should().Equal(
            DocumentTextDiffSegmentKind.Removed,
            DocumentTextDiffSegmentKind.Added);
        diff.Segments[0].Text.Should().Be(oldText);
        diff.Segments[1].Text.Should().Be(newText);
    }

    private static string TokenRun(string prefix, int count)
        => string.Join(' ', Enumerable.Range(0, count).Select(index => prefix + index));

    // -------------------------------------------------------------------------------------------
    // Frozen reference implementation of the pre-extraction algorithms
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void DocumentTextDiffHelper_MatchesTheFrozenWordAlgorithm_OverTheCorpus()
    {
        foreach (var (oldText, newText) in Corpus())
        {
            var actual = Describe(DocumentTextDiffHelper.Diff(oldText, newText));
            var expected = DescribeFrozenWordDiff(oldText, newText);

            actual.Should().Be(expected, "'{0}' -> '{1}'", oldText, newText);
        }
    }

    [Fact]
    public void TmTextLineDiff_MatchesTheFrozenLineAlgorithm_OverTheCorpus()
    {
        foreach (var (oldText, newText) in Corpus())
        {
            var actual = Describe(TmTextLineDiff.Compute(oldText, newText));
            var expected = DescribeFrozenLineDiff(oldText, newText);

            actual.Should().Be(expected, "'{0}' -> '{1}'", oldText, newText);
        }
    }

    [Fact]
    public void DocumentComparisonService_StillReportsTheSameSegments()
    {
        // The consumer that motivated the word-level helper: one changed paragraph, one added block.
        var comparison = new DocumentComparisonService().Compare(
            CreateDocument(("p1", "Smlouva je platná")),
            CreateDocument(("p1", "Smlouva je dnes platná"), ("p2", "Nový odstavec")));

        comparison.Summary.ChangedBlocks.Should().Be(1);
        comparison.Summary.AddedBlocks.Should().Be(1);
        Describe(comparison.TextDiff).Should().Be(
            "Unchanged:Smlouva je|Added:dnes|Unchanged:platná|Added:Nový odstavec");
    }

    private static DocumentEditorDocument CreateDocument(params (string Id, string Text)[] blocks)
    {
        var document = DocumentEditorDocument.Empty("doc-1");
        document.Blocks = blocks.Select((block, index) => new DocumentBlock
        {
            Id = block.Id,
            Type = DocumentBlockType.Paragraph,
            Order = index,
            Content = new ParagraphBlockContent
            {
                Inlines = [new TextRun { Text = block.Text }]
            }
        }).ToList();
        return document;
    }

    private static string Describe(DocumentTextDiffResult diff)
        => string.Join("|", diff.Segments.Select(segment => $"{segment.Kind}:{segment.Text}"));

    private static string Describe(IReadOnlyList<Tempo.Blazor.Abstractions.Shared.TmFileVersionDiffLine> lines)
        => string.Join("|", lines.Select(line =>
            $"{line.Kind}:{line.Text}:{line.OldLineNumber?.ToString() ?? string.Empty}:{line.NewLineNumber?.ToString() ?? string.Empty}"));

    private static IEnumerable<(string Old, string New)> Corpus()
    {
        string[] samples =
        [
            "",
            " ",
            "   ",
            "a",
            "a b",
            "b a",
            "a  b",
            "a b c",
            "a c b",
            "a.b,c",
            "a,b.c",
            "foo(bar)",
            "foo (bar)",
            "1000 Kč",
            "1200 Kč",
            "Příliš žluťoučký kůň úpěl",
            "Příliš žluťoučký kůň hlasitě úpěl",
            "x\ny",
            "x\ny\nz",
            "x\nz",
            "\n",
            "a\r\nb",
            "a\nb",
            "-!-",
            "Hello, world!"
        ];

        foreach (var oldText in samples)
        {
            foreach (var newText in samples)
            {
                yield return (oldText, newText);
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex FrozenTokenRegex =
        new(@"\p{L}[\p{L}\p{M}\p{N}]*|\p{N}+|[^\s]",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string DescribeFrozenWordDiff(string? oldText, string? newText)
    {
        var oldTokens = TokenizeFrozen(oldText);
        var newTokens = TokenizeFrozen(newText);
        var table = new int[oldTokens.Count + 1, newTokens.Count + 1];
        for (var i = oldTokens.Count - 1; i >= 0; i--)
        {
            for (var j = newTokens.Count - 1; j >= 0; j--)
            {
                table[i, j] = string.Equals(oldTokens[i], newTokens[j], StringComparison.Ordinal)
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var segments = new List<(string Kind, string Text)>();
        int oldIndex = 0, newIndex = 0;
        while (oldIndex < oldTokens.Count && newIndex < newTokens.Count)
        {
            if (string.Equals(oldTokens[oldIndex], newTokens[newIndex], StringComparison.Ordinal))
            {
                AppendFrozen(segments, "Unchanged", oldTokens[oldIndex]);
                oldIndex++;
                newIndex++;
            }
            else if (table[oldIndex + 1, newIndex] >= table[oldIndex, newIndex + 1])
            {
                AppendFrozen(segments, "Removed", oldTokens[oldIndex++]);
            }
            else
            {
                AppendFrozen(segments, "Added", newTokens[newIndex++]);
            }
        }

        while (oldIndex < oldTokens.Count)
        {
            AppendFrozen(segments, "Removed", oldTokens[oldIndex++]);
        }

        while (newIndex < newTokens.Count)
        {
            AppendFrozen(segments, "Added", newTokens[newIndex++]);
        }

        return string.Join("|", segments.Select(segment => $"{segment.Kind}:{segment.Text}"));
    }

    private static string DescribeFrozenLineDiff(string? oldText, string? newText)
    {
        var a = string.IsNullOrEmpty(oldText) ? [] : oldText.Replace("\r\n", "\n").Split('\n');
        var b = string.IsNullOrEmpty(newText) ? [] : newText.Replace("\r\n", "\n").Split('\n');
        int n = a.Length, m = b.Length;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var lines = new List<string>();
        int x = 0, y = 0, oldLine = 1, newLine = 1;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                lines.Add($"Unchanged:{a[x]}:{oldLine++}:{newLine++}");
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                lines.Add($"Removed:{a[x++]}:{oldLine++}:");
            }
            else
            {
                lines.Add($"Added:{b[y++]}::{newLine++}");
            }
        }

        while (x < n)
        {
            lines.Add($"Removed:{a[x++]}:" + $"{oldLine++}:");
        }

        while (y < m)
        {
            lines.Add($"Added:{b[y++]}::" + $"{newLine++}");
        }

        return string.Join("|", lines);
    }

    private static List<string> TokenizeFrozen(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? []
            : FrozenTokenRegex.Matches(text).Select(match => match.Value).ToList();

    private static void AppendFrozen(List<(string Kind, string Text)> segments, string kind, string token)
    {
        if (segments.Count > 0 && segments[^1].Kind == kind)
        {
            var last = segments[^1];
            segments[^1] = (kind, JoinFrozen(last.Text, token));
            return;
        }

        segments.Add((kind, token));
    }

    private static string JoinFrozen(string current, string next)
        => NeedsSpaceFrozen(current, next) ? current + " " + next : current + next;

    private static bool NeedsSpaceFrozen(string current, string next)
    {
        if (current.Length == 0 || next.Length == 0)
        {
            return false;
        }

        var previous = current[^1];
        var first = next[0];
        return (char.IsLetterOrDigit(previous) || previous is ')' or ']' or '}' or '.')
            && (char.IsLetterOrDigit(first) || first is '(' or '[' or '{');
    }
}
