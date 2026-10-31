using Bunit;
using FluentAssertions;
using Tempo.Blazor.AI.Markdown;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of <see cref="TmMarkdownView"/> — the component an AI answer is rendered through. It
/// asserts the render pipeline wiring (localized copy/citation labels, the `CodeBlockTemplate` seam), the
/// last-input render cache and the citation click callback. The security guarantees themselves are pinned
/// on <see cref="AiMarkdownRendererTests"/>; here the rendered DOM is re-checked where a regression would
/// be visible (no script element, no <c>javascript:</c> href).
/// </summary>
public sealed class TmMarkdownViewTests : LocalizationTestBase
{
    private const string InteropModulePath = "./_content/Tempo.Blazor.AI/js/ai-markdown.js";

    private BunitJSModuleInterop SetupInterop()
    {
        var module = JSInterop.SetupModule(InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        return module;
    }

    [Fact]
    public void RendersMarkdownInsideTheRootElement()
    {
        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "# Title\n\n- one\n- two"));

        cut.Find(".tm-markdown-view").Should().NotBeNull();
        cut.Find(".tm-markdown-view h1").TextContent.Should().Be("Title");
        cut.FindAll(".tm-markdown-view li").Select(item => item.TextContent).Should().Equal("one", "two");
    }

    [Fact]
    public void AppendsTheHostClass()
    {
        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "text")
            .Add(view => view.Class, "answer-body"));

        cut.Find(".tm-markdown-view").ClassList.Should().Contain("answer-body");
    }

    [Fact]
    public void EmptyMarkdown_RendersAnEmptyRoot()
    {
        var cut = Render<TmMarkdownView>(parameters => parameters.Add(view => view.Markdown, string.Empty));

        cut.Find(".tm-markdown-view").InnerHtml.Trim().Should().BeEmpty();
    }

    [Fact]
    public void RuntimeMarkupCannotProduceAScriptElementOrAnExecutableHref()
    {
        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "<script>alert(1)</script>\n\n[x](javascript:alert(1))"));

        cut.FindAll("script").Should().BeEmpty();
        cut.Markup.Should().NotContainEquivalentOf("javascript:");
        cut.Find(".tm-markdown-view a").GetAttribute("href").Should().Be("#");
    }

    [Fact]
    public void CopyAffordanceUsesTheLocalizedLabels()
    {
        SetupInterop();

        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "```csharp\nvar x = 1;\n```"));

        var copy = cut.Find("[data-copy-code]");
        copy.GetAttribute("aria-label").Should().Be("Copy csharp code");
        copy.GetAttribute("data-copied-label").Should().Be("Copied");
    }

    [Fact]
    public void CitationChip_UsesTheLocalizedAccessibleName()
    {
        SetupInterop();

        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "Answer [1](cite:src-1)."));

        var chip = cut.Find("[data-source-id=\"src-1\"]");
        chip.GetAttribute("aria-label").Should().Be("Source 1");
        chip.ClassList.Should().Contain("tm-markdown-view__citation");
    }

    [Fact]
    public async Task CitationClicksReachTheCallback_AndTheInteropModuleIsAttachedOnce()
    {
        var module = SetupInterop();
        string? cited = null;

        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "Answer [2](cite:src-2).")
            .Add(view => view.OnCitationClick, (string id) => cited = id));

        module.Invocations.Should().Contain(invocation => invocation.Identifier == "attach");

        await cut.InvokeAsync(() => cut.Instance.HandleCitationClickAsync("src-2"));

        cited.Should().Be("src-2");
    }

    [Fact]
    public void TableWrapperAndCodeScroller_AreKeyboardReachable()
    {
        SetupInterop();

        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "| a | b |\n| --- | --- |\n| 1 | 2 |\n\n```cs\nvar x = 1;\n```"));

        var table = cut.Find(".tm-markdown-view__table");
        table.GetAttribute("tabindex").Should().Be("0");
        table.GetAttribute("role").Should().Be("region");
        table.GetAttribute("aria-label").Should().Be("Table", "the region name is localized");

        cut.Find("pre").GetAttribute("tabindex").Should().Be("0",
            "the code scroller must be reachable without a mouse");
    }

    [Fact]
    public void CopyButton_KeepsItsAccessibleNameAndAnnouncesThroughASiblingLiveRegion()
    {
        SetupInterop();

        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "```csharp\nvar x = 1;\n```"));

        var button = cut.Find("[data-copy-code]");
        var header = cut.Find(".tm-markdown-view__code-header");
        var status = header.Children.FirstOrDefault(child => child.GetAttribute("role") == "status");

        status.Should().NotBeNull("the live region is a direct child of the header row");
        button.NextElementSibling.Should().Be(status, "it sits AFTER the button, as its sibling");
        status!.ClassList.Should().Contain("tm-sr-only");
        button.GetAttribute("aria-label").Should().Be("Copy csharp code",
            "the copied state is announced by the live region, the accessible name never changes");
    }

    [Fact]
    public void ReRenderingWithUnchangedMarkdown_DoesNotRenderAgain()
    {
        var cut = Render<TmMarkdownView>(parameters => parameters.Add(view => view.Markdown, "**same**"));
        cut.Instance.RenderCount.Should().Be(1);

        cut.Render(parameters => parameters.Add(view => view.Markdown, "**same**"));
        cut.Instance.RenderCount.Should().Be(1, "the last input is cached by hash and ordinal compare");

        cut.Render(parameters => parameters.Add(view => view.Markdown, "**changed**"));
        cut.Instance.RenderCount.Should().Be(2);
    }

    [Fact]
    public void CodeBlockTemplate_TakesOverTheCodeBlockMarkup()
    {
        AiMarkdownCodeBlockContext? captured = null;

        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "```js\nconst x = 1;\n```")
            .Add(view => view.CodeBlockTemplate, context =>
            {
                captured = context;
                return $"<pre class=\"host-highlighted\"><code>{System.Net.WebUtility.HtmlEncode(context.Code)}</code></pre>";
            }));

        captured.Should().NotBeNull();
        captured!.Language.Should().Be("js");
        captured.Code.Should().Be("const x = 1;\n");
        cut.Find("pre.host-highlighted").Should().NotBeNull();
        cut.FindAll("[data-copy-code]").Should().BeEmpty("the host owns the whole code block");
    }

    [Fact]
    public void CodeBlockTemplate_ComparesMethodAndTarget_NotTheDelegateInstance()
    {
        // The render cache compares the delegate's Method and Target, so a NEW delegate instance over the
        // same method+target is the same seam. This is why the XML doc asks for a stable reference (a cached
        // field or a method group): the comparison can only be stable if the method/target pair is.
        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "```js\nconst x = 1;\n```")
            .Add(view => view.CodeBlockTemplate, StaticCodeBlockTemplate));

        cut.Instance.RenderCount.Should().Be(1);

        cut.Render(parameters => parameters
            .Add(view => view.Markdown, "```js\nconst x = 1;\n```")
            .Add(view => view.CodeBlockTemplate, new Func<AiMarkdownCodeBlockContext, string>(StaticCodeBlockTemplate)));

        cut.Instance.RenderCount.Should().Be(1,
            "a second delegate instance over the same Method/Target must not re-run the markdown pipeline");
        cut.Find("pre.host-static").Should().NotBeNull();
    }

    private static string StaticCodeBlockTemplate(AiMarkdownCodeBlockContext context)
        => $"<pre class=\"host-static\"><code>{System.Net.WebUtility.HtmlEncode(context.Code)}</code></pre>";

    [Fact]
    public async Task DisposingTheView_DetachesTheInteropModule()
    {
        var module = SetupInterop();

        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "Answer [1](cite:src-1)."));

        module.Invocations.Should().Contain(invocation => invocation.Identifier == "attach");

        await cut.Instance.DisposeAsync();

        module.Invocations.Should().Contain(invocation => invocation.Identifier == "detach",
            "the delegated listener must not outlive the component");
    }

    [Fact]
    public void DataTestId_OverridesTheRootTestId()
    {
        var cut = Render<TmMarkdownView>(parameters => parameters
            .Add(view => view.Markdown, "text")
            .Add(view => view.DataTestId, "answer-markdown"));

        cut.Find("[data-testid=\"answer-markdown\"]").Should().NotBeNull();
    }
}
