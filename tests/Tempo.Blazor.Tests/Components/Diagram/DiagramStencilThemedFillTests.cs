using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;
using Tempo.Blazor.Components.Diagram.Stencils;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Fáze 8c — built-in Archimate3, BuiltIn and Extended node stencils paint Fill, Stroke and
/// section <c>TextStyle.Color</c> through catalogued <c>var(--tm-*)</c> tokens, never a literal
/// hex. An empty allowlist reason fails the test. Today RED: those packs still ship hex paints.
/// </summary>
public sealed class DiagramStencilThemedFillTests
{
    private static readonly Regex HexPaint = new(@"#[0-9a-fA-F]{3,8}\b", RegexOptions.Compiled);

    private static readonly Regex TokenPaint = new(
        @"^var\(\s*(--(?:tm|stencil)-[\w-]+)\s*\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Stencil id → reason a literal hex is still allowed. A missing or whitespace reason fails
    /// the test even if the id is listed — an undocumented exception is not an exception.
    /// </summary>
    private static readonly Dictionary<string, string> HexAllowlist = new(StringComparer.Ordinal)
    {
        // Empty on purpose: every Archimate3 / BuiltIn / Extended Fill, Stroke and TextStyle.Color
        // must be a token. IconSvg toolbox glyphs and table-cell BackgroundColor are out of this scan.
    };

    public static IEnumerable<object[]> NodeStencils()
    {
        foreach (var stencil in AllNodeStencils())
        {
            yield return [stencil.Id, stencil];
        }
    }

    internal static IEnumerable<DiagramStencil> AllNodeStencils()
    {
        IDiagramStencilProvider[] providers =
        [
            new Archimate3DiagramStencilProvider(),
            new BuiltInDiagramStencilProvider(),
            new ExtendedDiagramStencilProvider()
        ];

        return providers
            .SelectMany(provider => provider.GetStencilSets())
            .SelectMany(set => set.Stencils)
            .Where(stencil => stencil.Kind == DiagramStencilKind.Node);
    }

    [Theory]
    [MemberData(nameof(NodeStencils))]
    public void Node_layout_fill_stroke_and_section_ink_are_tokens_not_hex(string stencilId, DiagramStencil stencil)
    {
        stencil.Id.Should().Be(stencilId);
        AssertPaint(stencilId, "Layout.Fill", stencil.Layout.Fill);
        AssertPaint(stencilId, "Layout.Stroke", stencil.Layout.Stroke);

        foreach (var section in stencil.Layout.Sections)
        {
            AssertPaint(stencilId, $"Section:{section.DataKey}.TextStyle.Color", section.TextStyle?.Color);
        }
    }

    [Fact]
    public void Allowlist_entries_have_a_non_empty_reason()
    {
        foreach (var (id, reason) in HexAllowlist)
        {
            reason.Should().NotBeNullOrWhiteSpace(
                "allowlist id {0} has an empty reason — that is a failing exception, not a documented one", id);
        }
    }

    [Fact]
    public void Sticky_note_icon_svg_paints_the_yellow_fill_token()
    {
        var stencil = AllNodeStencils().Single(s => s.Id == "general.sticky-note");
        stencil.IconSvg.Should().Contain("var(--tm-diagram-stencil-yellow-fill)",
            "the toolbox glyph used to ship fill='#fef08a' which does not flip in dark");
        HexPaint.IsMatch(stencil.IconSvg ?? string.Empty).Should().BeFalse(
            "the sticky-note IconSvg must not carry a hardcoded hex: {0}", stencil.IconSvg);
    }

    [Fact]
    public void Table_basic_header_cells_paint_the_table_header_fill_token()
    {
        var stencil = AllNodeStencils().Single(s => s.Id == "table.basic");
        stencil.DefaultData.Should().ContainKey("cells");
        var cells = stencil.DefaultData["cells"] as List<DiagramTableCellData>;
        cells.Should().NotBeNull();
        cells!.Where(c => c.Row == 0).Should().NotBeEmpty()
            .And.OnlyContain(c =>
                c.Style != null
                && c.Style.BackgroundColor == "var(--tm-diagram-stencil-table-header-fill)");
    }

    [Fact]
    public void Scanned_providers_are_not_empty()
    {
        AllNodeStencils().Should().HaveCountGreaterThan(50,
            "the scan must actually walk Archimate3, BuiltIn and Extended — an empty walk is a false green");
    }

    private static void AssertPaint(string stencilId, string slot, string? paint)
    {
        if (string.IsNullOrWhiteSpace(paint))
        {
            return;
        }

        if (!HexPaint.IsMatch(paint))
        {
            if (paint.Contains("var(", StringComparison.Ordinal))
            {
                TokenPaint.IsMatch(paint.Trim()).Should().BeTrue(
                    "{0} {1} must be a bare var(--tm-…) / var(--stencil-…) token (no hex fallback): {2}", stencilId, slot, paint);
            }

            return;
        }

        HexAllowlist.TryGetValue(stencilId, out var reason).Should().BeTrue(
            "{0} {1} still carries a literal hex ({2}). Either paint through a token or add the id to the allowlist WITH a reason.",
            stencilId, slot, paint);
        reason.Should().NotBeNullOrWhiteSpace(
            "{0} is on the allowlist but the reason is empty", stencilId);
    }
}
