using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Localization;
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

    // ─── Redesign 146c7e1b, phase 6 (DE-2.5) ──────────────────────────────────

    [Fact]
    public void CreateVersion_IsThePrimaryFullWidthAction_AndTheHeaderIsTheSharedOne()
    {
        var cut = Render<TmDocumentVersionPanel>(parameters => parameters
            .Add(p => p.Versions, new[] { CreateVersion(DocumentVersionKind.Minor) }));

        var create = cut.Find("[data-testid='document-version-create-open']");
        create.ClassList.Should().Contain("tm-btn-primary").And.Contain("tm-document-version-panel__create");
        create.TextContent.Trim().Should().Be("Create version");
        cut.Find("[data-testid='document-inspector-header'] .tm-document-inspector-header__title").TextContent.Should().Be("Versions");
        cut.FindAll("[data-testid='document-inspector-header'] h3").Should().ContainSingle("the panel keeps its h3 heading level");
    }

    [Fact]
    public void Close_IsOneButtonOfTheHeader_AndAbsentWhileHostedInTheSidePanel()
    {
        var hosted = Render<TmDocumentVersionPanel>(parameters => parameters.Add(p => p.ShowClose, false));
        hosted.FindAll("[data-testid='document-version-panel-close']").Should().BeEmpty();

        var closed = 0;
        var standalone = Render<TmDocumentVersionPanel>(parameters => parameters
            .Add(p => p.OnClose, () => closed++));
        standalone.FindAll("[data-testid='document-version-panel-close']").Should().ContainSingle();
        standalone.Find("[data-testid='document-version-panel-close']").ClassList.Should().Contain("tm-document-inspector-header__close");
        standalone.Find("[data-testid='document-version-panel-close']").Click();
        closed.Should().Be(1);
    }

    [Fact]
    public void CurrentPreviewBadge_IsOnlyOnThePreviewedVersion()
    {
        var previewed = CreateVersion(DocumentVersionKind.Minor, "v-prev");
        var other = CreateVersion(DocumentVersionKind.Major, "v-other");

        var cut = Render<TmDocumentVersionPanel>(parameters => parameters
            .Add(p => p.Versions, new[] { previewed, other })
            .Add(p => p.PreviewVersion, previewed));

        var badges = cut.FindAll(".tm-document-version-panel__badge");
        badges.Should().ContainSingle();
        badges[0].TextContent.Should().Be("Current preview");
        badges[0].Closest("li")!.QuerySelector(".tm-document-version-panel__item--active").Should().NotBeNull();

        var none = Render<TmDocumentVersionPanel>(parameters => parameters.Add(p => p.Versions, new[] { previewed, other }));
        none.FindAll(".tm-document-version-panel__badge").Should().BeEmpty();
    }

    [Fact]
    public void PreviewHighlight_IsOnTheWholeEntry_SoTheBaseAndCompareButtonsSitInsideIt()
    {
        var previewed = CreateVersion(DocumentVersionKind.Minor, "v-prev");
        var other = CreateVersion(DocumentVersionKind.Major, "v-other");

        var cut = Render<TmDocumentVersionPanel>(parameters => parameters
            .Add(p => p.Versions, new[] { previewed, other })
            .Add(p => p.PreviewVersion, previewed));

        var entries = cut.FindAll("li.tm-document-version-panel__entry");
        var highlighted = entries.Single(entry => entry.ClassList.Contains("tm-document-version-panel__item--preview"));
        highlighted.QuerySelector("[data-testid='document-version-diff-base']").Should().NotBeNull("the diff buttons are inside the highlighted root");
        highlighted.QuerySelector("[data-testid='document-version-item']")!.GetAttribute("aria-current").Should().Be("true");
        entries.Count(entry => entry.ClassList.Contains("tm-document-version-panel__item--preview")).Should().Be(1);
    }

    [Fact]
    public void StatusMessage_IsAPoliteLiveRegionThatIsAlwaysInTheDom()
    {
        var empty = Render<TmDocumentVersionPanel>();
        var region = empty.Find("[data-testid='document-version-status']");
        region.GetAttribute("role").Should().Be("status");
        region.HasAttribute("hidden").Should().BeTrue("with no message the region is in the DOM but not shown");

        var cut = Render<TmDocumentVersionPanel>(parameters => parameters.Add(p => p.StatusMessage, "Version created."));
        var filled = cut.Find("[data-testid='document-version-status']");
        filled.TextContent.Should().Be("Version created.");
        filled.HasAttribute("hidden").Should().BeFalse();
        filled.GetAttribute("role").Should().Be("status");
    }

    [Fact]
    public void Meta_TimeGoesThroughTheRelativeTimeFormatter()
    {
        var formatter = Substitute.For<ITmRelativeTimeFormatter>();
        formatter.Format(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<TimeZoneInfo?>()).Returns("yesterday at 4:20 PM");
        formatter.Absolute(Arg.Any<DateTimeOffset>(), Arg.Any<TimeZoneInfo?>()).Returns("4 Mar 2026 10:30");
        Services.AddSingleton(formatter);

        var cut = Render<TmDocumentVersionPanel>(parameters => parameters
            .Add(p => p.Versions, new[] { CreateVersion(DocumentVersionKind.Minor) }));

        var meta = cut.Find(".tm-document-version-panel__item-meta");
        meta.TextContent.Should().Be("Minor version • yesterday at 4:20 PM • Ada Author");
        meta.QuerySelector("time")!.GetAttribute("title").Should().Be("4 Mar 2026 10:30");
    }

    [Fact]
    public void ActiveDiffButton_IsPrimary_AndTheOthersAreSecondary()
    {
        var first = CreateVersion(DocumentVersionKind.Minor, "v1");
        var second = CreateVersion(DocumentVersionKind.Major, "v2");
        var cut = Render<TmDocumentVersionPanel>(parameters => parameters.Add(p => p.Versions, new[] { first, second }));

        cut.FindAll("[data-testid='document-version-diff-base']").Should().OnlyContain(button => button.ClassList.Contains("tm-btn-secondary"));

        cut.FindAll("[data-testid='document-version-diff-compare']")[0].Click();

        var active = cut.FindAll("[data-testid='document-version-diff-compare']")[0];
        active.ClassList.Should().Contain("tm-btn-primary").And.Contain("tm-document-version-panel__diff-button--active");
        active.GetAttribute("aria-pressed").Should().Be("true");
        cut.FindAll("[data-testid='document-version-diff-compare']")[1].ClassList.Should().Contain("tm-btn-secondary").And.NotContain("tm-btn-primary");
        cut.FindAll("[data-testid='document-version-diff-base']")[0].ClassList.Should().NotContain("tm-btn-primary");
    }

    [Fact]
    public void ThePreviewStrip_LivesInTheDocRegion_NotInThePanel_WhenTheHostSaysSo()
    {
        var previewed = CreateVersion(DocumentVersionKind.Minor, "v-prev");

        var embedded = Render<TmDocumentVersionPanel>(parameters => parameters
            .Add(p => p.Versions, new[] { previewed })
            .Add(p => p.PreviewVersion, previewed)
            .Add(p => p.ShowPreviewStrip, false));
        embedded.FindAll("[data-testid='document-version-preview-state']").Should().BeEmpty();

        var standalone = Render<TmDocumentVersionPanel>(parameters => parameters
            .Add(p => p.Versions, new[] { previewed })
            .Add(p => p.PreviewVersion, previewed));
        standalone.Find("[data-testid='document-version-preview-state']").Should().NotBeNull("a standalone panel keeps its own strip");
        standalone.Find("[data-testid='document-version-current']").Should().NotBeNull();
        standalone.Find("[data-testid='document-version-restore']").Should().NotBeNull();
    }

    private static DocumentVersion CreateVersion(DocumentVersionKind kind, string? id = null)
        => new()
        {
            Id = id ?? "version-" + kind,
            DocumentId = "doc-1",
            Kind = kind,
            Label = "v1",
            Author = new DocumentEditorAuthor { Id = "author-1", DisplayName = "Ada Author" },
            CreatedAt = new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.Zero)
        };
}
