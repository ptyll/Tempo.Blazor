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

    /// <summary>
    /// Runs <paramref name="action"/> with <see cref="CultureInfo.DefaultThreadCurrentCulture"/> pinned
    /// to <paramref name="culture"/>, restoring the previous default afterwards. Uses the DEFAULT thread
    /// culture (not <see cref="CultureInfo.CurrentCulture"/> directly) per repo convention for culture-sensitive
    /// tests, and always restores in a finally so a failing assertion cannot leak culture into later tests.
    /// </summary>
    private static void UnderCulture(CultureInfo culture, Action action)
    {
        var previous = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        try
        {
            action();
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = previous;
        }
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

    [Fact]
    public void TmSelect_Long_RoundTrips_UnderCzechCulture()
    {
        UnderCulture(CultureInfo.GetCultureInfo("cs-CZ"), () =>
        {
            long captured = 0;
            var options = new List<SelectOption<long>>
            {
                new(1_234_567_890_123L, "Big"),
            };
            var cut = Render<TmSelect<long>>(p => p
                .Add(c => c.Options, options)
                .Add(c => c.ValueChanged, EventCallback.Factory.Create<long>(this, v => captured = v)));

            var rendered = cut.Find("option");
            rendered.GetAttribute("value").Should().Be("1234567890123");

            cut.Find("select").Change("1234567890123");

            captured.Should().Be(1_234_567_890_123L);
        });
    }

    [Fact]
    public void TmSelect_Byte_RoundTrips_UnderCzechCulture()
    {
        UnderCulture(CultureInfo.GetCultureInfo("cs-CZ"), () =>
        {
            byte captured = 0;
            var options = new List<SelectOption<byte>>
            {
                new((byte)200, "TwoHundred"),
            };
            var cut = Render<TmSelect<byte>>(p => p
                .Add(c => c.Options, options)
                .Add(c => c.ValueChanged, EventCallback.Factory.Create<byte>(this, v => captured = v)));

            var rendered = cut.Find("option");
            rendered.GetAttribute("value").Should().Be("200");

            cut.Find("select").Change("200");

            captured.Should().Be((byte)200);
        });
    }

    [Fact]
    public void TmSelect_Decimal_RoundTrips_UnderCzechCulture_WithoutCommaCorruption()
    {
        UnderCulture(CultureInfo.GetCultureInfo("cs-CZ"), () =>
        {
            decimal captured = 0m;
            var options = new List<SelectOption<decimal>>
            {
                new(1.5m, "OneHalf"),
            };
            var cut = Render<TmSelect<decimal>>(p => p
                .Add(c => c.Options, options)
                .Add(c => c.ValueChanged, EventCallback.Factory.Create<decimal>(this, v => captured = v)));

            // Rendered option value must stay invariant ("1.5"), never the Czech decimal comma ("1,5"),
            // otherwise the browser <select> would carry a value the parser below cannot read back.
            var rendered = cut.Find("option");
            rendered.GetAttribute("value").Should().Be("1.5");

            cut.Find("select").Change("1.5");

            captured.Should().Be(1.5m);
        });
    }

    [Fact]
    public void TmSelect_Double_RoundTrips_UnderCzechCulture_WithoutCommaCorruption()
    {
        UnderCulture(CultureInfo.GetCultureInfo("cs-CZ"), () =>
        {
            double captured = 0d;
            var options = new List<SelectOption<double>>
            {
                new(1.5d, "OneHalf"),
            };
            var cut = Render<TmSelect<double>>(p => p
                .Add(c => c.Options, options)
                .Add(c => c.ValueChanged, EventCallback.Factory.Create<double>(this, v => captured = v)));

            var rendered = cut.Find("option");
            rendered.GetAttribute("value").Should().Be("1.5");

            cut.Find("select").Change("1.5");

            captured.Should().Be(1.5d);
        });
    }
}
