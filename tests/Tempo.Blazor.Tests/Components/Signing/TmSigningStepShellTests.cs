using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

public class TmSigningStepShellTests : LocalizationTestBase
{
    [Fact]
    public void Render_Field_RendersLabelAndRequiredMarker()
    {
        var cut = Render<TmSigningStepShell>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Full name", Required = true }));

        cut.Find(".tm-signing-step-shell__title").TextContent.Should().Be("Full name");
        cut.Find(".tm-signing-step-shell__required").TextContent.Should().Contain("Required");
    }

    /// <summary>
    /// The title heading is the accessible name of every input of the step: it carries the
    /// instance's stable HTML id and <c>tabindex="-1"</c> as a focus target. Ids are PER COMPONENT
    /// INSTANCE — two shells rendering the same field uuid never share one.
    /// </summary>
    [Fact]
    public void Shell_TitleCarriesStableIdAndTabindexForFocus()
    {
        var cut = Render<TmSigningStepShell>(parameters => parameters
            .Add(p => p.Field, new SigningField { Uuid = "full-name", Name = "Full name" }));
        var sameField = Render<TmSigningStepShell>(parameters => parameters
            .Add(p => p.Field, new SigningField { Uuid = "full-name", Name = "Full name" }));

        var title = cut.Find(".tm-signing-step-shell__title");
        title.GetAttribute("id").Should().StartWith("tm-signing-step-title-");
        title.GetAttribute("tabindex").Should().Be("-1");
        title.GetAttribute("id").Should().NotBe(
            sameField.Find(".tm-signing-step-shell__title").GetAttribute("id"),
            "ids are per rendered instance, not derived from the field uuid");
    }

    /// <summary>
    /// The shell cascades its accessible plumbing as <see cref="TmSigningStepShellContext"/>:
    /// custom step content reads the context and its <see cref="TmSigningStepShellContext.TitleId"/>
    /// is the id the rendered h3 carries, so an input's <c>aria-labelledby</c> always resolves
    /// inside the rendered DOM.
    /// </summary>
    [Fact]
    public void Shell_CascadesContextWhoseTitleId_IsTheRenderedTitleId()
    {
        var cut = Render<TmSigningStepShell>(parameters => parameters
            .Add(p => p.Field, new SigningField { Uuid = "terms", Name = "Terms" })
            .AddChildContent<ContextCapture>());

        var capture = cut.FindComponent<ContextCapture>().Instance;
        capture.Context.Should().NotBeNull("the shell cascades its ids to its content");
        capture.Context!.TitleId.Should().Be(
            cut.Find(".tm-signing-step-shell__title").GetAttribute("id"),
            "the context's title id must be the id the rendered h3 carries");
    }

    /// <summary>
    /// Moving to a step (the runner advancing the field) moves focus to the step title, so
    /// keyboard and screen-reader users land at the top of the new step. The initial render must
    /// not steal focus. The PRECISE target node is pinned by the runner-level
    /// <c>Runner_GoNext_RequestsFocusOnStepTitle</c>; here the title is patched in place, and
    /// bUnit does not re-stamp patched nodes with the new element reference id.
    /// </summary>
    [Fact]
    public void Shell_FocusesItsTitle_WhenTheFieldChanges()
    {
        var cut = Render<TmSigningStepShell>(parameters => parameters
            .Add(p => p.Field, new SigningField { Uuid = "first", Name = "First" }));

        cut.Find(".tm-signing-step-shell__title").TextContent.Should().Be("First");

        cut.Render(parameters => parameters
            .Add(p => p.Field, new SigningField { Uuid = "second", Name = "Second" }));

        var invocation = JSInterop.VerifyFocusAsyncInvoke();
        var referenceId = invocation.Arguments[0].ToString();
        referenceId.Should().NotBeNullOrWhiteSpace("FocusAsync must be requested with a live element reference");
        cut.Find(".tm-signing-step-shell__title").TextContent.Should().Be("Second");
    }

    /// <summary>
    /// The focus move is best effort: when the focus JSInterop throws (a view whose JS circuit is
    /// already gone), the exception stays inside <see cref="SigningFocus.TryFocusAsync"/> and the
    /// shell keeps rendering.
    /// </summary>
    [Fact]
    public void Shell_FocusInteropThrowing_DoesNotBreakTheRender()
    {
        JSInterop.SetupVoid("Blazor._internal.ElementReference.focus")
            .SetException(new JSException("circuit gone"));

        var cut = Render<TmSigningStepShell>(parameters => parameters
            .Add(p => p.Field, new SigningField { Uuid = "first", Name = "First" }));

        cut.Render(parameters => parameters
            .Add(p => p.Field, new SigningField { Uuid = "second", Name = "Second" }));

        cut.Find(".tm-signing-step-shell__title").TextContent.Should().Be("Second");
        cut.FindAll(".tm-signing-step-shell").Should().ContainSingle("the shell survives the failed focus move");
    }

