using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// Plan b75365fb Fáze 4l — additive two-way expansion for the accordion:
/// <c>TmAccordionItem</c> gains <c>IsExpandedChanged</c> (<c>@bind-IsExpanded</c>) and a stable
/// <c>Key</c>, <c>TmAccordion</c> gains <c>ExpandedKeys</c>/<c>ExpandedKeysChanged</c>
/// (<c>@bind-ExpandedKeys</c>) and <c>OnItemToggled</c>. The <c>IsExpanded</c> parameter is NEVER
/// assigned inside the component (internal <c>_expanded</c> + callback); an uncontrolled host
/// (no callback) keeps today's behavior and the parameter value is re-applied only when it
/// actually changes; a controlled host owns the state (a host that does not update the value
/// rejects the toggle). Existing hosts without the new parameters get an identical DOM.
/// </summary>
public class TmAccordionBindingTests : LocalizationTestBase
{
    // ── (a) IsExpandedChanged fires once per activation ─────────────────────

    [Fact]
    public void IsExpandedChanged_FiresOncePerClickEnterAndSpace()
    {
        var host = Render<ControlledItemHost>();
        var header = host.Find(".tm-accordion-item__header");

        header.Click();
        host.Instance.Raised.Should().Equal([true], "one click raises exactly one callback");

        // Native <button> Enter sequence: keydown -> click (see TmAccordionTests for the sink trick).
        header.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        header.Click();
        host.Instance.Raised.Should().Equal([true, false], "Enter raises exactly one more callback");

        // Native <button> Space sequence: keydown -> keyup -> click.
        header.KeyDown(new KeyboardEventArgs { Key = " " });
        header.KeyUp(new KeyboardEventArgs { Key = " " });
        header.Click();
        host.Instance.Raised.Should().Equal([true, false, true], "Space raises exactly one more callback");

        host.Find(".tm-accordion-item__header").GetAttribute("aria-expanded").Should().Be("true");
    }

    // ── (b) controlled host owns the state ──────────────────────────────────

    [Fact]
    public void ControlledHost_ThatDoesNotUpdate_RejectsTheToggle()
    {
        var host = Render<ControlledItemHost>(p => p
            .Add(h => h.UpdateOnCallback, false));

        host.Find(".tm-accordion-item__header").Click();

        host.Instance.Raised.Should().Equal([true], "the click still reports the request");
        host.Find(".tm-accordion-item__header").GetAttribute("aria-expanded").Should().Be("false",
            "a controlled host that does not update the value rejects the toggle");
        host.FindAll(".tm-accordion-item--expanded").Should().BeEmpty();
    }

