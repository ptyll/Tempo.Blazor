using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.NotionEditor.Blocks.TempoBlocks;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>N12: the shared Tempo edit-modal shell — header, footer order, dirty discard, missing package.</summary>
public sealed class TmNotionTempoEditorModalTests : LocalizationTestBase
{
    [Fact]
    public void Shell_RendersTitleClose_AndDiscardBeforeSave()
    {
        var cut = RenderShell();

        cut.Find(".tm-modal-title").TextContent.Should().Be("Diagram editor");
        cut.Find(".tm-modal-close").Should().NotBeNull();

        var footer = cut.Find("[data-testid='notion-tempo-modal-footer']").TextContent;
        footer.IndexOf("Discard", StringComparison.Ordinal)
            .Should().BeLessThan(footer.IndexOf("Save", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DirtyDiscard_ConfirmsBeforeClosing()
    {
        var discarded = 0;
        var cut = RenderShell(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnDiscard, EventCallback.Factory.Create(this, () => discarded++)));

        await cut.Find("[data-testid='notion-tempo-modal-discard']").ClickAsync(new MouseEventArgs());

        discarded.Should().Be(0);
        cut.Find("[data-testid='notion-tempo-modal-discard-confirm']").Should().NotBeNull();

        await cut.Find(".tm-dialog-btn-ok").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => discarded.Should().Be(1));
    }

    [Fact]
    public async Task DirtyClose_ConfirmsBeforeDiscarding()
    {
        // Escape is the core TmModal's own handler and takes this same OnClose path — a keydown in
        // bUnit never reaches it, so the header close stands in for it.
        var discarded = 0;
        var cut = RenderShell(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnDiscard, EventCallback.Factory.Create(this, () => discarded++)));

        await cut.Find(".tm-modal-close").ClickAsync(new MouseEventArgs());

        discarded.Should().Be(0, "a dirty close asks first");
        cut.Find("[data-testid='notion-tempo-modal-discard-confirm']").Should().NotBeNull();
    }

    [Fact]
    public async Task CleanDiscard_ClosesWithoutConfirm()
    {
        var discarded = false;
        var cut = RenderShell(p => p
            .Add(x => x.OnDiscard, EventCallback.Factory.Create(this, () => discarded = true)));

        await cut.Find("[data-testid='notion-tempo-modal-discard']").ClickAsync(new MouseEventArgs());

        discarded.Should().BeTrue();
        cut.FindAll("[data-testid='notion-tempo-modal-discard-confirm']").Should().BeEmpty();
    }

    [Fact]
    public async Task Desktop_IsXLarge_AndMaximizeTogglesFullscreen()
    {
        var cut = RenderShell(p => p.Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.Find(".tm-modal").ClassList.Should().Contain("tm-modal--xl");
        cut.Find("[data-testid='notion-tempo-modal-maximize']")
            .GetAttribute("aria-label").Should().Be("Maximize");

        await cut.Find("[data-testid='notion-tempo-modal-maximize']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-modal").ClassList.Should().Contain("tm-modal--fullscreen");
        cut.Find(".tm-modal-overlay").GetAttribute("data-maximized").Should().Be("true");
    }

    [Fact]
    public void Below1024_IsCompact_AndHasNoMaximize()
    {
        var cut = RenderShell(p => p.Add(x => x.LayoutMode, TmLayoutMode.Mobile));

        cut.Find(".tm-modal-overlay").GetAttribute("data-editor-layout").Should().Be("mobile");
        cut.FindAll("[data-testid='notion-tempo-modal-maximize']").Should().BeEmpty();
    }

    [Fact]
    public void MissingPackage_DisablesSave()
    {
        var cut = RenderShell(p => p.Add(x => x.Missing, true).Add(x => x.MissingText, "missing"));

        cut.Find("[data-testid='notion-tempo-modal-save']").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-notion-tempo-modal__missing").TextContent.Should().Contain("missing");
    }

    private IRenderedComponent<TmNotionTempoEditorModal> RenderShell(
        Action<ComponentParameterCollectionBuilder<TmNotionTempoEditorModal>>? configure = null)
        => Render<TmNotionTempoEditorModal>(p =>
        {
            p.Add(x => x.Title, "Diagram editor");
            p.Add(x => x.DiscardText, "Discard");
            p.Add(x => x.SaveText, "Save and close");
            p.Add(x => x.ConfirmTitle, "Discard changes?");
            p.Add(x => x.ConfirmMessage, "Unsaved changes");
            p.Add(x => x.ConfirmOkText, "Discard");
            p.Add(x => x.ConfirmCancelText, "Cancel");
            configure?.Invoke(p);
        });
}
