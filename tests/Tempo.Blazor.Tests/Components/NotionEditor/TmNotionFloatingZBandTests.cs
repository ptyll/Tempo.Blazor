using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// N191 — floating Notion surfaces ride the <c>--tm-z-*</c> band scale, never a bare number:
/// hardcoded <c>z-index: 9997/9998/9999</c> in <c>TmNotionMentionMenu</c>,
/// <c>TmNotionInlineToolbar</c> and <c>TmNotionBlockTypeSwitcher</c> sat them in an unmanaged
/// level above the modal band (the same class of stacking defect as the 1060 notification
/// panel in N189). The follow-up sweep found nine more bare-band surfaces in <c>UI/</c>
/// (pickers at 9996–9998, comment panels at 1020/1030, a mention dropdown at 1100, page
/// history at 9000–9020, page search at 10000–10001, an AI menu escaped via
/// <c>calc(var(--tm-z-tooltip) + 9000)</c>) — all now tokenized (popover band for anchored
/// surfaces, modal for dialogs, overlay for full-screen take-overs, toast for the history
/// toast).
/// <para>
/// The guard is a SWEEP over every <c>UI/*.razor.css</c> like <see cref="LandmarkOwnershipTests"/>
/// — a fixed inventory let a fourth floating surface escape once, so the whole folder is
/// scanned for (a) a bare numeric z-index at band level (3+ digits — values 0–99 are in-flow
/// layering inside a stacking context, not a band claim) and (b) band-escape arithmetic
/// (<c>+ 9000</c>-style offsets that defeat the var they ride on). Asserted against the
/// stylesheet, not the DOM — bUnit renders no cascade.
/// </para>
/// </summary>
public sealed class TmNotionFloatingZBandTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A bare 3+ digit z-index is a band-level claim outside the token scale.</summary>
    private static readonly Regex BareBandLevel =
        new(@"z-index\s*:\s*\d{3,}", RegexOptions.Compiled, RegexTimeout);

    /// <summary>An offset of 100+ applied to a token defeats the band the var stands for.</summary>
    private static readonly Regex BandEscapeArithmetic =
        new(@"z-index\s*:[^;]*\+\s*\d{3,}", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex AnyLevel =
        new(@"z-index\s*:\s*([^;]+);", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex CssComment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    /// <summary>
    /// Every <c>Components/NotionEditor/**/*.razor.css</c> must declare z-index through tokens —
    /// a floating surface with a bare band-level number, or an offset that escapes its band, is
    /// a red build. CF10n: the sweep used to stop at <c>UI/</c>, which let five surfaces outside
    /// it (Share/Restrictions dialogs, DbRecord detail, page-tree context menu, TempoBlocks edit
    /// modals) keep bare band levels.
    /// </summary>
    [Fact]
    public void NoNotionSurfaceDeclaresABareBandLevel()
    {
        var notionDir = Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor.NotionEditor",
            "Components", "NotionEditor");

        var stylesheets = Directory.EnumerateFiles(
            notionDir, "*.razor.css", SearchOption.AllDirectories).ToList();
        stylesheets.Should().NotBeEmpty(
            "the z-band sweep must read a real stylesheet population — an empty UI/ "
            + "directory (moved folder, renamed pattern) would pass vacuously");

        var offenders = new List<string>();
        foreach (var file in stylesheets)
        {
            var css = CssComment.Replace(File.ReadAllText(file), string.Empty);
            if (BareBandLevel.IsMatch(css) || BandEscapeArithmetic.IsMatch(css))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        offenders.Should().BeEmpty(
            "a floating surface's z-index goes through --tm-z-* tokens (popover for anchored "
            + "surfaces, modal for dialogs, overlay for take-overs) — never a bare band-level "
            + "number or a +9000-style escape (N191 class). In-flow layering under 100 is not "
            + "a band claim and stays numeric. Findings: {0}",
            string.Join(" | ", offenders));
    }

    /// <summary>Every floating surface on the popover band declares exactly its token levels.</summary>
    [Theory]
    // anchored surface only — no click-capture backdrop
    [InlineData("UI/TmNotionInlineToolbar", 1)]
    // backdrop + panel pairs
    [InlineData("UI/TmNotionSlashMenu", 2)]
    [InlineData("UI/TmNotionBlockTypeSwitcher", 2)]
    [InlineData("UI/TmNotionColorPicker", 2)]
    [InlineData("UI/TmNotionEmojiPicker", 2)]
    [InlineData("UI/TmNotionBlockCommentPanel", 2)]
    [InlineData("UI/TmNotionTextCommentPanel", 2)]
    [InlineData("UI/TmNotionTokenDropdown", 2)]
    public void FloatingNotionSurfaces_SitOnThePopoverBand(string scopedCss, int zDeclarations)
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor.NotionEditor",
            "Components", "NotionEditor", $"{scopedCss}.razor.css"));

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

    /// <summary>
    /// CF18-7: surfaces migrated onto <c>TmOverlayPanel</c> declare NO z-index of their own —
    /// the popover top layer + overlay.js own stacking, and a stray band claim on the panel
    /// root would silently compete with the top layer.
    /// </summary>
    [Theory]
    [InlineData("UI/TmNotionAiMenu")]
    [InlineData("UI/TmNotionMentionMenu")]
    [InlineData("UI/TmCommentMentionInput")]
    public void OverlayHostedSurfaces_DeclareNoOwnZIndex(string scopedCss)
    {
        var css = CssComment.Replace(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor.NotionEditor",
            "Components", "NotionEditor", $"{scopedCss}.razor.css")), string.Empty);

        AnyLevel.Matches(css).Should().BeEmpty(
            $"{scopedCss}: its panel is a TmOverlayPanel — stacking is top-layer+overlay.js "
            + "territory; any own z-index here is a regression");
    }

    /// <summary>Full-screen take-overs ride the overlay band; their dialogs stack above it.</summary>
    [Theory]
    [InlineData("UI/TmNotionPageSearch", "--tm-z-overlay", 2)]
    [InlineData("UI/TmNotionPageHistory", "--tm-z-overlay", 4)]
    public void FullScreenTakeOvers_SitOnTheOverlayBand(string scopedCss, string band, int overlayDeclarations)
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor.NotionEditor",
            "Components", "NotionEditor", $"{scopedCss}.razor.css"));

        var declarations = AnyLevel.Matches(css);
        var onBand = declarations.Count(d => d.Groups[1].Value.Contains(band, StringComparison.Ordinal));
        onBand.Should().Be(
            overlayDeclarations,
            $"{scopedCss}: backdrop+panel ride {band}; PageHistory's confirm dialog stacks two "
            + "same-band offsets above its panel and its toast rides --tm-z-toast");
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
