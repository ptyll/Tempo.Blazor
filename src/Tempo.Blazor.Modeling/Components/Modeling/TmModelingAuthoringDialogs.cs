using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Overlay;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The NON-DRAG authoring dialogs of <see cref="TmModelingEditor"/> (phase 3, WCAG 2.5.7): the
/// "Insert element &gt; type &gt; container" sheet, the "Connect to &gt; target &gt; type" sheet and
/// the cascade delete confirmation. The HOSTING EDITOR owns every piece of state and commits the
/// result through the same semantic command a palette drop or a drawn edge uses — this type is the
/// presentation only, so the drag path and the keyboard path can never diverge.
/// <para>
/// It is an <c>internal</c> code-only renderer (phase 3 review, A8) rather than a Razor component:
/// the authoring surface is NOT public API (a host never renders these sheets itself), and a Razor
/// component would force a public generated class, a JSON documentation overlay and a public
/// parameter surface. The markup is built here, the styles live in
/// <c>TmModelingEditor.razor.css</c> (the editor root owns the dialog chrome).
/// </para>
/// </summary>
internal sealed class TmModelingAuthoringDialogs
{
    /// <summary>The editor's prefix-aware test-id resolver (<see cref="TmComponentBase.TestId"/>).</summary>
    public Func<string, string> TestId { get; init; } = name => name;

    // ── Insert element ───────────────────────────────────────────────────────

    /// <summary>Whether the insert-element dialog is open.</summary>
    public bool InsertOpen { get; init; }

    /// <summary>Dialog title.</summary>
    public string InsertTitle { get; init; } = string.Empty;

    /// <summary>Label of the semantic-type field.</summary>
    public string InsertTypeLabel { get; init; } = string.Empty;

    /// <summary>Type candidates (the active notation's palette).</summary>
    public IReadOnlyList<(string Value, string Label)> InsertTypes { get; init; } = [];

    /// <summary>Selected semantic type.</summary>
    public string InsertTypeValue { get; init; } = string.Empty;

    /// <summary>Raised when the selected type changes.</summary>
    public EventCallback<string> InsertTypeChanged { get; init; }

    /// <summary>Label of the target-container field.</summary>
    public string InsertContainerLabel { get; init; } = string.Empty;

    /// <summary>Label of the "no container" option.</summary>
    public string InsertRootLabel { get; init; } = string.Empty;

    /// <summary>Container candidates (elements whose notation type contains others).</summary>
    public IReadOnlyList<(string Value, string Label)> InsertContainers { get; init; } = [];

    /// <summary>Selected container element id (<c>""</c> = root).</summary>
    public string InsertContainerValue { get; init; } = string.Empty;

    /// <summary>Raised when the selected container changes.</summary>
    public EventCallback<string> InsertContainerChanged { get; init; }

    /// <summary>Label of the optional name field.</summary>
    public string InsertNameLabel { get; init; } = string.Empty;

    /// <summary>Placeholder of the optional name field (the default name hint).</summary>
    public string InsertNamePlaceholder { get; init; } = string.Empty;

    /// <summary>The typed name (empty = the generated default).</summary>
    public string InsertNameValue { get; init; } = string.Empty;

    /// <summary>Raised when the typed name changes.</summary>
    public EventCallback<string> InsertNameChanged { get; init; }

    /// <summary>Label of the confirm action.</summary>
    public string InsertConfirmLabel { get; init; } = string.Empty;

    /// <summary>Raised when the user confirms the insert.</summary>
    public EventCallback InsertConfirm { get; init; }

    /// <summary>Raised when the user cancels the insert.</summary>
    public EventCallback InsertCancel { get; init; }

    // ── Connect to (relationship) ────────────────────────────────────────────

    /// <summary>Whether the relationship dialog is open.</summary>
    public bool RelationshipOpen { get; init; }

    /// <summary>Dialog title.</summary>
    public string RelationshipTitle { get; init; } = string.Empty;

