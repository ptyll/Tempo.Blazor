using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 3 review — the UX / accessibility contracts (U1–U5, U7, U9–U11) plus the dialog's single
/// undo stack (A5) and the real Delete shortcut (A10): the delete confirmation names what it removes
/// and announces it grammatically, a repeated announcement re-announces, the sheets are alertdialog /
/// forms with the safe action focused, the header actions explain why they are inert, and a delete or
/// an insert made in the dialog can be undone from the dialog in one step.
/// </summary>
public sealed class TmModelingEditorUxReviewTests : LocalizationTestBase
{
    private readonly ModelingEditorTestClock _clock = new();

    public TmModelingEditorUxReviewTests()
    {
        Services.AddTempoBlazorModeling();
        Services.AddSingleton<TimeProvider>(_clock);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new AuthoringModelingModelProvider()));
    }

    // ── U1: focus after delete ──────────────────────────────────────────────

    [Fact]
    public async Task Delete_confirmation_restores_focus_to_the_next_tree_item()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "lane-sales");

        await cut.InvokeAsync(() => cut.Instance.RequestDeleteSelectedAsync());

        var scope = cut.Find("[data-testid='modeling-delete-confirm-dialog'] [data-restore-target]");
        scope.GetAttribute("data-restore-target").Should().Be(
            TmModelingEditor.TreeItemDomId("pool-audit"),
            "the element FOLLOWING the deleted lane is the focus target (U1), never the document body");
        cut.Find("[data-testid='modeling-tree-node-pool-audit']").Id.Should().Be(TmModelingEditor.TreeItemDomId("pool-audit"),
            "the tree item really carries that id");
    }

    // ── U2: repeated identical announcements re-announce ───────────────────

    [Fact]
    public async Task Repeated_identical_announcements_still_change_the_live_region()
    {
        AddStructureWriter();
        using var cut = RenderEditor();

        // Two identical non-drag inserts (same type, same default name, same root container) produce
        // the SAME announcement text — a live region must still speak the second one.
        await cut.InvokeAsync(() => cut.Instance.OpenInsertElementDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.ConfirmInsertElementAsync());
        var first = cut.Instance.TestStructureAnnouncement;
        first.Should().NotBeNull();

        await cut.InvokeAsync(() => cut.Instance.OpenInsertElementDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.ConfirmInsertElementAsync());
        var second = cut.Instance.TestStructureAnnouncement;

        second.Should().NotBe(first, "the identical message is re-announced with a trailing zero-width space");
        second!.TrimEnd('\u200B').Should().Be(first!.TrimEnd('\u200B'), "only the invisible marker differs");
        cut.Find("[data-testid='modeling-structure-live']").TextContent.Should().EndWith("\u200B");
    }

    // ── U3: the delete modal is an alertdialog ─────────────────────────────

    [Fact]
    public async Task Delete_confirmation_is_an_alertdialog_with_a_description_safe_focus_and_a_danger_confirm()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "lane-sales");

        await cut.InvokeAsync(() => cut.Instance.RequestDeleteSelectedAsync());

        var dialog = cut.Find("[data-testid='modeling-delete-confirm-dialog']");
        var alertDialog = dialog.QuerySelector("[role='alertdialog']");
        alertDialog.Should().NotBeNull("a destructive confirmation is an alertdialog (U3)");

        var summary = dialog.QuerySelector("[data-testid='modeling-delete-summary']");
        summary.Should().NotBeNull();
        alertDialog!.GetAttribute("aria-describedby").Should().Contain(summary!.Id,
            "the cascade sentence describes the alertdialog");

        var cancel = dialog.QuerySelector("[data-testid='modeling-delete-cancel']");
        cancel!.ClassList.Should().Contain("tm-modeling-authoring-dialog__cancel",
            "InitialFocusSelector targets the safe action");
        var confirm = dialog.QuerySelector("[data-testid='modeling-delete-confirm']");
        confirm!.ClassList.Should().Contain("tm-btn-danger", "the destructive action is the danger variant");
        confirm.GetAttribute("type").Should().Be("submit", "the sheet is a form (U9)");
    }

    // ── U4: grammatical delete / insert copy ───────────────────────────────

    [Fact]
    public async Task Delete_names_the_target_and_announces_grammatical_counts()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "lane-sales");

        await cut.InvokeAsync(() => cut.Instance.RequestDeleteSelectedAsync());
        cut.Instance.DeleteTitleText.Should().Be("Delete “Sales”?");
        cut.Instance.DeleteSummaryText.Should().Be("This also removes 2 nested elements and 1 relationship.");

        await cut.InvokeAsync(() => cut.Instance.ConfirmDeleteAsync());
        cut.Instance.TestStructureAnnouncement.Should().Be("Deleted “Sales” with 2 nested elements and 1 relationship.");
    }

    [Fact]
    public async Task Relationship_only_delete_names_both_endpoints()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await cut.InvokeAsync(() => cut.Instance.TestSelectRelationshipForTestAsync("flow-approve-end"));

        await cut.InvokeAsync(() => cut.Instance.RequestDeleteSelectedAsync());
        cut.Instance.DeleteTitleText.Should().Be("Delete the relationship “Approve request → Approved”?");
        cut.Instance.DeleteSummaryText.Should().BeEmpty("the title already names the whole removal");
    }

    // ── U5: localization ───────────────────────────────────────────────────

    [Fact]
    public void Phase_three_copy_is_translated_in_all_four_tables_and_the_mock()
    {
        var czech = BuildCzechLocalizer();
        czech["TmModelingEditor_RelationshipRejected"].Should().Be("Spojení z {0} do {1} není pro tuto notaci platné.");
        czech["TmModelingEditor_NewElementName"].Should().Be("Nový prvek – {0}");
        czech["TmModelingEditor_CapabilityMissing"].Should().NotContain("writer",
            "the capability hint names the SOURCE, not an internal writer (U5)");

        var english = BuildEnglishLocalizer();
        english["TmModelingEditor_NewElementName"].Should().Be("{0} (new)");
        english["TmModelingEditor_SelectElementFirst"].Should().Be("Select an element first");
        english["TmModelingEditor_CapabilityMissing"].Should().Be(
            "This model source cannot save structural changes (insert, delete, connect).");
    }

    // ── U6: public pending count ───────────────────────────────────────────

    [Fact]
    public async Task Pending_change_count_exposes_structure_and_view_changes()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        cut.Instance.PendingChangeCount.Should().Be(0);

        await PaletteInsertAsync(cut, "palette-pending", "bpmn2.task.user", 640, 520);

        cut.Instance.PendingChangeCount.Should().BeGreaterThan(1,
            "one added element plus its view node at least");
        var pending = cut.Instance.TestPendingChangeSet!;
        var expected = pending.AddedElements.Count + pending.Elements.Count + pending.RemovedElementIds.Count
            + pending.AddedRelationships.Count + pending.Relationships.Count + pending.RemovedRelationshipIds.Count
            + pending.ViewChanges.Sum(view => view.Nodes.Count + view.Connections.Count
                + view.RemovedNodeElementIds.Count + view.RemovedConnectionRelationshipIds.Count);
        cut.Instance.PendingChangeCount.Should().Be(expected);

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        cut.Instance.PendingChangeCount.Should().Be(0);
    }

    // ── U7: generated-id secondary line and "name (type)" option labels ────

    [Fact]
    public async Task Tree_secondary_line_shows_the_type_for_a_generated_id_without_a_source()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new GeneratedIdModelingModelProvider()));
        Services.AddSingleton<IModelingModelWriter>(
            new AuthoringModelingModelWriter(GeneratedIdModelingModelProvider.Key, ModelingModelWriterCapabilities.All));
        using var cut = RenderEditor(GeneratedIdModelingModelProvider.Key);

        var row = cut.Find("[data-testid='modeling-model-tree-panel'] [data-element-id='3f2a9c1e-5b6d-4a8f-9c0d-1e2f3a4b5c6d']");
        row.QuerySelector(".tm-modeling-model-tree__node-meta")!.TextContent.Trim().Should().Be("User task",
            "a raw generated id tells the user nothing; the type label does (U7)");
    }

    [Fact]
    public async Task Container_and_target_options_are_labelled_with_the_type()
    {
        AddStructureWriter();
        using var cut = RenderEditor();

        await cut.InvokeAsync(() => cut.Instance.OpenInsertElementDialogAsync());
        var options = cut.FindAll("[data-testid='modeling-insert-container'] option")
            .Select(option => option.TextContent.Trim())
            .ToList();
        options.Should().Contain("Sales (Lane)");
        options.Should().Contain("Order pool (Pool)");

        await cut.InvokeAsync(() => cut.Instance.CloseInsertElementDialogAsync());
        await SelectElementAsync(cut, "task-a");
        await cut.InvokeAsync(() => cut.Instance.OpenConnectDialogAsync());
        var targets = cut.FindAll("[data-testid='modeling-relationship-target'] option")
            .Select(option => option.TextContent.Trim())
            .ToList();
        targets.Should().Contain("Ship order (User task)");
    }

    // ── U9: the sheets are forms ───────────────────────────────────────────

    [Fact]
    public async Task Insert_confirm_is_a_submit_button_disabled_while_no_type_is_chosen()
    {
        AddStructureWriter();
        using var cut = RenderEditor();

        await cut.InvokeAsync(() => cut.Instance.OpenInsertElementDialogAsync());
        var dialog = cut.Find("[data-testid='modeling-insert-element-dialog']");
        dialog.QuerySelector("form").Should().NotBeNull("the insert sheet is a real form (U9)");

        var confirm = dialog.QuerySelector("[data-testid='modeling-insert-confirm']")!;
        confirm.GetAttribute("type").Should().Be("submit");

        cut.Find("[data-testid='modeling-insert-type']").Change(string.Empty);
        confirm = cut.Find("[data-testid='modeling-insert-confirm']");
        confirm.HasAttribute("disabled").Should().BeTrue("an insert without a type is not submittable");
    }

    // ── U10 / U11: mobile + aria-disabled header actions ───────────────────

    [Fact]
    public void Mobile_band_has_no_structural_delete()
    {
        AddStructureWriter();
        using var cut = RenderEditor(configure: builder => builder.Add(component => component.LayoutMode, TmLayoutMode.Mobile));
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-active-panel").Should().NotBeNull());

        cut.FindAll("[data-testid='modeling-inspector-delete']").Should().BeEmpty("the mobile band is G2 (U10)");
        cut.FindAll("[data-testid='modeling-insert-element']").Should().BeEmpty();
    }

    [Fact]
    public async Task Header_connect_and_delete_explain_themselves_while_nothing_is_selected()
    {
        AddStructureWriter();
        using var cut = RenderEditor();

        var connect = cut.Find("[data-testid='modeling-connect-to']");
        connect.GetAttribute("aria-disabled").Should().Be("true", "aria-disabled, not a native disabled (U11)");
        connect.HasAttribute("disabled").Should().BeFalse();
        var describedBy = connect.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{describedBy}").TextContent.Trim().Should().Be("Select an element first");

        var delete = cut.Find("[data-testid='modeling-delete-selection']");
        delete.GetAttribute("aria-disabled").Should().Be("true");

        // The real selection path (a tree row click) re-renders the header; the seam alone only
        // mutates the editor state and would leave the DOM stale.
        cut.Find("[data-testid='modeling-tree-node-task-a']").Click();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-connect-to']").GetAttribute("aria-disabled").Should().Be("false"));
        cut.Find("[data-testid='modeling-delete-selection']").GetAttribute("aria-disabled").Should().Be("false");
    }

    // ── A5: single undo stack in the dialog ────────────────────────────────

    [Fact]
    public async Task Dialog_insert_is_undone_from_the_dialog_undo_button()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        var elementCount = cut.Instance.TestModel!.Elements.Count;

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        await PaletteInsertAsync(cut, "palette-dialog", "bpmn2.task.user", 640, 520);
        cut.Instance.TestModel!.Elements.Should().HaveCount(elementCount + 1);
        var insertedId = cut.Instance.TestModel.Elements.Select(element => element.Id)
            .Single(id => id is not ("pool-pool" or "lane-sales" or "task-a" or "end-a" or "pool-audit" or "task-b" or "ai-1" or "ai-2"));
        cut.Find("[data-testid='modeling-dialog-undo']").GetAttribute("aria-disabled").Should().Be("false");

        cut.Find("[data-testid='modeling-dialog-undo']").Click();
        cut.WaitForAssertion(() => cut.Instance.TestModel!.Elements.Should().HaveCount(elementCount));
        var openedElementIds = cut.Instance.TestOpenedEditorDocument!.Nodes.Select(NodeElementId).ToList();
        openedElementIds.Should().NotContain(insertedId,
            "the dialog's document is regenerated by the SAME undo (A5)");
        cut.Instance.HasPendingModelChanges.Should().BeFalse();

        cut.Find("[data-testid='modeling-dialog-redo']").Click();
        cut.WaitForAssertion(() => cut.Instance.TestModel!.Elements.Should().HaveCount(elementCount + 1));
    }

    [Fact]
    public async Task Dialog_editor_asks_the_host_for_history_instead_of_its_own_stack()
    {
        AddStructureWriter();
        using var cut = RenderEditor();

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        await PaletteInsertAsync(cut, "palette-history", "bpmn2.task.user", 640, 520);

        // The dialog's DE rendered its header history slot, but NOT its own History toolbar group.
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] [data-tm-group='history']").Should().BeEmpty(
            "the single-stack contract hides the diagram editor's own history group (A5)");
        cut.Find("[data-testid='modeling-dialog-undo']").Should().NotBeNull();
    }

    // ── A10: the tree's Delete key is real ─────────────────────────────────

    [Fact]
    public async Task Delete_key_on_a_tree_item_asks_for_the_cascade()
    {
        AddStructureWriter();
        using var cut = RenderEditor();

        var row = cut.Find("[data-testid='modeling-tree-node-task-a']");
        row.GetAttribute("aria-keyshortcuts").Should().Contain("Delete");

        row.KeyDown(new KeyboardEventArgs { Key = "Delete" });

        cut.WaitForAssertion(() => cut.Instance.DeleteConfirmationOpen.Should().BeTrue());
        cut.Instance.TestSelectedElementId.Should().Be("task-a", "the key acts on the focused row");
        cut.Instance.DeleteTitleText.Should().Be("Delete “Approve request”?");
    }

    [Fact]
    public async Task Delete_shortcut_is_not_advertised_without_the_structure_capability()
    {
        Services.AddSingleton<IModelingModelWriter>(
            new AuthoringModelingModelWriter(AuthoringModelingModelProvider.Key, ModelingModelWriterCapabilities.Content));
        using var cut = RenderEditor();

        cut.Find("[data-testid='modeling-tree-node-task-a']").GetAttribute("aria-keyshortcuts")
            .Should().NotContain("Delete", "the advertised Delete is only there when the host routes it");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private AuthoringModelingModelWriter AddStructureWriter()
    {
        var writer = new AuthoringModelingModelWriter(AuthoringModelingModelProvider.Key, ModelingModelWriterCapabilities.All);
        Services.AddSingleton<IModelingModelWriter>(writer);
        return writer;
    }

    private IRenderedComponent<TmModelingEditor> RenderEditor(
        string providerKey = AuthoringModelingModelProvider.Key,
        Action<ComponentParameterCollectionBuilder<TmModelingEditor>>? configure = null)
    {
        var cut = Render<TmModelingEditor>(builder =>
        {
            builder.Add(component => component.ProviderKey, providerKey);
            builder.Add(component => component.AllowModelEditing, true);
            builder.Add(component => component.WriterCapabilities, ModelingModelWriterCapabilities.All);
            builder.Add(component => component.OnModelChanged,
                EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { }));
            configure?.Invoke(builder);
        });

        cut.WaitForAssertion(
            () => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"),
            TimeSpan.FromSeconds(5));
        return cut;
    }

    private static Task SelectElementAsync(IRenderedComponent<TmModelingEditor> cut, string elementId)
        => cut.InvokeAsync(() => cut.Instance.TestSelectElementForTestAsync(elementId));

    /// <summary>The model element id a rendered node carries (<c>modelElementId</c> data), or empty.</summary>
    private static string NodeElementId(DiagramNode node)
        => node.Data is not null && node.Data.TryGetValue("modelElementId", out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;

    private static async Task PaletteInsertAsync(
        IRenderedComponent<TmModelingEditor> cut,
        string nodeId,
        string stencilId,
        double x,
        double y)
    {
        await cut.InvokeAsync(async () =>
        {
            var document = cut.Instance.TestDocument!;
            document.Nodes.Add(new DiagramNode
            {
                Id = nodeId,
                StencilId = stencilId,
                X = x,
                Y = y,
                W = 160,
                H = 88,
                Data = new Dictionary<string, object>(StringComparer.Ordinal) { ["label"] = "Palette shape" }
            });
            await cut.Instance.SyncStructureFromDocumentAsync(document);
        });
    }
}

/// <summary>One element whose semantic id is generated (a GUID) and which has NO source identity —
/// the tree's secondary-line fixture (U7).</summary>
internal sealed class GeneratedIdModelingModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.review.generated-id";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = new ModelingModelDto
        {
            Id = "modeling-review-generated-id",
            Title = "Generated id",
            Notation = "bpmn2",
            SupportedNotations = ["bpmn2"],
            Metadata = new ModelingMetadataDto { SourceSystem = "Unit tests" },
            Elements =
            [
                new()
                {
                    Id = "3f2a9c1e-5b6d-4a8f-9c0d-1e2f3a4b5c6d",
                    Notation = "bpmn2",
                    SemanticType = "UserTask",
                    Name = "Approve request"
                }
            ]
        };
        model.Views.Add(new ModelingViewDto
        {
            Id = "generated-view",
            Name = "Generated",
            Notation = "bpmn2",
            Nodes = [new() { ElementId = "3f2a9c1e-5b6d-4a8f-9c0d-1e2f3a4b5c6d", X = 80, Y = 80, Width = 160, Height = 88 }]
        });
        return Task.FromResult(model);
    }
}
