using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// File-picker contract of <see cref="TmAiComposer"/> (plan 7f21f26b, Fáze 6): the picked files become chips,
/// <c>MaxAttachments</c> and <c>MaxFileSize</c> are enforced with a localized message in the component's single
/// live region, and the metadata the host needs travels with the submit.
/// </summary>
public sealed class TmAiComposerAttachmentTests : LocalizationTestBase
{
    private const string InteropModulePath = TmAiComposerTests.InteropModulePath;

    private void SetupInterop()
    {
        var module = JSInterop.SetupModule(InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.SetupVoid("resize", _ => true).SetVoidResult();
        module.SetupVoid("placeSuggestions", _ => true).SetVoidResult();
        module.Setup<bool>("speechSupported", _ => true).SetResult(false);
    }

    private IRenderedComponent<TmAiComposer> RenderComposer(
        Action<ComponentParameterCollectionBuilder<TmAiComposer>>? configure = null)
        => Render<TmAiComposer>(parameters => configure?.Invoke(parameters));

    private static InputFileContent Text(string name, string body = "hello")
        => InputFileContent.CreateFromText(body, name, contentType: "text/plain");

    [Fact]
    public void TooManyFiles_AreRejectedWithALocalizedMessage()
    {
        SetupInterop();
        var changed = new List<IReadOnlyList<AiComposerAttachment>>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.MaxAttachments, 2)
            .Add(component => component.AttachmentsChanged, value => changed.Add(value)));

        cut.FindComponent<InputFile>().UploadFiles(Text("a.txt"), Text("b.txt"), Text("c.txt"));

