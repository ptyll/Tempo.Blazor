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

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            // The pin must actually have taken before the tests below rely on it -- otherwise a broken pin
            // would leave every "under Czech culture" assertion vacuously true under the machine's own default.
            CultureInfo.CurrentCulture.Name.Should().Be(culture.Name);

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

    // ── Nullable TValue: displaying and selecting the empty (null) option ─────────

    [Fact]
    public void TmSelect_NullableDecimal_NullOption_RendersEmptyValueSelectedAndSelectValueEmpty()
    {
        // A null-valued SelectOption must render as <option value=""> (never a value-less option,
        // whose value would silently become its text content) and, with Value=null, carry the
        // selected marker while the <select> itself pins the preselection with value="".
        var options = new List<SelectOption<decimal?>>
        {
            new(null, "Kořenová"),
            new(1.5m, "1.5 %"),
        };
        var cut = Render<TmSelect<decimal?>>(p => p
            .Add(c => c.Value, (decimal?)null)
            .Add(c => c.Options, options));

        var nullOption = cut.FindAll("option")[0];
        nullOption.TextContent.Trim().Should().Be("Kořenová");
        nullOption.GetAttribute("value").Should().BeEmpty("FormatOptionValue(null) must render value=\"\"");
        nullOption.HasAttribute("selected").Should().BeTrue("Value=null must mark the null option selected");
        cut.Find("select").GetAttribute("value").Should().BeEmpty("the <select> must carry value=\"\" so the preselection lands on the null option");
    }

    [Fact]
    public void TmSelect_NullableDecimal_Selection_Moves_Between_Null_And_Value_Options()
    {
        var options = new List<SelectOption<decimal?>>
        {
            new(null, "Kořenová"),
            new(1.5m, "1.5 %"),
        };
        var cut = Render<TmSelect<decimal?>>(p => p
            .Add(c => c.Value, (decimal?)null)
            .Add(c => c.Options, options));

        cut.Find("select").GetAttribute("value").Should().BeEmpty();
        cut.FindAll("option")[0].HasAttribute("selected").Should().BeTrue();

        cut.Render(p => p.Add(c => c.Value, (decimal?)1.5m));

        cut.Find("select").GetAttribute("value").Should().Be("1.5");
        cut.FindAll("option")[0].HasAttribute("selected").Should().BeFalse();
        cut.FindAll("option")[1].HasAttribute("selected").Should().BeTrue();

        cut.Render(p => p.Add(c => c.Value, (decimal?)null));

        cut.Find("select").GetAttribute("value").Should().BeEmpty();
        cut.FindAll("option")[0].HasAttribute("selected").Should().BeTrue("selecting back to the null option must re-mark it selected");
    }

    [Fact]
    public void TmSelect_NullableDecimal_ChangeToEmpty_FiresValueChangedNull_Repeatedly()
    {
        var captured = new List<decimal?>();
        var options = new List<SelectOption<decimal?>>
        {
            new(null, "Kořenová"),
            new(1.5m, "1.5 %"),
        };
        var cut = Render<TmSelect<decimal?>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => captured.Add(v))));

        cut.Find("select").Change("1.5");
        cut.Find("select").Change("");
        cut.Find("select").Change("1.5");
        cut.Find("select").Change("");

        captured.Should().Equal(new decimal?[] { 1.5m, null, 1.5m, null },
            "switching to a real option and back to the empty option must round-trip repeatedly");
    }

    [Fact]
    public void TmSelect_NullableInt_NullOption_RendersAndSelectsEmpty()
    {
        var options = new List<SelectOption<int?>>
        {
            new(null, "Kořenová"),
            new(5, "Pět"),
        };
        int? captured = 5;
        var cut = Render<TmSelect<int?>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<int?>(this, v => captured = v)));

        var nullOption = cut.FindAll("option")[0];
        nullOption.GetAttribute("value").Should().BeEmpty();
        nullOption.HasAttribute("selected").Should().BeTrue();
        cut.Find("select").GetAttribute("value").Should().BeEmpty();

        cut.Find("select").Change("");
        captured.Should().BeNull();

        cut.Find("select").Change("5");
        captured.Should().Be(5);

        cut.Find("select").Change("");
        captured.Should().BeNull();
    }

    [Fact]
    public void TmSelect_NullableGuid_NullOption_RendersAndSelectsEmpty()
    {
        var sample = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000001");
        var options = new List<SelectOption<Guid?>>
        {
            new(null, "Kořenová"),
            new(sample, "Alpha"),
        };
        Guid? captured = sample;
        var cut = Render<TmSelect<Guid?>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<Guid?>(this, v => captured = v)));

        var nullOption = cut.FindAll("option")[0];
        nullOption.GetAttribute("value").Should().BeEmpty();
        nullOption.HasAttribute("selected").Should().BeTrue();
        cut.Find("select").GetAttribute("value").Should().BeEmpty();

        cut.Find("select").Change("");
        captured.Should().BeNull();

        cut.Find("select").Change(sample.ToString());
        captured.Should().Be(sample);
    }

    [Fact]
    public void TmSelect_NullableEnum_NullOption_RendersAndSelectsEmpty()
    {
        var options = new List<SelectOption<SampleStatus?>>
        {
            new(null, "Kořenová"),
            new(SampleStatus.Active, "Active"),
        };
        SampleStatus? captured = SampleStatus.Active;
        var cut = Render<TmSelect<SampleStatus?>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<SampleStatus?>(this, v => captured = v)));

        var nullOption = cut.FindAll("option")[0];
        nullOption.GetAttribute("value").Should().BeEmpty();
        nullOption.HasAttribute("selected").Should().BeTrue();
        cut.Find("select").GetAttribute("value").Should().BeEmpty();

        cut.Find("select").Change("");
        captured.Should().BeNull();

        cut.Find("select").Change("Active");
        captured.Should().Be(SampleStatus.Active);
    }

    [Fact]
    public void TmSelect_String_NullOption_RendersEmptyValueSelected_AndChangeEmptyYieldsNull()
    {
        // For TValue=string a null-valued option also renders value="" (like every nullable case);
        // an empty change event maps to null — the empty string and null share one wire value, and
        // the pre-existing parse contract ("" → default) is unchanged.
        var options = new List<SelectOption<string>>
        {
            new(null!, "Kořenová"),
            new("dbo", "dbo"),
        };
        string? captured = "dbo";
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => captured = v)));

        var nullOption = cut.FindAll("option")[0];
        nullOption.GetAttribute("value").Should().BeEmpty();
        nullOption.HasAttribute("selected").Should().BeTrue();

        cut.Find("select").Change("");
        captured.Should().BeNull();

        cut.Find("select").Change("dbo");
        captured.Should().Be("dbo");

        cut.Find("select").Change("");
        captured.Should().BeNull();
    }

    [Fact]
    public void TmSelect_String_EmptyStringValue_WithoutNullOption_SelectsEmptyOption()
    {
        // Empty string vs null: without a null-valued option in the list, the "" option is the only
        // value="" option and carries the selection; the <select> pins it with value="".
        var options = new List<SelectOption<string>>
        {
            new("", "Prázdný"),
            new("dbo", "dbo"),
        };
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Value, "")
            .Add(c => c.Options, options));

        cut.Find("select").GetAttribute("value").Should().BeEmpty();
        cut.FindAll("option")[0].HasAttribute("selected").Should().BeTrue();
        cut.FindAll("option")[1].HasAttribute("selected").Should().BeFalse();
    }

    [Fact]
    public void TmSelect_String_NullAndEmptyOptions_Collide_OnEmptyWireValue()
    {
        // A null-valued and an ""-valued string option both render value="" — HTML has a single
        // empty wire value, so the FIRST one is what the browser (and AngleSharp) resolves the
        // <select value=""> preselection to. Documented behaviour: don't mix null and "" options;
        // a change to "" parses back to null in either case.
        var options = new List<SelectOption<string>>
        {
            new(null!, "Kořenová"),
            new("", "Prázdný"),
            new("dbo", "dbo"),
        };
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Value, "")
            .Add(c => c.Options, options));

        cut.Find("select").GetAttribute("value").Should().BeEmpty();
        cut.FindAll("option")[0].HasAttribute("selected").Should().BeTrue("the first value=\"\" option is what value=\"\" resolves to");
        // The ""-option can also carry a selected marker (it matches Value by equality), but the
        // wire resolution of <select value=""> lands on the first value="" option — the null one.
    }

    // ── PlaceholderSelectable ────────────────────────────────────

    [Fact]
    public void TmSelect_PlaceholderSelectable_RendersPlaceholderEnabled_AndMapsEmptyToDefault()
    {
        int? captured = 3;
        var options = new List<SelectOption<int?>>
        {
            new(3, "Three"),
            new(4, "Four"),
        };
        var cut = Render<TmSelect<int?>>(p => p
            .Add(c => c.Placeholder, "Vyberte…")
            .Add(c => c.PlaceholderSelectable, true)
            .Add(c => c.Value, (int?)3)
            .Add(c => c.Options, options)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<int?>(this, v => captured = v)));

        var placeholder = cut.FindAll("option")[0];
        placeholder.TextContent.Trim().Should().Be("Vyberte…");
        placeholder.GetAttribute("value").Should().BeEmpty();
        placeholder.HasAttribute("disabled").Should().BeFalse("PlaceholderSelectable makes the placeholder pickable");

        cut.Find("select").Change("");
        captured.Should().BeNull("selecting the placeholder maps value=\"\" back to default(TValue)");

        cut.Find("select").Change("4");
        captured.Should().Be(4);
    }

    [Fact]
    public void TmSelect_PlaceholderSelectable_False_KeepsPlaceholderDisabled()
    {
        var options = new List<SelectOption<int?>> { new(3, "Three") };
        var cut = Render<TmSelect<int?>>(p => p
            .Add(c => c.Placeholder, "Vyberte…")
            .Add(c => c.Options, options));

        cut.FindAll("option")[0].HasAttribute("disabled").Should().BeTrue();
    }

    // ── DOM stability for existing usage (no null option, non-nullable, disabled placeholder) ──

    [Fact]
    public void TmSelect_String_ValueNull_DoesNotEmitSelectValueAttribute()
    {
        // Without a null-valued option in the list, TValue=string keeps the old no-attribute
        // behaviour — the value="" pin only appears when a null option exists (see the
        // null-option tests below).
        var options = new List<SelectOption<string>>
        {
            new("dbo", "dbo"),
            new("sales", "sales"),
        };
        var cut = Render<TmSelect<string>>(p => p.Add(c => c.Options, options));

        // string is not Nullable<T> and there is no null option: no <select value> attribute —
        // the option's selected marker carries the display.
        cut.Find("select").HasAttribute("value").Should().BeFalse();
    }

    [Fact]
    public void TmSelect_NonNullableInt_DefaultValue_DoesNotEmitSelectValueAttribute()
    {
        var options = new List<SelectOption<int>> { new(0, "Zero"), new(1, "One") };
        var cut = Render<TmSelect<int>>(p => p
            .Add(c => c.Value, 0)
            .Add(c => c.Options, options));

        cut.Find("select").HasAttribute("value").Should().BeFalse("non-nullable default keeps the no-attribute behaviour");
        cut.FindAll("option")[0].HasAttribute("selected").Should().BeTrue("int 0 still matches the 0-valued option");
    }

    [Fact]
    public void TmSelect_NullableWithoutEmptyOption_ValueNull_DoesNotEmitSelectValueAttribute()
    {
        // Without a placeholder and without a null-valued option there is nothing for value="" to
        // land on — emitting it would deselect everything (selectedIndex = -1). The old behaviour
        // (no attribute, browser shows the first option) must stay.
        var options = new List<SelectOption<int?>> { new(1, "One"), new(2, "Two") };
        var cut = Render<TmSelect<int?>>(p => p.Add(c => c.Options, options));

        cut.Find("select").HasAttribute("value").Should().BeFalse();
        cut.FindAll("option")[0].HasAttribute("selected").Should().BeFalse();
    }

    [Fact]
    public void TmSelect_DisabledPlaceholder_DefaultDom_Unchanged()
    {
        // Freezes the pre-existing placeholder markup: value="", disabled, selected when Value is default.
        var options = new List<SelectOption<string>> { new("a", "Alpha") };
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Placeholder, "Choose...")
            .Add(c => c.Options, options));

        var placeholder = cut.FindAll("option")[0];
        placeholder.GetAttribute("value").Should().BeEmpty();
        placeholder.HasAttribute("disabled").Should().BeTrue();
        placeholder.HasAttribute("selected").Should().BeTrue();
        cut.Find("select").HasAttribute("value").Should().BeFalse("TmSelect<string> keeps the no-value-attribute behaviour");
    }

    // ── TValue=string: a null-valued option pins the <select value=""> preselection ─────────

    [Fact]
    public void TmSelect_String_NullOption_ValueNull_PinsSelectValueEmpty_AndTogglesAcrossRerenders()
    {
        // For TValue=string the empty-option pin is driven by the presence of a null-valued option
        // (not by a nullable TValue): with Value null the <select> carries value="" so the
        // preselection lands on the null option, and the attribute toggles on later re-renders.
        var options = new List<SelectOption<string>>
        {
            new(null!, "Kořenová"),
            new("dbo", "dbo"),
        };
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Value, (string?)null)
            .Add(c => c.Options, options));

        cut.Find("select").GetAttribute("value").Should().BeEmpty("string + null option pins value=\"\" when Value is null");
        cut.FindAll("option")[0].HasAttribute("selected").Should().BeTrue();

        cut.Render(p => p.Add(c => c.Value, "dbo"));
        cut.Find("select").GetAttribute("value").Should().Be("dbo");

        cut.Render(p => p.Add(c => c.Value, (string?)null));
        cut.Find("select").GetAttribute("value").Should().BeEmpty("the pin must come back when Value returns to null");
    }

    // ── ChildContent: the pin also covers a nullable TValue with hand-written options ─────────

    [Fact]
    public void TmSelect_NullableInt_ChildContent_ValueNull_PinsSelectValueEmpty()
    {
        // With ChildContent the component cannot add selected markers to raw <option> elements —
        // for a nullable TValue the <select value=""> pin is what lands the preselection on the
        // hand-written empty option.
        var cut = Render<TmSelect<int?>>(p => p
            .Add(c => c.Value, (int?)null)
            .AddChildContent("<option value=\"\">Kořenová</option><option value=\"2\">Books</option>"));

        cut.Find("select").GetAttribute("value").Should().BeEmpty();
        cut.FindAll("option")[0].TextContent.Trim().Should().Be("Kořenová");
    }

    // ── Accessibility: a selectable placeholder is not an accessible name ─────────

    [Fact]
    public void TmSelect_PlaceholderSelectable_NoLabel_DoesNotUsePlaceholderAsAriaLabel()
    {
        // A selectable placeholder is a real empty choice, not a name for the control — it must
        // not double as the aria-label. Documented contract: set Label so the <select> keeps an
        // accessible name when PlaceholderSelectable is true.
        var cut = Render<TmSelect<int?>>(p => p
            .Add(x => x.Placeholder, "No priority")
            .Add(x => x.PlaceholderSelectable, true));

        cut.Find("select").GetAttribute("aria-label").Should().BeNull();
    }

    // ── Required + PlaceholderSelectable DOM freeze ─────────────

    [Fact]
    public void TmSelect_Required_PlaceholderSelectable_Dom_Freeze()
    {
        // Freezes the Required + PlaceholderSelectable DOM: the <select> keeps required +
        // aria-required while the placeholder stays an enabled <option value=""> — picking it
        // submits the empty value, so native validation counts the field as missing. That is the
        // documented intent (a required select must not silently accept the empty choice), not a
        // rendering defect; hosts that need a required-and-clearable select must validate client-side.
        var options = new List<SelectOption<int?>> { new(1, "Low"), new(2, "High") };
        var cut = Render<TmSelect<int?>>(p => p
            .Add(x => x.Label, "Priority")
            .Add(x => x.Required, true)
            .Add(x => x.Placeholder, "No priority")
            .Add(x => x.PlaceholderSelectable, true)
            .Add(x => x.Options, options));

        var select = cut.Find("select");
        select.HasAttribute("required").Should().BeTrue();
        select.GetAttribute("aria-required").Should().Be("true");

        var placeholder = cut.FindAll("option")[0];
        placeholder.TextContent.Trim().Should().Be("No priority");
        placeholder.GetAttribute("value").Should().BeEmpty();
        placeholder.HasAttribute("disabled").Should().BeFalse();
    }

    // ── Full-markup snapshots (fixed Id) ─────────

    private const string SnapshotArrowMarkup =
        "<div class=\"tm-select-arrow\" aria-hidden=\"true\"><svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" width=\"16\" height=\"16\"><polyline points=\"6 9 12 15 18 9\" /></svg></div>";

    [Fact]
    public void TmSelect_Markup_String_Placeholder_Options_ValueNull()
    {
        // string + Placeholder + Options + Value null: no <select value> attribute (no null option),
        // the disabled placeholder carries the selection.
        var options = new List<SelectOption<string>> { new("a", "Alpha"), new("b", "Beta") };
        var cut = Render<TmSelect<string>>(p => p
            .Add(x => x.Id, "tmsel-snap-1")
            .Add(x => x.Label, "Department")
            .Add(x => x.Placeholder, "Choose...")
            .Add(x => x.Options, options)
            .Add(x => x.Value, (string?)null));

        cut.MarkupMatches(
            @"<div class=""tm-select-wrapper "">
                <label for=""tmsel-snap-1"" class=""tm-select-label "">Department</label>
                <div class=""tm-select-container"">
                    <select id=""tmsel-snap-1"" class=""tm-select"">
                        <option value="""" disabled selected>Choose...</option>
                        <option value=""a"">Alpha</option>
                        <option value=""b"">Beta</option>
                    </select>
                    " + SnapshotArrowMarkup + @"
                </div>
            </div>");
    }

    [Fact]
    public void TmSelect_Markup_Int_Options_ValueZero()
    {
        // Non-nullable int with Value 0 (the default): no <select value> attribute, the 0-option
        // carries the selected marker.
        var options = new List<SelectOption<int>> { new(0, "Zero"), new(1, "One") };
        var cut = Render<TmSelect<int>>(p => p
            .Add(x => x.Id, "tmsel-snap-2")
            .Add(x => x.Label, "Level")
            .Add(x => x.Options, options)
            .Add(x => x.Value, 0));

        cut.MarkupMatches(
            @"<div class=""tm-select-wrapper "">
                <label for=""tmsel-snap-2"" class=""tm-select-label "">Level</label>
                <div class=""tm-select-container"">
                    <select id=""tmsel-snap-2"" class=""tm-select"">
                        <option value=""0"" selected>Zero</option>
                        <option value=""1"">One</option>
                    </select>
                    " + SnapshotArrowMarkup + @"
                </div>
            </div>");
    }

    [Fact]
    public void TmSelect_Markup_String_ValueB()
    {
        // Plain string selection: the <select value="b"> pin carries the preselection.
        var options = new List<SelectOption<string>> { new("a", "Alpha"), new("b", "Beta") };
        var cut = Render<TmSelect<string>>(p => p
            .Add(x => x.Id, "tmsel-snap-3")
            .Add(x => x.Options, options)
            .Add(x => x.Value, "b"));

        cut.MarkupMatches(
            @"<div class=""tm-select-wrapper "">
                <div class=""tm-select-container"">
                    <select id=""tmsel-snap-3"" value=""b"" class=""tm-select"">
                        <option value=""a"">Alpha</option>
                        <option value=""b"" selected>Beta</option>
                    </select>
                    " + SnapshotArrowMarkup + @"
                </div>
            </div>");
    }

    [Fact]
    public void TmSelect_Markup_NullableInt_Placeholder_ValueNull()
    {
        // int? + Placeholder + Value null: the <select> pins value="" and the disabled placeholder
        // carries the selection.
        var options = new List<SelectOption<int?>> { new(1, "Low"), new(2, "High") };
        var cut = Render<TmSelect<int?>>(p => p
            .Add(x => x.Id, "tmsel-snap-4")
            .Add(x => x.Label, "Priority")
            .Add(x => x.Placeholder, "No priority")
            .Add(x => x.Options, options)
            .Add(x => x.Value, (int?)null));

        cut.MarkupMatches(
            @"<div class=""tm-select-wrapper "">
                <label for=""tmsel-snap-4"" class=""tm-select-label "">Priority</label>
                <div class=""tm-select-container"">
                    <select id=""tmsel-snap-4"" value="""" class=""tm-select"">
                        <option value="""" disabled selected>No priority</option>
                        <option value=""1"">Low</option>
                        <option value=""2"">High</option>
                    </select>
                    " + SnapshotArrowMarkup + @"
                </div>
            </div>");

        cut.Find("select").GetAttribute("value").Should().BeEmpty();
        var placeholder = cut.FindAll("option")[0];
        placeholder.HasAttribute("disabled").Should().BeTrue();
        placeholder.HasAttribute("selected").Should().BeTrue();
    }

    [Fact]
    public void TmSelect_Markup_NullableInt_Placeholder_And_NullOption_ValueNull()
    {
        // Nullable select with BOTH a non-selectable placeholder and a null-valued option, Value
        // null: value="" pins the preselection and both empty-valued options carry the selected
        // marker (they match Value by equality); the wire resolution lands on the first one.
        var options = new List<SelectOption<int?>> { new(null, "Kořenová"), new(5, "Pět") };
        var cut = Render<TmSelect<int?>>(p => p
            .Add(x => x.Id, "tmsel-snap-5")
            .Add(x => x.Label, "Parent")
            .Add(x => x.Placeholder, "Choose...")
            .Add(x => x.Options, options)
            .Add(x => x.Value, (int?)null));

        cut.MarkupMatches(
            @"<div class=""tm-select-wrapper "">
                <label for=""tmsel-snap-5"" class=""tm-select-label "">Parent</label>
                <div class=""tm-select-container"">
                    <select id=""tmsel-snap-5"" value="""" class=""tm-select"">
                        <option value="""" disabled selected>Choose...</option>
                        <option value="""" selected>Kořenová</option>
                        <option value=""5"">Pět</option>
                    </select>
                    " + SnapshotArrowMarkup + @"
                </div>
            </div>");

        cut.Find("select").GetAttribute("value").Should().BeEmpty();
        cut.FindAll("option")[0].HasAttribute("disabled").Should().BeTrue();
        cut.FindAll("option")[0].HasAttribute("selected").Should().BeTrue();
        cut.FindAll("option")[1].HasAttribute("selected").Should().BeTrue();
    }
}
