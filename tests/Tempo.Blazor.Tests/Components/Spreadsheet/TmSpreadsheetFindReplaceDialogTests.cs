using Bunit;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Dialogs;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetFindReplaceDialogTests : LocalizationTestBase
{
    [Fact]
    public void Render_ShowsFieldsOptionsAndButtons()
    {
        var cut = Render<TmSpreadsheetFindReplaceDialog>();

        cut.FindAll(".tm-spreadsheet-find__query").Count.Should().Be(1);
        cut.FindAll(".tm-spreadsheet-find__replace").Count.Should().Be(1);
        cut.FindAll(".tm-spreadsheet-find__option").Count.Should().Be(4);

        var text = cut.Find(".tm-spreadsheet-find").TextContent;
        text.Should().Contain("Find and replace");
        text.Should().Contain("Match case");
        text.Should().Contain("Match entire cell");
        text.Should().Contain("Search in formulas");
        text.Should().Contain("Replace");
        text.Should().Contain("Replace all");
    }

    [Fact]
    public void QueryInput_RaisesSearchWithQuery()
    {
        SpreadsheetSearchOptions? received = null;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.OnSearchRequested, EventCallback.Factory.Create<SpreadsheetSearchOptions>(this, o => received = o)));

        cut.Find(".tm-spreadsheet-find__query").Input("hello");

        received.Should().NotBeNull();
        received!.Query.Should().Be("hello");
    }

    [Fact]
    public void MatchCaseToggle_RaisesSearchWithOption()
    {
        SpreadsheetSearchOptions? received = null;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.OnSearchRequested, EventCallback.Factory.Create<SpreadsheetSearchOptions>(this, o => received = o)));

        var matchCase = cut.FindAll(".tm-spreadsheet-find__option input")[0];
        matchCase.Change(true);

        received.Should().NotBeNull();
        received!.MatchCase.Should().BeTrue();
    }

    [Fact]
    public void InFormulasToggle_SetsSearchInFormulas()
    {
        SpreadsheetSearchOptions? received = null;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.OnSearchRequested, EventCallback.Factory.Create<SpreadsheetSearchOptions>(this, o => received = o)));

        cut.FindAll(".tm-spreadsheet-find__option input")[2].Change(true);

        received!.SearchIn.Should().Be(SpreadsheetSearchIn.Formulas);
    }

    [Fact]
    public void FindNextButton_FiresOnFindNext()
    {
        var fired = false;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.OnFindNext, EventCallback.Factory.Create(this, () => fired = true)));

        // First action button is Find previous, second is Find next.
        cut.FindAll(".tm-spreadsheet-find__actions .tm-spreadsheet-find__btn")[1].Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void ReplaceButton_FiresWithReplacementText()
    {
        string? received = null;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.OnReplaceRequested, EventCallback.Factory.Create<string>(this, s => received = s)));

        cut.Find(".tm-spreadsheet-find__replace").Input("world");
        cut.FindAll(".tm-spreadsheet-find__btn--text")[0].Click();

        received.Should().Be("world");
    }

    [Fact]
    public void ReplaceAllButton_FiresWithReplacementText()
    {
        string? received = null;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.OnReplaceAllRequested, EventCallback.Factory.Create<string>(this, s => received = s)));

        cut.Find(".tm-spreadsheet-find__replace").Input("xyz");
        cut.FindAll(".tm-spreadsheet-find__btn--text")[1].Click();

        received.Should().Be("xyz");
    }

    [Fact]
    public void CloseButton_FiresOnClose()
    {
        var fired = false;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => fired = true)));

        cut.Find(".tm-spreadsheet-find__close").Click();

        fired.Should().BeTrue();
    }

    // ── Keyboard activation ─────────────────────────────────────────────────
    // Enter/F3 are scoped to the text inputs; the action controls are native
    // <button>s whose bubbled keydown must not also trigger find-next.

    [Fact]
    public void EnterOnReplaceAllButton_FiresOnce_WithoutFindNext()
    {
        // Real sequence for Enter on a focused <button>: keydown -> click.
        var findNexts = 0;
        var replaceAlls = new List<string>();
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.OnFindNext, EventCallback.Factory.Create(this, () => findNexts++))
            .Add(x => x.OnReplaceAllRequested, EventCallback.Factory.Create<string>(this, s => replaceAlls.Add(s))));

        cut.Find(".tm-spreadsheet-find__replace").Input("xyz");
        var button = cut.FindAll(".tm-spreadsheet-find__btn--text")[1];
        button.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-spreadsheet-find__btn--text")[1].Click();

        replaceAlls.Should().Equal("xyz");
        findNexts.Should().Be(0);
    }

    [Fact]
    public void EnterInQueryInput_StillFindsNext()
    {
        var findNexts = 0;
        var findPreviouses = 0;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.OnFindNext, EventCallback.Factory.Create(this, () => findNexts++))
            .Add(x => x.OnFindPrevious, EventCallback.Factory.Create(this, () => findPreviouses++)));

        var query = cut.Find(".tm-spreadsheet-find__query");
        query.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        query.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", ShiftKey = true });
        query.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "F3" });

        findNexts.Should().Be(2);
        findPreviouses.Should().Be(1);
    }

    [Fact]
    public void EscapeOnCloseButton_ClosesOnce()
    {
        var closes = 0;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closes++)));

        cut.Find(".tm-spreadsheet-find__close")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        closes.Should().Be(1);
    }

    [Fact]
    public void Counter_ShowsMatchPosition()
    {
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.MatchIndex, 3)
            .Add(x => x.MatchCount, 12));

        cut.Find(".tm-spreadsheet-find__counter").TextContent.Should().Contain("3 of 12");
    }

    [Fact]
    public void Counter_ShowsNoMatches_WhenQueryHasNoHits()
    {
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(x => x.MatchCount, 0));

        cut.Find(".tm-spreadsheet-find__query").Input("missing");

        cut.Find(".tm-spreadsheet-find__counter").TextContent.Should().Contain("No matches");
    }
}
