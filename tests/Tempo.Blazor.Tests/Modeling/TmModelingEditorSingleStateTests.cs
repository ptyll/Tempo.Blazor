using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// The single-state surfaces of <see cref="TmModelingEditor"/> (final UX review M4 + the height
/// contract): the Retry affordance on the recoverable error/empty screens, the docked panels
/// staying hidden while no workspace is mounted, and the Height parameter's CSS-length guard.
/// Split from <see cref="TmModelingEditorTests"/> to respect the 1200-line source ceiling.
/// </summary>
public sealed class TmModelingEditorSingleStateTests : LocalizationTestBase
{
    public TmModelingEditorSingleStateTests()
    {
        Services.AddTempoBlazorModeling();
    }

    [Fact]
    public void Error_state_offers_the_retry_button()
    {
        // Final UX review (M4): the recoverable error screen carries the shared Reload affordance.
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new ThrowingModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.ProviderKey, ThrowingModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("error"));

        var retry = cut.Find("[data-testid='modeling-editor-retry']");
        retry.TextContent.Should().Contain("Reload", "the retry shares the action bar's Reload label");
        cut.FindAll(".tm-modeling-editor__shell .tm-editor-shell__panel--left, .tm-modeling-editor__shell .tm-editor-shell__panel--right, .tm-modeling-editor__shell .tm-editor-shell__panel-toggle").Should().BeEmpty(
            "the docked tree/inspector panels stay hidden while the workspace is not mounted");
    }

    [Fact]
    public async Task Error_retry_drives_the_reload_path()
    {
        // The retry is the action bar's Reload: a click re-arms the load (the provider is still
        // failing, so the state stays error and the button survives — but no throw escapes).
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new ThrowingModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.ProviderKey, ThrowingModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("error"));

        await cut.InvokeAsync(() => cut.Find("[data-testid='modeling-editor-retry']").Click());
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("error",
            "the provider keeps failing, but the retry path itself ran"));
        cut.Find("[data-testid='modeling-editor-retry']").Should().NotBeNull("the retry stays offered");
    }

    [Fact]
    public void Empty_state_with_a_configured_source_offers_the_retry_button()
    {
        // Final UX review (M4): an empty state with a provider set is recoverable — the same
        // Reload button; without a source there is nothing to reload and no button renders.
        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.ProviderKey, "tempo.tests.modeling.missing"));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("empty"));
        cut.Find("[data-testid='modeling-editor-retry']").Should().NotBeNull();

        using var bare = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));
        bare.WaitForAssertion(() => bare.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("empty"));
        bare.FindAll("[data-testid='modeling-editor-retry']").Should().BeEmpty(
            "an empty state without a configured source has nothing to reload");
    }

    [Theory]
    [InlineData("calc(100vh - 2rem)", true)]
    [InlineData("100%", true)]
    [InlineData("100dvh", true)]
    [InlineData("600px", true)]
    [InlineData(".5rem", true)]
    [InlineData("-12.5vh", true)]
    [InlineData("evil;position:fixed", false)]
    [InlineData("expression({alert(1)})", false)]
    [InlineData("url(javascript:alert(1))", false)]
    [InlineData("min(768px, 100vh)", false)]
    [InlineData("600", false)]
    [InlineData(" ", false)]
    public void Height_parameter_rejects_values_that_are_not_css_lengths(string height, bool accepted)
    {
        // Final review: the inline style must never become an injection vector — a Height value
        // that is not a simple CSS length or a single calc() expression falls back to the
        // stylesheet contract (the same shape the parameter documents).
        Tempo.Blazor.Components.Modeling.TmModelingEditor.IsValidCssLength(height).Should().Be(accepted);

        if (!accepted)
        {
            Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));
            using var cut = Render<TmModelingEditor>(parameters => parameters
                .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
                .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key)
                .Add(p => p.Height, height));
            cut.Find("[data-testid='modeling-editor']").GetAttribute("style").Should().BeNull(
                $"a rejected Height ({height}) falls back to the stylesheet default");
        }
    }

    private sealed class ThrowingModelingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.single-state-throw";

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
            => Task.FromException<ModelingModelDto>(new InvalidOperationException("Provider failure"));
    }
}
