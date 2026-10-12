using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Navigation;

/// <summary>
/// Guards the HeaderLeading/HeaderTrailing slots of <see cref="TmTabs"/>.
/// <para>
/// The whole safety argument for adding the slots is "with no slot the markup does not change at
/// all", so it is MEASURED here rather than argued: <see cref="NoSlotMarkupIsByteIdenticalToTheFrozenPreSlotBaseline"/>
/// compares a sha256 of the rendered markup against a hash frozen BEFORE the slots existed.
/// </para>
/// </summary>
public class TmTabsHeaderSlotsTests : LocalizationTestBase
{
    /// <summary>
    /// sha256 of <see cref="RenderNoSlots"/>'s markup, measured on the pre-slot TmTabs.razor
    /// (Tempo main @ ab4a6851, before HeaderLeading/HeaderTrailing were added). 1269 bytes.
    /// <para>
    /// DO NOT recompute this from the current implementation — that would make the test vacuous.
    /// It is the reference that proves existing consumers see an unchanged DOM, and it is the
    /// reason the wrapper row must stay CONDITIONAL: emitting the row unconditionally moves the
    /// strip one level down and this hash changes.
    /// </para>
    /// </summary>
    private const string PreSlotMarkupSha256 =
        "9ed0c8d568723921558b342e9f54c02c47648ab880369de2b7e01a35b5c572cf";

    /// <summary>
    /// The ONE thing normalised away before hashing: the element-reference capture marker the
    /// renderer stamps on each tab button since TmTabs began moving DOM focus with the roving
    /// tabindex (<c>ElementReference.FocusAsync</c> needs a handle on the button).
    /// <para>
    /// It is normalised rather than folded into a new constant because its value is a fresh GUID on
    /// every render, so no hash over markup containing it can be stable — recomputing
    /// <see cref="PreSlotMarkupSha256"/> would produce a test that fails on its next run. Removing
    /// it is safe for what this hash is FOR: it is a renderer bookkeeping marker, not part of the
    /// component's contract with consumers — it carries no class, no role, no id, and matches no
    /// selector any consumer could write, because its value is unknowable before the render.
    /// </para>
    /// <para>
    /// Measured delta when the markers are left in: 1458 bytes vs the frozen 1269, i.e. exactly
    /// 3 x 63 characters for the three tab buttons and NOTHING else. That is the whole reason the
    /// removal count is asserted below: a normaliser that quietly swallowed a second change would
    /// hand back a green hash for markup that really had moved. CRLF→LF is the only other
    /// normalisation (see <see cref="NormalizeForBaselineHash"/>).
    /// </para>
    /// </summary>
    private static readonly Regex ElementReferenceMarker =
        new(" blazor:elementReference=\"[0-9a-f-]{36}\"", RegexOptions.Compiled);

    // ── The byte-identity guarantee ────────────────────────

    [Fact]
    public void NoSlotMarkupIsByteIdenticalToTheFrozenPreSlotBaseline()
    {
        var rendered = RenderNoSlots().Markup;

        ElementReferenceMarker.Matches(rendered).Count.Should().Be(
            3,
            "exactly one element-reference marker per tab button may be normalised away — one per "
            + "@ref in the strip. A different count means the marker moved somewhere else in the "
            + "markup and the normaliser is no longer removing only what it documents; rendered:\n{0}",
            rendered);

        var markup = NormalizeForBaselineHash(rendered);
        var bytes = Encoding.UTF8.GetBytes(markup);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        sha.Should().Be(
            PreSlotMarkupSha256,
            "TmTabs without HeaderLeading/HeaderTrailing must render the pre-slot markup byte for byte "
            + "(measured {0} bytes, expected 1269); rendered markup was:\n{1}",
            bytes.Length,
            markup);
    }

    /// <summary>
    /// Line endings are a property of the CHECKOUT, not of the DOM contract the frozen hash locks:
    /// the Razor compiler copies the source file's literal line endings into the render tree, so a
    /// CRLF working copy (<c>core.autocrlf=true</c>) renders <c>\r\n</c> where the LF tree the
    /// baseline was measured on renders <c>\n</c> — two bytes on this fixture, but enough to break
    /// the byte identity above for a checkout that is otherwise perfect. The hash over the LF form
    /// and over its CRLF twin must therefore be the same, and the LF form must be the frozen
    /// baseline itself (the denominator: two wrong hashes would otherwise compare equal).
    /// </summary>
    [Fact]
    public void TheBaselineHash_DoesNotDependOnTheCheckoutLineEndings()
    {
        var rendered = RenderNoSlots().Markup;

        // Normalise FIRST, then re-synthesise the CRLF twin from the LF form: the raw render's
        // endings follow the checkout, so converting the raw text could double every CR on a CRLF
        // machine — and \r\r\n is a genuinely different string no CR-removing normaliser maps back.
        var lf = NormalizeForBaselineHash(rendered);
        var crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);

        PreSlotHash(lf).Should().Be(
            PreSlotMarkupSha256,
            "the denominator: the normalised form must be the frozen pre-slot markup itself, or the "
            + "CRLF comparison below could pass over two equally wrong hashes");

