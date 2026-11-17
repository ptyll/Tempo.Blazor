using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// Tail of <see cref="TmSelectTests"/> split out to keep every source file under the
/// 1200-line ceiling (N270/F13): default values, ReadOnly, stable ids, AriaDescribedBy
/// and LabelTrailing. The class is partial — one logical test family, two files.
/// </summary>
public partial class TmSelectTests
{

    // Regression (phase-4 review): a NON-nullable value type at its default is a real choice. The
    // select used to omit `value` and the browser painted an empty control after a dialog switched
    // back to an option whose value equals the type default (Ascending / Any / Stop / General / 0).
    [Fact]
    public void DefaultValue_NonNullableValueType_EmitsItsValue()
    {
        var options = new List<SelectOption<int>>
        {
            new(0, "Zero"),
            new(1, "One")
        };
        var cut = Render<TmSelect<int>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.Value, 0));

        cut.Find("select").GetAttribute("value").Should().Be("0",
            "value 0 is a selectable option, not the empty state");
        cut.FindAll("option").First(o => o.GetAttribute("value") == "0")
            .HasAttribute("selected").Should().BeTrue("the 0-option still carries the selected marker");
    }

    [Fact]
    public void DefaultValue_NonNullableEnum_EmitsItsValue()
    {
        var options = new List<SelectOption<DayOfWeek>>
        {
            new(DayOfWeek.Sunday, "Sunday"),
            new(DayOfWeek.Monday, "Monday")
        };
        var cut = Render<TmSelect<DayOfWeek>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.Value, DayOfWeek.Sunday));

        cut.Find("select").GetAttribute("value").Should().Be("Sunday");
    }

    // ── ReadOnly, stable id, AriaDescribedBy, LabelTrailing (plan 98c80bc3, Fáze 3) ──

    /// <summary>
    /// Fáze 3: <c>ReadOnly</c> is the readable "locked field" state — the select stays focusable with
    /// readable text (a native &lt;select&gt; has no readonly attribute, so the state carries
    /// <c>aria-readonly="true"</c> plus the <c>tm-select-readonly</c> class), and it is NOT the disabled
    /// presentation: no <c>disabled</c> attribute and no disabled gray.
    /// </summary>
    [Fact]
    public void TmSelect_ReadOnly_IsReadableAndNotDisabled()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech") })
            .Add(c => c.Value, "cz")
            .Add(c => c.ReadOnly, true));

        var select = cut.Find("select");
        select.GetAttribute("aria-readonly").Should().Be("true");
        select.ClassList.Should().Contain("tm-select-readonly");
        select.ClassList.Should().NotContain("tm-select-disabled");
        select.HasAttribute("disabled").Should().BeFalse(
            "readonly text stays readable and focusable — disabled is the different, gray state");
    }

    [Fact]
    public async Task TmSelect_ReadOnly_IgnoresUserChanges()
    {
        string? raised = null;
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech"), new("de", "German") })
            .Add(c => c.Value, "cz")
            .Add(c => c.ReadOnly, true)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => raised = v)));

        await cut.Find("select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "de" });

        raised.Should().BeNull("a read-only select never raises ValueChanged from the user");
    }

    /// <summary>
    /// Review round (plan 98c80bc3, Fáze 3): the DOM restore after a refused read-only change hands
    /// focus back to the select ONLY while the user is actually working with it — a programmatic or
    /// UA-quirk change that fires while the pointer/keyboard is elsewhere must not yank focus onto
    /// the select. The component tracks the focus state through <c>@onfocus</c>/<c>@onblur</c> and
    /// restores only while it holds.
    /// </summary>
    [Fact]
    public async Task TmSelect_ReadOnly_RestoresFocusAfterARemountOnlyWhileTheUserIsOnTheSelect()
    {
        const string focusInvocation = "Blazor._internal.domWrapper.focus";

        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech"), new("de", "German") })
            .Add(c => c.Value, "cz")
            .Add(c => c.ReadOnly, true));

        // A change attempt while the user is NOT on the select: the remount must not steal focus.
        await cut.Find("select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "de" });
        JSInterop.Invocations.Count(i => i.Identifier == focusInvocation).Should().Be(0,
            "nobody was working with the select, so the restore must not move focus");

        // The user focuses the select and tries again: now the post-remount restore fires.
        cut.Find("select").Focus();
        await cut.Find("select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "de" });
        JSInterop.Invocations.Count(i => i.Identifier == focusInvocation).Should().Be(1,
            "the user was on the select, so the remount hands focus back");

        // The user left; a further attempt stays quiet.
        cut.Find("select").Blur();
        await cut.Find("select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "de" });
        JSInterop.Invocations.Count(i => i.Identifier == focusInvocation).Should().Be(1,
            "after blur the restore must not fire again");
    }


    [Fact]
    public void TmSelect_NotReadOnly_HasNoReadonlyState()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech") }));

        var select = cut.Find("select");
        select.GetAttribute("aria-readonly").Should().BeNull();
        select.ClassList.Should().NotContain("tm-select-readonly");
    }

    /// <summary>
    /// Fáze 3: the generated default id is a prefixed sequential id that is stable for the lifetime of
    /// the component instance — not a random GUID fragment — so a label's <c>for</c> and every
    /// <c>aria-describedby</c> derived from it survive parameter snapshots and are readable in E2E.
    /// </summary>
    [Fact]
    public void TmSelect_DefaultId_IsPrefixedAndStableForTheInstance()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech") }));

        var first = cut.Instance.Id;
        first.Should().MatchRegex("^tm-select-\\d+$",
            "the default id is the component prefix plus a sequence number, not a GUID fragment");

        cut.Render(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech") })
            .Add(c => c.Value, "cz"));
        cut.Instance.Id.Should().Be(first, "the id never churns between parameter snapshots");

        Render<TmSelect<string>>().Instance.Id.Should().NotBe(first,
            "two instances still get distinct ids");
    }

    /// <summary>
    /// The counter is SHARED across the closed generic types: TmSelect&lt;string&gt;, TmSelect&lt;int&gt;
    /// and TmSelect&lt;SomeEnum&gt; on one page must never share an id (the task panel renders three
    /// different TValues side by side). Caught by the Gantt markup snapshot, which showed three
    /// "tm-select-1" selects when the counter lived in the generic type's own statics.
    /// </summary>
    [Fact]
    public void TmSelect_DefaultId_IsUniqueAcrossClosedGenericTypes()
    {
        var options = new List<SelectOption<int>> { new(1, "One") };

        var idOfString = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech") })).Instance.Id;
        var idOfInt = Render<TmSelect<int>>(p => p.Add(c => c.Options, options)).Instance.Id;

        idOfString.Should().NotBe(idOfInt);
        idOfInt.Should().MatchRegex("^tm-select-\\d+$");
    }

    [Fact]
    public void TmSelect_ErrorId_DerivesFromTheStableDefaultId()
    {
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.Error, "Required"));

        cut.Find("#" + cut.Instance.Id + "-error").Should().NotBeNull();
    }

    /// <summary>
    /// Fáze 3: <c>AriaDescribedBy</c> names extra ids (a hint counter, a host description) the control
    /// must reference — the ids are APPENDED to the component's own chain and duplicates are dropped.
    /// </summary>
    [Fact]
    public void TmSelect_AriaDescribedBy_AppendsToTheOwnChain()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech") })
            .Add(c => c.AriaDescribedBy, "hint-counter"));

        cut.Find("select").GetAttribute("aria-describedby").Should().Be("hint-counter");

        var withError = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech") })
            .Add(c => c.Error, "Required")
            .Add(c => c.AriaDescribedBy, "hint-counter"));

        withError.Find("select").GetAttribute("aria-describedby")
            .Should().Be($"{withError.Instance.Id}-error hint-counter",
                "the own chain stays first, the host ids follow");
    }

    [Fact]
    public void TmSelect_AriaDescribedBy_IsDeduplicatedAgainstTheOwnChain()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech") })
            .Add(c => c.HelpText, "Pick one")
            .Add(c => c.AriaDescribedBy, "field-help"));

        // The select's own chain (help text) already references {Id}-help; AriaDescribedBy points at
        // the TmFormField help id, which must appear exactly once in the merged list.
        var described = cut.Find("select").GetAttribute("aria-describedby")!;
        described.Split(' ').Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Fáze 3: the <c>LabelTrailing</c> slot renders at the end of the label row — the slot for the
    /// counter or the status chip a host used to overlay absolutely over the field. Without the slot
    /// the label renders exactly as before (no extra wrapper element).
    /// </summary>
    [Fact]
    public void TmSelect_LabelTrailing_RendersAtTheEndOfTheLabelRow()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech") })
            .Add(c => c.Label, "Region")
            .Add(c => c.LabelTrailing, "<span id='region-count'>3</span>"));

        var row = cut.Find(".tm-input-label-row");
        row.Should().NotBeNull();
        row.QuerySelector("label")!.TextContent.Trim().Should().Be("Region");
        row.QuerySelector("#region-count")!.TextContent.Should().Be("3",
            "the slot content sits at the end of the label row");
    }

    [Fact]
    public void TmSelect_WithoutLabelTrailing_KeepsThePlainLabelMarkup()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, new List<SelectOption<string>> { new("cz", "Czech") })
            .Add(c => c.Label, "Region"));

        cut.FindAll(".tm-input-label-row").Should().BeEmpty(
            "no slot, no wrapper — the label markup is unchanged for hosts that do not use it");
    }
 }
