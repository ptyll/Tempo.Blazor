using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Gallery;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Gallery;

public class TmImageGalleryLightboxTests : LocalizationTestBase
{
    private static TestImage Img(string id, string url, string? title = null) =>
        new(id, url, title);

    private record TestImage(
        string Id,
        string Url,
        string? Title = null) : IGalleryImage
    {
        public string? ThumbnailUrl  => null;
        public string? Description   => null;
        public IEnumerable<string> Tags => [];
        public DateTime? UploadedAt  => null;
        public string? UploadedBy   => null;
        public long?   FileSizeBytes => null;
    }

    [Fact]
    public void Gallery_ClickThumbnail_OpensLightbox()
    {
        var images = new[] { Img("1", "http://img/1.jpg", "First") };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.Find(".tm-gallery-item").Click();

        cut.FindAll(".tm-lightbox").Should().HaveCount(1);
    }

    [Fact]
    public void Gallery_Thumbnail_IsANativeButton_WithNoEmulationResidue()
    {
        var images = new[] { Img("1", "http://img/1.jpg", "First") };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        var item = cut.Find(".tm-gallery-item");
        item.TagName.Should().Be("BUTTON",
            "a real <button> gives keyboard users native activation (WCAG 2.1.1) and gives the focus" +
            " restore something focusable to return to when the lightbox closes");
        item.GetAttribute("role").Should().BeNull("role=button on a <button> is emulation residue");
        item.GetAttribute("tabindex").Should().BeNull("tabindex=0 on a <button> is emulation residue");
    }

    [Fact]
    public void Gallery_Thumbnail_NullTitle_FallsBackToLocalizedOpenImageName()
    {
        var images = new[]
        {
            Img("1", "http://img/1.jpg"),
            Img("2", "http://img/2.jpg"),
        };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.FindAll(".tm-gallery-item")[0].GetAttribute("aria-label")
            .Should().Be("Open image 1 of 2",
                "a thumbnail without a Title still needs an accessible name (WCAG 4.1.2)");
    }

    [Fact]
    public void Gallery_Thumbnail_WhitespaceTitle_FallsBackToPositionedName()
    {
        var images = new[] { Img("1", "http://img/1.jpg", "   ") };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        var item = cut.Find(".tm-gallery-item");
        item.GetAttribute("aria-label").Should().Be("Open image 1 of 1",
            "a whitespace-only title is no name — the same rule as a null title (WCAG 4.1.2)");
        cut.FindAll(".tm-gallery-item-title").Should().BeEmpty(
            "the visible title span must not render whitespace-only text");
    }

    [Fact]
    public void Gallery_Thumbnail_WithTitle_AriaLabelIsTheTitle()
    {
        var images = new[] { Img("1", "http://img/1.jpg", "Sunset over the lake") };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        var item = cut.Find(".tm-gallery-item");
        item.GetAttribute("aria-label").Should().Be("Sunset over the lake");

        // No double announcement: the button's aria-label is the one name, so the visible
        // title inside it is hidden from the accessibility tree.
        cut.Find(".tm-gallery-item-title").GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void Gallery_Thumbnail_NativeActivation_OpensLightboxExactlyOnce()
    {
        var images = new[] { Img("1", "http://img/1.jpg", "First") };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));
        var item = cut.Find(".tm-gallery-item");

        // A native <button> must not carry emulated key handlers
        // (docs/keyboard-activation-convention.md); bUnit does not synthesize the native
        // keyboard→click sequence, so the real browser order is dispatched explicitly:
        // Enter → keydown + click, Space → keydown + keyup + click. The missing-handler
        // exceptions ARE the proof that no emulation can double-fire.
        var enterDown = () => item.KeyDown("Enter");
        enterDown.Should().Throw<MissingEventHandlerException>();
        var spaceDown = () => item.KeyDown(" ");
        spaceDown.Should().Throw<MissingEventHandlerException>();
        var spaceUp = () => item.KeyUp(" ");
        spaceUp.Should().Throw<MissingEventHandlerException>();