    [Fact]
    public void ControlledHost_ThatBinds_ReflectsTheToggle()
    {
        var host = Render<ControlledItemHost>();

        host.Find(".tm-accordion-item__header").Click();

        host.Instance.Raised.Should().Equal([true]);
        host.Find(".tm-accordion-item__header").GetAttribute("aria-expanded").Should().Be("true");
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);
    }

    [Fact]
    public void ControlledItem_SeedsExpanded_FromTheParameter()
    {
        var host = Render<ControlledItemHost>(p => p.Add(h => h.InitialExpanded, true));

        host.Find(".tm-accordion-item__header").GetAttribute("aria-expanded").Should().Be("true");
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);
    }

    // ── (c) uncontrolled: re-render does not reset, param change does ───────

    [Fact]
    public void Uncontrolled_ParentRerender_DoesNotResetUserState()
    {
        var host = Render<UncontrolledHost>();

        host.Find(".tm-accordion-item__header").Click();
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1, "the user opened the item");

        host.Find("[data-testid='host-rerender']").Click();
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1,
            "a parent re-render with an unchanged IsExpanded=false must NOT close the user-opened item " +
            "(the pre-4l bug overwrote the user state on every parent render)");
    }

    [Fact]
    public void Uncontrolled_HostParamChange_IsApplied()
    {
        var host = Render<UncontrolledHost>();

        host.Find(".tm-accordion-item__header").Click();
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);

        // false -> true: a real value change flows in (still expanded).
        host.Find("[data-testid='host-set-true']").Click();
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);

        // The user closes; a re-render with the unchanged true must not reopen.
        host.Find(".tm-accordion-item__header").Click();
        host.FindAll(".tm-accordion-item--expanded").Should().BeEmpty();
        host.Find("[data-testid='host-rerender']").Click();
        host.FindAll(".tm-accordion-item--expanded").Should().BeEmpty(
            "a parent re-render with an unchanged IsExpanded=true must not reopen the user-closed item");

        // true -> false is again a real change and is applied (still collapsed), then the host
        // flips back to true and the item opens — the declarative switch keeps working.
        host.Find("[data-testid='host-set-false']").Click();
        host.FindAll(".tm-accordion-item--expanded").Should().BeEmpty();
        host.Find("[data-testid='host-set-true']").Click();
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1,
            "a host that flips the IsExpanded value still drives the item declaratively");
    }

    // ── (d) single mode: sibling close callback + event order ───────────────

    [Fact]
    public void SingleMode_OpeningB_ClosesControlledA_AndReportsBoth_InOrder()
    {
        var host = Render<SingleModeControlledHost>();

        host.FindAll(".tm-accordion-item__header")[1].Click();

        host.Instance.Events.Should().Equal(
            ["cb:a:False", "toggled:a:False:0", "cb:b:True", "toggled:b:True:1"],
            "single mode closes the expanded sibling BEFORE the clicked item opens; each change " +
            "raises the item's IsExpandedChanged and one OnItemToggled notification");

        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);
        host.FindAll(".tm-accordion-item--expanded .tm-accordion-item__body")
            .Should().ContainSingle().Which.TextContent.Should().Contain("Body B");
    }

    [Fact]
    public void SingleMode_CollapsingTheOpenItem_ReportsOnlyThatItem()
    {
        var host = Render<SingleModeControlledHost>();

        host.FindAll(".tm-accordion-item__header")[0].Click(); // collapse A

        host.Instance.Events.Should().Equal(["cb:a:False", "toggled:a:False:0"]);
        host.FindAll(".tm-accordion-item--expanded").Should().BeEmpty();
    }

    [Fact]
    public void SingleMode_UncontrolledSiblingClosedImplicitly_StaysClosedOnHostRerender()
    {
        var host = Render<SingleModeUncontrolledHost>();

        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1, "A seeds IsExpanded=true");
        host.FindAll(".tm-accordion-item__header")[1].Click(); // open B — single mode closes A implicitly
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);

        host.Find("[data-testid='host-rerender']").Click();
        var expanded = host.FindAll(".tm-accordion-item--expanded");
        expanded.Should().HaveCount(1,
            "the host keeps passing IsExpanded=true to A, but the implicit single-mode close is a " +
            "user-driven state the unchanged parameter must not undo");
        expanded[0].QuerySelector(".tm-accordion-item__body")!.TextContent.Should().Contain("Body B");
    }

    // ── (e) ExpandedKeys group control ──────────────────────────────────────

    [Fact]
    public void ExpandedKeys_Seed_OpensTheKeyedItems_IgnoringIsExpanded()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, true)
            .Add(h => h.Keys, new List<string> { "b" }));

        var expanded = host.FindAll(".tm-accordion-item--expanded");
        expanded.Should().HaveCount(1);
        expanded[0].QuerySelector(".tm-accordion-item__title")!.TextContent.Should().Be("B");
        host.FindAll(".tm-accordion-item__header")[0].GetAttribute("aria-expanded").Should().Be("false",
            "item A carries IsExpanded=true but a non-null ExpandedKeys owns every keyed item");
    }

    [Fact]
    public void ExpandedKeys_Multiple_ClickEmitsOneSnapshot_InDomOrder()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, true)
            .Add(h => h.Keys, new List<string> { "a" }));

        host.FindAll(".tm-accordion-item__header")[2].Click(); // C

        host.Instance.ChangedCount.Should().Be(1, "one click emits exactly one snapshot");
        host.Instance.Snapshots[0].Should().Equal(["a", "c"], "the snapshot follows the item order in the DOM");
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(2);
        host.Instance.ToggledEvents.Should().Equal(["c:True"]);
    }

    [Fact]
    public void ExpandedKeys_SingleMode_SnapshotCarriesAtMostOneKey()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, false)
            .Add(h => h.Keys, new List<string> { "a" }));

        host.FindAll(".tm-accordion-item__header")[2].Click(); // C

        host.Instance.ChangedCount.Should().Be(1);
        host.Instance.Snapshots[0].Should().Equal(["c"],
            "single mode replaces the expanded key instead of accumulating");
        host.Instance.ToggledEvents.Should().Equal(["a:False", "c:True"],
            "the implicit sibling close is reported before the clicked item opens");
    }

    [Fact]
    public void ExpandedKeys_SingleMode_SeedWithManyKeys_KeepsTheFirstInDomOrder()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, false)
            .Add(h => h.Keys, new List<string> { "b", "a" }));

        var expanded = host.FindAll(".tm-accordion-item--expanded");
        expanded.Should().HaveCount(1,
            "a seed with several keys in single mode keeps the first item in DOM order");
        expanded[0].QuerySelector(".tm-accordion-item__title")!.TextContent.Should().Be("A");
    }

    [Fact]
    public void ExpandedKeys_KeylessItem_StaysSelfManaged()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, true)
            .Add(h => h.Keys, new List<string> { "a" }));

        host.FindAll(".tm-accordion-item__header")[3].Click(); // keyless "free"

        host.Instance.ChangedCount.Should().Be(0, "a keyless item is outside the group control");
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(2,
            "the keyed item A stays open and the keyless item toggled itself");
        host.Instance.ToggledEvents.Should().Equal(["<null>:True"],
            "a keyless item reports a null Key in the toggle notification");
    }

    [Fact]
    public void ExpandedKeys_RejectsHostUpdate_KeepsTheRenderedState()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, true)
            .Add(h => h.Keys, new List<string> { "a" })
            .Add(h => h.UpdateOnCallback, false));

        host.FindAll(".tm-accordion-item__header")[2].Click(); // C

        host.Instance.ChangedCount.Should().Be(1, "the request is still reported");
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1,
            "a host that does not adopt the snapshot keeps the previous expansion");
    }

    [Fact]
    public void ExpandedKeys_SingleMode_ClosingTheOnlyRenderedItem_EmptiesTheSnapshot()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, false)
            .Add(h => h.Keys, new List<string> { "b", "a" }));

        var expanded = host.FindAll(".tm-accordion-item--expanded");
        expanded.Should().HaveCount(1, "single mode renders only the first registered seeded item");
        expanded[0].QuerySelector(".tm-accordion-item__title")!.TextContent.Should().Be("A");

        host.FindAll(".tm-accordion-item__header")[0].Click(); // close A

        host.Instance.ChangedCount.Should().Be(1);
        host.Instance.Snapshots[0].Should().BeEmpty(
            "closing the only RENDERED expanded item empties the snapshot — B's key was never " +
            "rendered expanded and must not resurrect");
        host.Instance.ToggledEvents.Should().Equal(["a:False"]);
        host.FindAll(".tm-accordion-item--expanded").Should().BeEmpty(
            "after the host adopts the empty snapshot no item is expanded");
    }

    [Fact]
    public void ExpandedKeys_SingleMode_KeylessClick_ClosesTheGroupControlledSiblingThroughTheSnapshot()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, false)
            .Add(h => h.Keys, new List<string> { "a" }));

        host.FindAll(".tm-accordion-item__header")[3].Click(); // keyless "Free"

        host.Instance.ChangedCount.Should().Be(1,
            "the group-controlled sibling closes through the emitted snapshot — without it two " +
            "items would stay open while the event claims otherwise");
        host.Instance.Snapshots[0].Should().BeEmpty();
        host.Instance.ToggledEvents.Should().Equal(["a:False", "<null>:True"],
            "the implicit sibling close is reported before the clicked item opens");
        var expanded = host.FindAll(".tm-accordion-item--expanded");
        expanded.Should().HaveCount(1, "only the clicked keyless item stays open");
        expanded[0].QuerySelector(".tm-accordion-item__title")!.TextContent.Should().Be("Free");
    }

    [Fact]
    public void ExpandedKeys_Multiple_UnknownKeysSurviveAtTheEndOfTheSnapshot()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, true)
            .Add(h => h.Keys, new List<string> { "zzz", "a" }));

        host.FindAll(".tm-accordion-item__header")[2].Click(); // C

        host.Instance.Snapshots[0].Should().Equal(["a", "c", "zzz"],
            "registered keys come in registration order, a key without a matching item survives at the end");
    }

    [Fact]
    public void ExpandedKeys_SingleMode_UnknownKeysSurvive_ButAtMostOneRenderedKey()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, false)
            .Add(h => h.Keys, new List<string> { "zzz" }));

        host.FindAll(".tm-accordion-item__header")[2].Click(); // C

        host.Instance.Snapshots[0].Should().Equal(["c", "zzz"],
            "the single-mode cap of one key applies to RENDERED items; the unknown key survives untouched");
    }

    [Fact]
    public void ExpandedKeysChanged_SnapshotIsAnImmutableArray()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, true)
            .Add(h => h.Keys, new List<string> { "a" }));

        host.FindAll(".tm-accordion-item__header")[2].Click(); // C

        host.Instance.Snapshots[0].Should().BeOfType<string[]>(
            "the snapshot is documented as immutable — an array is not aliased and cannot be resized");
    }

    [Fact]
    public void ExpandedKeys_CompareOrdinal_CaseSensitive()
    {
        var host = Render<KeysHost>(p => p
            .Add(h => h.Multiple, true)
            .Add(h => h.Keys, new List<string> { "A" })); // uppercase — not the item key "a"

        host.FindAll(".tm-accordion-item--expanded").Should().BeEmpty(
            "keys compare ordinally (case-sensitive): 'A' does not match the item key 'a'");
    }

    // ── (e2) group state reaches items without body content ────────────────

    [Fact]
    public void GroupState_ReachesItemsWithoutBodyContent_WhenTheHostChangesTheKeys()
    {
        var host = Render<BodylessKeysHost>();
        host.FindAll(".tm-accordion-item__header")[1].GetAttribute("aria-expanded").Should().Be("false");

        host.Find("[data-testid='host-set-keys']").Click();

        host.FindAll(".tm-accordion-item__header")[1].GetAttribute("aria-expanded").Should().Be("true",
            "the group state reaches an item without ChildContent when only ExpandedKeys changes");
        host.FindAll(".tm-accordion-item__header")[0].GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void GroupState_SingleModeDeepLinkChange_ReachesBodyLessItems()
    {
        var host = Render<BodylessKeysHost>(p => p.Add(h => h.Multiple, false));
        host.FindAll(".tm-accordion-item__header")[0].GetAttribute("aria-expanded").Should().Be("false");

        host.Find("[data-testid='host-set-keys']").Click(); // host sets Keys = ["b"] (deep-link style)

        host.FindAll(".tm-accordion-item__header")[1].GetAttribute("aria-expanded").Should().Be("true",
            "a single-mode deep-link change expands the keyed item even without body content");
        host.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);
    }

    // ── (f) disabled items raise nothing ────────────────────────────────────

    [Fact]
    public void DisabledItem_RaisesNoCallback()
    {
        var host = Render<ControlledItemHost>(p => p.Add(h => h.Disabled, true));

        host.Find(".tm-accordion-item__header").Click();

        host.Instance.Raised.Should().BeEmpty();
        host.FindAll(".tm-accordion-item--expanded").Should().BeEmpty();
    }

    // ── (g) DOM identity for existing hosts ─────────────────────────────────

    [Fact]
    public void Accordion_WithoutNewParameters_RendersTheSameDom()
    {
        // Captured from the pre-4l implementation (HEAD d6b9273b): the additive API must not
        // change a single attribute for a host that passes none of the new parameters.
        var cut = Render<AccordionMarkupProbe>();

        cut.Markup.Should().Be(AccordionMarkupProbe.ExpectedMarkup,
            "an accordion host without the new parameters renders byte-identical markup");
    }

    // ── source guard: the parameter is never assigned inside the component ──

    [Fact]
    public void AccordionSource_NeverAssignsTheIsExpandedParameter()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var files = new[]
        {
            Path.Combine(root, "src", "Tempo.Blazor", "Components", "DataDisplay", "TmAccordionItem.razor"),
            Path.Combine(root, "src", "Tempo.Blazor", "Components", "DataDisplay", "TmAccordion.razor"),
        };

        foreach (var file in files)
        {
            File.Exists(file).Should().BeTrue($"the guard scans {file}");
            var source = StripComments(File.ReadAllText(file));
            var assignments = Regex.Matches(source, @"\bIsExpanded\s*=[^=]")
                .Select(m => m.Value)
                .ToList();
            assignments.Should().BeEmpty(
                $"{Path.GetFileName(file)} must never assign the IsExpanded PARAMETER — the rendered " +
                "state lives in the internal _expanded field and changes flow out through " +
                "IsExpandedChanged (the ReadOnly contract convention); found: {0}", string.Join(", ", assignments));
        }
    }

    private static string StripComments(string source)
    {
        var noRazorComments = Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);
        var noMarkupComments = Regex.Replace(noRazorComments, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(noMarkupComments, @"//[^\n]*", string.Empty);
    }

    // ── hosts ───────────────────────────────────────────────────────────────

    /// <summary>One controlled item; the host optionally refuses to adopt the raised value.</summary>
    public sealed class ControlledItemHost : ComponentBase
    {
        [Parameter] public bool InitialExpanded { get; set; }
        [Parameter] public bool UpdateOnCallback { get; set; } = true;
        [Parameter] public bool Disabled { get; set; }

        public List<bool> Raised { get; } = [];
        private bool _value;

        protected override void OnInitialized() => _value = InitialExpanded;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmAccordionItem>(0);
            builder.AddAttribute(1, nameof(TmAccordionItem.Title), "Bound");
            builder.AddAttribute(2, nameof(TmAccordionItem.IsExpanded), _value);
            builder.AddAttribute(3, nameof(TmAccordionItem.IsExpandedChanged),
                EventCallback.Factory.Create<bool>(this, v =>
                {
                    Raised.Add(v);
                    if (UpdateOnCallback)
                    {
                        _value = v;
                    }
                }));
            builder.AddAttribute(4, nameof(TmAccordionItem.Disabled), Disabled);
            builder.AddAttribute(5, nameof(TmAccordionItem.AdditionalAttributes),
                new Dictionary<string, object>
                {
                    // No-op key sinks so bUnit can dispatch keydown/keyup on the header button.
                    ["onkeydown"] = EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }),
                    ["onkeyup"] = EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }),
                });
            builder.AddAttribute(6, nameof(TmAccordionItem.ChildContent),
                (RenderFragment)(b => b.AddContent(0, "Body")));
            builder.CloseComponent();
        }
    }

    /// <summary>Uncontrolled item plus host buttons that re-render or flip the parameter value.</summary>
    public sealed class UncontrolledHost : ComponentBase
    {
        private bool _paramValue;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmAccordionItem>(0);
            builder.AddAttribute(1, nameof(TmAccordionItem.Title), "Uncontrolled");
            builder.AddAttribute(2, nameof(TmAccordionItem.IsExpanded), _paramValue);
            builder.AddAttribute(3, nameof(TmAccordionItem.ChildContent),
                (RenderFragment)(b => b.AddContent(0, "Body")));
            builder.CloseComponent();

            builder.OpenElement(4, "button");
            builder.AddAttribute(5, "data-testid", "host-rerender");
            builder.AddAttribute(6, "onclick", EventCallback.Factory.Create(this, () => { }));
            builder.AddContent(7, "rerender");
            builder.CloseElement();

            builder.OpenElement(8, "button");
            builder.AddAttribute(9, "data-testid", "host-set-true");
            builder.AddAttribute(10, "onclick", EventCallback.Factory.Create(this, () => _paramValue = true));
            builder.AddContent(11, "set true");
            builder.CloseElement();

            builder.OpenElement(12, "button");
            builder.AddAttribute(13, "data-testid", "host-set-false");
            builder.AddAttribute(14, "onclick", EventCallback.Factory.Create(this, () => _paramValue = false));
            builder.AddContent(15, "set false");
            builder.CloseElement();
        }
    }

    /// <summary>Single-mode accordion with two controlled items recording every callback in order.</summary>
    public sealed class SingleModeControlledHost : ComponentBase
    {
        public List<string> Events { get; } = [];
        private bool _a = true;
        private bool _b;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmAccordion>(0);
            builder.AddAttribute(1, nameof(TmAccordion.Multiple), false);
            builder.AddAttribute(2, nameof(TmAccordion.OnItemToggled),
                EventCallback.Factory.Create<AccordionItemToggleEventArgs>(this, e =>
                    Events.Add($"toggled:{e.Key}:{e.Expanded}:{e.Index}")));
            builder.AddAttribute(3, nameof(TmAccordion.ChildContent), (RenderFragment)(b =>
            {
                b.OpenComponent<TmAccordionItem>(0);
                b.AddAttribute(1, nameof(TmAccordionItem.Title), "A");
                b.AddAttribute(2, nameof(TmAccordionItem.Key), "a");
                b.AddAttribute(3, nameof(TmAccordionItem.IsExpanded), _a);
                b.AddAttribute(4, nameof(TmAccordionItem.IsExpandedChanged),
                    EventCallback.Factory.Create<bool>(this, v => { Events.Add($"cb:a:{v}"); _a = v; }));
                b.AddAttribute(5, nameof(TmAccordionItem.ChildContent),
                    (RenderFragment)(x => x.AddContent(0, "Body A")));
                b.CloseComponent();

                b.OpenComponent<TmAccordionItem>(6);
                b.AddAttribute(7, nameof(TmAccordionItem.Title), "B");
                b.AddAttribute(8, nameof(TmAccordionItem.Key), "b");
                b.AddAttribute(9, nameof(TmAccordionItem.IsExpanded), _b);
                b.AddAttribute(10, nameof(TmAccordionItem.IsExpandedChanged),
                    EventCallback.Factory.Create<bool>(this, v => { Events.Add($"cb:b:{v}"); _b = v; }));
                b.AddAttribute(11, nameof(TmAccordionItem.ChildContent),
                    (RenderFragment)(x => x.AddContent(0, "Body B")));
                b.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    /// <summary>
    /// Group-controlled accordion: items a/b/c keyed (A seeds <c>IsExpanded=true</c> to prove the
    /// parameter is ignored under group control) plus a keyless self-managed item.
    /// </summary>
    public sealed class KeysHost : ComponentBase
    {
        [Parameter] public bool Multiple { get; set; }
        [Parameter] public IReadOnlyCollection<string>? Keys { get; set; }
        [Parameter] public bool UpdateOnCallback { get; set; } = true;

        public int ChangedCount { get; private set; }
        public List<IReadOnlyCollection<string>> Snapshots { get; } = [];
        public List<string> ToggledEvents { get; } = [];

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmAccordion>(0);
            builder.AddAttribute(1, nameof(TmAccordion.Multiple), Multiple);
            builder.AddAttribute(2, nameof(TmAccordion.ExpandedKeys), Keys);
            builder.AddAttribute(3, nameof(TmAccordion.ExpandedKeysChanged),
                EventCallback.Factory.Create<IReadOnlyCollection<string>>(this, keys =>
                {
                    ChangedCount++;
                    Snapshots.Add(keys);
                    if (UpdateOnCallback)
                    {
                        Keys = keys;
                    }
                }));
            builder.AddAttribute(4, nameof(TmAccordion.OnItemToggled),
                EventCallback.Factory.Create<AccordionItemToggleEventArgs>(this, e =>
                    ToggledEvents.Add($"{e.Key ?? "<null>"}:{e.Expanded}")));
            builder.AddAttribute(5, nameof(TmAccordion.ChildContent), (RenderFragment)(b =>
            {
                AddItem(b, 0, "A", "a", isExpanded: true);
                AddItem(b, 6, "B", "b");
                AddItem(b, 12, "C", "c");
                AddItem(b, 18, "Free", null);
            }));
            builder.CloseComponent();
        }

        private static void AddItem(RenderTreeBuilder b, int seq, string title, string? key, bool isExpanded = false)
        {
            b.OpenComponent<TmAccordionItem>(seq);
            b.AddAttribute(seq + 1, nameof(TmAccordionItem.Title), title);
            if (key is not null)
            {
                b.AddAttribute(seq + 2, nameof(TmAccordionItem.Key), key);
            }
            b.AddAttribute(seq + 3, nameof(TmAccordionItem.IsExpanded), isExpanded);
            b.AddAttribute(seq + 4, nameof(TmAccordionItem.ChildContent),
                (RenderFragment)(x => x.AddContent(0, $"Body {title}")));
            b.CloseComponent();
        }
    }

    /// <summary>Single-mode accordion with two UNCONTROLLED items (A seeds <c>IsExpanded=true</c>) and a host re-render button.</summary>
    public sealed class SingleModeUncontrolledHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmAccordion>(0);
            builder.AddAttribute(1, nameof(TmAccordion.Multiple), false);
            builder.AddAttribute(2, nameof(TmAccordion.ChildContent), (RenderFragment)(b =>
            {
                b.OpenComponent<TmAccordionItem>(0);
                b.AddAttribute(1, nameof(TmAccordionItem.Title), "A");
                b.AddAttribute(2, nameof(TmAccordionItem.IsExpanded), true);
                b.AddAttribute(3, nameof(TmAccordionItem.ChildContent),
                    (RenderFragment)(x => x.AddContent(0, "Body A")));
                b.CloseComponent();

                b.OpenComponent<TmAccordionItem>(4);
                b.AddAttribute(5, nameof(TmAccordionItem.Title), "B");
                b.AddAttribute(6, nameof(TmAccordionItem.ChildContent),
                    (RenderFragment)(x => x.AddContent(0, "Body B")));
                b.CloseComponent();
            }));
            builder.CloseComponent();

            builder.OpenElement(3, "button");
            builder.AddAttribute(4, "data-testid", "host-rerender");
            builder.AddAttribute(5, "onclick", EventCallback.Factory.Create(this, () => { }));
            builder.AddContent(6, "rerender");
            builder.CloseElement();
        }
    }

    /// <summary>
    /// Group-controlled accordion whose keyed items render NO body content; a host button flips
    /// the keys (deep-link style), proving the group state reaches items that get no parameter
    /// pass of their own.
    /// </summary>
    public sealed class BodylessKeysHost : ComponentBase
    {
        [Parameter] public bool Multiple { get; set; } = true;

        public IReadOnlyCollection<string> Keys { get; private set; } = Array.Empty<string>();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "data-testid", "host-set-keys");
            builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, () => Keys = ["b"]));
            builder.AddContent(3, "set keys");
            builder.CloseElement();

            builder.OpenComponent<TmAccordion>(4);
            builder.AddAttribute(5, nameof(TmAccordion.Multiple), Multiple);
            builder.AddAttribute(6, nameof(TmAccordion.ExpandedKeys), Keys);
            builder.AddAttribute(7, nameof(TmAccordion.ChildContent), (RenderFragment)(b =>
            {
                b.OpenComponent<TmAccordionItem>(0);
                b.AddAttribute(1, nameof(TmAccordionItem.Title), "A");
                b.AddAttribute(2, nameof(TmAccordionItem.Key), "a");
                b.CloseComponent();

                b.OpenComponent<TmAccordionItem>(3);
                b.AddAttribute(4, nameof(TmAccordionItem.Title), "B");
                b.AddAttribute(5, nameof(TmAccordionItem.Key), "b");
                b.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    /// <summary>Renders the pre-4l reference accordion and carries the markup captured from HEAD.</summary>
    public sealed class AccordionMarkupProbe : ComponentBase
    {
        /// <summary>Captured from the pre-4l render at HEAD d6b9273b (bUnit markup of the probe below).</summary>
        public const string ExpectedMarkup = """
<div class="tm-accordion "><div class="tm-accordion-item   "><button type="button" class="tm-accordion-item__header" aria-expanded="false" blazor:onclick="1"><div class="tm-accordion-item__header-text"><div><span class="tm-accordion-item__title">First</span></div></div>
        <span class="tm-accordion-item__chevron"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg></span></button></div><div class="tm-accordion-item   "><button type="button" class="tm-accordion-item__header" aria-expanded="false" blazor:onclick="2"><div class="tm-accordion-item__header-text"><div><span class="tm-accordion-item__title">Second</span></div></div>
        <span class="tm-accordion-item__chevron"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg></span></button></div><div class="tm-accordion-item   "><button type="button" class="tm-accordion-item__header" aria-expanded="false" blazor:onclick="3"><div class="tm-accordion-item__header-text"><div><span class="tm-accordion-item__title">Third</span></div></div>
        <span class="tm-accordion-item__chevron"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg></span></button></div></div>
""";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmAccordion>(0);
            builder.AddAttribute(1, nameof(TmAccordion.ChildContent), (RenderFragment)(b =>
            {
                AddItem(b, 0, "First", "Content One");
                AddItem(b, 2, "Second", "Content Two");
                AddItem(b, 4, "Third", "Content Three");
            }));
            builder.CloseComponent();
        }

        private static void AddItem(RenderTreeBuilder b, int seq, string title, string content)
        {
            b.OpenComponent<TmAccordionItem>(seq);
            b.AddAttribute(seq, nameof(TmAccordionItem.Title), title);
            b.AddAttribute(seq + 1, nameof(TmAccordionItem.ChildContent),
                (RenderFragment)(x => x.AddContent(0, content)));
            b.CloseComponent();
        }
    }
}
