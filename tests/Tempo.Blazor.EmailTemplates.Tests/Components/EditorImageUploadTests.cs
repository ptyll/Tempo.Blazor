using Tempo.Blazor.Components.Files;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7 — <c>ImageUploadOptions</c> (§3.2). The accept list and the byte limit are enforced <b>before</b>
/// the host's upload handler runs, and a host that throws or returns an empty URL produces a localized
/// error in the panel instead of a crash or a broken <c>src</c>: the block's <c>Src</c> never changes on a
/// rejected upload.
/// </summary>
public class EditorImageUploadTests : BunitContext
{
    private readonly EmailImageBlock _image = new() { Src = "https://cdn.example.com/original.png" };
    private EmailTemplateDocument _document = new();

    public EditorImageUploadTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();

        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(_image);
        section.Columns.Add(column);
        _document.Sections.Add(section);
    }

    private IRenderedComponent<TmEmailPropertyPanel> RenderPanel(
        Func<IBrowserFile, Task<string>>? upload,
        ImageUploadOptions? options = null,
        List<string>? changes = null)
        => Render<TmEmailPropertyPanel>(p =>
        {
            p.Add(c => c.Document, _document);
            p.Add(c => c.SelectedId, _image.Id);
            p.Add(c => c.OnImageUpload, upload);
            p.Add(c => c.ImageUpload, options);
            p.Add(c => c.OnChanged, () =>
            {
                changes?.Add("changed");
                return Task.CompletedTask;
            });
        });

    // The handler calls StateHasChanged, so it has to run on the renderer's dispatcher (a real file pick
    // is dispatched by the browser, a test call is not).
    private Task UploadAsync(IRenderedComponent<TmEmailPropertyPanel> cut, IBrowserFile file)
        => cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected
            .InvokeAsync(new InputFileChangeEventArgs(new[] { file })));

    [Fact]
    public async Task AFileOverTheByteLimit_IsRejectedBeforeTheHostIsCalled()
    {
        var calls = 0;
        var changes = new List<string>();
        var cut = RenderPanel(
            _ => { calls++; return Task.FromResult("https://cdn.example.com/new.png"); },
            new ImageUploadOptions { Accept = "image/*", MaxBytes = 1024 },
            changes);

        await UploadAsync(cut, new FakeBrowserFile("big.png", "image/png", 4096));

        calls.Should().Be(0, "the limit is checked before the host pays for an upload");
        _image.Src.Should().Be("https://cdn.example.com/original.png");
        changes.Should().BeEmpty();
        var error = cut.Find("[data-tm-image-upload-error]").TextContent;
        error.Should().NotBeNullOrWhiteSpace();
        error.Should().NotContain("EmailEditor_", "the panel shows the localized text, not the resource key");
        error.Should().Contain("1", "the message states the configured limit");
    }

    [Fact]
    public async Task AFileOutsideTheAcceptList_IsRejectedBeforeTheHostIsCalled()
    {
        var calls = 0;
        var cut = RenderPanel(
            _ => { calls++; return Task.FromResult("https://cdn.example.com/new.png"); },
            new ImageUploadOptions { Accept = "image/png", MaxBytes = 1024 * 1024 });

        await UploadAsync(cut, new FakeBrowserFile("vector.svg", "image/svg+xml", 128));

        calls.Should().Be(0);
        _image.Src.Should().Be("https://cdn.example.com/original.png");
        var error = cut.Find("[data-tm-image-upload-error]").TextContent;
        error.Should().NotContain("EmailEditor_", "the panel shows the localized text, not the resource key");
        error.Should().Contain("image/png", "the message says which types are accepted");
    }

    [Fact]
    public async Task AHostException_ShowsALocalizedError_AndKeepsTheOriginalSource()
    {
        var cut = RenderPanel(_ => throw new InvalidOperationException("storage down"),
            new ImageUploadOptions { Accept = "image/*", MaxBytes = 1024 * 1024 });

        await UploadAsync(cut, new FakeBrowserFile("ok.png", "image/png", 128));

        _image.Src.Should().Be("https://cdn.example.com/original.png");
        var error = cut.Find("[data-tm-image-upload-error]").TextContent;
        error.Should().NotBeNullOrWhiteSpace();
        error.Should().NotContain("EmailEditor_", "the panel shows the localized text, not the resource key");
    }

    [Fact]
    public async Task AnEmptyHostUrl_ShowsALocalizedError_AndKeepsTheOriginalSource()
    {
        var cut = RenderPanel(_ => Task.FromResult(string.Empty),
            new ImageUploadOptions { Accept = "image/*", MaxBytes = 1024 * 1024 });

        await UploadAsync(cut, new FakeBrowserFile("empty.png", "image/png", 128));

        _image.Src.Should().Be("https://cdn.example.com/original.png");
        var error = cut.Find("[data-tm-image-upload-error]").TextContent;
        error.Should().NotBeNullOrWhiteSpace();
        error.Should().NotContain("EmailEditor_", "the panel shows the localized text, not the resource key");
    }

    [Fact]
    public async Task AnAcceptedUpload_ReplacesTheSource_AndRaisesAChange()
    {
        var changes = new List<string>();
        var cut = RenderPanel(_ => Task.FromResult("https://cdn.example.com/new.png"),
            new ImageUploadOptions { Accept = "image/*", MaxBytes = 1024 * 1024 }, changes);

        await UploadAsync(cut, new FakeBrowserFile("small.png", "image/png", 1024));

        _image.Src.Should().Be("https://cdn.example.com/new.png");
        changes.Should().ContainSingle();
        cut.FindAll("[data-tm-image-upload-error]").Should().BeEmpty();
    }

    [Fact]
    public async Task NoOptionsMeansSensibleDefaults_ThatStillValidate()
    {
        var calls = 0;
        var cut = RenderPanel(_ => { calls++; return Task.FromResult("https://cdn.example.com/new.png"); });

        // 40 MB is far beyond any sane inline image and must be refused by the default limit.
        await UploadAsync(cut, new FakeBrowserFile("huge.png", "image/png", 40L * 1024 * 1024));

        calls.Should().Be(0);
        cut.FindAll("[data-tm-image-upload-error]").Should().ContainSingle();
    }

    private sealed class FakeBrowserFile : IBrowserFile
    {
        private readonly byte[] _content;

        public FakeBrowserFile(string name, string contentType, long size)
        {
            Name = name;
            ContentType = contentType;
            Size = size;
            _content = new byte[Math.Min(size, 16)];
        }

        public string Name { get; }
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UnixEpoch;
        public long Size { get; }
        public string ContentType { get; }

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            if (Size > maxAllowedSize) throw new IOException($"The file is larger than {maxAllowedSize} bytes.");
            return new MemoryStream(_content, writable: false);
        }
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