        cut.FindAll(".tm-ai-composer__attachment").Should().HaveCount(2, "the limit caps the list");
        cut.Find("[data-testid='ai-composer-announcement']").TextContent.Trim()
            .Should().Be("You can attach at most 2 files.");
        changed.Should().ContainSingle(
            "one picker interaction raises once — a rejected file never reaches the host");
        changed[0].Should().HaveCount(2);
    }

    /// <summary>
    /// The limit message is a plural family: "soubor / soubory / souborů" (and "file / files" in English).
    /// A single always-genitive template is wrong for 1 and for 2–4 in Czech and for 1 in English.
    /// </summary>
    [Theory]
    [InlineData(1, "Přiložit lze nejvýše 1 soubor.")]
    [InlineData(2, "Přiložit lze nejvýše 2 soubory.")]
    [InlineData(5, "Přiložit lze nejvýše 5 souborů.")]
    public void TheAttachmentLimit_AgreesWithItsCountInCzech(int limit, string expected)
    {
        UseCzechLocalization();
        SetupInterop();
        var cut = RenderComposer(parameters => parameters.Add(component => component.MaxAttachments, limit));

        var files = Enumerable.Range(1, limit + 1).Select(index => Text($"file{index}.txt")).ToArray();
        cut.FindComponent<InputFile>().UploadFiles(files);

        cut.FindAll(".tm-ai-composer__attachment").Should().HaveCount(limit);
        cut.Find("[data-testid='ai-composer-announcement']").TextContent.Trim().Should().Be(expected);
    }

    /// <summary>
    /// A file picked while the host's handler is still running belongs to the NEXT prompt: the submit drops
    /// the attachments it actually sent and leaves the newcomer alone, instead of clearing the list blindly.
    /// </summary>
    [Fact]
    public async Task AnAttachmentPickedWhileTheHandlerRuns_SurvivesThePostSubmitClear()
    {
        SetupInterop();
        var gate = new TaskCompletionSource();
        var changed = new List<IReadOnlyList<AiComposerAttachment>>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, EventCallback.Factory.Create<AiComposerSubmit>(
                this, async (AiComposerSubmit _) => await gate.Task))
            .Add(component => component.AttachmentsChanged, value => changed.Add(value)));

        cut.FindComponent<InputFile>().UploadFiles(Text("sent.txt"));
        cut.Find("textarea").Input("Look at this");
        var first = cut.InvokeAsync(() => cut.Instance.SubmitAsync());

        cut.FindComponent<InputFile>().UploadFiles(Text("next.txt"));
        await cut.InvokeAsync(() => gate.SetResult());
        await first;

        cut.FindAll(".tm-ai-composer__attachment").Should().ContainSingle();
        cut.Find(".tm-ai-composer__attachment-name").TextContent.Trim().Should().Be("next.txt",
            "the file the reader added during the send is not part of what was submitted");
        changed[^1].Select(attachment => attachment.Name).Should().Equal("next.txt");
    }

    [Fact]
    public void AnOversizedFile_IsRejectedWithALocalizedMessage()
    {
        SetupInterop();
        var cut = RenderComposer(parameters => parameters.Add(component => component.MaxFileSize, 4));

        cut.FindComponent<InputFile>().UploadFiles(Text("small.txt", "hey"), Text("big.txt", "too large"));

        cut.FindAll(".tm-ai-composer__attachment").Should().ContainSingle();
        cut.Find(".tm-ai-composer__attachment-name").TextContent.Trim().Should().Be("small.txt");
        cut.Find("[data-testid='ai-composer-announcement']").TextContent.Trim()
            .Should().Be("big.txt is larger than the 4 B limit.");

        cut.Find("[data-testid='ai-composer-announcement']").GetAttribute("role").Should().Be("status");
        cut.Find("[data-testid='ai-composer-announcement']").GetAttribute("aria-live").Should().Be("polite");
    }

    [Fact]
    public void MaxFileSizeZero_DisablesTheSizeCheck()
    {
        SetupInterop();
        var cut = RenderComposer(parameters => parameters.Add(component => component.MaxFileSize, 0));

        cut.FindComponent<InputFile>().UploadFiles(Text("big.txt", new string('x', 4096)));

        cut.FindAll(".tm-ai-composer__attachment").Should().ContainSingle();
        cut.Find("[data-testid='ai-composer-announcement']").TextContent.Trim().Should().BeEmpty();
    }

    [Fact]
    public void TheAcceptedTypes_AreForwardedToThePicker()
    {
        SetupInterop();
        var cut = RenderComposer(parameters => parameters.Add(component => component.Accept, "image/*,.pdf"));

        cut.Find("input[type='file']").GetAttribute("accept").Should().Be("image/*,.pdf");
    }

    [Fact]
    public void TheFileInput_IsNamedAndKeyboardReachable()
    {
        SetupInterop();
        var cut = RenderComposer();

        var input = cut.Find("input[type='file']");
        input.GetAttribute("aria-label").Should().Be("Attach files",
            "the visually clipped file input still needs a name");
        input.HasAttribute("multiple").Should().BeTrue("the composer accepts several files at once");
    }

    [Fact]
    public void TheBatch_IsRaisedOnceForAllAcceptedFiles()
    {
        SetupInterop();
        var changed = new List<IReadOnlyList<AiComposerAttachment>>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.AttachmentsChanged, value => changed.Add(value)));

        cut.FindComponent<InputFile>().UploadFiles(Text("a.txt"), Text("b.txt"));

        changed.Should().ContainSingle("one picker interaction is one state change, not one per file");
        changed[0].Select(attachment => attachment.Name).Should().Equal("a.txt", "b.txt");
    }

    [Fact]
    public void TheAttachmentKeepsTheBrowserFileAsItsOpaqueContent()
    {
        SetupInterop();
        var cut = RenderComposer();
        var content = Text("report.pdf", "pdf body");

        cut.FindComponent<InputFile>().UploadFiles(content);

        cut.Instance.CurrentAttachments.Should().ContainSingle();
        cut.Instance.CurrentAttachments[0].Content.Should().BeAssignableTo<IBrowserFile>(
            "the host reads the bytes through the browser file the component carries untouched");
        cut.Instance.CurrentAttachments[0].Name.Should().Be("report.pdf");
    }
}
