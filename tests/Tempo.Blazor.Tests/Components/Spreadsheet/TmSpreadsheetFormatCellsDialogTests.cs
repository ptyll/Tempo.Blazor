using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetFormatCellsDialogTests : LocalizationTestBase
{
    // ── Keyboard activation ─────────────────────────────────────────────────
    // Ctrl+Enter -> Apply lives on the dialog root; the tab bar and the footer
    // actions isolate their keydowns so the browser's native keydown -> click
    // on a <button> can't also trigger the root shortcut.

    private IRenderedComponent<TmSpreadsheetFormatCellsDialog> RenderDialog(
        Action<SpreadsheetCellStyle>? onApply = null,
        Action? onClose = null)
        => Render<TmSpreadsheetFormatCellsDialog>(p => p
            .Add(c => c.Style, new SpreadsheetCellStyle())
            .Add(c => c.OnApply,
                EventCallback.Factory.Create<SpreadsheetCellStyle>(this, s => onApply?.Invoke(s)))
            .Add(c => c.OnClose,
                EventCallback.Factory.Create(this, () => onClose?.Invoke())));

    [Fact]
    public void CtrlEnter_OnApplyButton_AppliesExactlyOnce()
    {
        var applies = 0;
        var cut = RenderDialog(onApply: _ => applies++);

        // .tm-fcd__actions buttons: [0] = Cancel, [1] = Apply
        var apply = cut.FindAll(".tm-fcd__actions button")[1];
        apply.KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        cut.FindAll(".tm-fcd__actions button")[1].Click();

        applies.Should().Be(1);
    }

    [Fact]
    public void Enter_OnCancelButton_Closes_WithoutApplying()
    {
        var applies = 0;
        var closes = 0;
        var cut = RenderDialog(onApply: _ => applies++, onClose: () => closes++);

        var cancel = cut.FindAll(".tm-fcd__actions button")[0];
        cancel.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-fcd__actions button")[0].Click();

        closes.Should().Be(1);
        applies.Should().Be(0);
    }

    [Fact]
    public void Escape_OnApplyButton_ClosesOnce()
    {
        var applies = 0;
        var closes = 0;
        var cut = RenderDialog(onApply: _ => applies++, onClose: () => closes++);

        // Escape stays usable from the footer: the actions group handles it
        // locally and stops it from also reaching the root's Escape case.
        cut.FindAll(".tm-fcd__actions button")[1]
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });

        closes.Should().Be(1);
        applies.Should().Be(0);
    }

    [Fact]
    public void CtrlEnter_OnTab_SwitchesTab_WithoutApplying()
    {
        var applies = 0;
        var cut = RenderDialog(onApply: _ => applies++);

        var fontTab = cut.FindAll(".tm-fcd__tab").First(e => e.TextContent.Contains("Font"));
        fontTab.KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        cut.FindAll(".tm-fcd__tab").First(e => e.TextContent.Contains("Font")).Click();

        applies.Should().Be(0);
        cut.FindAll(".tm-fcd__tab").First(e => e.TextContent.Contains("Font"))
            .ClassList.Should().Contain("tm-fcd__tab--active");
    }

    [Fact]
    public void CtrlEnter_InBodyInput_StillApplies()
    {
        var applies = 0;
        var cut = RenderDialog(onApply: _ => applies++);

        // Switch the Number tab to a category that renders an input field.
        cut.FindAll(".tm-fcd__listbox-item").First(e => e.TextContent.Contains("Number")).Click();
        var input = cut.Find(".tm-fcd__body input");

        input.KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        applies.Should().Be(1);
    }
}
