using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// N191 — the floating Notion surfaces the slash menu's N5 fix already moved onto the
/// <c>--tm-z-popover</c> band: hardcoded <c>z-index: 9997/9998/9999</c> in
/// <c>TmNotionMentionMenu</c>, <c>TmNotionInlineToolbar</c> and
/// <c>TmNotionBlockTypeSwitcher</c> sat them in an unmanaged level above the modal band
/// (<c>--tm-z-modal</c>, 1040 is the top of the token scale — 9998 floats ABOVE it, which is
/// the same class of stacking defect as the 1060 notification panel in N189).
/// <para>
/// Asserted against the stylesheet, not the DOM — bUnit renders no cascade. The contract
/// mirrors <c>TmNotionSlashMenu.razor.css</c>: the anchored card declares
/// <c>z-index: var(--tm-z-popover, 1030)</c> and its click-capture backdrop sits one step
/// under it (<c>calc(var(--tm-z-popover, 1030) - 1)</c>); no bare numeric z-index remains.
/// </para>
/// </summary>
public sealed class TmNotionFloatingZBandTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex HardcodedLevel =
        new(@"z-index\s*:\s*\d", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex AnyLevel =
        new(@"z-index\s*:\s*([^;]+);", RegexOptions.Compiled, RegexTimeout);

    /// <summary>The anchored surface declares the popover band; a backdrop one step under it.</summary>
    [Theory]
    // menu card only — the inline toolbar has no click-capture backdrop
    [InlineData("UI/TmNotionInlineToolbar", 1)]
    // backdrop + panel pairs
    [InlineData("UI/TmNotionMentionMenu", 2)]
    [InlineData("UI/TmNotionBlockTypeSwitcher", 2)]
    public void FloatingNotionSurfaces_SitOnThePopoverBand(string scopedCss, int zDeclarations)
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor.NotionEditor",
            "Components", "NotionEditor", $"{scopedCss}.razor.css"));

        css.Should().NotMatchRegex(
            HardcodedLevel,
            $"{scopedCss}: hardcoded z-index levels float outside the --tm-z-* scale — " +
            "the anchored surface belongs on --tm-z-popover like TmNotionSlashMenu (N5/N191)");

        var declarations = AnyLevel.Matches(css);
        declarations.Should().HaveCount(
            zDeclarations,
            $"{scopedCss}: expected exactly the backdrop+surface z-index declarations");

        foreach (Match declaration in declarations)
        {
            declaration.Groups[1].Value.Should().Contain(
                "--tm-z-popover",
                $"{scopedCss}: every z-index the surface declares goes through the popover band");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find TempoBlazor.slnx.");
    }
}
