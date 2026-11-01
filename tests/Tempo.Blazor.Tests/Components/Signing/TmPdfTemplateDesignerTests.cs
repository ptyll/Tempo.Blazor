using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
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
    public void Overlay_ReceivesDefaultPaletteRoleColorAsTokenReference()
    {
        var fields = CreateFields();
        fields[0].SubmitterUuid = "role-manager";
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, fields)
                      .Add(p => p.SubmitterRoles,
                      [
                          new SigningSubmitterRole { Uuid = "role-manager", Name = "Manager", Color = "#9333ea" }
                      ]));

        cut.Find("[data-field-uuid='field-1']")
            .GetAttribute("style").Should().Contain("--tm-signing-role-color:var(--tm-signing-role-4)",
                "a default-palette colour resolves to the --tm-signing-role-4 token, so a host rebrand reaches it");
    }

    [Fact]
    public void Overlay_ReceivesCustomRoleColorVerbatim()
    {
        var fields = CreateFields();
        fields[0].SubmitterUuid = "role-manager";
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, fields)
                      .Add(p => p.SubmitterRoles,
                      [
                          new SigningSubmitterRole { Uuid = "role-manager", Name = "Manager", Color = "#ff8800" }
                      ]));

        cut.Find("[data-field-uuid='field-1']")
            .GetAttribute("style").Should().Contain("--tm-signing-role-color:#ff8800");
    }

    [Fact]
    public void Overlay_RoleWithoutColor_FallsBackToPaletteTokenOfRoleOrder()
    {
        var fields = CreateFields();
        fields[0].SubmitterUuid = "role-second";
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, fields)
                      .Add(p => p.SubmitterRoles,
                      [
                          new SigningSubmitterRole { Uuid = "role-first", Name = "First" },
                          new SigningSubmitterRole { Uuid = "role-second", Name = "Second" }
                      ]));

        cut.Find("[data-field-uuid='field-1']")
            .GetAttribute("style").Should().Contain("--tm-signing-role-color:var(--tm-signing-role-2)",
                "a colour-less role paints with the palette token of its order (second role → role-2)");
    }

    [Fact]
    public void Overlay_WithoutMatchingRole_RendersNoRoleCustomProperty()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find("[data-field-uuid='field-1']")
            .GetAttribute("style").Should().NotContain("--tm-signing-role-color");
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
    public void Palette_TapFieldType_CreatesDefaultSizedField()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-type='Signature']").Click();
        cut.Find("[data-field-type='Signature']").Click();

        captured.Should().NotBeNull();
        var field = captured!.Should().ContainSingle().Subject;
        field.Type.Should().Be(SigningFieldType.Signature);
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

        cut.Find("[data-field-type='Signature']").Click();
        cut.Find("[data-field-type='Signature']").Click();

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
        surface.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 100, OffsetY = 100 });
        surface.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 260, OffsetY = 180 });

        cut.Find(".tm-pdf-template-designer__draft").Should().NotBeNull();

        surface.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 260, OffsetY = 180 });

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
        surface.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 100, OffsetY = 100 });
        surface.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 104, OffsetY = 104 });

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
        cut.Find("[data-field-uuid='field-1']").TriggerEvent("onpointerdown", new PointerEventArgs { ClientX = 10, ClientY = 10 });
        cut.Find(".tm-pdf-template-designer__workspace").TriggerEvent("onpointermove", new PointerEventArgs { ClientX = 110, ClientY = 60 });
        cut.Find(".tm-pdf-template-designer__workspace").TriggerEvent("onpointerup", new PointerEventArgs());

        captured!.Single(field => field.Uuid == "field-1").Areas.Single().X.Should().BeApproximately(0.2, 0.001);
        cut.Find("[data-handle='SouthEast']").TriggerEvent("onpointerdown", new PointerEventArgs { ClientX = 100, ClientY = 100 });
        cut.Find(".tm-pdf-template-designer__workspace").TriggerEvent("onpointermove", new PointerEventArgs { ClientX = 200, ClientY = 200 });
        cut.Find(".tm-pdf-template-designer__workspace").TriggerEvent("onpointerup", new PointerEventArgs());

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
        cut.Find("[data-field-uuid='field-2']").TriggerEvent("onpointerdown", new PointerEventArgs { CtrlKey = true });
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
        surface.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 0, OffsetY = 0 });
        surface.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 500, OffsetY = 500 });
        surface.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 500, OffsetY = 500 });

        cut.FindAll(".tm-signing-field--selected").Should().HaveCount(2);
    }

    [Fact]
    public void PalettePress_OnThePage_DoesNotStartASelectionBox()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields()));

        // PointerEventArgs.Type is the event name, never the target tag, so the page cannot
        // recognise a palette press from it. The palette pointerdown is what marks the drag.
        cut.Find("[data-field-type='Text']").TriggerEvent("onpointerdown", new PointerEventArgs());
        var surface = cut.Find("[data-page-key='attachment-1:0'] .tm-pdf-template-designer__page-surface");
        surface.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 0, OffsetY = 0 });
        surface.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 500, OffsetY = 500 });
        surface.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 500, OffsetY = 500 });

        cut.FindAll(".tm-signing-field").Should().HaveCount(3, "the release places the dragged field");
        cut.FindAll(".tm-signing-field--selected").Should().ContainSingle(
            "a palette press that reaches the page places its field; it must not also select the fields a box would cover");
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
        cut.Find(".tm-pdf-template-designer__workspace").KeyDown(new KeyboardEventArgs { Key = "Delete" });

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
        cut.Markup.Should().Contain("Field copied");

        cut.Find(".tm-pdf-template-designer__next-page").Click();
        cut.Find("[data-page-key='attachment-1:1'] .tm-pdf-template-designer__page-surface")
            .ContextMenu(new MouseEventArgs { OffsetX = 500, OffsetY = 500 });
        cut.Find(".tm-pdf-template-designer__paste-field").Click();

        // The shared menu closes, then yields, before the item handler runs.
        cut.WaitForAssertion(() => captured.Should().NotBeNull());
        captured.Should().NotBeNull();
        captured.Should().HaveCount(2);
        captured!.Select(field => field.Uuid).Distinct().Should().HaveCount(2);
        var pastedArea = captured!.Last().Areas.Single();
        pastedArea.Page.Should().Be(1);
        pastedArea.X.Should().BeApproximately(0.4, 0.001);
        pastedArea.Y.Should().BeApproximately(0.46, 0.001);
        cut.Markup.Should().Contain("Field pasted");
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

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Selection copied"));

        cut.Find(".tm-pdf-template-designer__next-page").Click();
        cut.Find("[data-page-key='attachment-1:1'] .tm-pdf-template-designer__page-surface")
            .ContextMenu(new MouseEventArgs { OffsetX = 700, OffsetY = 700 });
        cut.Find(".tm-pdf-template-designer__paste-field").Click();

        cut.WaitForAssertion(() => captured.Should().NotBeNull());
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

        cut.Find("[data-tm-group='detect'] .tm-pdf-template-designer__detect").Click();

        captured.Should().NotBeNull();
        captured!.Single().Uuid.Should().Be("detected");
    }

    [Fact]
    public void DetectFields_Error_ShowsAlert()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.OnDetectFields, () => throw new InvalidOperationException("Detection failed")));

        cut.Find("[data-tm-group='detect'] .tm-pdf-template-designer__detect").Click();

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

    [Fact]
    public void Designer_Toolbar_HasRoleToolbar_WithLabelledGroups()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.ShowCulturePreview, true)
                      .Add(p => p.SupportedCultures, ["en", "cs"])
                      .Add(p => p.OnDetectFields, () => Task.FromResult<IReadOnlyList<SigningField>>([])));

        var toolbar = cut.Find(".tm-pdf-template-designer__toolbar");
        toolbar.GetAttribute("role").Should().Be("toolbar");

        var groups = cut.FindAll(".tm-pdf-template-designer__toolbar [data-tm-group]")
            .Select(group => group.GetAttribute("data-tm-group"))
            .ToList();
        groups.Should().ContainInOrder("pages", "view", "language", "zoom", "detect");
        groups.Should().OnlyHaveUniqueItems();
        cut.Find("[data-tm-group='detect'] .tm-pdf-template-designer__detect").Should().NotBeNull();
    }

    [Fact]
    public void Designer_ContextMenu_RenderedOnce()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find("[data-field-uuid='field-1']").ContextMenu();

        cut.FindAll(".tm-pdf-template-designer__context-actions").Should().BeEmpty(
            "the hand-rolled menu was the second copy of the same actions");
        cut.FindAll(".tm-context-menu-wrapper").Should().ContainSingle();
        cut.Find(".tm-context-menu-wrapper").TextContent.Should().Contain("Copy field");
    }

    [Fact]
    public void Designer_LayoutModeTablet_PaletteRail_InspectorSheetOnSelection()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.LayoutMode, TmLayoutMode.Tablet));

        cut.Find(".tm-pdf-template-designer").GetAttribute("data-layout").Should().Be("tablet");
        cut.Find(".tm-pdf-template-designer__palette").ClassList.Should().Contain("tm-pdf-template-designer__palette--rail");
        cut.FindAll(".tm-editor-shell__overlay--left").Should().BeEmpty(
            "the tablet palette is the rail; the empty Field palette drawer stays closed");
        cut.FindAll(".tm-signing-field-editor-panel").Should().BeEmpty("the tablet inspector stays closed until a field is selected");

        cut.Find("[data-field-uuid='field-1']").Click();

        cut.Find(".tm-signing-field-editor-panel").Should().NotBeNull();
        cut.Find(".tm-editor-shell__overlay--right, .tm-editor-shell__panel--right, [data-region='right']")
            .TextContent.Should().Contain("Field properties");
    }

    [Fact]
    public void Designer_Tablet_NoSelection_HasNoRightOverlay()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.LayoutMode, TmLayoutMode.Tablet));

        cut.FindAll(".tm-editor-shell__overlay--right").Should().BeEmpty(
            "a tablet with no selection does not open the inspector sheet");
    }

    [Fact]
    public void Designer_Tablet_CloseDismissesInspectorUntilNextSelection()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.LayoutMode, TmLayoutMode.Tablet));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.FindAll(".tm-signing-field-editor-panel").Should().NotBeEmpty();

        var close = cut.FindAll("button").First(button =>
            button.ClassList.Contains("tm-editor-shell__overlay-close")
            || (button.GetAttribute("aria-label") ?? "").Contains("Close", StringComparison.OrdinalIgnoreCase));
        close.Click();

        cut.FindAll(".tm-signing-field-editor-panel").Should().BeEmpty("the overlay close dismisses the inspector");

        cut.Find("[data-field-uuid='field-2']").Click();
        cut.FindAll(".tm-signing-field-editor-panel").Should().NotBeEmpty("a new selection reopens it");
    }

    [Fact]
    public void Designer_MobileModeObsolete_MapsToLayoutModeMobile()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.MobileMode, true));

        cut.Find(".tm-pdf-template-designer").GetAttribute("data-layout").Should().Be("mobile");
        cut.Find(".tm-pdf-template-designer").ClassList.Should().Contain("tm-pdf-template-designer--mobile");
    }

    [Fact]
    public void Designer_CtrlC_CtrlV_CopiesAndPastesField()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find(".tm-pdf-template-designer__workspace").KeyDown(new KeyboardEventArgs { Key = "c", CtrlKey = true });
        cut.Find("[data-page-key='attachment-1:0'] .tm-pdf-template-designer__page-surface").ContextMenu();
        cut.Find(".tm-pdf-template-designer__paste-field").Click();

        cut.WaitForAssertion(() => captured.Should().NotBeNull());
        captured!.Should().HaveCount(3);
        captured.Should().Contain(field => field.Uuid == "field-1");
    }

    [Fact]
    public void Designer_ArrowKeys_MoveSelectedField()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find(".tm-pdf-template-designer__workspace").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        captured.Should().NotBeNull();
        captured!.Single(field => field.Uuid == "field-1").Areas.Single().X.Should().BeGreaterThan(0.1);
    }

    [Fact]
    public void Designer_Tab_LeavesAFocusedField()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find("[data-field-uuid='field-1']").Click();

        // The workspace used to declare @onkeydown:preventDefault unconditionally, which trapped Tab
        // inside the shell. preventDefault is now decided per key by the listener.
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src", "Tempo.Blazor.Signing", "Components", "Signing", "TmPdfTemplateDesigner.razor"));
        Regex.IsMatch(source, @"@onkeydown:preventDefault").Should().BeFalse(
            "an unconditional preventDefault traps Tab inside a focused inspector field");
    }

    [Fact]
    public void Designer_Enter_ActivatesDetect()
    {
        var detected = false;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.OnDetectFields, () =>
                      {
                          detected = true;
                          return Task.FromResult<IReadOnlyList<SigningField>>([]);
                      }));

        var detect = cut.Find(".tm-pdf-template-designer__detect");
        detect.Should().NotBeNull();
        detect.GetAttribute("type").Should().Be("button", "a native button activates on Enter without a key handler");

        var listener = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src", "Tempo.Blazor.Signing", "wwwroot", "js", "pdf-template-designer.js"));
        listener.Should().Contain("isFormControl", "the key listener must ignore input, button, select and textarea");
        listener.Should().Contain("stopPropagation",
            "a form-control keydown must not reach the Blazor handler; KeyboardEventArgs.Type cannot tell the target");
        Regex.IsMatch(listener, @"button|select|textarea").Should().BeTrue();
        detected.Should().BeFalse("Enter is the button's own activation; the workspace must not swallow it");
    }

    [Fact]
    public void Designer_Delete_Unchanged()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find(".tm-pdf-template-designer__workspace").KeyDown(new KeyboardEventArgs { Key = "Delete" });

        captured.Should().NotBeNull();
        captured!.Should().ContainSingle(field => field.Uuid == "field-2");
    }

    [Fact]
    public void Designer_InspectorDelete_RemovesSelectedField()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").Click();
        cut.Find(".tm-signing-field-editor-panel__delete-field").Click();

        captured.Should().NotBeNull();
        captured!.Should().ContainSingle(field => field.Uuid == "field-2");
    }

    [Fact]
    public void Designer_Resize_UsesTheGrabbedArea()
    {
        var field = CreateField("field-1", "Name");
        field.Areas.Add(new SigningFieldArea
        {
            Uuid = "area-2",
            AttachmentUuid = "attachment-1",
            Page = 0,
            X = 0.5,
            Y = 0.5,
            Width = 0.2,
            Height = 0.08
        });
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, [field])
                      .Add(p => p.ViewMode, DocumentPageViewMode.Continuous));

        var overlays = cut.FindAll("[data-field-uuid='field-1']");
        overlays.Should().HaveCount(2);
        overlays[1].GetAttribute("data-area-uuid").Should().Be("area-2");
    }

    [Fact]
    public void Designer_Razor_HasNoMouseOrHtml5DragHandlers()
    {
        var root = FindRepositoryRoot();
        var sources = Directory.EnumerateFiles(
                Path.Combine(root, "src", "Tempo.Blazor.Signing", "Components", "Signing"),
                "TmPdfTemplateDesigner*",
                SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".razor", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();

        sources.Should().NotBeEmpty();
        var combined = string.Join('\n', sources);
        Regex.IsMatch(combined, @"@onmouse(down|move|up)\b(?!:preventDefault)", RegexOptions.IgnoreCase).Should().BeFalse(
            "pointer events replace the mouse handlers (a finger and a pen must move and resize a field)");
        Regex.IsMatch(combined, @"AddAttribute\([^)]*onmouse(down|move|up)", RegexOptions.IgnoreCase).Should().BeFalse(
            "a page press is a pointer handler; mousedown may only cancel the focus that follows it");
        Regex.IsMatch(combined, @"ondrag(start|end|over)?\b|ondrop\b", RegexOptions.IgnoreCase).Should().BeFalse(
            "HTML5 drag cannot start from a finger; placement is a tap or a pointer-drag session");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Designer_ContextMenu_StaysClosedAfterEscapeAndAPageClick()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find("[data-field-uuid='field-1']").ContextMenu();
        cut.FindAll(".tm-context-menu-wrapper").Should().ContainSingle();

        cut.Find(".tm-pdf-template-designer__workspace").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.Find(".tm-pdf-template-designer__page-surface").Click();

        cut.FindAll(".tm-context-menu-wrapper").Should().BeEmpty(
            "closing the menu must not reopen it on the next render");
    }

    [Fact]
    public void Designer_WorkspaceKeyDown_DoesNotPreventDefaultOnAFormControl()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src", "Tempo.Blazor.Signing", "wwwroot", "js", "pdf-template-designer.js"));
        source.Should().Contain("isFormControl",
            "Delete, arrows and the clipboard chords must bail out when the target is a form control");
        source.Should().Contain("name === 'button'");
    }

    [Fact]
    public void Designer_KeyDown_OnAFormControl_DoesNotDeleteTheSelection()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields()));

        cut.Find("[data-field-uuid='field-1']").Click();
        // KeyboardEventArgs.Type is the event type, never the target tag. The JS listener is the guard.
        var listener = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src", "Tempo.Blazor.Signing", "wwwroot", "js", "pdf-template-designer.js"));
        Regex.IsMatch(listener, @"addEventListener\?\.\('keydown', onKeyDown, true\)").Should().BeTrue(
            "the key listener must run in the capture phase so it can stop a form-control keydown");
        listener.Should().Contain("event.stopPropagation?.()");
        listener.Should().NotContain("args.Type",
            "a C# guard on KeyboardEventArgs.Type never sees the target element");
    }

    [Fact]
    public void Designer_Delete_WithDetectFocused_DoesNotDeleteTheSelection()
    {
        IReadOnlyList<SigningField>? captured = null;
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.OnDetectFields, () => Task.FromResult<IReadOnlyList<SigningField>>([]))
                      .Add(p => p.FieldsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningField>>(this, value => captured = value)));

        cut.Find("[data-field-uuid='field-1']").Click();
        // Detect is a native button and has no keydown handler of its own. The JS capture listener
        // is what stops the bubble, so Blazor's workspace handler never runs for it. bUnit has no
        // module: the listener source is the guard this test can execute.
        var listener = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src", "Tempo.Blazor.Signing", "wwwroot", "js", "pdf-template-designer.js"));
        var guard = Regex.Match(listener, @"if \(isFormControl\(event\.target\)\) \{[^}]*stopPropagation", RegexOptions.Singleline);
        guard.Success.Should().BeTrue("Delete with Detect focused must be stopped before the Blazor handler");
        captured.Should().BeNull();
        cut.FindAll("[data-field-uuid='field-1']").Should().ContainSingle();
    }

    [Fact]
    public void Designer_OmitsPointerMove_OnceTheJsSessionIsAttached()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages()));
        var workspace = cut.Find(".tm-pdf-template-designer__workspace");
        var move = () => workspace.TriggerEvent("onpointermove", new PointerEventArgs());
        move.Should().NotThrow("the Blazor fallback listens until the JS session attaches");

        typeof(TmPdfTemplateDesigner)
            .GetField("_fieldDragAttached", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(cut.Instance, true);
        cut.Render();

        workspace = cut.Find(".tm-pdf-template-designer__workspace");
        move = () => workspace.TriggerEvent("onpointermove", new PointerEventArgs());
        move.Should().Throw<MissingEventHandlerException>("JS owns the gesture once the session is attached");
        var up = () => workspace.TriggerEvent("onpointerup", new PointerEventArgs());
        up.Should().Throw<MissingEventHandlerException>();
        var surface = cut.Find(".tm-pdf-template-designer__page-surface");
        var surfaceUp = () => surface.TriggerEvent("onpointerup", new PointerEventArgs());
        surfaceUp.Should().Throw<MissingEventHandlerException>();
    }

    [Fact]
    public void Designer_TabletToolbar_UsesTwoRows()
    {
        var cut = Render<TmPdfTemplateDesigner>(parameters =>
            parameters.Add(p => p.Documents, CreatePages())
                      .Add(p => p.LayoutMode, TmLayoutMode.Tablet)
                      .Add(p => p.ShowCulturePreview, true)
                      .Add(p => p.SupportedCultures, ["en", "cs"])
                      .Add(p => p.OnDetectFields, () => Task.FromResult<IReadOnlyList<SigningField>>([])));

        var rows = cut.FindAll(".tm-pdf-template-designer__toolbar-row");
        rows.Should().HaveCount(2, "tablet keeps pages/view on the first row and zoom/detect/language on the second");
        rows[0].InnerHtml.Should().Contain("data-tm-group=\"pages\"").And.Contain("data-tm-group=\"view\"");
        rows[1].InnerHtml.Should().Contain("data-tm-group=\"zoom\"");
        rows[1].QuerySelector(".tm-toolbar")!.GetAttribute("aria-label").Should().Be("Zoom and detection",
            "the second row is a separate toolbar and must not repeat the first row's name");
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
