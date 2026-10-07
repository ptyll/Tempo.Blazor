using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// Escape / overlay-close behaviour for TmDialog, now expressed as explicit parameters aligned with
/// TmModal and TmDrawer. Defaults preserve the historical behaviour: Escape cancels; a backdrop click
/// does not dismiss a decision prompt.
/// </summary>
public class TmDialogInteractionTests : LocalizationTestBase
{
    [Fact]
    public void Dialog_Escape_Cancels_ByDefault()
    {
        bool? result = null;
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.OnResult, EventCallback.Factory.Create<bool?>(this, v => result = v)));

        // Escape is delivered by the focus-trap module at the document level; the module calls back
        // into the scope's JSInvokable, which the dialog wires to its cancel.
        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        result.Should().BeFalse();
    }

    [Fact]
    public void Dialog_Escape_Ignored_WhenCloseOnEscapeFalse()
    {
        bool invoked = false;
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.CloseOnEscape, false)
            .Add(d => d.OnResult, EventCallback.Factory.Create<bool?>(this, _ => invoked = true)));

        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        invoked.Should().BeFalse();
    }

    [Fact]
    public void Dialog_OverlayClick_DoesNotClose_ByDefault()
    {
        bool invoked = false;
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.OnResult, EventCallback.Factory.Create<bool?>(this, _ => invoked = true)));

        cut.Find(".tm-modal-overlay").Click();

        invoked.Should().BeFalse("a decision prompt must not be dismissed by an accidental backdrop click");
    }

    [Fact]
    public void Dialog_OverlayClick_Cancels_WhenCloseOnOverlayClickTrue()
    {
        bool? result = null;
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.CloseOnOverlayClick, true)
            .Add(d => d.OnResult, EventCallback.Factory.Create<bool?>(this, v => result = v)));

        cut.Find(".tm-modal-overlay").Click();

        result.Should().BeFalse();
    }
}
