using System.Runtime.CompilerServices;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class TmNotionAiMenuTests : LocalizationTestBase
{
    public TmNotionAiMenuTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Close"] = "Close",
            ["Notion_AI_Assistant"] = "AI",
            ["Notion_AI_Generate"] = "Generate",
            ["Notion_AI_Improve"] = "Improve",
            ["Notion_AI_Summarize"] = "Summarize",
            ["Notion_AI_Ask"] = "Ask",
            ["Notion_AI_Accept"] = "Accept",
            ["Notion_AI_Discard"] = "Discard",
            ["Notion_AI_Retry"] = "Retry",
            ["Notion_AI_Thinking"] = "Thinking...",
            ["Notion_AI_Error"] = "AI request failed",
            ["Notion_AI_Run"] = "Run",
            ["Notion_AI_Cancel"] = "Cancel",
            ["Notion_AI_OutputLabel"] = "Result",
            ["Notion_AI_PromptPlaceholder"] = "Describe what should be written...",
            ["Notion_AI_QuestionPlaceholder"] = "Ask a question about this page...",
            ["Notion_AI_SummarizeDescription"] = "Create a concise summary of this page.",
            ["Notion_AI_SourceEmpty"] = "Select text in the editor before improving it.",
            ["Notion_AI_EmptyPrompt"] = "Enter a prompt first.",
            ["Notion_AI_Mode_Grammar"] = "Grammar",
            ["Notion_AI_Mode_Shorten"] = "Shorten",
            ["Notion_AI_Mode_Lengthen"] = "Lengthen",
            ["Notion_AI_Mode_Tone"] = "Tone",
            ["Notion_AI_Mode_Simplify"] = "Simplify",
            ["Notion_AI_Mode_Translate"] = "Translate"
        });
    }

    [Fact]
    public void VisibleMenu_RendersAllPrimaryModes()
    {
        var provider = new RecordingAIProvider();

        var cut = RenderMenu(provider);

        cut.Find("[data-testid='notion-ai-mode-generate']").TextContent.Should().Contain("Generate");
        cut.Find("[data-testid='notion-ai-mode-improve']").TextContent.Should().Contain("Improve");
        cut.Find("[data-testid='notion-ai-mode-summarize']").TextContent.Should().Contain("Summarize");
        cut.Find("[data-testid='notion-ai-mode-ask']").TextContent.Should().Contain("Ask");
    }

    [Fact]
    public async Task Generate_StreamsText_AndAcceptReturnsHtml()
    {
        var provider = new RecordingAIProvider();
        string? accepted = null;
        var cut = RenderMenu(provider, p => p
            .Add(c => c.OnAccepted, html => accepted = html));

        cut.Find("[data-testid='notion-ai-prompt']").Input("release notes");
        await cut.Find("[data-testid='notion-ai-run']").ClickAsync(new());

        cut.WaitForAssertion(() => cut.Find("[data-testid='notion-ai-output']").TextContent.Should().Contain("Generated release notes"));
        await cut.Find("[data-testid='notion-ai-accept']").ClickAsync(new());

        accepted.Should().Contain("Generated release notes");
        provider.GenerateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Improve_UsesSelectedTextMode_AndAcceptReturnsHtml()
    {
        var provider = new RecordingAIProvider();
        string? accepted = null;
        var cut = RenderMenu(provider, p => p
            .Add(c => c.InitialMode, NotionAiMenuMode.Improve)
            .Add(c => c.SourceText, "This sentence is noisy.")
            .Add(c => c.OnAccepted, html => accepted = html));

        await cut.Find("[data-testid='notion-ai-improve-shorten']").ClickAsync(new());
        await cut.Find("[data-testid='notion-ai-run']").ClickAsync(new());

        cut.WaitForAssertion(() => cut.Find("[data-testid='notion-ai-output']").TextContent.Should().Contain("Shorten"));
        await cut.Find("[data-testid='notion-ai-accept']").ClickAsync(new());

        accepted.Should().Contain("Improved This sentence is noisy.");
        provider.LastImproveMode.Should().Be(AiImproveMode.Shorten);
    }

    [Fact]
    public async Task Summarize_RendersProviderSummary()
    {
        var provider = new RecordingAIProvider();
        var cut = RenderMenu(provider, p => p
            .Add(c => c.InitialMode, NotionAiMenuMode.Summarize)
            .Add(c => c.PageId, "page-42"));

        await cut.Find("[data-testid='notion-ai-run']").ClickAsync(new());

        cut.WaitForAssertion(() => cut.Find("[data-testid='notion-ai-output']").TextContent.Should().Contain("Summary for page-42"));
    }

    [Fact]
    public async Task Ask_StreamsAnswerForQuestion()
    {
        var provider = new RecordingAIProvider();
        var cut = RenderMenu(provider, p => p
            .Add(c => c.InitialMode, NotionAiMenuMode.Ask)
            .Add(c => c.PageId, "page-42"));

        cut.Find("[data-testid='notion-ai-question']").Input("What changed?");
        await cut.Find("[data-testid='notion-ai-run']").ClickAsync(new());

        cut.WaitForAssertion(() => cut.Find("[data-testid='notion-ai-output']").TextContent.Should().Contain("Answer What changed?"));
        provider.LastQuestionScope.Should().Be("page-42");
    }

    [Fact]
    public async Task Retry_RepeatsLastRequest()
    {
        var provider = new RecordingAIProvider();
        var cut = RenderMenu(provider);

        cut.Find("[data-testid='notion-ai-prompt']").Input("outline");
        await cut.Find("[data-testid='notion-ai-run']").ClickAsync(new());
        cut.WaitForAssertion(() => provider.GenerateCalls.Should().Be(1));

        await cut.Find("[data-testid='notion-ai-retry']").ClickAsync(new());

        cut.WaitForAssertion(() => provider.GenerateCalls.Should().Be(2));
    }

    [Fact]
    public async Task EmptyPrompt_ShowsLocalizedErrorWithoutCallingProvider()
    {
        var provider = new RecordingAIProvider();
        var cut = RenderMenu(provider);

        await cut.Find("[data-testid='notion-ai-run']").ClickAsync(new());

        cut.Find("[data-testid='notion-ai-error']").TextContent.Should().NotBeNullOrWhiteSpace();
        provider.GenerateCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProviderError_RendersErrorAndRetryAction()
    {
        var provider = new RecordingAIProvider { ThrowOnGenerate = true };
        var cut = RenderMenu(provider);

        cut.Find("[data-testid='notion-ai-prompt']").Input("broken request");
        await cut.Find("[data-testid='notion-ai-run']").ClickAsync(new());

        cut.WaitForAssertion(() => cut.Find("[data-testid='notion-ai-error']").TextContent.Should().Contain("failed"));
        cut.Find("[data-testid='notion-ai-retry']").Should().NotBeNull();
    }

    [Fact]
    public async Task Discard_CancelsAndClosesMenu()
    {
        var provider = new RecordingAIProvider();
        var closed = false;
        var cut = RenderMenu(provider, p => p
            .Add(c => c.OnClosed, () => closed = true));

        cut.Find("[data-testid='notion-ai-prompt']").Input("draft");
        await cut.Find("[data-testid='notion-ai-discard']").ClickAsync(new());

        closed.Should().BeTrue();
    }

    [Fact]
    public void NonPanelAnchor_UsesTopAndLeft_InvariantCulture()
    {
        var cut = RenderMenu(new RecordingAIProvider());

        cut.Find(".tm-notion-ai__anchor").GetAttribute("style")
            .Should().Contain("top:12.5px;left:24.5px");
    }

    private IRenderedComponent<TmNotionAiMenu> RenderMenu(
        RecordingAIProvider provider,
        Action<ComponentParameterCollectionBuilder<TmNotionAiMenu>>? configure = null)
    {
        return Render<TmNotionAiMenu>(parameters =>
        {
            parameters.Add(p => p.Visible, true);
            parameters.Add(p => p.Top, 12.5);
            parameters.Add(p => p.Left, 24.5);
            parameters.Add(p => p.Provider, provider);
            configure?.Invoke(parameters);
        });
    }

    private sealed class RecordingAIProvider : INotionAIProvider
    {
        public int GenerateCalls { get; private set; }
        public AiImproveMode? LastImproveMode { get; private set; }
        public string? LastQuestionScope { get; private set; }
        public bool ThrowOnGenerate { get; set; }

        public async IAsyncEnumerable<string> GenerateAsync(
            AiCompletionRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            GenerateCalls++;
            if (ThrowOnGenerate)
            {
                throw new InvalidOperationException("Provider failed");
            }

            foreach (var chunk in new[] { "Generated ", request.Prompt })
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
        }

        public async IAsyncEnumerable<string> ImproveTextAsync(
            string text,
            AiImproveMode mode,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastImproveMode = mode;
            foreach (var chunk in new[] { "Improved ", text, $" ({mode})" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
        }

        public Task<string> SummarizePageAsync(string pageId, CancellationToken cancellationToken)
            => Task.FromResult($"Summary for {pageId}");

        public async IAsyncEnumerable<string> AnswerQuestionAsync(
            string question,
            string? scopePageId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastQuestionScope = scopePageId;
            foreach (var chunk in new[] { "Answer ", question })
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
        }
    }
}
