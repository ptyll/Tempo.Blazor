using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

public class TmPdfTemplateDesignerTests : LocalizationTestBase
{
    [Fact]
    public void Render_RootAndEmptyState()
    {
        var cut = Render<TmPdfTemplateDesigner>();

        cut.Find(".tm-pdf-template-designer").Should().NotBeNull();
        cut.Find(".tm-empty-state").TextContent.Should().Contain("No documents");
    }

    [Fact]
    public void Canvas_IsNamedRegion_NotMainLandmark()
    {
        // N190 — the host layout owns the single <main> landmark; the designer canvas is a
        // labelled <section> region instead (036ce5fd/a59cac9a rule, generalised).
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages()));

        cut.FindAll("main").Should().BeEmpty("the host layout owns the single <main> landmark");
        cut.Find("section.tm-pdf-template-designer__canvas")
            .GetAttribute("aria-label").Should().Be("Template canvas");
    }

    [Fact]
    public void Render_DocumentsAndFields_UsesViewerAndOverlay()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields()));

        cut.FindAll(".tm-document-page-viewer").Should().HaveCount(1);
        cut.FindAll(".tm-signing-field").Should().HaveCount(2);
        cut.Find(".tm-pdf-template-designer__page-label").TextContent.Should().Contain("1 / 2");
    }

    [Fact]
    public void CulturePreviewSelector_ChangesCultureAndKeepsSelectedField()
    {
        string? culture = null;
        var field = CreateField("field-1", "Name");
        field.Labels.Translations["cs-CZ"] = "Jméno";
        IRenderedComponent<TmPdfTemplateDesigner>? cut = null;
        cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, new[] { field })
                      .Add(p => p.SupportedCultures, new[] { "en-US", "cs-CZ" })
                      .Add(p => p.ShowCulturePreview, true)
                      .Add(p => p.Culture, "en-US")
                      .Add(p => p.CultureChanged, EventCallback.Factory.Create<string?>(this, value =>
                      {
                          culture = value;
                          cut!.Render(parameters => parameters.Add(p => p.Culture, value));
                      })));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find(".tm-pdf-template-designer__culture-preview").Change("cs-CZ");

        culture.Should().Be("cs-CZ");
        cut.Find("[data-field-uuid='field-1']").TextContent.Should().Contain("Jméno");
        cut.FindAll(".tm-signing-field--selected").Should().HaveCount(1);
        cut.Find(".tm-signing-field-editor-panel__localization").Should().NotBeNull();
    }

    [Fact]
    public void Render_ContinuousView_RendersAllPages()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.ViewMode, DocumentPageViewMode.Continuous));

        cut.FindAll(".tm-document-page-viewer").Should().HaveCount(2);
    }

    [Fact]
    public void PageNavigation_NextAndPreviousChangeVisiblePage()
    {
        int? pageIndex = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.PageIndexChanged, value => pageIndex = value));

        cut.Find("[data-page-key='attachment-1:0']").Should().NotBeNull();
        cut.Find(".tm-pdf-template-designer__next-page").Click();

        pageIndex.Should().Be(1);
        cut.Find("[data-page-key='attachment-1:1']").Should().NotBeNull();
        cut.Find(".tm-pdf-template-designer__page-label").TextContent.Should().Contain("2 / 2");

        cut.Find(".tm-pdf-template-designer__previous-page").Click();
        pageIndex.Should().Be(0);
        cut.Find("[data-page-key='attachment-1:0']").Should().NotBeNull();
    }

    [Fact]
    public void ZoomControls_UpdateScaleAndMode()
    {
        double? scale = null;
        DocumentPageZoomMode? zoomMode = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.ScaleChanged, value => scale = value)
                      .Add(p => p.ZoomModeChanged, value => zoomMode = value));

        cut.Find(".tm-pdf-template-designer__zoom-in").Click();

        scale.Should().Be(1.25);
        zoomMode.Should().Be(DocumentPageZoomMode.Custom);

        cut.Find(".tm-pdf-template-designer__fit-page").Click();

        scale.Should().Be(0.85);
        zoomMode.Should().Be(DocumentPageZoomMode.FitPage);
    }

    [Fact]
    public void Render_AcceptsFieldsChangedAndDocuments()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find(".tm-signing-field-editor-panel__name").Change("Updated name");

        captured.Should().NotBeNull();
        captured!.Single(field => field.Uuid == "field-1").Name.Should().Be("Updated name");
    }

    [Fact]
    public void Palette_RendersAllowedFieldTypesAndCanBeFiltered()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.AllowedFieldTypes, new[] { SigningFieldType.Text, SigningFieldType.Signature }));

        cut.FindAll(".tm-pdf-template-designer__palette-item").Should().HaveCount(2);
        cut.Find("[data-field-type='Text']").TextContent.Should().Contain("Text");
        cut.Find("[data-field-type='Signature']").TextContent.Should().Contain("Signature");
    }

    [Fact]
    public void Palette_ClickFieldType_EntersDrawMode()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages()));

        cut.Find("[data-field-type='Text']").Click();

        cut.Find(".tm-pdf-template-designer").ClassList.Should().Contain("tm-pdf-template-designer--drawing");
        cut.Find("[data-field-type='Text']").ClassList.Should().Contain("tm-pdf-template-designer__palette-item--active");
    }

    [Fact]
    public void Palette_DragDropFieldType_CreatesDefaultSizedField()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-type='Signature']").DragStart(new DragEventArgs());
        cut.Find(".tm-pdf-template-designer").ClassList.Should().Contain("tm-pdf-template-designer--dragging");

        var surface = cut.Find("[data-page-key='attachment-1:0'] .tm-pdf-template-designer__page-surface");
        surface.Drop(new DragEventArgs { OffsetX = 500, OffsetY = 500 });

        captured.Should().NotBeNull();
        var field = captured!.Should().ContainSingle().Subject;
        field.Type.Should().Be(SigningFieldType.Signature);
        field.Areas.Single().X.Should().BeApproximately(0.33, 0.001);
        field.Areas.Single().Y.Should().BeApproximately(0.4675, 0.001);
        field.Areas.Single().Width.Should().BeApproximately(0.34, 0.001);
        field.Areas.Single().Height.Should().BeApproximately(0.065, 0.001);
    }

    [Fact]
    public void Palette_DragDropFieldType_AfterCultureChangeCreatesLocalizedDefaultLabel()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Culture, "cs-CZ")
                      .Add(p => p.FallbackCulture, "en-US")
                      .Add(p => p.SupportedCultures, new[] { "en-US", "cs-CZ" })
                      .Add(p => p.ShowCulturePreview, true)
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-type='Signature']").DragStart(new DragEventArgs());
        cut.Find("[data-page-key='attachment-1:0'] .tm-pdf-template-designer__page-surface")
            .Drop(new DragEventArgs { OffsetX = 500, OffsetY = 500 });

        var field = captured!.Should().ContainSingle().Subject;
        field.Name.Should().Be("Signature");
        field.Labels.Default.Should().Be("Signature");
        field.Labels.Translations["cs-CZ"].Should().Be("Signature");
    }

    [Fact]
    public void Palette_WhenDisabled_IsHidden()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Disabled, true));

        cut.FindAll(".tm-pdf-template-designer__palette").Should().BeEmpty();
    }

    [Fact]
    public void DrawField_CreatesFieldAndArea()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.SubmitterRoles, CreateRoles())
                      .Add(p => p.SelectedSubmitterUuid, "role-2")
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-type='Text']").Click();
        var surface = cut.Find("[data-page-key='attachment-1:0'] .tm-pdf-template-designer__page-surface");
        surface.MouseDown(new MouseEventArgs { OffsetX = 100, OffsetY = 100 });
        surface.MouseMove(new MouseEventArgs { OffsetX = 260, OffsetY = 180 });

        cut.Find(".tm-pdf-template-designer__draft").Should().NotBeNull();

        surface.MouseUp(new MouseEventArgs { OffsetX = 260, OffsetY = 180 });

        captured.Should().NotBeNull();
        var field = captured!.Single();
        field.Type.Should().Be(SigningFieldType.Text);
        field.SubmitterUuid.Should().Be("role-2");
        field.Areas.Single().AttachmentUuid.Should().Be("attachment-1");
        field.Areas.Single().Page.Should().Be(0);
    }

    [Fact]
    public void DrawField_SmallRectangle_IsIgnored()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-type='Text']").Click();
        var surface = cut.Find("[data-page-key='attachment-1:0'] .tm-pdf-template-designer__page-surface");
        surface.MouseDown(new MouseEventArgs { OffsetX = 100, OffsetY = 100 });
        surface.MouseUp(new MouseEventArgs { OffsetX = 104, OffsetY = 104 });

        captured.Should().BeNull();
    }

    [Fact]
    public void SelectMoveAndResize_UpdateFieldArea()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find("[data-field-uuid='field-1']").MouseDown(new MouseEventArgs { ClientX = 10, ClientY = 10 });
        cut.Find(".tm-pdf-template-designer").MouseMove(new MouseEventArgs { ClientX = 110, ClientY = 60 });
        cut.Find(".tm-pdf-template-designer").MouseUp();

        captured!.Single(field => field.Uuid == "field-1").Areas.Single().X.Should().BeApproximately(0.2, 0.001);
        cut.Find("[data-handle='SouthEast']").MouseDown(new MouseEventArgs { ClientX = 100, ClientY = 100 });
        cut.Find(".tm-pdf-template-designer").MouseMove(new MouseEventArgs { ClientX = 200, ClientY = 200 });
        cut.Find(".tm-pdf-template-designer").MouseUp();

        captured!.Single(field => field.Uuid == "field-1").Areas.Single().Width.Should().BeGreaterThan(0.2);
    }

    [Fact]
    public void Select_WithControlKey_AddsToMultiSelect()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find("[data-field-uuid='field-2']").Click(new MouseEventArgs { CtrlKey = true });

        cut.FindAll(".tm-signing-field--selected").Should().HaveCount(2);
        cut.Find(".tm-pdf-template-designer__selection-bounds").Should().NotBeNull();
    }

    [Fact]
    public void Select_WithControlMouseDown_DoesNotReplaceExistingSelection()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find("[data-field-uuid='field-2']").MouseDown(new MouseEventArgs { CtrlKey = true });
        cut.Find("[data-field-uuid='field-2']").Click(new MouseEventArgs { CtrlKey = true });

        cut.FindAll(".tm-signing-field--selected").Should().HaveCount(2);
    }

    [Fact]
    public void DragSelectionBox_SelectsMultipleFields()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields()));

        var surface = cut.Find("[data-page-key='attachment-1:0'] .tm-pdf-template-designer__page-surface");
        surface.MouseDown(new MouseEventArgs { OffsetX = 0, OffsetY = 0 });
        surface.MouseMove(new MouseEventArgs { OffsetX = 500, OffsetY = 500 });
        surface.MouseUp(new MouseEventArgs { OffsetX = 500, OffsetY = 500 });

        cut.FindAll(".tm-signing-field--selected").Should().HaveCount(2);
    }

    [Fact]
    public void DeleteSelected_RemovesMultipleFields()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find("[data-field-uuid='field-2']").Click(new MouseEventArgs { CtrlKey = true });
        cut.Find(".tm-pdf-template-designer__delete-selected").Click();

        captured.Should().NotBeNull();
        captured.Should().BeEmpty();
    }

    [Fact]
    public void DeleteKey_RemovesSelectedField()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find(".tm-pdf-template-designer").KeyDown(new KeyboardEventArgs { Key = "Delete" });

        captured.Should().NotBeNull();
        captured!.Should().ContainSingle(field => field.Uuid == "field-2");
    }

    [Fact]
    public void ContextMenus_RenderUsingContextMenuComponent()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find("[data-field-uuid='field-1']").ContextMenu();

        cut.Find(".tm-context-menu-wrapper").Should().NotBeNull();
        cut.Markup.Should().Contain("Copy field");
        cut.Markup.Should().Contain("Delete field");
        cut.Markup.Should().NotContain("Settings");
    }

    [Fact]
    public void PageContextMenu_OffersPasteAndAutodetect()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.OnDetectFields, () => Task.FromResult<IReadOnlyList<SigningField>>([])));

        cut.Find("[data-page-key='attachment-1:0'] .tm-pdf-template-designer__page-surface").ContextMenu();

        cut.Markup.Should().Contain("Paste field");
        cut.Markup.Should().Contain("Autodetect fields");
    }

    [Fact]
    public void CopyPaste_CreatesFieldCopyOnCurrentPage()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields().Take(1).ToArray())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").ContextMenu();
        cut.Find(".tm-pdf-template-designer__copy-field").Click();

        cut.FindAll(".tm-pdf-template-designer__context-actions").Should().BeEmpty();
        cut.Find(".tm-pdf-template-designer__clipboard-status").TextContent.Should().Contain("Field copied");

        cut.Find(".tm-pdf-template-designer__next-page").Click();
        cut.Find("[data-page-key='attachment-1:1'] .tm-pdf-template-designer__page-surface")
            .ContextMenu(new MouseEventArgs { OffsetX = 500, OffsetY = 500 });
        cut.Find(".tm-pdf-template-designer__paste-field").Click();

        captured.Should().NotBeNull();
        captured.Should().HaveCount(2);
        captured!.Select(field => field.Uuid).Distinct().Should().HaveCount(2);
        var pastedArea = captured!.Last().Areas.Single();
        pastedArea.Page.Should().Be(1);
        pastedArea.X.Should().BeApproximately(0.4, 0.001);
        pastedArea.Y.Should().BeApproximately(0.46, 0.001);
        cut.Find(".tm-pdf-template-designer__clipboard-status").TextContent.Should().Contain("Field pasted");
    }

    [Fact]
    public void CopySelection_PastesAllSelectedFields()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find("[data-field-uuid='field-2']").Click(new MouseEventArgs { CtrlKey = true });
        cut.Find("[data-field-uuid='field-1']").ContextMenu();
        cut.Find(".tm-pdf-template-designer__copy-selection").Click();

        cut.Find(".tm-pdf-template-designer__clipboard-status").TextContent.Should().Contain("Selection copied");

        cut.Find(".tm-pdf-template-designer__next-page").Click();
        cut.Find("[data-page-key='attachment-1:1'] .tm-pdf-template-designer__page-surface")
            .ContextMenu(new MouseEventArgs { OffsetX = 700, OffsetY = 700 });
        cut.Find(".tm-pdf-template-designer__paste-field").Click();

        captured.Should().NotBeNull();
        captured!.Should().HaveCount(4);
        captured!.Count(field => field.Areas.Single().Page == 1).Should().Be(2);
        cut.FindAll(".tm-signing-field--selected").Should().HaveCount(2);
    }

    [Fact]
    public void CopyToAllPages_CreatesAreaOnEachDocumentPage()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields().Take(1).ToArray())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find(".tm-signing-field-editor-panel__copy-to-pages").Click();

        captured.Should().NotBeNull();
        captured!.Single().Areas.Should().HaveCount(2);
    }

    [Fact]
    public void DetectFields_AddsReturnedFieldsAndShowsLoadingState()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.OnDetectFields, () => Task.FromResult<IReadOnlyList<SigningField>>([CreateField("detected", "Detected")]))
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find(".tm-pdf-template-designer__detect").Click();

        captured.Should().NotBeNull();
        captured!.Single().Uuid.Should().Be("detected");
    }

    [Fact]
    public void DetectFields_Error_ShowsAlert()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.OnDetectFields, () => throw new InvalidOperationException("Detection failed")));

        cut.Find(".tm-pdf-template-designer__detect").Click();

        cut.Find(".tm-alert").TextContent.Should().Contain("Detection failed");
    }

    [Fact]
    public void MobileMode_UsesCompactPalette()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.MobileMode, true));

        cut.Find(".tm-pdf-template-designer").ClassList.Should().Contain("tm-pdf-template-designer--mobile");
        cut.Find(".tm-pdf-template-designer__palette").ClassList.Should().Contain("tm-pdf-template-designer__palette--compact");
        cut.Find(".tm-pdf-template-designer__mobile-draw").Click();
        cut.Find(".tm-pdf-template-designer").ClassList.Should().Contain("tm-pdf-template-designer--drawing");
    }

    private static IReadOnlyList<SigningDocumentPage> CreatePages()
    {
        return
        [
            new SigningDocumentPage
            {
                AttachmentUuid = "attachment-1",
                PageIndex = 0,
                ImageUrl = "/page-1.png",
                Width = 1000,
                Height = 1000,
                Label = "Page 1"
            },
            new SigningDocumentPage
            {
                AttachmentUuid = "attachment-1",
                PageIndex = 1,
                ImageUrl = "/page-2.png",
                Width = 1000,
                Height = 1000,
                Label = "Page 2"
            }
        ];
    }

    private static IReadOnlyList<SigningField> CreateFields()
    {
        return
        [
            CreateField("field-1", "Name", x: 0.1, y: 0.1),
            CreateField("field-2", "Signature", SigningFieldType.Signature, x: 0.35, y: 0.1)
        ];
    }

    private static SigningField CreateField(
        string uuid,
        string name,
        SigningFieldType type = SigningFieldType.Text,
        double x = 0.1,
        double y = 0.1)
    {
        return new SigningField
        {
            Uuid = uuid,
            Name = name,
            Type = type,
            Areas =
            [
                new SigningFieldArea
                {
                    Uuid = $"{uuid}-area",
                    AttachmentUuid = "attachment-1",
                    Page = 0,
                    X = x,
                    Y = y,
                    Width = 0.2,
                    Height = 0.08
                }
            ]
        };
    }

    private static IReadOnlyList<SigningSubmitterRole> CreateRoles()
    {
        return
        [
            new SigningSubmitterRole { Uuid = "role-1", Name = "Signer" },
            new SigningSubmitterRole { Uuid = "role-2", Name = "Approver" }
        ];
    }
}