    /// <summary>
    /// The runner forwards its step validation through a named cascade; the shell shows it when
    /// the step has no validation message of its own — exactly one alert for the step.
    /// </summary>
    [Fact]
    public void Shell_ShowsRunnerValidation_WhenOwnMessageMissing()
    {
        var cut = Render<ShellCascadeHost>(parameters => parameters
            .Add(p => p.RunnerValidationMessage, "Full name is required."));

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Be("Full name is required.");
        cut.Find(".tm-signing-step-shell").ClassList.Should().Contain("tm-signing-step-shell--invalid");
    }

    [Fact]
    public void Shell_PrefersItsOwnValidationMessage_OverTheRunnerCascade()
    {
        var cut = Render<ShellCascadeHost>(parameters =>
        {
            parameters.Add(p => p.RunnerValidationMessage, "Full name is required.");
            parameters.Add(p => p.OwnValidationMessage, "This field does not match the required format");
        });

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Be("This field does not match the required format");
        cut.FindAll(".tm-signing-step-shell__validation").Should().ContainSingle();
    }

    [Fact]
    public void Render_OptionalField_RendersOptionalMarker()
    {
        var cut = Render<TmSigningStepShell>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Company" }));

        cut.Find(".tm-signing-step-shell__optional").TextContent.Should().Contain("Optional");
    }

    [Fact]
    public void Render_Description_RendersSimpleMarkdown()
    {
        var cut = Render<TmSigningStepShell>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Terms" })
            .Add(p => p.Description, "Accept **all** terms"));

        cut.Markup.Should().Contain("<strong>all</strong>");
    }

    [Fact]
    public void Render_LocalizedFieldText_UsesRequestedCulture()
    {
        var field = new SigningField
        {
            Name = "internal-name",
            Labels = { Translations = { ["cs"] = "Celé jméno" } },
            Descriptions = { Translations = { ["cs"] = "Vyplňte **jméno** podepisujícího." } }
        };

        var cut = Render<TmSigningStepShell>(parameters => parameters
            .Add(p => p.Field, field)
            .Add(p => p.Culture, "cs-CZ"));

        cut.Find(".tm-signing-step-shell__title").TextContent.Should().Be("Celé jméno");
        cut.Markup.Should().Contain("<strong>");
        cut.Find(".tm-signing-step-shell__description").TextContent.Should().Contain("jméno");
    }

    [Fact]
    public void Render_ValidationMessage_AddsInvalidClass()
    {
        var cut = Render<TmSigningStepShell>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Terms" })
            .Add(p => p.ValidationMessage, "Required"));

        cut.Find(".tm-signing-step-shell").ClassList.Should().Contain("tm-signing-step-shell--invalid");
        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Be("Required");
    }

    [Fact]
    public void Render_AppearsOn_ShowsDocumentPosition()
    {
        var cut = Render<TmSigningStepShell>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Terms" })
            .Add(p => p.AppearsOn, "Page 2"));

        cut.Find(".tm-signing-step-shell__appears-on").TextContent.Should().Be("Page 2");
    }

    /// <summary>Step content that captures the shell's cascaded <see cref="TmSigningStepShellContext"/>.</summary>
    private sealed class ContextCapture : ComponentBase
    {
        [CascadingParameter]
        public TmSigningStepShellContext? Context { get; set; }
    }

    /// <summary>Host that renders the shell inside the runner's named validation cascade.</summary>
    private sealed class ShellCascadeHost : ComponentBase
    {
        [Parameter] public string? RunnerValidationMessage { get; set; }

        [Parameter] public string? OwnValidationMessage { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.OpenComponent<CascadingValue<string?>>(1);
            builder.AddAttribute(2, "Name", "TmSigningRunnerValidation");
            builder.AddAttribute(3, "Value", RunnerValidationMessage);
            builder.AddAttribute(4, "ChildContent", (RenderFragment)(content =>
            {
                content.OpenComponent<TmSigningStepShell>(5);
                content.AddAttribute(6, "Field", new SigningField { Uuid = "terms", Name = "Terms" });
                content.AddAttribute(7, "ValidationMessage", OwnValidationMessage);
                content.CloseComponent();
            }));
            builder.CloseComponent();
            builder.CloseElement();
        }
    }
}
