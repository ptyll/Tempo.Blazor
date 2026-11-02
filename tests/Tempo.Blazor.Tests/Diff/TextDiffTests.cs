using FluentAssertions;
using Tempo.Blazor.Diff;

namespace Tempo.Blazor.Tests.Diff;

/// <summary>
/// Contract tests for the shared diff engine in <c>Tempo.Blazor.Abstractions</c>
/// (<see cref="TextDiff"/>). The engine is the single implementation behind the word-level
/// <c>DocumentTextDiffHelper</c>, the line-level <c>TmTextLineDiff</c> and the upcoming
/// <c>TmInlineDiff</c> component, so the guarantees pinned here are load-bearing:
/// <list type="bullet">
/// <item>hunks tile both inputs (concatenating <see cref="TextDiffHunk.OldText"/> reproduces the
/// old text, concatenating <see cref="TextDiffHunk.NewText"/> the new text), which is what makes
/// a partial accept result in a valid text;</item>
/// <item>hunk ids are deterministic, so an accepted-change set survives a re-render;</item>
/// <item>an input above the configured limits degrades to a coarser granularity instead of
/// throwing or freezing the UI, and the degradation is reported.</item>
/// </list>
/// </summary>
public sealed class TextDiffTests
{
    // ---------------------------------------------------------------------------------------
    // Word granularity
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Words_Insertion_ProducesOneInsertHunk()
    {
        var diff = TextDiff.Words("Smlouva je platná", "Smlouva je dnes platná");

        diff.Granularity.Should().Be(TextDiffGranularity.Words);
        diff.Fallback.Should().Be(TextDiffFallbackKind.None);
        diff.Changes.Should().ContainSingle();
        var change = diff.Changes[0];
        change.Kind.Should().Be(TextDiffHunkKind.Insert);
        change.OldText.Should().BeEmpty();
        change.NewText.Should().Be("dnes ");
    }

    [Fact]
    public void Words_Deletion_ProducesOneDeleteHunk()
    {
        var diff = TextDiff.Words("Smlouva je stále platná", "Smlouva je platná");

        diff.Changes.Should().ContainSingle();
        var change = diff.Changes[0];
        change.Kind.Should().Be(TextDiffHunkKind.Delete);
        change.OldText.Should().Be("stále ");
        change.NewText.Should().BeEmpty();
    }

    [Fact]
    public void Words_Replacement_ProducesOneReplaceHunk()
    {
        var diff = TextDiff.Words("Klient zaplatí 1000 Kč", "Klient zaplatí 1200 Kč");

        diff.Changes.Should().ContainSingle();
        var change = diff.Changes[0];
        change.Kind.Should().Be(TextDiffHunkKind.Replace);
        change.OldText.Should().Be("1000 ");
        change.NewText.Should().Be("1200 ");
        change.OldUnitCount.Should().Be(1);
        change.NewUnitCount.Should().Be(1);
    }

    [Fact]
    public void Words_Punctuation_KeepsItsOwnTokenAndIsNotAChange()
    {
        var diff = TextDiff.Words("Hello, world!", "Hello, brave world!");

        diff.Changes.Should().ContainSingle(change =>
            change.Kind == TextDiffHunkKind.Insert && change.NewText == "brave ");
        diff.Hunks.Where(hunk => hunk.Kind == TextDiffHunkKind.Equal)
            .Select(hunk => hunk.OldText)
            .Should().Equal("Hello, ", "world!");
    }

    [Fact]
    public void Words_WhitespaceOnlyDifference_IsNotAChange()
    {
        var diff = TextDiff.Words("a  b   c", "a b c");

        diff.HasChanges.Should().BeFalse();
        diff.Hunks.Should().ContainSingle();
        diff.Apply([]).Should().Be("a  b   c");
        diff.Apply(diff.HunkIds).Should().Be("a b c");
    }

    [Fact]
    public void Words_Diacritics_AreComparedOrdinal()
    {
        var diff = TextDiff.Words("Příliš žluťoučký kůň úpěl", "Příliš žluťoučký kůň hlasitě úpěl");

        diff.Changes.Should().ContainSingle(change =>
            change.Kind == TextDiffHunkKind.Insert && change.NewText == "hlasitě ");
    }

