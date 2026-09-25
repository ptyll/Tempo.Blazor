using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmSelect&lt;TValue&gt;.</summary>
public class TmSelectTests : LocalizationTestBase
{
    [Fact]
    public void TmSelect_Renders_Select_Element()
    {
        var cut = Render<TmSelect<string>>();
        cut.Find("select").Should().NotBeNull();
    }

    [Fact]
    public void TmSelect_Has_Base_CssClass()
    {
        var cut = Render<TmSelect<string>>();
        cut.Find("select").ClassList.Should().Contain("tm-select");
    }

    [Fact]
    public void TmSelect_NoLabel_WithPlaceholder_UsesPlaceholderAsAriaLabel()
    {
        // Accessibility: a select with only a placeholder (no visible <label>) must still have
        // an accessible name so it passes axe select-name.
        var cut = Render<TmSelect<string>>(p => p
            .Add(x => x.Placeholder, "Filter by department"));

        cut.Find("select").GetAttribute("aria-label").Should().Be("Filter by department");
    }

    [Fact]
    public void TmSelect_WithLabel_DoesNotSetAriaLabel()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(x => x.Label, "Department")
            .Add(x => x.Placeholder, "Filter by department"));

        // The visible <label for> already names the control; no aria-label to avoid double-naming.
        cut.Find("select").GetAttribute("aria-label").Should().BeNull();
    }

    [Fact]
    public void TmSelect_Label_Renders_Label_Element()
    {
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.Label, "Status"));
        cut.Find("label").TextContent.Trim().Should().Be("Status");
    }

    [Fact]
    public void TmSelect_No_Label_When_Null()
    {
        var cut = Render<TmSelect<string>>();
        cut.FindAll("label").Should().BeEmpty();
    }

    [Fact]
    public void TmSelect_Placeholder_Renders_Disabled_Option()
    {
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.Placeholder, "Choose..."));
        var placeholderOption = cut.Find("option[disabled]");
        placeholderOption.TextContent.Should().Contain("Choose...");
    }

    [Fact]
    public void TmSelect_No_Placeholder_Option_When_Null()
    {
        var cut = Render<TmSelect<string>>(p => p
            .AddChildContent("<option value='a'>A</option>"));
        cut.FindAll("option[disabled]").Should().BeEmpty();
    }

    [Fact]
    public void TmSelect_Disabled_Sets_Disabled_Attribute()
    {
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.Disabled, true));
        cut.Find("select").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmSelect_Error_Adds_Error_CssClass()
    {
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.Error, "Required"));
        cut.Find("select").ClassList.Should().Contain("tm-select-error");
    }

    [Fact]
    public void TmSelect_Error_Shows_Error_Message()
    {
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.Error, "Select a value"));
        cut.Find("[data-testid='select-error']").TextContent.Should().Contain("Select a value");
    }

    [Fact]
    public void TmSelect_HelpText_Shown_When_No_Error()
    {
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.HelpText, "Pick one"));
        cut.Find("[data-testid='select-help']").TextContent.Should().Contain("Pick one");
    }

    [Fact]
    public void TmSelect_ChildContent_Renders_Options()
    {
        var cut = Render<TmSelect<string>>(p => p
            .AddChildContent("<option value='a'>Alpha</option><option value='b'>Beta</option>"));
        cut.FindAll("option").Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void TmSelect_ValueChanged_Fires_On_Change()
    {
        string? captured = null;
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => captured = v))
            .AddChildContent("<option value='alpha'>Alpha</option>"));

        cut.Find("select").Change("alpha");

        captured.Should().Be("alpha");
    }

    [Fact]
    public void TmSelect_Options_Renders_Option_Elements()
    {
        var options = new List<SelectOption<string>>
        {
            new("admin", "Admin"),
            new("editor", "Editor"),
            new("viewer", "Viewer"),
        };
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.Options, options));

        var rendered = cut.FindAll("option");
        rendered.Count.Should().Be(3);
        rendered[0].TextContent.Trim().Should().Be("Admin");
        rendered[0].GetAttribute("value").Should().Be("admin");
        rendered[1].TextContent.Trim().Should().Be("Editor");
        rendered[2].TextContent.Trim().Should().Be("Viewer");
    }

    [Fact]
    public void TmSelect_Options_With_Placeholder_Renders_Placeholder_First()
    {
        var options = new List<SelectOption<string>>
        {
            new("a", "Alpha"),
            new("b", "Beta"),
        };
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.Placeholder, "Choose..."));

        var rendered = cut.FindAll("option");
        rendered.Count.Should().Be(3); // placeholder + 2 options
        rendered[0].TextContent.Trim().Should().Be("Choose...");
        rendered[0].HasAttribute("disabled").Should().BeTrue();
        rendered[1].TextContent.Trim().Should().Be("Alpha");
    }

    [Fact]
    public void TmSelect_Options_DisabledOption_Renders_Disabled()
    {
        var options = new List<SelectOption<string>>
        {
            new("a", "Alpha"),
            new("b", "Beta", isDisabled: true),
        };
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.Options, options));

        var rendered = cut.FindAll("option");
        rendered[0].HasAttribute("disabled").Should().BeFalse();
        rendered[1].HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmSelect_Options_ValueChanged_Fires_On_Selection()
    {
        string? captured = null;
        var options = new List<SelectOption<string>>
        {
            new("x", "Option X"),
            new("y", "Option Y"),
        };
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => captured = v)));

        cut.Find("select").Change("y");

        captured.Should().Be("y");
    }

    [Fact]
    public void TmSelect_Options_And_ChildContent_Together()
    {
        var options = new List<SelectOption<string>>
        {
            new("a", "From Options"),
        };
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, options)
            .AddChildContent("<option value='b'>From Child</option>"));

        var rendered = cut.FindAll("option");
        rendered.Count.Should().Be(2);
    }

    // ── Preselection with ChildContent (raw <option>) ───────────

    [Fact]
    public void TmSelect_ChildContent_Preselects_BoundValue_ViaSelectValueAttribute()
    {
        // Regrese: se syrovými <option> potomky musí předvybraná hodnota dojet do <select> přes
        // jeho value atribut (Blazor deferred value), ne spadnout na první možnost.
        var cut = Render<TmSelect<string>>(p => p
            .Add(x => x.Value, "sales")
            .AddChildContent("<option value=\"\">all</option><option value=\"dbo\">dbo</option><option value=\"sales\">sales</option>"));

        cut.Find("select").GetAttribute("value").Should().Be("sales");
    }

    [Fact]
    public void TmSelect_ChildContent_DefaultValue_OmitsSelectValueAttribute()
    {
        var cut = Render<TmSelect<string>>(p => p
            .AddChildContent("<option value=\"\">all</option><option value=\"dbo\">dbo</option>"));

        cut.Find("select").HasAttribute("value").Should().BeFalse();
    }

    // ── Required (accessibility) ─────────────────────────────────

    [Fact]
    public void TmSelect_Required_SetsAriaRequiredOnSelectElement()
    {
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.Required, true));
        cut.Find("select").GetAttribute("aria-required").Should().Be("true");
    }

    [Fact]
    public void TmSelect_Required_AddsRequiredMarkerClassToLabel()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Label, "Status")
            .Add(c => c.Required, true));
        cut.Find("label").ClassList.Should().Contain("tm-input-label-required");
    }

    [Fact]
    public void TmSelect_NotRequired_HasNoAriaRequiredAndNoMarker()
    {
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.Label, "Status"));
        cut.Find("select").HasAttribute("aria-required").Should().BeFalse();
        cut.Find("label").ClassList.Should().NotContain("tm-input-label-required");
    }

    // ── Numeric TValue round-trip (culture-invariant) ───────────

    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private enum SampleStatus
    {
        Draft = 0,
        Active = 1,
        Archived = 2,
    }

    /// <summary>
    /// Scopes <see cref="CultureInfo.CurrentCulture"/> and <see cref="CultureInfo.CurrentUICulture"/> to
    /// <paramref name="culture"/> for the duration of <paramref name="action"/>, restoring both in a finally
    /// so a failing assertion cannot leak culture into later tests. This is the repo convention for
    /// culture-sensitive tests (<c>TmMoneyDisplayTests.UsesCurrentCultureNumberFormatting</c>,
    /// <c>TmChartTimeAxisTests.UseCulture</c>): pinning <see cref="CultureInfo.DefaultThreadCurrentCulture"/>
    /// instead would only seed threads that have not already read/set their own culture, and is not
    /// guaranteed to reach bUnit's synchronous renderer on the already-running xUnit worker thread.
    /// </summary>
    private static void UnderCulture(CultureInfo culture, Action action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // The pin must actually have taken before the tests below rely on it -- otherwise a broken pin
        // would leave every "under Czech culture" assertion vacuously true under the machine's own default.
        CultureInfo.CurrentCulture.Name.Should().Be(culture.Name);

        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    /// <summary>
    /// Renders a single-option <see cref="TmSelect{TValue}"/> preselected to <paramref name="value"/> under
    /// <paramref name="culture"/> (Czech by default) and asserts: (1) the rendered <c>&lt;select value&gt;</c>
    /// preselection attribute matches the actually-rendered <c>&lt;option value&gt;</c> verbatim -- read back
    /// from the DOM, never a hardcoded literal, so the assertion cannot drift from what the component emits;
    /// (2) selecting that same raw string fires <c>ValueChanged</c>; and (3) the round-tripped value equals
    /// the original via <see cref="EqualityComparer{T}.Default"/>, which -- like <see cref="double.Equals(double)"/>
    /// and <see cref="float.Equals(float)"/> -- treats NaN as equal to NaN, so this also covers the NaN cases.
    /// </summary>
    private void AssertRoundTrip<T>(T value, CultureInfo? culture = null)
    {
        UnderCulture(culture ?? Czech, () =>
        {
            T? captured = default;
            var hasCaptured = false;
            var options = new List<SelectOption<T>> { new(value, "V") };

            var cut = Render<TmSelect<T>>(p => p
                .Add(c => c.Value, value)
                .Add(c => c.Options, options)
                .Add(c => c.ValueChanged, EventCallback.Factory.Create<T?>(this, v =>
                {
                    captured = v;
                    hasCaptured = true;
                })));

            var raw = cut.Find("option").GetAttribute("value");

            cut.Find("select").GetAttribute("value").Should().Be(raw,
                "the <select value> preselection must match the rendered <option value> exactly");

            cut.Find("select").Change(raw ?? string.Empty);

            hasCaptured.Should().BeTrue("ValueChanged must fire when the rendered option value is re-selected");
            EqualityComparer<T>.Default.Equals(captured, value).Should().BeTrue(
                $"round-tripping {typeof(T).Name} value '{value}' through render+parse should yield an equal value, got '{captured}'");
        });
    }

    // 11 numeric TValue types the fix added, one representative value each, plus a couple of nullable forms
    // and the pre-existing string/int/Guid/enum/short? behaviour re-asserted as a regression guard.
    [Fact] public void TmSelect_Short_RoundTrips() => AssertRoundTrip<short>(-1234);
    [Fact] public void TmSelect_NullableShort_RoundTrips() => AssertRoundTrip<short?>(3);
    [Fact] public void TmSelect_UShort_MaxValue_RoundTrips() => AssertRoundTrip(ushort.MaxValue);
    [Fact] public void TmSelect_Int_RoundTrips_Regression() => AssertRoundTrip(-1);
    [Fact] public void TmSelect_UInt_MaxValue_RoundTrips() => AssertRoundTrip(uint.MaxValue);
    [Fact] public void TmSelect_Long_RoundTrips() => AssertRoundTrip(1_234_567_890_123L);
    [Fact] public void TmSelect_Long_MinValue_RoundTrips() => AssertRoundTrip(long.MinValue);
    [Fact] public void TmSelect_ULong_MaxValue_RoundTrips() => AssertRoundTrip(ulong.MaxValue);
    [Fact] public void TmSelect_Byte_RoundTrips() => AssertRoundTrip<byte>(200);
    [Fact] public void TmSelect_SByte_Negative_RoundTrips() => AssertRoundTrip<sbyte>(-100);
    [Fact] public void TmSelect_Decimal_RoundTrips_WithoutCommaCorruption() => AssertRoundTrip(1.5m);
    [Fact] public void TmSelect_Decimal_28DigitScale_RoundTrips() => AssertRoundTrip(1.000000000000000000000000001m);
    [Fact] public void TmSelect_NullableDecimal_RoundTrips() => AssertRoundTrip<decimal?>(2.5m);
    [Fact] public void TmSelect_Double_RoundTrips_WithoutCommaCorruption() => AssertRoundTrip(1.5d);
    [Fact] public void TmSelect_Double_NaN_RoundTrips() => AssertRoundTrip(double.NaN);
    [Fact] public void TmSelect_Double_PositiveInfinity_RoundTrips() => AssertRoundTrip(double.PositiveInfinity);
    [Fact] public void TmSelect_Double_NegativeInfinity_RoundTrips() => AssertRoundTrip(double.NegativeInfinity);
    [Fact] public void TmSelect_Float_RoundTrips() => AssertRoundTrip(0.1f);
    [Fact] public void TmSelect_NullableFloat_RoundTrips() => AssertRoundTrip<float?>(0.1f);
    [Fact] public void TmSelect_Guid_RoundTrips_Regression() => AssertRoundTrip(Guid.Parse("a1b2c3d4-0000-0000-0000-000000000001"));
    [Fact] public void TmSelect_Enum_RoundTrips_Regression() => AssertRoundTrip(SampleStatus.Active);
    [Fact] public void TmSelect_String_RoundTrips_Regression() => AssertRoundTrip("sales");

    [Fact]
    public void TmSelect_Double_NegativeZero_PreservesSignBitThroughRoundTrip()
    {
        // EqualityComparer<double> (like ==) treats -0.0 and 0.0 as equal, so AssertRoundTrip's generic
        // assertion above cannot see a sign flip -- 1/x can, since 1/-0.0 == -Infinity but 1/0.0 == +Infinity.
        UnderCulture(Czech, () =>
        {
            double captured = double.NaN;
            var options = new List<SelectOption<double>> { new(-0.0, "V") };
            var cut = Render<TmSelect<double>>(p => p
                .Add(c => c.Value, -0.0)
                .Add(c => c.Options, options)
                .Add(c => c.ValueChanged, EventCallback.Factory.Create<double>(this, v => captured = v)));

            var raw = cut.Find("option").GetAttribute("value");
            cut.Find("select").Change(raw ?? string.Empty);

            double.IsNegative(captured).Should().BeTrue("the sign of zero must survive render + parse");
        });
    }

    [Fact]
    public void TmSelect_NullableShort_Selection_ParsesToTypedValue()
    {
        short? captured = -1;
        var options = new List<SelectOption<short?>>
        {
            new((short)3, "Three"),
        };
        var cut = Render<TmSelect<short?>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<short?>(this, v => captured = v)));

        cut.Find("select").Change("3");

        captured.Should().Be((short?)3);
    }

    [Fact]
    public void TmSelect_NullableShort_EmptySelection_ParsesToNull()
    {
        short? captured = 3;
        var options = new List<SelectOption<short?>>
        {
            new((short)3, "Three"),
        };
        var cut = Render<TmSelect<short?>>(p => p
            .Add(c => c.Placeholder, "Choose...")
            .Add(c => c.Options, options)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<short?>(this, v => captured = v)));

        cut.Find("select").Change("");

        captured.Should().BeNull();
    }
}
