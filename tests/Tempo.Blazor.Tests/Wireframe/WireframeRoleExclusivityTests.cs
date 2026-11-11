using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 7ba95ed6 phase 16: a UI ROLE is the library-agnostic name an authoring agent asks for, so it has to
/// resolve to ONE component. <c>otp-input</c> was claimed by two components at once — the dedicated
/// <c>TmOtpInput</c> and the generic <c>TmMaskedTextBox</c> — which made the role ambiguous, put a
/// <c>role-ambiguous</c> warning on every OTP a design asked for and resolved to the wrong component.
/// The dedicated component now owns the role exclusively.
/// <para>
/// The built-in catalogue also declares a handful of DELIBERATE overlaps: a composite's part (a
/// <c>TmAccordionItem</c> is an <c>accordion</c>, a <c>TmTabPanel</c> is <c>tabs</c>) and a family member
/// that can stand in for its kind (a <c>TmPivotTable</c> is a <c>data-table</c>) both claim the parent role
/// so a design can reach them at all. Those are pinned by name below: the operations engine reports
/// <c>role-ambiguous</c> for each one, so a NEW collision has to be a conscious addition to that list
/// instead of an accident that silently changes what a role resolves to.
/// </para>
/// </summary>
public sealed class WireframeRoleExclusivityTests
{
    private static WireframeSchemaRegistry Registry() => new([new BuiltInComponentSchemas()]);

    [Fact]
    public void OtpInputRole_IsOwnedExclusivelyByTheDedicatedComponent()
    {
        var registry = Registry();

        var candidates = registry.ResolveByRole("otp-input", scope: null, targetPackIds: null);

        candidates.Select(schema => schema.Type).Should().Equal(
            ["TmOtpInput"],
            "the dedicated OTP component owns `otp-input`; a mask is a different control");

        new BuiltInComponentSchemas().GetSchemas().Single(schema => schema.Type == "TmMaskedTextBox")
            .Roles.Should().Equal("masked-input");
    }

    [Fact]
    public void EveryBuiltInRoleResolvesToAHomeComponent_AndOnlyTheDocumentedRolesOverlap()
    {
        var registry = Registry();

        var roles = new BuiltInComponentSchemas()
            .GetSchemas()
            .SelectMany(schema => (schema.Roles ?? []).Select(role => (Role: role, schema.Type)))
            .GroupBy(pair => pair.Role, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(pair => pair.Type).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        roles.Should().NotBeEmpty("the built-in catalogue is the role vocabulary's source of truth");

        foreach (var (role, types) in roles)
        {
            var resolved = registry.ResolveByRole(role, scope: null, targetPackIds: null);

            resolved.Select(schema => schema.Type).Should().BeEquivalentTo(
                types,
                $"role '{role}' must resolve to every component that declares it");
            resolved.Should().NotBeEmpty($"role '{role}' is declared, so it must resolve");
        }

        var ambiguous = roles.Where(pair => pair.Value.Length > 1).Select(pair => pair.Key).ToArray();

        ambiguous.Should().BeEquivalentTo(
            IntentionalOverlapRoles,
            "a new role collision must be a conscious decision — either give the role one home component "
            + "or document the overlap here (the operations engine reports `role-ambiguous` for each one)");

        ambiguous.Should().NotContain(
            "otp-input",
            "the OTP role belongs to the dedicated component alone (that is the whole point of phase 16)");
    }

    /// <summary>
    /// The built-in role overlaps that are intentional and documented. Every one of them resolves to its
    /// FIRST candidate (the operations engine's primary), so the order of the role map decides which
    /// component an authoring agent gets. Adding a component that claims an already-used role means adding
    /// its role here with the reason the overlap is wanted.
    /// </summary>
    private static readonly string[] IntentionalOverlapRoles =
    [
        "accordion",            // a TmAccordionItem is the part of an accordion
        "attachment-manager",   // the activity and file-manager surfaces host attachments
        "avatar",               // the comment composer renders the author's avatar
        "badge",                // the auto-save indicator is a status badge
        "button",               // toolbar/export/prompt actions are buttons
        "calendar-view",        // a calendar grid is the month presentation of a calendar view
        "card",                 // the widget selector arranges cards
        "chart",                // a sparkline-free pivot/heatmap/stock chart is still a chart
        "chip",                 // reactions and filter chips are chips
        "context-menu",         // a context-menu item is the part of a context menu
        "copy-button",          // the share-link panel's copy affordance
        "dashboard",            // the dock manager and portfolio arrange dashboard widgets
        "data-table",           // the pivot table, import preview and tree list are tables
        "date-range-picker",    // the time and date-time ranges are ranges
        "datetime-picker",      // the date-time RANGE also picks a date-time
        "diagram-editor",       // the workflow canvas and minimap are the editor's parts
        "dialog",               // the document-open and email-item dialogs are dialogs
        "dropdown",             // a dropdown item is the part of a dropdown
        "entity-picker",        // the user picker picks a user entity
        "expression-editor",    // the formula builder edits an expression
        "file-drop",            // the import wizard and file manager accept files
        "filter-builder",       // a filter chip and the condition builder build filters
        "form-field",           // a form row and the validated/properties fields wrap a label
        "form-section",         // a form row groups fields inside a section
        "icon",                 // a QR code and a barcode are drawn icons
        "image-gallery",        // the lightbox is the gallery's zoomed view
        "list-view",            // chat, activity and tree surfaces present a list of records
        "number-input",         // a decimal input is a number input
        "popover",              // the reaction picker floats like a popover
        "rich-text-editor",     // the document and notion editors are rich-text surfaces
        "scheduler",            // a Gantt is a resource scheduler
        "search-input",         // the command palette, query input and filterable dropdown search
        "section",              // a divider, stack, splitter and toggle section are layout sections
        "select",               // the colour pickers choose one value from a list
        "signature-pad",        // the signature capture is the pad's own capture flow
        "stepper",              // the import wizard and submission timeline show ordered steps
        "tabs",                 // a tab panel is the part of a tab strip
        "text-area",            // comments and prompts accept multi-line text
        "text-input",           // the chat composer and query input accept a short text value
        "time-picker",          // the time-range and time input pick a time of day
        "timeline",             // the activity log and audit trail show ordered events
        "toggle",               // a toggle section is toggled like a switch
        "toolbar",              // toolbar buttons, dividers and groups are the toolbar's parts
    ];
}