        PreSlotHash(crlf).Should().Be(
            PreSlotHash(lf),
            "the same markup with CRLF line endings must hash exactly like its LF twin — a Windows "
            + "checkout under core.autocrlf=true renders \r\n where an LF checkout renders \n, and "
            + "the frozen baseline measures the DOM contract, not the checkout");
    }

    [Fact]
    public void WithoutSlotsNoHeaderRowWrapperIsEmitted()
    {
        var cut = RenderNoSlots();

        cut.FindAll(".tm-tabs__header-row").Should().BeEmpty(
            "the wrapper row is what would push .tm-tabs__header out of being a DIRECT child of .tm-tabs, "
            + "breaking every consumer selector shaped `.tm-tabs > .tm-tabs__header`");
    }

    [Fact]
    public void WithoutSlotsTheHeaderIsADirectChildOfTheTabsRoot()
    {
        var cut = RenderNoSlots();

        var header = cut.Find(".tm-tabs__header");
        header.ParentElement!.ClassList.Should().Contain(
            "tm-tabs",
            "consumer CSS and e2e selectors use the child combinator `.tm-tabs > .tm-tabs__header`");
    }

    // ── The wrapper appears when — and only when — a slot is used ──

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void WithASlotTheHeaderRowWrapperIsEmitted(bool leading, bool trailing)
    {
        var cut = RenderWithSlots(leading, trailing);

        cut.FindAll(".tm-tabs__header-row").Should().ContainSingle(
            "supplying HeaderLeading={0}/HeaderTrailing={1} must wrap the strip in exactly one row, "
            + "otherwise the slot content has nothing to sit next to",
            leading,
            trailing);

        cut.Find(".tm-tabs__header").ParentElement!.ClassList.Should().Contain("tm-tabs__header-row");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OnlyTheSuppliedSlotIsEmitted(bool leading, bool trailing)
    {
        var cut = RenderWithSlots(leading, trailing);

        cut.FindAll(".tm-tabs__header-leading").Should().HaveCount(leading ? 1 : 0);
        cut.FindAll(".tm-tabs__header-trailing").Should().HaveCount(trailing ? 1 : 0);
    }

    // ── a11y: slots are SIBLINGS of role=tablist, never descendants ──

    [Theory]
    [InlineData(".tm-tabs__header-leading")]
    [InlineData(".tm-tabs__header-trailing")]
    public void SlotContentIsNotInsideTheTablist(string slotSelector)
    {
        var cut = RenderWithSlots(leading: true, trailing: true);

        var slot = cut.Find(slotSelector);

        slot.Closest("[role='tablist']").Should().BeNull(
            "{0} must be a SIBLING of the tab strip, not a descendant: TmTabs runs a roving tabindex "
            + "(Arrow/Home/End, tabindex 0/-1, aria-selected and the DOM focus) and an element inside "
            + "the tablist that can never be selected both misreports the strip's contract to "
            + "assistive technology and becomes arrow-key reachable",
            slotSelector);

        slot.ParentElement!.ClassList.Should().Contain(
            "tm-tabs__header-row",
            "the slot's parent must be the wrapper row — that is what makes it a sibling of the strip");
    }

    // ── Document ORDER: leading BEFORE the strip, trailing AFTER it ──
    //
    // Sibling-hood is NOT the whole contract. The XML doc on HeaderLeading promises the slot is
    // "rendered immediately BEFORE the tab strip" and HeaderTrailing "immediately AFTER" it, and
    // consumers build on that: reading order, DOM order and tab order are the same order, so a
    // back-affordance in HeaderLeading must be reached before the first tab. Swapping the two
    // `@if` branches in TmTabs' HeaderRow leaves EVERY assertion above green — both slots stay
    // siblings of role=tablist and stay children of the wrapper row — so order needs its own
    // tooth. Each side is asserted separately, so a red run names which side moved.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLeadingSlotIsRenderedImmediatelyBeforeTheTablist(bool trailing)
    {
        var cut = RenderWithSlots(leading: true, trailing: trailing);

        var tablist = cut.Find("[role='tablist']");
        var previous = tablist.PreviousElementSibling;

        previous.Should().NotBeNull(
            "HeaderLeading is documented as rendered immediately BEFORE the tab strip, so the strip "
            + "must not be the FIRST element of .tm-tabs__header-row");

        previous!.ClassList.Should().Contain(
            "tm-tabs__header-leading",
            "the element immediately PRECEDING role=tablist must be the LEADING slot, but it was "
            + "'{0}' — DOM order is tab order, and a leading affordance rendered after the strip "
            + "is reached after every tab instead of before the first one",
            previous.ClassName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheTrailingSlotIsRenderedImmediatelyAfterTheTablist(bool leading)
    {
        var cut = RenderWithSlots(leading: leading, trailing: true);

        var tablist = cut.Find("[role='tablist']");
        var next = tablist.NextElementSibling;

        next.Should().NotBeNull(
            "HeaderTrailing is documented as rendered immediately AFTER the tab strip, so the strip "
            + "must not be the LAST element of .tm-tabs__header-row");

        next!.ClassList.Should().Contain(
            "tm-tabs__header-trailing",
            "the element immediately FOLLOWING role=tablist must be the TRAILING slot, but it was "
            + "'{0}' — a trailing action rendered before the strip is reached before the first tab",
            next.ClassName);
    }

    [Fact]
    public void TheHeaderRowRendersLeadingThenTheStripThenTrailing()
    {
        var cut = RenderWithSlots(leading: true, trailing: true);

        var row = cut.Find(".tm-tabs__header-row");
        var children = row.Children.ToArray();
        var rendered = string.Join(", ", children.Select(c => c.ClassName));

        // Class PRESENCE, not equality of the whole class attribute. What this test owns is ORDER;
        // a later modifier on any of the three (`tm-tabs__header tm-tabs__header--sticky`) would
        // break a string comparison without a single element having moved. ClassList membership is
        // token-based, so `tm-tabs__header-leading` never satisfies a check for `tm-tabs__header`.
        children.Should().HaveCount(
            3,
            "the wrapper row holds exactly the leading slot, the strip and the trailing slot; "
            + "rendered [{0}]",
            rendered);

        children[0].ClassList.Should().Contain(
            "tm-tabs__header-leading",
            "the wrapper row's children must be leading → strip → trailing in document order; "
            + "rendered [{0}]",
            rendered);

        children[1].ClassList.Should().Contain(
            "tm-tabs__header",
            "the strip must sit BETWEEN the two slots; rendered [{0}]",
            rendered);

        children[2].ClassList.Should().Contain(
            "tm-tabs__header-trailing",
            "the wrapper row's children must be leading → strip → trailing in document order; "
            + "rendered [{0}]",
            rendered);
    }

    [Fact]
    public void SlotContentDoesNotBecomeATab()
    {
        var cut = RenderWithSlots(leading: true, trailing: true);

        cut.FindAll("[role='tab']").Should().HaveCount(
            3,
            "the slots must not add anything the roving focus would walk over");
    }

    [Fact]
    public void SlotContentIsRendered()
    {
        var cut = RenderWithSlots(leading: true, trailing: true);

        cut.Find(".tm-tabs__header-leading").TextContent.Should().Contain("LEAD");
        cut.Find(".tm-tabs__header-trailing").TextContent.Should().Contain("TRAIL");
    }

    // ── Helpers ────────────────────────────────────────────

    /// <summary>
    /// The normalisations EVERY baseline comparison applies before hashing, in documented order:
    /// the element-reference capture marker (<see cref="ElementReferenceMarker"/>, asserted to be
    /// exactly three before it is removed) and CRLF→LF. The second normalisation exists because the
    /// Razor compiler copies the source file's literal line endings into the markup — a CRLF
    /// working copy renders <c>\r\n</c> where the LF tree the frozen baseline was measured on
    /// renders <c>\n</c>. Line endings are a property of the checkout, not of the DOM contract this
    /// hash freezes; 2 bytes on this fixture (three tab-strip rows carrying one <c>\r</c> each).
    /// </summary>
    private static string NormalizeForBaselineHash(string markup) =>
        ElementReferenceMarker.Replace(markup, string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    /// <summary>The sha256 of <paramref name="markup"/> after <see cref="NormalizeForBaselineHash"/>.</summary>
    private static string PreSlotHash(string markup)
    {
        var normalized = NormalizeForBaselineHash(markup);
        var bytes = Encoding.UTF8.GetBytes(normalized);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>
    /// Renders the exact fixture the frozen <see cref="PreSlotMarkupSha256"/> was measured from —
    /// icon, badge and disabled tab included, so the hash covers every branch of the strip.
    /// Changing this fixture invalidates the reference.
    /// </summary>
    private IRenderedComponent<TmTabs> RenderNoSlots()
        => Render<TmTabs>(p => AddPanels(p));

    private IRenderedComponent<TmTabs> RenderWithSlots(bool leading, bool trailing)
        => Render<TmTabs>(p =>
        {
            AddPanels(p);
            if (leading)
                p.Add(x => x.HeaderLeading, (RenderFragment)(b => b.AddMarkupContent(0, "<span>LEAD</span>")));
            if (trailing)
                p.Add(x => x.HeaderTrailing, (RenderFragment)(b => b.AddMarkupContent(0, "<span>TRAIL</span>")));
        });

    private static ComponentParameterCollectionBuilder<TmTabs> AddPanels(
        ComponentParameterCollectionBuilder<TmTabs> p)
        => p
            .Add(x => x.ActiveTabId, "tab1")
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .Add(x => x.Icon, "info")
                .AddChildContent("Content One"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab2")
                .Add(x => x.Title, "Second")
                .Add(x => x.Badge, "3")
                .AddChildContent("Content Two"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab3")
                .Add(x => x.Title, "Third")
                .Add(x => x.Disabled, true)
                .AddChildContent("Content Three"));
}