    /// <summary>Name of the source element (the selected one).</summary>
    public string SourceLabel { get; init; } = string.Empty;

    /// <summary>Composite format of the source line ("Source: {0}").</summary>
    public string RelationshipSourceFormat { get; init; } = "{0}";

    /// <summary>Whether the target field renders (the keyboard path) or the target is fixed (a drawn edge).</summary>
    public bool ShowRelationshipTarget { get; init; } = true;

    /// <summary>Label of the target field.</summary>
    public string RelationshipTargetLabel { get; init; } = string.Empty;

    /// <summary>Target candidates (every other element of the model).</summary>
    public IReadOnlyList<(string Value, string Label)> RelationshipTargets { get; init; } = [];

    /// <summary>Selected target element id.</summary>
    public string RelationshipTargetValue { get; init; } = string.Empty;

    /// <summary>Raised when the selected target changes.</summary>
    public EventCallback<string> RelationshipTargetChanged { get; init; }

    /// <summary>Label of the relationship-type field.</summary>
    public string RelationshipTypeLabel { get; init; } = string.Empty;

    /// <summary>The ALLOWED relationship types of the current pair (never the whole notation).</summary>
    public IReadOnlyList<(string Value, string Label)> RelationshipTypes { get; init; } = [];

    /// <summary>Selected relationship type.</summary>
    public string RelationshipTypeValue { get; init; } = string.Empty;

    /// <summary>Raised when the selected type changes.</summary>
    public EventCallback<string> RelationshipTypeChanged { get; init; }

    /// <summary>The notation rules' explanation when the pair allows no relationship at all.</summary>
    public string? RelationshipRejection { get; init; }

    /// <summary>The rules' suggested fix for the rejection.</summary>
    public string? RelationshipRejectionFix { get; init; }

    /// <summary>Title of the rejection card (phase 3 review, U8 — a bold headline above the rules'
    /// own sentence).</summary>
    public string RelationshipRejectionTitle { get; init; } = string.Empty;

    /// <summary>Label of the confirm action.</summary>
    public string RelationshipConfirmLabel { get; init; } = string.Empty;

    /// <summary>Raised when the user confirms the relationship.</summary>
    public EventCallback RelationshipConfirm { get; init; }

    /// <summary>Raised when the user cancels the relationship.</summary>
    public EventCallback RelationshipCancel { get; init; }

    // ── Delete confirmation ──────────────────────────────────────────────────

    /// <summary>Whether the delete confirmation is open.</summary>
    public bool DeleteOpen { get; init; }

    /// <summary>Confirmation title.</summary>
    public string DeleteTitle { get; init; } = string.Empty;

    /// <summary>The localized cascade summary (grammatical plural counts; empty for a
    /// relationship-only confirmation).</summary>
    public string DeleteSummary { get; init; } = string.Empty;

    /// <summary>Label of the destructive confirm action.</summary>
    public string DeleteConfirmLabel { get; init; } = string.Empty;

    /// <summary>Raised when the user confirms the deletion.</summary>
    public EventCallback DeleteConfirm { get; init; }

    /// <summary>Raised when the user cancels the deletion.</summary>
    public EventCallback DeleteCancel { get; init; }

    /// <summary>The id focus returns to when the confirmation closes (phase 3 review, U1).</summary>
    public string? DeleteRestoreFocusTargetId { get; init; }

    /// <summary>Label of the cancel action (shared by the three dialogs).</summary>
    public string CancelLabel { get; init; } = string.Empty;

    /// <summary>Per-instance ids of the delete confirmation's description paragraph.</summary>
    private readonly string _deleteSummaryId = $"tm-modeling-delete-summary-{Guid.NewGuid():N}";

    /// <summary>Renders the three dialogs (only the open ones render).</summary>
    public RenderFragment Render() => builder =>
    {
        if (InsertOpen)
        {
            RenderInsertDialog(builder);
        }

        if (RelationshipOpen)
        {
            RenderRelationshipDialog(builder);
        }

        if (DeleteOpen)
        {
            RenderDeleteDialog(builder);
        }
    };

