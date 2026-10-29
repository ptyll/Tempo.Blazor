using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// The "Open in editor" dialog contract of <see cref="TmModelingEditor"/> (Fáze 4, G6): the core
/// TmModal surface, focus return through the data-modeling-focus hooks, the inert background and
/// the live propagation. Split from <see cref="TmModelingEditorTests"/> to keep both under the
/// source-file length ceiling.
/// </summary>
public sealed class TmModelingEditorDialogTests : LocalizationTestBase
{
    private const string ModelingModulePath = "./_content/Tempo.Blazor.Modeling/js/modeling-editor.js";

    public TmModelingEditorDialogTests()
    {
        Services.AddTempoBlazorModeling();
    }

    [Fact]
    public async Task Focus_return_after_closing_editor_is_root_scoped_and_test_id_free()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-open-diagram-close']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().BeEmpty());

        JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "eval")
            .Should().BeEmpty("focus restoration must stay CSP-safe — no eval");

        var moduleInvocations = module.Invocations.Select(invocation => invocation.Identifier).ToList();
        moduleInvocations.Should().Contain("focusWithin",
            "the overlay focus move and the focus return go through the modeling-editor.js module");
        moduleInvocations.Count(id => id == "focusWithin").Should().BeGreaterThanOrEqualTo(2,
            "one call focuses the close button when the overlay opens, the second restores the trigger");

        // Every focusWithin call is scoped to the editor root ElementReference and never keys
        // off data-testid (the stable data-modeling-focus hooks are the contract — a host
        // renaming test ids through TestIdPrefix must not break focus return).
        foreach (var invocation in module.Invocations.Where(i => i.Identifier == "focusWithin"))
        {
            invocation.Arguments.Should().HaveCount(2);
            invocation.Arguments[0].Should().BeOfType<ElementReference>(
                "the query is scoped to the editor root, not the document");
            invocation.Arguments[1]!.ToString()!.Should().NotContain("data-testid");
        }

        var selectors = module.Invocations
            .Where(i => i.Identifier == "focusWithin")
            .Select(i => (string)i.Arguments[1]!)
            .ToList();
        selectors.Should().Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenEditorCloseFocusSelector,
            "opening the overlay focuses the close button through its data-modeling-focus hook");
        selectors.Should().Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenInEditorFocusSelector,
            "closing the overlay returns focus to the trigger through its data-modeling-focus hook");
    }

    [Fact]
    public async Task Focus_return_still_targets_the_data_modeling_focus_hooks_with_a_test_id_prefix()
    {
        // TestIdPrefix namespaces the editor-owned ids ("a-modeling-open-diagram-close", …) while
        // the preview child's trigger keeps its bare id. The focus return must keep working across
        // BOTH because it targets data-modeling-focus hooks, never data-testid.
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key)
            .Add(p => p.TestIdPrefix, "a"));

        cut.WaitForAssertion(() => cut.Find("[data-testid='a-modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='a-modeling-open-diagram-editor']").Should().NotBeNull();
            cut.Find("[data-testid='a-modeling-open-diagram-close']")
                .Should().NotBeNull("the editor namespaced its own close button");
        });

        cut.Find("[data-testid='a-modeling-open-diagram-close']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='a-modeling-open-diagram-editor']").Should().BeEmpty());

        module.Invocations
            .Where(i => i.Identifier == "focusWithin")
            .Select(i => (string)i.Arguments[1]!)
            .Should().Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenEditorCloseFocusSelector)
            .And.Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenInEditorFocusSelector,
                "the focus return is driven by the stable data-modeling-focus hooks, not data-testid");
    }

    [Fact]
    public async Task Open_in_editor_renders_tm_modal_with_backdrop_and_dialog_semantics()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();

        // The dialog is a core TmModal: a viewport-positioned overlay with the scrim class,
        // carrying the preserved modeling-open-diagram-editor test id on its root.
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-open-diagram-editor']").ClassList.Should().Contain("tm-modal-overlay",
                "the 'Open in editor' surface is a TmModal overlay (scrim), not an in-editor box"));
        var overlay = cut.Find("[data-testid='modeling-open-diagram-editor']");

        var dialog = cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-focus-scope");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = dialog.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty("the dialog is named by its title element");
        cut.Find($"[data-testid='modeling-open-diagram-editor'] [id='{labelledBy}']").Should().NotBeNull();
    }

    [Fact]
    public async Task Open_editor_title_uses_document_title_and_falls_back_to_open_in_editor()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();

        // The generated document carries the model title — the modal header shows it, never the
        // old "Preview" fallback (the pre-redesign default).
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-modal-title").TextContent.Should().Be("Modeling editor test"));

        // A document without a title falls back to the localized "Open in editor" label.
        var untitled = ModelingEditorTestModels.CreateModel();
        untitled.Title = string.Empty;
        var document = cut.Instance.DiagramGenerator.Generate(untitled, new ModelingDiagramGenerationOptionsDto
        {
            IncludeIssues = true
        }).Document;
        document.Title = " ";
        // The trigger hands the preview's CURRENT document to the editor's open path — hand it
        // the untitled one the same way the real click would.
        var preview = cut.FindComponent<Tempo.Blazor.Components.Modeling.TmModelingDiagramPreview>();
        await cut.InvokeAsync(() => preview.Instance.OnOpenInEditor.InvokeAsync(document));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-modal-title").TextContent
                .Should().Be("Open in editor", "an untitled document falls back to the localized open-in-editor label"));
    }

    [Fact]
    public async Task Escape_closes_open_editor_dialog_and_restores_focus_to_trigger()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        // Single focus-restore owner (final review): the editor's focusWithin close path owns
        // the restore — the modal's own TmFocusScope restore is DISABLED (data-restore-focus=
        // "false"), so a mobile close cannot race a restore target that does not exist there.
        var triggerId = cut.Find("[data-testid='modeling-open-in-editor-button']").GetAttribute("id");
        triggerId.Should().NotBeNullOrEmpty("the trigger keeps the stable DOM id contract");
        var scopeElement = cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-focus-scope");
        scopeElement.GetAttribute("data-restore-focus").Should().Be("false",
            "the modal restore is disabled — the editor's focusWithin path is the single owner");

        // Escape arrives through the focus-trap module callback (document-level listener):
        // the topmost modal scope raises OnEscape and TmModal closes through the editor's
        // close path, which restores the trigger focus.
        var modal = cut.FindComponents<Tempo.Blazor.Components.Feedback.TmModal>()
            .Single(component => component.Markup.Contains("modeling-open-diagram-editor"));
        var scopeInstance = modal.FindComponents<Tempo.Blazor.Components.Feedback.TmFocusScope>().Single();
        await cut.InvokeAsync(() => scopeInstance.Instance.HandleFocusTrapEscapeAsync());

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().BeEmpty());
        module.Invocations
            .Where(i => i.Identifier == "focusWithin")
            .Select(i => (string)i.Arguments[1]!)
            .Should().Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenInEditorFocusSelector,
                "the close path restores focus onto the trigger through its data-modeling-focus hook");
    }

    [Fact]
    public async Task Escape_consuming_surfaces_close_without_closing_the_dialog()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        // The canvas' CAPTURE-phase Escape consumers raise OnCloseSearch / OnCloseViewMenu on the
        // editor (the dialog stays open — the event was marked handled before the trap saw it).
        var modal = cut.FindComponents<Tempo.Blazor.Components.Feedback.TmModal>()
            .Single(component => component.Markup.Contains("modeling-open-diagram-editor"));
        var canvas = modal.FindComponents<Tempo.Blazor.Components.Diagram.TmDiagramCanvas>().Single();

        // Search open → Escape closes the PANEL, not the dialog.
        cut.Find("[data-testid='modeling-open-diagram-editor'] [data-tm-group='search'] button")
            .Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-diagram-search-panel")
            .Should().NotBeNull());
        await cut.InvokeAsync(() => canvas.Instance.JsOnSearchEscape());
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-diagram-search-panel")
            .Should().BeEmpty("the canvas' search Escape consumer closes the panel");
        cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull("the dialog stays open");

        // View menu open → Escape closes the MENU, not the dialog.
        cut.Find("[data-testid='modeling-open-diagram-editor'] [data-testid='diagram-editor-more']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor__view-menu")
            .GetAttribute("data-open").Should().Be("true"));
        await cut.InvokeAsync(() => canvas.Instance.JsOnViewMenuEscape());
        cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor__view-menu")
            .GetAttribute("data-open").Should().BeNull("the canvas' view-menu Escape consumer closes the menu");
        cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull("the dialog stays open");

        // Only the unhandled path — the focus-scope module callback — closes the dialog.
        var scope = modal.FindComponents<Tempo.Blazor.Components.Feedback.TmFocusScope>().Single();
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().BeEmpty());
    }

    [Fact]
    public async Task Workspace_is_inert_while_dialog_open()
    {
        var focusTrap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        focusTrap.SetupVoid("activate", _ => true).SetVoidResult();
        focusTrap.SetupVoid("noteReturnTarget", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();

        // The modal scope is a MODAL trap: aria-modal on the dialog and modal=true handed to the
        // focus-trap module (the module inertes the page for a modal trap only).
        cut.WaitForAssertion(() =>
        {
            var dialog = cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-focus-scope");
            dialog.GetAttribute("aria-modal").Should().Be("true");
        });

        focusTrap.Invocations
            .Where(i => i.Identifier == "activate")
            .Select(i => i.Arguments[5])
            .Should().Contain(true, "the dialog activates a modal trap — the module inertes the background");

        // The dialog surface stays OUTSIDE the dialog subtree: the trap's inert background covers it.
        var overlay = cut.Find("[data-testid='modeling-open-diagram-editor']");
        overlay.QuerySelector(".tm-modeling-editor__canvas-body").Should().BeNull(
            "the modeling canvas surface is the inert background, not dialog content");
        cut.Find(".tm-modeling-editor__canvas-body").Should().NotBeNull();
    }

    [Fact]
    public async Task Dialog_has_single_header_without_duplicate_title()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-modal-header").Should().NotBeNull());

        // One header: the TmModal header owns the title and subtitle; the embedded diagram
        // editor renders WITHOUT its own document title (ShowTitle=false through the editor API).
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-modal-header").Should().ContainSingle();
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor__title").Should().BeEmpty(
            "the dialog title lives in the TmModal header only — the diagram editor hides its own");
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-modal-title").Should().ContainSingle();
        cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-modal-subtitle").TextContent
            .Should().Be("Full diagram editor for the generated modeling document.");
    }

    [Fact]
    public async Task Live_changes_propagate_to_preview_without_footer()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        // G8: the dialog has no footer — changes keep propagating live.
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-modal-footer").Should().BeEmpty(
            "live propagation has no Apply/Cancel footer");

        // A diagram-editor change flows through the close-free path: the dialog's document AND
        // the preview document behind it update (the status strip counts the nodes).
        var modal = cut.FindComponents<Tempo.Blazor.Components.Feedback.TmModal>()
            .Single(component => component.Markup.Contains("modeling-open-diagram-editor"));
        var embeddedEditor = modal.FindComponents<Tempo.Blazor.Components.Diagram.TmDiagramEditor>().Single();
        var current = embeddedEditor.Instance.Document;
        current.Should().NotBeNull("the dialog embeds the generated document");
        var modified = new DiagramDocument
        {
            Id = current.Id,
            Title = "Renamed by the editor",
            CreatedAt = current.CreatedAt,
            ModifiedAt = current.ModifiedAt,
            Pages = current.Pages,
            ActivePageIndex = current.ActivePageIndex,
            Nodes = current.Nodes.Append(new DiagramNode
            {
                Id = "editor-added-node",
                StencilId = "general.rectangle",
                X = 40,
                Y = 40,
                W = 120,
                H = 60
            }).ToList(),
            Edges = current.Edges
        };
        await cut.InvokeAsync(() => embeddedEditor.Instance.DocumentChanged.InvokeAsync(modified));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-diagram-node-count").Should().Be("2");
            cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-modal-title").TextContent.Should().Be("Renamed by the editor");
            cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull(
                "the change did not close the dialog");
        });

        // A scrim click must not close the dialog either (live changes must not be lost).
        cut.Find("[data-testid='modeling-open-diagram-editor']").Click();
        cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull(
            "the scrim is not a close affordance while edits propagate live");
    }

    [Fact]
    public async Task Provider_change_closes_open_editor_dialog_through_the_close_path()
    {
        // Phase-3 carry-forward, re-verified on the TmModal dialog (phase 4): a header provider
        // change closes the open dialog through CloseGeneratedDiagramAsync — the same path the
        // close button uses — so the focus restore onto the preview trigger still runs.
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new CountingModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key)
            .Add(p => p.ShowProviderSelector, true));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-provider-select']").Change(CountingModelingModelProvider.Key);

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().BeEmpty());
        module.Invocations
            .Where(i => i.Identifier == "focusWithin")
            .Select(i => (string)i.Arguments[1]!)
            .Should().Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenInEditorFocusSelector,
                "the provider change closes through the close path, restoring the trigger focus");
    }

    [Fact]
    public async Task Open_editor_dialog_on_tablet_uses_modal_presentation_and_decides_its_own_layout()
    {
        // Fáze 6: on a tablet the dialog stays a TmModal (~90% wide, NOT a fullscreen sheet).
        // Final review: the dialog DE is AUTO with the 768/1100 pair — the dialog body decides
        // its own layout, the editor's resolved mode is never inherited (the pin went stale the
        // moment the editor's container and the ~90vw body disagreed).
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Tablet)
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();

        cut.WaitForAssertion(() =>
        {
            var overlay = cut.Find("[data-testid='modeling-open-diagram-editor']");
            overlay.ClassList.Should().Contain("tm-modal-overlay", "the tablet dialog is the core TmModal");
            overlay.ClassList.Should().NotContain("tm-modal--fullscreen-presentation",
                "a tablet keeps the ~90% dialog, not the fullscreen mobile presentation");
        });

        var dialog = cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-focus-scope");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");

        // The dialog body resolves the embedded editor's layout ITSELF (final review: the dialog
        // DE is Auto with the 768/1100 pair — never pinned to the editor's resolved mode).
        // bUnit runs no container measurement, so the editor renders its desktop InitialMode —
        // proof that the editor's own tablet resolution is not inherited.
        var embedded = cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor[data-editor-layout]");
        embedded.GetAttribute("data-editor-layout").Should().Be("desktop",
            "the dialog body decides the layout (Auto + 768/1100); the editor's tablet mode is NOT inherited");
    }

    [Fact]
    public async Task Tablet_dialog_starts_with_minimap_collapsed()
    {
        // Fáze 9: the dialog seeds the DE's additive MinimapInitiallyCollapsed with the editor's
        // resolved mode — on a tablet the floating minimap starts COLLAPSED (the overlay covered
        // the touch canvas) and reopens from its button; on desktop it keeps today's expanded card.
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Tablet)
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-diagram-minimap").Should().BeEmpty(
            "the tablet dialog's minimap starts collapsed (a tablet band editor, not the DE default)");

        // The knob is the public DE API: the embedded editor instance carries the seed.
        cut.FindComponents<Tempo.Blazor.Components.Diagram.TmDiagramEditor>()
            .Single(component => component.Markup.Contains("tm-modeling-editor__open-editor-diagram"))
            .Instance.MinimapInitiallyCollapsed.Should().BeTrue();
    }

    [Fact]
    public async Task Tablet_dialog_escape_consumers_close_their_surface_not_the_dialog()
    {
        // Fáze 6: the embedded diagram editor is tablet-docked inside the dialog — its Escape
        // consumers (search panel, view menu) must keep winning over the dialog's own Escape,
        // exactly like on the desktop (order-independent Escape consumers, phase 4).
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Tablet)
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        var modal = cut.FindComponents<Tempo.Blazor.Components.Feedback.TmModal>()
            .Single(component => component.Markup.Contains("modeling-open-diagram-editor"));
        var canvas = modal.FindComponents<Tempo.Blazor.Components.Diagram.TmDiagramCanvas>().Single();

        cut.Find("[data-testid='modeling-open-diagram-editor'] [data-tm-group='search'] button").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-diagram-search-panel")
            .Should().NotBeNull());
        await cut.InvokeAsync(() => canvas.Instance.JsOnSearchEscape());
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-diagram-search-panel")
            .Should().BeEmpty("the search Escape consumer closes the panel");
        cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull("the dialog stays open");

        var scope = modal.FindComponents<Tempo.Blazor.Components.Feedback.TmFocusScope>().Single();
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().BeEmpty());
    }

    [Fact]
    public async Task Open_editor_dialog_survives_a_layout_flip_with_one_instance_and_one_modal()
    {
        // Final review (MAJOR): the dialog renders ONCE, outside both layout branches — a flip
        // across the 768 boundary used to tear it down and rebuild it (a NEW TmDiagramEditor
        // instance, a fresh engine boot). The same instance must survive Tablet → Mobile →
        // Tablet, and the DOM must hold exactly ONE modal the whole time.
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Tablet)
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        var dialogEditor = cut.FindComponents<Tempo.Blazor.Components.Diagram.TmDiagramEditor>()
            .Single(component => component.Markup.Contains("tm-modeling-editor__open-editor-diagram"));
        var dialogEditorId = dialogEditor.Instance.GetHashCode();

        // Tablet -> Mobile: the dialog presents fullscreen but the instance survives.
        cut.Render(parameters => parameters.Add(p => p.LayoutMode, TmLayoutMode.Mobile));
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().ContainSingle("exactly one modal on the mobile band");
            cut.Find("[data-testid='modeling-open-diagram-editor']").ClassList
                .Should().Contain("tm-modal--fullscreen-presentation", "the mobile band presents fullscreen");
        });

        var mobileEditor = cut.FindComponents<Tempo.Blazor.Components.Diagram.TmDiagramEditor>()
            .Single(component => component.Markup.Contains("tm-modeling-editor__open-editor-diagram"));
        mobileEditor.Instance.GetHashCode().Should().Be(dialogEditorId,
            "the flip re-presents the SAME dialog editor (no teardown, no engine reboot)");

        // Mobile -> Tablet: still the same instance, back to the ~90% dialog.
        cut.Render(parameters => parameters.Add(p => p.LayoutMode, TmLayoutMode.Tablet));
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().ContainSingle("exactly one modal after the round trip");
            cut.Find("[data-testid='modeling-open-diagram-editor']").ClassList
                .Should().NotContain("tm-modal--fullscreen-presentation", "the tablet band presents the dialog");
        });

        var tabletEditor = cut.FindComponents<Tempo.Blazor.Components.Diagram.TmDiagramEditor>()
            .Single(component => component.Markup.Contains("tm-modeling-editor__open-editor-diagram"));
        tabletEditor.Instance.GetHashCode().Should().Be(dialogEditorId,
            "the instance survives both directions of the flip");
    }
}
