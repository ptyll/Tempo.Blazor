using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Phase 7 - the preview decides "did the document change?" from a cheap signature (the editor's document
/// revision) instead of serializing the whole document on every parent re-render. A parent that re-renders
/// without a change must not reach <see cref="IEmailPreviewRenderer"/> (decision EMAIL-PREVIEW-DOCUMENT-REVISION).
/// </summary>
public class PreviewChangeSignatureTests : BunitContext
{
    private readonly CountingRenderer _renderer = new();

    public PreviewChangeSignatureTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new Echo());
        Services.AddSingleton<ITmEmailLocalizer>(new Echo());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddTempoEmailTemplateEditor();
        Services.AddHttpClient();
        Services.AddSingleton<IEmailPreviewRenderer>(_renderer);
        Services.AddSingleton(TimeProvider.System);
    }

    private static EmailTemplateDocument Doc()
    {
        var doc = new EmailTemplateDocument { Subject = "Subj" };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = "Hello" });
        section.Columns.Add(column);
        doc.Sections.Add(section);
        return doc;
    }

    [Fact]
    public void A_parent_re_render_with_the_same_revision_does_not_call_the_renderer()
    {
        var doc = Doc();
        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, doc).Add(c => c.DocumentRevision, 1L));
        cut.WaitForAssertion(() => _renderer.Calls.Should().Be(1));

        for (var i = 0; i < 5; i++)
        {
            cut.Render(p => p.Add(c => c.Document, doc).Add(c => c.DocumentRevision, 1L));
        }

        _renderer.Calls.Should().Be(1, "nothing changed: the signature is the revision, no re-render and no request");
    }

    [Fact]
    public void The_revision_is_the_signature_not_the_content()
    {
        // Proof that the preview does NOT look at the content when a revision is supplied: an in-place edit
        // that the host did not announce with a new revision is (by contract) not a change.
        var doc = Doc();
        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, doc).Add(c => c.DocumentRevision, 1L));
        cut.WaitForAssertion(() => _renderer.Calls.Should().Be(1));

        ((EmailTextBlock)doc.Sections[0].Columns[0].Blocks[0]).Content = "Edited without a revision bump";
        cut.Render(p => p.Add(c => c.Document, doc).Add(c => c.DocumentRevision, 1L));

        _renderer.Calls.Should().Be(1);
    }

    [Fact]
    public void A_new_revision_renders_again_with_the_edited_content()
    {
        var doc = Doc();
        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, doc).Add(c => c.DocumentRevision, 1L));
        cut.WaitForAssertion(() => _renderer.Calls.Should().Be(1));

        ((EmailTextBlock)doc.Sections[0].Columns[0].Blocks[0]).Content = "Edited";
        cut.Render(p => p.Add(c => c.Document, doc).Add(c => c.DocumentRevision, 2L));

        cut.WaitForAssertion(() => _renderer.Calls.Should().Be(2));
    }

    [Fact]
    public void A_different_document_instance_renders_again_even_with_the_same_revision()
    {
        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, Doc()).Add(c => c.DocumentRevision, 1L));
        cut.WaitForAssertion(() => _renderer.Calls.Should().Be(1));

        cut.Render(p => p.Add(c => c.Document, Doc()).Add(c => c.DocumentRevision, 1L));

        cut.WaitForAssertion(() => _renderer.Calls.Should().Be(2));
    }

    [Fact]
    public void Without_a_revision_the_preview_still_compares_content_as_before()
    {
        var doc = Doc();
        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, doc));
        cut.WaitForAssertion(() => _renderer.Calls.Should().Be(1));

        cut.Render(p => p.Add(c => c.Document, doc));
        _renderer.Calls.Should().Be(1, "same content: the request key dedupes");

        ((EmailTextBlock)doc.Sections[0].Columns[0].Blocks[0]).Content = "Edited";
        cut.Render(p => p.Add(c => c.Document, doc));
        cut.WaitForAssertion(() => _renderer.Calls.Should().Be(2));
    }

    [Fact]
    public void The_editors_own_preview_rerenders_only_when_the_document_changed()
    {
        var doc = Doc();
        var editor = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));
        editor.Find("[data-tm-preview-btn]").Click();
        editor.WaitForAssertion(() => _renderer.Calls.Should().Be(1));

        editor.Render(p => p.Add(c => c.Document, doc));
        editor.Render(p => p.Add(c => c.Document, doc));
        _renderer.Calls.Should().Be(1, "a parent re-render without an edit is not a document change");

        editor.FindAll("[data-tm-block=\"text\"]").First().Click();   // adds a block -> a commit -> a new revision
        editor.WaitForAssertion(() => _renderer.Calls.Should().Be(2));
    }

    private sealed class CountingRenderer : IEmailPreviewRenderer
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public Task<EmailPreviewResult> RenderAsync(EmailPreviewRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new EmailPreviewResult("<html><body>x</body></html>", "x", "s", null, [], EmailPreviewStatus.Rendered));
        }
    }

    private sealed class Echo : ITmLocalizer, ITmEmailLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