    [Fact]
    public void Words_ComposedAndDecomposedDiacritics_AreNotEqual()
    {
        // "Příliš" written with a combining acute (U+0301) is a different token than the precomposed
        // one — the engine compares ordinally, exactly like the legacy helper, so the difference is
        // reported instead of being silently normalized.
        var composed = "Příliš";
        var decomposed = "Pri\u0301lis\u030c";

        var diff = TextDiff.Words(composed, decomposed);

        diff.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void Words_IdenticalInputs_ReturnASingleEqualHunk()
    {
        var diff = TextDiff.Words("Smlouva je platná", "Smlouva je platná");

        diff.Hunks.Should().ContainSingle();
        diff.Hunks[0].Kind.Should().Be(TextDiffHunkKind.Equal);
        diff.HasChanges.Should().BeFalse();
        diff.Apply([]).Should().Be("Smlouva je platná");
        diff.Apply(diff.HunkIds).Should().Be("Smlouva je platná");
    }

    [Fact]
    public void EmptyInputs_AreHandledWithoutThrowing()
    {
        var bothEmpty = TextDiff.Words(null, null);
        bothEmpty.Hunks.Should().ContainSingle(hunk =>
            hunk.Kind == TextDiffHunkKind.Equal && hunk.OldText == string.Empty && hunk.NewText == string.Empty);
        bothEmpty.HasChanges.Should().BeFalse();
        bothEmpty.Apply([]).Should().BeEmpty();

        var fromNothing = TextDiff.Words(string.Empty, "one two");
        fromNothing.Hunks.Should().ContainSingle(hunk =>
            hunk.Kind == TextDiffHunkKind.Insert && hunk.OldText == string.Empty && hunk.NewText == "one two");
        fromNothing.Apply([]).Should().BeEmpty();
        fromNothing.Apply(fromNothing.HunkIds).Should().Be("one two");

        var toNothing = TextDiff.Words("one two", string.Empty);
        toNothing.Hunks.Should().ContainSingle(hunk =>
            hunk.Kind == TextDiffHunkKind.Delete && hunk.OldText == "one two");
        toNothing.Apply([]).Should().Be("one two");
        toNothing.Apply(toNothing.HunkIds).Should().BeEmpty();
    }

    [Fact]
    public void Words_WhitespaceOnlyOldText_ReplacesTheWholeText()
    {
        // A whitespace-only input has no tokens, so a token-level delete cannot express it. The
        // engine therefore keeps the characters in the hunk instead of dropping them silently —
        // "reject everything" must still return the original text.
        var diff = TextDiff.Words("   ", "a");

        diff.Apply([]).Should().Be("   ");
        diff.Apply(diff.HunkIds).Should().Be("a");
    }

    [Fact]
    public void HunkIds_AreStableAndUnique()
    {
        var first = TextDiff.Words("one two three four", "one TWO three FOUR");
        var second = TextDiff.Words("one two three four", "one TWO three FOUR");

        second.Hunks.Select(hunk => hunk.Id).Should().Equal(first.Hunks.Select(hunk => hunk.Id));
        first.Hunks.Select(hunk => hunk.Id).Should().OnlyHaveUniqueItems();
        first.HunkIds.Should().Equal(first.Hunks.Select(hunk => hunk.Id));
    }

    [Fact]
    public void Hunks_TileBothInputs()
    {
        var cases = new (string Old, string New)[]
        {
            ("", ""),
            ("", "abc"),
            ("abc", ""),
            ("Smlouva je platná", "Smlouva je dnes platná"),
            ("Klient zaplatí 1000 Kč", "Klient zaplatí 1200 Kč"),
            ("a b c d e", "a c e"),
            ("one two three", "one 2 three"),
            ("  leading", "leading"),
            ("trailing  ", "trailing"),
            ("a\r\nb", "a\nb"),
            ("Příliš žluťoučký kůň", "Příliš bílý kůň"),
            ("x", "y")
        };

        foreach (var (oldText, newText) in cases)
        {
            var diff = TextDiff.Words(oldText, newText);

            string.Concat(diff.Hunks.Select(hunk => hunk.OldText)).Should().Be(oldText,
                "the old ranges of '{0}' must tile the old input", oldText);
            string.Concat(diff.Hunks.Select(hunk => hunk.NewText)).Should().Be(newText,
                "the new ranges of '{0}' must tile the new input", newText);

            var oldCursor = 0;
            var newCursor = 0;
            foreach (var hunk in diff.Hunks)
            {
                hunk.OldRange.Start.Should().Be(oldCursor);
                hunk.NewRange.Start.Should().Be(newCursor);
                hunk.OldRange.Length.Should().Be(hunk.OldText.Length);
                hunk.NewRange.Length.Should().Be(hunk.NewText.Length);
                oldCursor = hunk.OldRange.End;
                newCursor = hunk.NewRange.End;
            }

            oldCursor.Should().Be(oldText.Length);
            newCursor.Should().Be(newText.Length);
        }
    }

    [Fact]
    public void Apply_WithNoAcceptedHunks_ReturnsTheOriginalText()
    {
        var diff = TextDiff.Words("one two three four", "one TWO three FOUR");

        diff.Apply(Array.Empty<string>()).Should().Be("one two three four");
        diff.Apply(null).Should().Be("one two three four");
    }

    [Fact]
    public void Apply_WithEveryHunkAccepted_ReturnsTheNewText()
    {
        var diff = TextDiff.Words("one two three four", "one TWO three FOUR");

        diff.Apply(diff.HunkIds).Should().Be("one TWO three FOUR");
    }

    [Fact]
    public void Apply_WithASubsetOfChangeHunks_KeepsTheRejectedChangesInTheOldWording()
    {
        var diff = TextDiff.Words("one two three four", "one TWO three FOUR");
        diff.Changes.Should().HaveCount(2);

        var acceptFirstOnly = new[] { diff.Changes[0].Id };

        diff.Apply(acceptFirstOnly).Should().Be("one TWO three four");
    }

    [Fact]
    public void Apply_WithAMixOfAcceptedAndRejectedChanges_ProducesAValidText()
    {
        var diff = TextDiff.Words("a b c d e", "a X c d Y");
        diff.Changes.Select(change => change.Kind).Should().AllBeEquivalentTo(TextDiffHunkKind.Replace);

        var acceptSecondOnly = new[] { diff.Changes[1].Id };

        diff.Apply(acceptSecondOnly).Should().Be("a b c d Y");
    }

    [Fact]
    public void Apply_IgnoresUnknownHunkIds()
    {
        var diff = TextDiff.Words("a b", "a c");

        diff.Apply(["does-not-exist"]).Should().Be("a b");
    }

    // ---------------------------------------------------------------------------------------
    // Line granularity
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Lines_DetectsAddedRemovedAndUnchangedLines()
    {
        var diff = TextDiff.Lines("a\nb\nc", "a\nB\nc");

        diff.Granularity.Should().Be(TextDiffGranularity.Lines);
        diff.Changes.Should().ContainSingle(change =>
            change.Kind == TextDiffHunkKind.Replace && change.OldText == "b\n" && change.NewText == "B\n");
        diff.Changes[0].Id.Should().Be("replace-2-2");
        diff.Changes[0].OldUnitCount.Should().Be(1);
        diff.Changes[0].NewUnitCount.Should().Be(1);
    }

    [Fact]
    public void Lines_UnitCountsCountLinesIncludingTrailingEmptyOnes()
    {
        // "a\n" is two lines: "a" and the empty line after the break. The character span of a hunk
        // cannot express the empty one, so the unit count is the only reliable line count.
        var identical = TextDiff.Lines("a\n", "a\n");
        identical.Hunks.Should().ContainSingle();
        identical.Hunks[0].OldText.Should().Be("a\n");
        identical.Hunks[0].OldUnitCount.Should().Be(2);
        identical.Hunks[0].NewUnitCount.Should().Be(2);

        var diff = TextDiff.Lines("a\n", "a\nb");
        diff.Hunks[0].OldUnitCount.Should().Be(1);
        diff.Changes.Should().ContainSingle();
        diff.Changes[0].OldText.Should().BeEmpty();
        diff.Changes[0].OldUnitCount.Should().Be(1);
        diff.Changes[0].NewText.Should().Be("b");
        diff.Apply([]).Should().Be("a\n");
        diff.Apply(diff.HunkIds).Should().Be("a\nb");
    }

    [Fact]
    public void Lines_TileBothInputsIncludingLineBreaks()
    {
        var oldText = "line1\r\nline2\r\nline3";
        var newText = "line1\r\nlineX\r\nline3\r\nline4";
        var diff = TextDiff.Lines(oldText, newText);

        string.Concat(diff.Hunks.Select(hunk => hunk.OldText)).Should().Be(oldText);
        string.Concat(diff.Hunks.Select(hunk => hunk.NewText)).Should().Be(newText);
        diff.Apply([]).Should().Be(oldText);
        diff.Apply(diff.HunkIds).Should().Be(newText);
    }

    [Fact]
    public void Lines_IdenticalInputs_ReturnASingleEqualHunk()
    {
        var diff = TextDiff.Lines("x\ny", "x\ny");

        diff.Hunks.Should().ContainSingle(hunk => hunk.Kind == TextDiffHunkKind.Equal);
        diff.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Lines_EmptyInputs_AreHandledWithoutThrowing()
    {
        var bothEmpty = TextDiff.Lines(null, null);
        bothEmpty.Apply([]).Should().BeEmpty();
        bothEmpty.HasChanges.Should().BeFalse();

        var fromNothing = TextDiff.Lines(string.Empty, "one\ntwo");
        fromNothing.Apply([]).Should().BeEmpty();
        fromNothing.Apply(fromNothing.HunkIds).Should().Be("one\ntwo");
        fromNothing.Changes.Should().ContainSingle(change => change.Kind == TextDiffHunkKind.Insert);

        var toNothing = TextDiff.Lines("one\ntwo", string.Empty);
        toNothing.Changes.Should().ContainSingle(change => change.Kind == TextDiffHunkKind.Delete);
        toNothing.Changes[0].Id.Should().Be("delete-0-0");
        toNothing.Apply(toNothing.HunkIds).Should().BeEmpty();
        toNothing.Apply([]).Should().Be("one\ntwo");
    }

    // ---------------------------------------------------------------------------------------
    // Performance limits and the documented fallback
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Words_AboveMaxTokens_FallsBackToLineGranularityWithoutThrowing()
    {
        var oldText = "a b c d e f";
        var newText = "a b c d e X";

        var diff = TextDiff.Words(oldText, newText, new TextDiffOptions { MaxTokens = 3 });

        diff.Fallback.Should().Be(TextDiffFallbackKind.LineLevel);
        diff.Granularity.Should().Be(TextDiffGranularity.Lines);
        diff.IsDegraded.Should().BeTrue();
        diff.Apply([]).Should().Be(oldText);
        diff.Apply(diff.HunkIds).Should().Be(newText);
    }

    [Fact]
    public void Words_AboveMaxCells_FallsBackToLineGranularityWithoutThrowing()
    {
        var oldText = "a b c d e f";
        var newText = "f e d c b a";

        var diff = TextDiff.Words(oldText, newText, new TextDiffOptions { MaxCells = 4 });

        diff.Fallback.Should().Be(TextDiffFallbackKind.LineLevel);
        diff.Apply(diff.HunkIds).Should().Be(newText);
    }

    [Fact]
    public void Lines_AboveMaxTokens_CollapseToOneChangeHunk()
    {
        var oldText = "a\nb\nc\nd";
        var newText = "w\nx\ny\nz";

        var diff = TextDiff.Lines(oldText, newText, new TextDiffOptions { MaxTokens = 2 });

        diff.Fallback.Should().Be(TextDiffFallbackKind.SingleChange);
        diff.IsDegraded.Should().BeTrue();
        diff.Changes.Should().ContainSingle(change => change.Kind == TextDiffHunkKind.Replace);
        diff.Apply([]).Should().Be(oldText);
        diff.Apply(diff.HunkIds).Should().Be(newText);
    }

    [Fact]
    public void Lines_AboveMaxTokens_WithIdenticalInputs_StaysEqual()
    {
        var diff = TextDiff.Lines("a\nb\nc", "a\nb\nc", new TextDiffOptions { MaxTokens = 1 });

        diff.Fallback.Should().Be(TextDiffFallbackKind.SingleChange);
        diff.Hunks.Should().ContainSingle(hunk => hunk.Kind == TextDiffHunkKind.Equal);
        diff.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Words_UnderTheLimits_DoNotReportAFallback()
    {
        var diff = TextDiff.Words("a b", "a c", new TextDiffOptions { MaxTokens = 100 });

        diff.IsDegraded.Should().BeFalse();
        diff.Granularity.Should().Be(TextDiffGranularity.Words);
    }

    [Fact]
    public void DefaultOptions_AllowTwentyThousandTokens()
    {
        TextDiffOptions.Default.MaxTokens.Should().Be(20_000);
        TextDiffOptions.Default.MaxCells.Should().BePositive();

        var options = new TextDiffOptions();
        options.MaxTokens.Should().Be(TextDiffOptions.Default.MaxTokens);
        options.MaxCells.Should().Be(TextDiffOptions.Default.MaxCells);
    }
}