    // ── Insert element ───────────────────────────────────────────────────────

    private void RenderInsertDialog(RenderTreeBuilder builder)
    {
        builder.OpenComponent<TmModal>(0);
        builder.AddAttribute(1, nameof(TmModal.Show), true);
        builder.AddAttribute(2, nameof(TmModal.Title), InsertTitle);
        builder.AddAttribute(3, nameof(TmModal.Size), ModalSize.Small);
        builder.AddAttribute(4, nameof(TmModal.ShowFooter), false);
        builder.AddAttribute(5, nameof(TmModal.ShowDefaultFooterButtons), false);
        builder.AddAttribute(6, nameof(TmModal.CloseOnOverlayClick), false);
        builder.AddAttribute(7, nameof(TmModal.CloseOnEscape), true);
        builder.AddAttribute(8, nameof(TmModal.OnClose), InsertCancel);
        builder.AddAttribute(9, nameof(TmModal.Class), "tm-modeling-authoring-dialog");
        builder.AddAttribute(10, nameof(TmModal.AdditionalAttributes), Attributes("modeling-insert-element-dialog"));
        builder.AddAttribute(11, nameof(TmModal.ChildContent), (RenderFragment)(body =>
        {
            body.OpenElement(0, "form");
            body.AddAttribute(1, "class", "tm-modeling-authoring-dialog__body");
            body.AddAttribute(2, "onsubmit", EventCallback.Factory.Create<EventArgs>(this, OnInsertSubmit));
            body.AddEventPreventDefaultAttribute(3, "onsubmit", true);

            RenderField(body, 10, "modeling-insert-type", InsertTypeLabel, control =>
            {
                control.OpenElement(0, "select");
                control.AddAttribute(1, "data-testid", TestId("modeling-insert-type"));
                control.AddAttribute(2, "value", InsertTypeValue);
                control.AddAttribute(3, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(
                    this, args => InsertTypeChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty)));
                foreach (var option in InsertTypes)
                {
                    control.OpenElement(4, "option");
                    control.AddAttribute(5, "value", option.Value);
                    control.AddContent(6, option.Label);
                    control.CloseElement();
                }

                control.CloseElement();
            });

            RenderField(body, 20, "modeling-insert-container", InsertContainerLabel, control =>
            {
                control.OpenElement(0, "select");
                control.AddAttribute(1, "data-testid", TestId("modeling-insert-container"));
                control.AddAttribute(2, "value", InsertContainerValue);
                control.AddAttribute(3, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(
                    this, args => InsertContainerChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty)));
                control.OpenElement(4, "option");
                control.AddAttribute(5, "value", string.Empty);
                control.AddContent(6, InsertRootLabel);
                control.CloseElement();
                foreach (var option in InsertContainers)
                {
                    control.OpenElement(7, "option");
                    control.AddAttribute(8, "value", option.Value);
                    control.AddContent(9, option.Label);
                    control.CloseElement();
                }

                control.CloseElement();
            });

            RenderField(body, 30, "modeling-insert-name", InsertNameLabel, control =>
            {
                control.OpenElement(0, "input");
                control.AddAttribute(1, "type", "text");
                control.AddAttribute(2, "data-testid", TestId("modeling-insert-name"));
                control.AddAttribute(3, "value", InsertNameValue);
                control.AddAttribute(4, "placeholder", InsertNamePlaceholder);
                control.AddAttribute(5, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(
                    this, args => InsertNameChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty)));
                control.CloseElement();
            });

            RenderActions(
                body,
                40,
                @class: "tm-modeling-authoring-dialog__cancel",
                cancelLabel: CancelLabel,
                cancelTestId: TestId("modeling-insert-cancel"),
                cancel: InsertCancel,
                confirmLabel: InsertConfirmLabel,
                confirmTestId: TestId("modeling-insert-confirm"),
                confirmDisabled: string.IsNullOrWhiteSpace(InsertTypeValue),
                confirm: InsertConfirm,
                confirmVariant: ButtonVariant.Primary);

            body.CloseElement();
        }));
        builder.CloseComponent();
    }

    private Task OnInsertSubmit(EventArgs args)
        => string.IsNullOrWhiteSpace(InsertTypeValue) ? Task.CompletedTask : InsertConfirm.InvokeAsync();

    // ── Connect to (relationship) ────────────────────────────────────────────

    private void RenderRelationshipDialog(RenderTreeBuilder builder)
    {
        var rejected = !string.IsNullOrWhiteSpace(RelationshipRejection);
        builder.OpenComponent<TmModal>(0);
        builder.AddAttribute(1, nameof(TmModal.Show), true);
        builder.AddAttribute(2, nameof(TmModal.Title), RelationshipTitle);
        builder.AddAttribute(3, nameof(TmModal.Size), ModalSize.Small);
        builder.AddAttribute(4, nameof(TmModal.ShowFooter), false);
        builder.AddAttribute(5, nameof(TmModal.ShowDefaultFooterButtons), false);
        builder.AddAttribute(6, nameof(TmModal.CloseOnOverlayClick), false);
        builder.AddAttribute(7, nameof(TmModal.CloseOnEscape), true);
        builder.AddAttribute(8, nameof(TmModal.OnClose), RelationshipCancel);
        builder.AddAttribute(9, nameof(TmModal.Class), "tm-modeling-authoring-dialog");
        builder.AddAttribute(10, nameof(TmModal.AdditionalAttributes), Attributes("modeling-relationship-dialog"));
        builder.AddAttribute(11, nameof(TmModal.ChildContent), (RenderFragment)(body =>
        {
            body.OpenElement(0, "form");
            body.AddAttribute(1, "class", "tm-modeling-authoring-dialog__body");
            body.AddAttribute(2, "onsubmit", EventCallback.Factory.Create<EventArgs>(this, OnRelationshipSubmit));
            body.AddEventPreventDefaultAttribute(3, "onsubmit", true);

            body.OpenElement(10, "p");
            body.AddAttribute(11, "class", "tm-modeling-authoring-dialog__source");
            body.AddAttribute(12, "data-testid", TestId("modeling-relationship-source"));
            body.AddContent(13, string.Format(System.Globalization.CultureInfo.CurrentUICulture, RelationshipSourceFormat, SourceLabel));
            body.CloseElement();

            if (ShowRelationshipTarget)
            {
                RenderField(body, 20, "modeling-relationship-target", RelationshipTargetLabel, control =>
                {
                    control.OpenElement(0, "select");
                    control.AddAttribute(1, "data-testid", TestId("modeling-relationship-target"));
                    control.AddAttribute(2, "value", RelationshipTargetValue);
                    control.AddAttribute(3, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(
                        this, args => RelationshipTargetChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty)));
                    foreach (var option in RelationshipTargets)
                    {
                        control.OpenElement(4, "option");
                        control.AddAttribute(5, "value", option.Value);
                        control.AddContent(6, option.Label);
                        control.CloseElement();
                    }

                    control.CloseElement();
                });
            }

            if (rejected)
            {
                body.OpenElement(30, "div");
                body.AddAttribute(31, "class", "tm-modeling-authoring-dialog__rejection");
                body.AddAttribute(32, "role", "alert");
                body.AddAttribute(33, "data-testid", TestId("modeling-relationship-rejection"));

                body.OpenElement(34, "p");
                body.AddAttribute(35, "class", "tm-modeling-authoring-dialog__rejection-title");
                body.OpenElement(36, "span");
                body.AddAttribute(37, "class", "tm-modeling-authoring-dialog__rejection-icon");
                body.AddAttribute(38, "aria-hidden", "true");
                body.OpenComponent<TmIcon>(39);
                body.AddAttribute(40, nameof(TmIcon.Name), IconNames.AlertTriangle);
                body.AddAttribute(41, nameof(TmIcon.Size), IconSize.Sm);
                body.CloseComponent();
                body.CloseElement();
                body.AddContent(42, RelationshipRejectionTitle);
                body.CloseElement();

                body.OpenElement(43, "p");
                body.AddContent(44, RelationshipRejection);
                body.CloseElement();

                if (!string.IsNullOrWhiteSpace(RelationshipRejectionFix))
                {
                    body.OpenElement(45, "p");
                    body.AddAttribute(46, "class", "tm-modeling-authoring-dialog__fix");
                    body.AddContent(47, RelationshipRejectionFix);
                    body.CloseElement();
                }

                body.CloseElement();
            }
            else
            {
                RenderField(body, 50, "modeling-relationship-type", RelationshipTypeLabel, control =>
                {
                    control.OpenElement(0, "select");
                    control.AddAttribute(1, "data-testid", TestId("modeling-relationship-type"));
                    control.AddAttribute(2, "value", RelationshipTypeValue);
                    control.AddAttribute(3, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(
                        this, args => RelationshipTypeChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty)));
                    foreach (var option in RelationshipTypes)
                    {
                        control.OpenElement(4, "option");
                        control.AddAttribute(5, "value", option.Value);
                        control.AddContent(6, option.Label);
                        control.CloseElement();
                    }

                    control.CloseElement();
                });
            }

            RenderActions(
                body,
                60,
                @class: "tm-modeling-authoring-dialog__cancel",
                cancelLabel: CancelLabel,
                cancelTestId: TestId("modeling-relationship-cancel"),
                cancel: RelationshipCancel,
                confirmLabel: RelationshipConfirmLabel,
                confirmTestId: TestId("modeling-relationship-confirm"),
                confirmDisabled: rejected || string.IsNullOrWhiteSpace(RelationshipTypeValue),
                confirm: RelationshipConfirm,
                confirmVariant: ButtonVariant.Primary);

            body.CloseElement();
        }));
        builder.CloseComponent();
    }

    private Task OnRelationshipSubmit(EventArgs args)
        => !string.IsNullOrWhiteSpace(RelationshipRejection) || string.IsNullOrWhiteSpace(RelationshipTypeValue)
            ? Task.CompletedTask
            : RelationshipConfirm.InvokeAsync();

    // ── Delete confirmation ──────────────────────────────────────────────────

    private void RenderDeleteDialog(RenderTreeBuilder builder)
    {
        var hasSummary = !string.IsNullOrWhiteSpace(DeleteSummary);
        builder.OpenComponent<TmModal>(0);
        builder.AddAttribute(1, nameof(TmModal.Show), true);
        builder.AddAttribute(2, nameof(TmModal.Title), DeleteTitle);
        builder.AddAttribute(3, nameof(TmModal.Size), ModalSize.Small);
        builder.AddAttribute(4, nameof(TmModal.ShowFooter), false);
        builder.AddAttribute(5, nameof(TmModal.ShowDefaultFooterButtons), false);
        builder.AddAttribute(6, nameof(TmModal.CloseOnOverlayClick), false);
        builder.AddAttribute(7, nameof(TmModal.CloseOnEscape), true);
        builder.AddAttribute(8, nameof(TmModal.OnClose), DeleteCancel);
        builder.AddAttribute(9, nameof(TmModal.Class), "tm-modeling-authoring-dialog");

        // A destructive confirmation is an ALERTDIALOG (phase 3 review, U3): the body sentence is
        // its description, focus starts on Cancel (the safe action) and the confirm action is the
        // danger variant. The restore target is the element the editor picked (U1) — never <body>.
        builder.AddAttribute(10, nameof(TmModal.DialogRole), "alertdialog");
        builder.AddAttribute(11, nameof(TmModal.AriaDescribedBy), hasSummary ? _deleteSummaryId : null);
        builder.AddAttribute(12, nameof(TmModal.InitialFocusSelector), ".tm-modeling-authoring-dialog__cancel");
        builder.AddAttribute(13, nameof(TmModal.RestoreFocusTargetId), DeleteRestoreFocusTargetId);
        builder.AddAttribute(14, nameof(TmModal.AdditionalAttributes), Attributes("modeling-delete-confirm-dialog"));
        builder.AddAttribute(15, nameof(TmModal.ChildContent), (RenderFragment)(body =>
        {
            body.OpenElement(0, "div");
            body.AddAttribute(1, "class", "tm-modeling-authoring-dialog__body");
            if (hasSummary)
            {
                body.OpenElement(10, "p");
                body.AddAttribute(11, "id", _deleteSummaryId);
                body.AddAttribute(12, "data-testid", TestId("modeling-delete-summary"));
                body.AddContent(13, DeleteSummary);
                body.CloseElement();
            }

            RenderActions(
                body,
                20,
                @class: "tm-modeling-authoring-dialog__cancel",
                cancelLabel: CancelLabel,
                cancelTestId: TestId("modeling-delete-cancel"),
                cancel: DeleteCancel,
                confirmLabel: DeleteConfirmLabel,
                confirmTestId: TestId("modeling-delete-confirm"),
                confirmDisabled: false,
                confirm: DeleteConfirm,
                confirmVariant: ButtonVariant.Danger);
            body.CloseElement();
        }));
        builder.CloseComponent();
    }

    // ── Shared leaf builders ─────────────────────────────────────────────────

    /// <summary>A labelled field row: a <c>&lt;label&gt;</c> wrapping the caption and the control.</summary>
    private static void RenderField(RenderTreeBuilder builder, int sequence, string _testId, string label, RenderFragment control)
    {
        builder.OpenElement(sequence, "label");
        builder.AddAttribute(sequence + 1, "class", "tm-modeling-authoring-dialog__field");
        builder.OpenElement(sequence + 2, "span");
        builder.AddContent(sequence + 3, label);
        builder.CloseElement();
        builder.AddContent(sequence + 4, control);
        builder.CloseElement();
    }

    /// <summary>The Cancel / Confirm row of a dialog.</summary>
    private void RenderActions(
        RenderTreeBuilder builder,
        int sequence,
        string @class,
        string cancelLabel,
        string cancelTestId,
        EventCallback cancel,
        string confirmLabel,
        string confirmTestId,
        bool confirmDisabled,
        EventCallback confirm,
        ButtonVariant confirmVariant)
    {
        builder.OpenElement(sequence, "div");
        builder.AddAttribute(sequence + 1, "class", "tm-modeling-authoring-dialog__actions");

        builder.OpenComponent<TmButton>(sequence + 2);
        builder.AddAttribute(sequence + 3, nameof(TmButton.Variant), ButtonVariant.Secondary);
        builder.AddAttribute(sequence + 4, nameof(TmButton.Class), @class);
        builder.AddAttribute(sequence + 5, nameof(TmButton.OnClick), cancel);
        builder.AddAttribute(sequence + 6, nameof(TmButton.ChildContent), (RenderFragment)(content => content.AddContent(0, cancelLabel)));
        builder.AddAttribute(sequence + 7, "data-testid", cancelTestId);
        builder.CloseComponent();

        builder.OpenComponent<TmButton>(sequence + 8);
        builder.AddAttribute(sequence + 9, nameof(TmButton.Type), ButtonType.Submit);
        builder.AddAttribute(sequence + 10, nameof(TmButton.Variant), confirmVariant);
        builder.AddAttribute(sequence + 11, nameof(TmButton.Disabled), confirmDisabled);
        builder.AddAttribute(sequence + 12, nameof(TmButton.OnClick), confirm);
        builder.AddAttribute(sequence + 13, nameof(TmButton.ChildContent), (RenderFragment)(content => content.AddContent(0, confirmLabel)));
        builder.AddAttribute(sequence + 14, "data-testid", confirmTestId);
        builder.CloseComponent();

        builder.CloseElement();
    }

    private Dictionary<string, object> Attributes(string testId)
        => new(StringComparer.Ordinal) { ["data-testid"] = TestId(testId) };
}
