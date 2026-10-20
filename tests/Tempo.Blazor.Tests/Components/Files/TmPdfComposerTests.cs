using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using FluentAssertions;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// bUnit tests for the shared <see cref="TmPdfComposer"/> (PdfViewer redesign phase 4):
/// the sticky reply composer used by both annotation panels — disabled submit while empty,
/// primary submit, optional ghost secondary action, Ctrl+Enter submit.
/// </summary>
public class TmPdfComposerTests : LocalizationTestBase
{
    private IRenderedComponent<TmPdfComposer> Render(Action<Bunit.ComponentParameterCollectionBuilder<TmPdfComposer>>? configure = null)
    {
        return Render<TmPdfComposer>(p =>
        {
            p.Add(x => x.Placeholder, "Reply…");
            p.Add(x => x.SubmitLabel, "Reply");
            p.Add(x => x.InputTestId, "pdf-annotation-reply-input");
            p.Add(x => x.SubmitTestId, "pdf-annotation-reply-submit");
            configure?.Invoke(p);
        });
    }

    [Fact]
    public void Submit_Disabled_WhenEmpty()
    {
        var cut = Render();

        var submit = cut.Find("[data-testid='pdf-annotation-reply-submit']");
        submit.HasAttribute("disabled").Should().BeTrue("an empty body cannot be submitted");

        cut.Find("[data-testid='pdf-annotation-reply-input']").Input("Something");
        cut.Find("[data-testid='pdf-annotation-reply-submit']").HasAttribute("disabled")
            .Should().BeFalse("typing a body enables the submit button");

        // An explicit CanSubmit=false keeps the button disabled regardless of the body.
        var forced = Render(p => p.Add(x => x.CanSubmit, false));
        forced.Find("[data-testid='pdf-annotation-reply-input']").Input("Something");
        forced.Find("[data-testid='pdf-annotation-reply-submit']").HasAttribute("disabled")
            .Should().BeTrue("an explicit CanSubmit=false wins over the non-empty body");
    }

    [Fact]
    public async Task Submit_RaisesOnSubmit_AndClearsBody()
    {
        var submitted = new List<string>();
        var cut = Render(p => p.Add(x => x.OnSubmit, EventCallback.Factory.Create<string>(this, v => submitted.Add(v))));

        cut.Find("[data-testid='pdf-annotation-reply-input']").Input("A reply");
        cut.Find("[data-testid='pdf-annotation-reply-submit']").Click();

        submitted.Should().ContainSingle().Which.Should().Be("A reply");
        cut.Find("[data-testid='pdf-annotation-reply-submit']").HasAttribute("disabled")
            .Should().BeTrue("the composer clears its body after submitting");

        // Ctrl+Enter in the textarea submits as well.
        cut.Find("[data-testid='pdf-annotation-reply-input']").Input("Second");
        cut.Find("[data-testid='pdf-annotation-reply-input']")
            .KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        submitted.Should().HaveCount(2);
        submitted[1].Should().Be("Second");
    }

    [Fact]
    public async Task Secondary_RaisesOnSecondary()
    {
        var secondary = false;
        var cut = Render(p =>
        {
            p.Add(x => x.SecondaryLabel, "Resolve");
            p.Add(x => x.SecondaryTestId, "pdf-annotation-resolve");
            p.Add(x => x.OnSecondary, EventCallback.Factory.Create(this, () => secondary = true));
        });

        cut.Find("[data-testid='pdf-annotation-resolve']").Click();
        await Task.CompletedTask;
        secondary.Should().BeTrue();
    }

    [Fact]
    public void Sticky_RendersStickyClass()
    {
        var cut = Render(p => p.Add(x => x.Sticky, true));
        cut.Find(".tm-pdf-composer").ClassList.Should().Contain("tm-pdf-composer--sticky");
    }
}