        item.Click();
        cut.FindAll(".tm-lightbox").Should().HaveCount(1, "the native click opens the lightbox once");
    }

    [Fact]
    public void Gallery_Lightbox_ShowsFullImage()
    {
        var images = new[] { Img("1", "http://img/1.jpg", "Hero") };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.Find(".tm-gallery-item").Click();

        cut.Find(".tm-lightbox img").GetAttribute("src").Should().Contain("1.jpg");
    }

    [Fact]
    public void Gallery_Lightbox_UntitledImage_AltNamesItByPosition()
    {
        var images = new[]
        {
            Img("1", "http://img/1.jpg"),
            Img("2", "http://img/2.jpg"),
        };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.FindAll(".tm-gallery-item")[1].Click();

        cut.Find(".tm-lightbox img").GetAttribute("alt").Should().Be("Image 2 of 2",
            "an untitled image still needs alt text in the lightbox (WCAG 1.1.1)");
    }

    [Fact]
    public void Gallery_Lightbox_CloseButton_Closes()
    {
        var images = new[] { Img("1", "http://img/1.jpg") };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.Find(".tm-gallery-item").Click();
        cut.Find(".tm-lightbox-close").Click();

        cut.FindAll(".tm-lightbox").Should().BeEmpty();
    }

    [Fact]
    public void Gallery_Lightbox_ArrowRight_NavigatesToNext()
    {
        var images = new[]
        {
            Img("1", "http://img/1.jpg", "First"),
            Img("2", "http://img/2.jpg", "Second"),
        };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.FindAll(".tm-gallery-item")[0].Click();
        cut.Find(".tm-lightbox-next").Click();

        cut.Find(".tm-lightbox img").GetAttribute("src").Should().Contain("2.jpg");
    }

    [Fact]
    public void Gallery_Lightbox_ArrowLeft_NavigatesToPrevious()
    {
        var images = new[]
        {
            Img("1", "http://img/1.jpg", "First"),
            Img("2", "http://img/2.jpg", "Second"),
        };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.FindAll(".tm-gallery-item")[1].Click();
        cut.Find(".tm-lightbox-prev").Click();

        cut.Find(".tm-lightbox img").GetAttribute("src").Should().Contain("1.jpg");
    }

    [Fact]
    public void Gallery_Lightbox_Escape_Closes()
    {
        var images = new[] { Img("1", "http://img/1.jpg") };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.Find(".tm-gallery-item").Click();
        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync()).GetAwaiter().GetResult();

        cut.FindAll(".tm-lightbox").Should().BeEmpty();
    }

    [Fact]
    public void Gallery_Lightbox_ShowsImageCount()
    {
        var images = new[]
        {
            Img("1", "http://img/1.jpg"),
            Img("2", "http://img/2.jpg"),
            Img("3", "http://img/3.jpg"),
        };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        cut.FindAll(".tm-gallery-item")[0].Click();

        cut.Find(".tm-lightbox-counter").TextContent.Should().Contain("1");
        cut.Find(".tm-lightbox-counter").TextContent.Should().Contain("3");
    }

    [Fact]
    public async Task Gallery_WithProvider_FetchesTicketUrl()
    {
        var provider = new FakeProvider("http://ticket/1.jpg");
        Services.AddSingleton<IImageGalleryDataProvider>(provider);

        var images = new[] { Img("img-1", "http://img/1.jpg", "Via ticket") };
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images,        images)
            .Add(c => c.DataProvider,  provider));

        cut.Find(".tm-gallery-item").Click();
        await cut.InvokeAsync(() => { }); // allow async ticket fetch

        cut.Find(".tm-lightbox img").GetAttribute("src").Should().Be("http://ticket/1.jpg");
    }

    [Fact]
    public void Gallery_DeleteButton_ShowsWhenCanDelete()
    {
        var images = new[] { Img("1", "http://img/1.jpg") };
        var provider = new FakeProvider("http://ticket/1.jpg");
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images,       images)
            .Add(c => c.DataProvider, provider)
            .Add(c => c.CanDelete,    true));

        cut.Find(".tm-gallery-item").Click();

        cut.FindAll(".tm-lightbox-delete").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Gallery_DeleteLastImage_ClosesTheLightbox_AndLeavesAFocusableGallery()
    {
        // cluster-D §3(3): deleting the DISPLAYED last image leaves no restore id and a detached
        // opener, so the gallery focuses the new last thumbnail from OnAfterRenderAsync instead.
        // The SetParametersAndRender below simulates a parent re-render BEFORE the delete: code
        // that re-created the @ref array in OnParametersSet wiped the captured references there
        // (Blazor captures a @ref only when the element is inserted, never on a re-render), and
        // the post-delete focus then landed nowhere. This test has teeth — it asserts the actual
        // focus interop, not just that the call would not fault.
        var images = new[]
        {
            Img("1", "http://img/1.jpg", "First"),
            Img("2", "http://img/2.jpg", "Last"),
        };
        var provider = new FakeProvider("http://ticket/2.jpg");
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images,       images)
            .Add(c => c.DataProvider, provider));

        // Parent re-render before opening: this is what used to kill the captured references.
        cut.Render(p => p.Add(c => c.CanDelete, true));

        cut.FindAll(".tm-gallery-item")[1].Click();
        cut.Find(".tm-lightbox-delete").Click();
        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-lightbox").Should().BeEmpty("the delete closes the lightbox");
        cut.FindAll(".tm-gallery-item").Should().HaveCount(1, "the displayed image is gone");

        // The thumbnail now at the deleted position (the new last one) is the remaining item.
        // Its element reference id is what the gallery must hand to the focus interop. (bUnit
        // renders the blazor:elementReference marker attribute with an empty value, so the id
        // is read from the component's captured reference itself.)
        var newLastRefId = CapturedItemRefId(cut.Instance, 0);
        newLastRefId.Should().NotBeNullOrWhiteSpace(
            "the re-render must not have wiped the captured reference of the remaining thumbnail");

        cut.WaitForAssertion(() =>
        {
            var focusedIds = JSInterop.Invocations
                .Where(i => i.Identifier == FocusInvocation)
                .SelectMany(i => i.Arguments.OfType<ElementReference>())
                .Select(er => er.Id)
                .ToList();
            focusedIds.Should().Contain(newLastRefId,
                "OnAfterRenderAsync must focus the new last thumbnail — a parent re-render " +
                "before the delete must not have killed the delete-last focus restore");
        });
    }

    [Fact]
    public async Task Gallery_DeleteOnlyImage_FocusesTheGalleryRoot()
    {
        // Deleting the ONLY image leaves no thumbnail to return to: focus moves to the gallery
        // root (tabindex="-1"), so it does not drop to <body> and keyboard users keep their
        // place in the page.
        var images = new[] { Img("1", "http://img/1.jpg", "Only") };
        var provider = new FakeProvider("http://ticket/1.jpg");
        var cut = Render<TmImageGallery>(p => p
            .Add(c => c.Images,       images)
            .Add(c => c.DataProvider, provider)
            .Add(c => c.CanDelete,    true));

        var rootRefId = CapturedRootRefId(cut.Instance);
        rootRefId.Should().NotBeNullOrWhiteSpace("the gallery root carries a captured reference");

        cut.Find(".tm-gallery-item").Click();
        cut.Find(".tm-lightbox-delete").Click();
        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-gallery-item").Should().BeEmpty("the only image is gone");
        cut.WaitForAssertion(() =>
        {
            var focusedIds = JSInterop.Invocations
                .Where(i => i.Identifier == FocusInvocation)
                .SelectMany(i => i.Arguments.OfType<ElementReference>())
                .Select(er => er.Id)
                .ToList();
            focusedIds.Should().Contain(rootRefId,
                "an emptied gallery must move focus to its root, not drop it on <body>");
        });
    }

    /// <summary>
    /// Id of the element reference the gallery captured for thumbnail <paramref name="index"/>.
    /// bUnit marks captured elements with an empty <c>blazor:elementReference</c> attribute, so
    /// the id is only observable on the component's captured <see cref="ElementReference"/>.
    /// </summary>
    private static string? CapturedItemRefId(TmImageGallery gallery, int index) =>
        ((ElementReference[])typeof(TmImageGallery)
            .GetField("_itemRefs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(gallery)!)[index].Id;

    private static string? CapturedRootRefId(TmImageGallery gallery) =>
        ((ElementReference)typeof(TmImageGallery)
            .GetField("_rootRef", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(gallery)!).Id;

    // ── Focus restore to the thumbnail on screen (Fáze 8, cluster-D §3/§5) ─────

    [Fact]
    public void Gallery_Lightbox_RestoreTarget_FollowsTheImageOnScreen()
    {
        var images = new[]
        {
            Img("1", "http://img/1.jpg", "First"),
            Img("2", "http://img/2.jpg", "Second"),
        };
        var cut = Render<TmImageGallery>(p => p.Add(c => c.Images, images));

        var items = cut.FindAll(".tm-gallery-item");
        var firstId = items[0].GetAttribute("id");
        var secondId = items[1].GetAttribute("id");

        items[0].Click();
        cut.Find(".tm-lightbox").GetAttribute("data-restore-target").Should().Be(firstId,
            "closing returns focus to the thumbnail of the image on screen");

        cut.Find(".tm-lightbox-next").Click();
        cut.Find(".tm-lightbox").GetAttribute("data-restore-target").Should().Be(secondId,
            "after browsing, close returns to the thumbnail now shown — not the original opener");
    }

    [Fact]
    public void TmLightbox_WithRestoreTarget_RendersItOnTheScopeRoot()
    {
        var cut = Render<TmLightbox>(p => p
            .Add(l => l.IsOpen, true)
            .Add(l => l.RestoreFocusTargetId, "my-thumb"));

        cut.Find(".tm-lightbox").GetAttribute("data-restore-target").Should().Be("my-thumb");
    }

    [Fact]
    public void TmLightbox_WithoutRestoreTarget_RendersNoAttribute()
    {
        var cut = Render<TmLightbox>(p => p.Add(l => l.IsOpen, true));

        cut.Find(".tm-lightbox").GetAttribute("data-restore-target").Should().BeNull(
            "a standalone lightbox falls back to the opener captured at activation");
    }

    private sealed class FakeProvider(string ticketUrl) : IImageGalleryDataProvider
    {
        public Task<IReadOnlyList<IGalleryImage>> GetImagesAsync(string? entityId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IGalleryImage>>([]);

        public Task<string> GetImageTicketUrlAsync(string imageId, CancellationToken ct = default)
            => Task.FromResult(ticketUrl);

        public Task DeleteImageAsync(string imageId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // ElementReference.FocusAsync surfaces in bUnit's Loose JSInterop as
    // "Blazor._internal.domWrapper.focus" (same identifier as TmMultiSelect/TmSplitButton).
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    // ── Activate after promote (F3 follow-up, c51ddf81 #1) ────────────────────

    [Fact]
    public void TmLightbox_FocusTrap_ActivatesOnlyAfterPromotion()
    {
        var top = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js");
        var promote = top.SetupVoid("promote", _ => true); // no result: the call stays pending
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        var activate = trap.SetupVoid("activate", _ => true).SetVoidResult();

        var cut = Render<TmLightbox>(p => p.Add(l => l.IsOpen, true));

        activate.Invocations["activate"].Should().BeEmpty(
            "the trap must not activate while the promote interop is still pending — an unpromoted " +
            "popover root can be display:none and the initial-focus move would land on <body>");

        promote.SetVoidResult();
        cut.WaitForAssertion(() => activate.Invocations["activate"].Should().HaveCount(1, "completing the promote is what releases the trap's activation"));
    }
}
