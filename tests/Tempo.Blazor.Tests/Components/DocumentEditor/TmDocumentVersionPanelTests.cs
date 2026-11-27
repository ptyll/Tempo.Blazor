using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class TmDocumentVersionPanelTests : LocalizationTestBase
{
    [Theory]
    [InlineData(DocumentVersionKind.Minor, "Minor version")]
    [InlineData(DocumentVersionKind.Major, "Major version")]
    [InlineData(DocumentVersionKind.Autosave, "Autosave")]
    [InlineData(DocumentVersionKind.Restore, "Restore point")]
    public void Meta_UsesLocalizedKind_NotEnumName(DocumentVersionKind kind, string expectedLabel)
    {
        var cut = Render<TmDocumentVersionPanel>(parameters => parameters
            .Add(p => p.Versions, new[] { CreateVersion(kind) }));

        var meta = cut.Find(".tm-document-version-panel__item-meta").TextContent;

        meta.Should().StartWith(expectedLabel + " ");
        meta.Split(" • ")[0].Should().Be(expectedLabel);
    }

    [Fact]
    public void Meta_UsesCzechKindLabel_WithCzechLocalizer()
    {
        UseCzechLocalization();

        var cut = Render<TmDocumentVersionPanel>(parameters => parameters
            .Add(p => p.Versions, new[] { CreateVersion(DocumentVersionKind.Major) }));

        cut.Find(".tm-document-version-panel__item-meta").TextContent.Split(" • ")[0].Should().Be("Hlavní verze");
    }

    private static DocumentVersion CreateVersion(DocumentVersionKind kind)
        => new()
        {
            Id = "version-" + kind,
            DocumentId = "doc-1",
            Kind = kind,
            Label = "v1",
            Author = new DocumentEditorAuthor { Id = "author-1", DisplayName = "Ada Author" },
            CreatedAt = new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.Zero)
        };
}
