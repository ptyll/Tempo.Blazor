using Bunit;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetDataValidationDialogTests : LocalizationTestBase
{
    // ── Rendering ─────────────────────────────────────────────────────────────

    [Fact]
    public void Render_DefaultsToSettingsTab()
    {
        var cut = Render<TmSpreadsheetDataValidationDialog>();

        cut.Find(".tm-dvd__tab--active").TextContent.Trim().Should().Be("Settings");
        cut.Find(".tm-dvd__select select").Should().NotBeNull("type select should be visible on Settings tab");
    }

    [Fact]
    public void Render_ShowsThreeTabs()
    {
        var cut = Render<TmSpreadsheetDataValidationDialog>();

        var tabs = cut.FindAll(".tm-dvd__tab");
        tabs.Should().HaveCount(3);
        tabs[0].TextContent.Trim().Should().Be("Settings");
        tabs[1].TextContent.Trim().Should().Be("Input message");
        tabs[2].TextContent.Trim().Should().Be("Error alert");
    }

    [Fact]
    public void NewRule_DefaultsTheErrorAlertOn_SoTheWarningStyleSurvives()
    {
        // A new rule used to seed "Show error alert" OFF, so a user who picked the Warning style got a
        // rule with no ErrorAlert — which the engine treats as Stop. The severity silently vanished.
        SpreadsheetDataValidation? applied = null;
        var cut = Render<TmSpreadsheetDataValidationDialog>(p => p
            .Add(c => c.Validation, new SpreadsheetDataValidation())
            .Add(c => c.OnSave, EventCallback.Factory.Create<SpreadsheetDataValidation>(this, v => applied = v)));

        // Settings: a whole-number rule between 1 and 10 (fills the values the rule needs).
        cut.Find(".tm-dvd__select select").Change("Whole");
        cut.FindAll(".tm-dvd__input")[0].Change("1");
        cut.FindAll(".tm-dvd__input")[1].Change("10");

        // Error alert: the severity select (the only select on that tab) picks Warning.
        cut.FindAll(".tm-dvd__tab")[2].Click();
        cut.Find(".tm-dvd__form .tm-dvd__select select").Change("Warning");
        cut.FindAll(".tm-dvd__actions button").Last().Click();

        applied.Should().NotBeNull();
        applied!.ErrorAlert.Should().NotBeNull("a new rule keeps the error alert on by default");
        applied.ErrorAlert!.Style.Should().Be(SpreadsheetValidationErrorStyle.Warning,
            "the picked severity must reach the rule, not fall back to Stop");
    }

    // ── Tab navigation ────────────────────────────────────────────────────────

    [Fact]
    public void ClickTab_InputMessage_ShowsInputFields()
    {
        var cut = Render<TmSpreadsheetDataValidationDialog>();

        cut.FindAll(".tm-dvd__tab")[1].Click();

        cut.Find(".tm-dvd__tab--active").TextContent.Trim().Should().Be("Input message");
        cut.Markup.Should().Contain("Show input message when cell is selected");
    }

    [Fact]
    public void ClickTab_ErrorAlert_ShowsErrorFields()
    {
        var cut = Render<TmSpreadsheetDataValidationDialog>();

        cut.FindAll(".tm-dvd__tab")[2].Click();

        cut.Find(".tm-dvd__tab--active").TextContent.Trim().Should().Be("Error alert");
        cut.Markup.Should().Contain("Show error alert after invalid data is entered");
    }

    // ── Operator select visibility ────────────────────────────────────────────

    [Fact]
    public void Settings_AnyType_HidesOperatorSelect()
    {
        var cut = Render<TmSpreadsheetDataValidationDialog>();

        // Default is "Any" — operator select should not be shown
        // Only the type select is rendered; no second <select>
        cut.FindAll(".tm-dvd__select select").Should().HaveCount(1);
    }

    [Fact]
    public void Settings_WholeType_ShowsOperatorSelect()
    {
        var cut = Render<TmSpreadsheetDataValidationDialog>();

        // Change type to Whole
        cut.Find(".tm-dvd__select select").Change("Whole");

        cut.FindAll(".tm-dvd__select select").Should().HaveCount(2, "Whole type should show operator select");
    }

    [Fact]
    public void Settings_ListType_HidesOperatorSelect()
    {
        var cut = Render<TmSpreadsheetDataValidationDialog>();

        cut.Find(".tm-dvd__select select").Change("List");

        // List type: no operator select, but source input appears
        cut.FindAll(".tm-dvd__select select").Should().HaveCount(1, "List type hides operator select");
        cut.Markup.Should().Contain("Source");
    }

    [Fact]
    public void Settings_ListType_ShowsDropDownCheckbox()
    {
        var cut = Render<TmSpreadsheetDataValidationDialog>();

        cut.Find(".tm-dvd__select select").Change("List");

        var checkboxes = cut.FindAll(".tm-dvd__checkbox");
        checkboxes.Any(el => el.TextContent.Contains("In-cell dropdown")).Should().BeTrue();
    }

    // ── Initialization from Validation parameter ──────────────────────────────

    [Fact]
    public void InitWithValidation_PopulatesTypeAndFormulas()
    {
        var dv = new SpreadsheetDataValidation
        {
            Range = new SpreadsheetRange(0, 0, 2, 0),
            Type = SpreadsheetValidationType.Whole,
            Operator = SpreadsheetValidationOperator.Between,
            Formula1 = "1",
            Formula2 = "100",
            AllowBlank = false
        };

        var cut = Render<TmSpreadsheetDataValidationDialog>(p => p
            .Add(c => c.Validation, dv));

        // Type select should show "Whole number"
        cut.Find(".tm-dvd__select select").InnerHtml.Should().Contain("Whole");

        // Formula inputs should be pre-filled
        var inputs = cut.FindAll(".tm-dvd__input");
        inputs[0].GetAttribute("value").Should().Be("1");  // Formula1
        inputs[1].GetAttribute("value").Should().Be("100"); // Formula2
    }

    [Fact]
    public void InitWithValidation_PopulatesInputMessageTab()
    {
        var dv = new SpreadsheetDataValidation
        {
            Range = new SpreadsheetRange(0, 0, 0, 0),
            Type = SpreadsheetValidationType.Any,
            InputMessage = new SpreadsheetInputMessage { Title = "Tip", Message = "Enter a number." }
        };

        var cut = Render<TmSpreadsheetDataValidationDialog>(p => p
            .Add(c => c.Validation, dv));

        cut.FindAll(".tm-dvd__tab")[1].Click();

        var inputs = cut.FindAll(".tm-dvd__input");
        inputs[0].GetAttribute("value").Should().Be("Tip");
        cut.Find(".tm-dvd__textarea").GetAttribute("value")?.Should().Contain("Enter a number.");
    }

    // ── Apply / Cancel ────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_EmitsValidationWithSelectedType()
    {
        SpreadsheetDataValidation? saved = null;
        var cut = Render<TmSpreadsheetDataValidationDialog>(p => p
            .Add(c => c.Validation, new SpreadsheetDataValidation { Range = new SpreadsheetRange(0, 0, 0, 0) })
            .Add(c => c.OnSave, EventCallback.Factory.Create<SpreadsheetDataValidation>(this, dv => saved = dv)));

        // Change type to List
        cut.Find(".tm-dvd__select select").Change("List");

        await cut.InvokeAsync(() => cut.FindAll(".tm-dvd__actions button").Last().Click());

        saved.Should().NotBeNull();
        saved!.Type.Should().Be(SpreadsheetValidationType.List);
    }

    [Fact]
    public async Task Apply_WholeType_EmitsFormulas()
    {
        SpreadsheetDataValidation? saved = null;
        var cut = Render<TmSpreadsheetDataValidationDialog>(p => p
            .Add(c => c.Validation, new SpreadsheetDataValidation { Range = new SpreadsheetRange(0, 0, 0, 0) })
            .Add(c => c.OnSave, EventCallback.Factory.Create<SpreadsheetDataValidation>(this, dv => saved = dv)));

        cut.Find(".tm-dvd__select select").Change("Whole");

        // Re-find inputs after type re-render to avoid stale element references
        await cut.InvokeAsync(() => cut.FindAll(".tm-dvd__input")[0].Change("5"));
        await cut.InvokeAsync(() => cut.FindAll(".tm-dvd__input")[1].Change("20"));
        await cut.InvokeAsync(() => cut.FindAll(".tm-dvd__actions button").Last().Click());

        saved!.Formula1.Should().Be("5");
        saved.Formula2.Should().Be("20");
    }

    [Fact]
    public async Task Cancel_InvokesOnClose()
    {
        var closed = false;
        var cut = Render<TmSpreadsheetDataValidationDialog>(p => p
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        await cut.InvokeAsync(() => cut.FindAll(".tm-dvd__actions button")[0].Click());

        closed.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_EmitsErrorAlert_WhenShowErrorAlertChecked()
    {
        SpreadsheetDataValidation? saved = null;
        // No Validation param — defaults leave _showErrorAlert = true
        var cut = Render<TmSpreadsheetDataValidationDialog>(p => p
            .Add(c => c.OnSave, EventCallback.Factory.Create<SpreadsheetDataValidation>(this, dv => saved = dv)));

        cut.FindAll(".tm-dvd__tab")[2].Click();

        // ShowErrorAlert checkbox is ticked by default — fill in title and message
        // Re-find after tab switch to avoid stale element references
        await cut.InvokeAsync(() => cut.FindAll(".tm-dvd__input")[0].Change("Oops"));
        await cut.InvokeAsync(() => cut.Find(".tm-dvd__textarea").Change("Wrong value!"));
        await cut.InvokeAsync(() => cut.FindAll(".tm-dvd__actions button").Last().Click());

        saved!.ErrorAlert.Should().NotBeNull();
        saved.ErrorAlert!.Title.Should().Be("Oops");
        saved.ErrorAlert.Message.Should().Be("Wrong value!");
    }

    [Fact]
    public async Task Apply_NoErrorAlert_WhenShowErrorAlertUnchecked()
    {
        SpreadsheetDataValidation? saved = null;
        var cut = Render<TmSpreadsheetDataValidationDialog>(p => p
            .Add(c => c.Validation, new SpreadsheetDataValidation { Range = new SpreadsheetRange(0, 0, 0, 0) })
            .Add(c => c.OnSave, EventCallback.Factory.Create<SpreadsheetDataValidation>(this, dv => saved = dv)));

        cut.FindAll(".tm-dvd__tab")[2].Click();
        await cut.InvokeAsync(() => cut.Find(".tm-dvd__checks input").Change(false));
        await cut.InvokeAsync(() => cut.FindAll(".tm-dvd__actions button").Last().Click());

        saved!.ErrorAlert.Should().BeNull();
    }
}
