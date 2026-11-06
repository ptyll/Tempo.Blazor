using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>Phase 3: the shared shell-panel header. One close control, an h2 title, Escape closes.</summary>
public sealed class TmNotionPanelHeaderTests : LocalizationTestBase
{
    [Fact]
    public void Close_RaisesOnClosed()
    {
        var closed = 0;
        var cut = Render<TmNotionPanelHeader>(p => p
            .Add(c => c.Title, "Blog")
            .Add(c => c.OnClosed, EventCallback.Factory.Create(this, () => closed++)));

        cut.Find("h2").TextContent.Should().Be("Blog");
        cut.Find(".tm-notion-panel-header__close").Click();

        closed.Should().Be(1);
    }

    [Fact]
    public async Task Escape_RaisesOnClosed()
    {
        var closed = 0;
        var cut = Render<TmNotionPanelHeader>(p => p
            .Add(c => c.Title, "Audit")
            .Add(c => c.OnClosed, EventCallback.Factory.Create(this, () => closed++)));

        await cut.Find(".tm-notion-panel-header").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        closed.Should().Be(1);
    }

    [Fact]
    public void Open_MovesFocusToTheTitle_OnlyOnce()
    {
        // The docked shell panel closes on Escape only while focus is inside it; the toolbar
        // trigger keeps focus outside unless the panel takes it on open.
        var cut = Render<TmNotionPanelHeader>(p => p.Add(c => c.Title, "Analytics"));

        var focus = JSInterop.VerifyFocusAsyncInvoke();
        focus.Arguments[0].Should().BeOfType<ElementReference>();

        cut.Render(p => p.Add(c => c.Subtitle, "3 views"));
        JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus").Should().Be(1);
    }

    [Fact]
    public void FocusOnOpen_False_LeavesFocusAlone()
    {
        Render<TmNotionPanelHeader>(p => p
            .Add(c => c.Title, "Analytics")
            .Add(c => c.FocusOnOpen, false));

        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "Blazor._internal.domWrapper.focus");
    }
}
