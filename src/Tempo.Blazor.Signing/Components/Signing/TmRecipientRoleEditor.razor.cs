using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Edits signing template roles and concrete submission recipient details.</summary>
/// <remarks>
/// Newly created roles default to the <see cref="SigningRolePalette"/> colours. Those hex values are
/// serialized template data — a saved template stores the colour string verbatim — and mirror the
/// <c>--tm-signing-role-1..6</c> tokens declared in the Signing stylesheet; the tokens exist so CSS
/// can paint role colours (and so hosts can rebrand the palette), while this editor keeps writing
/// concrete colours into the model.
/// </remarks>
public partial class TmRecipientRoleEditor : IAsyncDisposable
{
    /// <summary>Draft-slot keys of the free-text fields that commit on blur / Enter (phase 7
    /// review: the stored value must never be normalized mid-keystroke).</summary>
    private const string RoleFieldName = "name";
    private const string RoleFieldFullName = "fullName";
    private const string RoleFieldEmail = "email";
    private const string RoleFieldPhone = "phone";

    private readonly List<SigningSubmitterRole> _roles = [];
    private readonly HashSet<string> _collapsedRoleUuids = new(StringComparer.Ordinal);
    private readonly Dictionary<(string RoleUuid, string Field), string> _drafts = new();
    private IReadOnlyList<SigningSubmitterRole>? _lastRoles;
    private int? _dragSourceIndex;
    private SigningEditorInterop? _interop;
    private string? _pendingFocusSelector;
    private string? _moveAnnouncement;

    /// <summary>Per-instance scope attribute for the focus selectors (never a DOM id — two editors
    /// on one page must not collide).</summary>
    private readonly string _editorId = $"tm-role-editor-{Guid.NewGuid():N}";

    /// <summary>Injects the JS runtime for the remove/move focus hand-off (best-effort).</summary>
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Roles or submission recipients currently edited.</summary>
    [Parameter] public IReadOnlyList<SigningSubmitterRole> Roles { get; set; } = [];

    /// <summary>Callback invoked whenever roles change.</summary>
    [Parameter] public EventCallback<IReadOnlyList<SigningSubmitterRole>> RolesChanged { get; set; }

    /// <summary>Whether the editor edits template roles or concrete submission recipients.</summary>
    [Parameter] public TmRecipientRoleEditorMode Mode { get; set; }

    /// <summary>Fields available for invite-via-field rules.</summary>
    [Parameter] public IReadOnlyList<SigningField> Fields { get; set; } = [];

    /// <summary>Whether the editor controls are disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private bool HasValidationErrors => _roles.Any(IsEmailMissing);

    private string RootClass
    {
        get
        {
            var classes = new List<string> { "tm-recipient-role-editor" };
            AddClass(classes, Disabled, "tm-recipient-role-editor--disabled");
            AddClass(classes, HasValidationErrors, "tm-recipient-role-editor--invalid");
            AddClass(classes, Mode == TmRecipientRoleEditorMode.TemplateRoles, "tm-recipient-role-editor--template");
            AddClass(classes, Mode == TmRecipientRoleEditorMode.SubmissionRecipients, "tm-recipient-role-editor--submission");

            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes.Add(Class);
            }

            return string.Join(" ", classes);
        }
    }

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_lastRoles, Roles))
        {
            _roles.Clear();
            _roles.AddRange(NormalizeRoles(Roles));

            // Forget collapse state of roles that no longer exist (a removed uuid must never
            // silently expand a different role).
            _collapsedRoleUuids.RemoveWhere(uuid => _roles.All(role => !string.Equals(role.Uuid, uuid, StringComparison.Ordinal)));
            PruneDrafts();

            if (_roles.Count == 0)
            {
                _roles.Add(CreateDefaultRole(0));
            }

            _lastRoles = Roles;
        }
    }

    /// <summary>
    /// Applies the focus hand-off AFTER the render that moved the rows (phase 7 review): a selector
    /// captured before the render would hit the DOM before Blazor rebuilt it. The Add action is the
    /// fallback when the primary target no longer exists.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocusSelector is null)
        {
            return;
        }

        var selector = _pendingFocusSelector;
        _pendingFocusSelector = null;

        _interop ??= new SigningEditorInterop(JS);
        // Stay on the renderer sync context (see TmConditionBuilder.OnAfterRenderAsync).
        if (!await _interop.FocusSelectorAsync(selector))
        {
            await _interop.FocusSelectorAsync(AddActionSelector);
        }
    }

    private string AddActionSelector => $"[data-role-editor='{_editorId}'] .tm-recipient-role-editor__add";

    /// <summary>Queues the focus move onto <paramref name="selector"/> for the next render; the
    /// editor's own Add action is the fallback target.</summary>
    private void RequestFocus(string selector)
    {
        _pendingFocusSelector = selector;
    }

    private string GetCardClass(SigningSubmitterRole role)
    {
        var classes = new List<string> { "tm-recipient-role-editor__card" };
        AddClass(classes, IsEmailMissing(role), "tm-recipient-role-editor__card--invalid");
        AddClass(classes, !IsExpanded(role), "tm-recipient-role-editor__card--collapsed");
        return string.Join(" ", classes);
    }

    /// <summary>Whether the role's editing body is rendered (cards start expanded — today's
    /// behaviour; the collapse is the compact opt-in of the card header).</summary>
    private bool IsExpanded(SigningSubmitterRole role)
    {
        return !_collapsedRoleUuids.Contains(role.Uuid);
    }

    private Task ToggleRoleAsync(SigningSubmitterRole role)
    {
        if (Disabled)
        {
            return Task.CompletedTask;
        }

        if (!_collapsedRoleUuids.Remove(role.Uuid))
        {
            _collapsedRoleUuids.Add(role.Uuid);
        }

        return Task.CompletedTask;
    }

    /// <summary>The collapsed header's contact preview (full name over email; empty for template
    /// roles whose name already rides the pill).</summary>
    private string GetHeaderPreview(SigningSubmitterRole role)
    {
        if (Mode != TmRecipientRoleEditorMode.SubmissionRecipients)
        {
            return string.Empty;
        }

        var name = string.IsNullOrWhiteSpace(role.FullName) ? null : role.FullName;
        var email = string.IsNullOrWhiteSpace(role.Email) ? null : role.Email;
        return name is not null && email is not null ? $"{name} · {email}" : name ?? email ?? string.Empty;
    }

    /// <summary>
    /// The pill's inline style: the role colour through the runtime local custom property. The text
    /// colour rides the style only when it resolves through a token — default-palette colours (and
    /// the legacy role-6 value) take their per-theme <c>--tm-signing-role-N-on</c> twin and a
    /// colour-less (or unparsable) colour paints the primary fill, whose on-colour token the theme
    /// resolves. A CUSTOM colour keeps the fixed paint, so its fixed ink rides the tone class
    /// (<see cref="GetPillClass"/>) instead of an inline hex.
    /// </summary>
    private string GetPillStyle(SigningSubmitterRole role)
    {
        var style = $"--tm-signing-role-color: {SigningRolePalette.ToCssColor(role.Color)}";
        return SigningRolePalette.IsLightPaint(role.Color) is null
            ? $"{style}; color: {SigningRolePalette.ContrastTextFor(role.Color)}"
            : style;
    }

    /// <summary>The pill classes: the base plus the tone class for a CUSTOM paint — the light
    /// paint takes the near-black ink, the dark paint the white one (both fixed tokens). A paint
    /// whose ink the style already carries as a token (palette colours, colour-less) adds none.</summary>
    private string GetPillClass(SigningSubmitterRole role)
    {
        var classes = new List<string> { "tm-recipient-role-editor__role-pill" };
        switch (SigningRolePalette.IsLightPaint(role.Color))
        {
            case true:
                classes.Add("tm-recipient-role-editor__role-pill--on-light");
                break;
            case false:
                classes.Add("tm-recipient-role-editor__role-pill--on-dark");
                break;
        }

        return string.Join(" ", classes);
    }

    private static string GetSwatchStyle(int swatchIndex)
    {
        return $"--tm-signing-role-color: var(--tm-signing-role-{(swatchIndex % SigningRolePalette.DefaultColors.Count) + 1})";
    }

    /// <summary>The localized colour name of a palette swatch (the hex is not an accessible name).</summary>
    private string GetSwatchName(int swatchIndex)
    {
        return (swatchIndex % SigningRolePalette.DefaultColors.Count) switch
        {
            0 => Loc["TmRecipientRoleEditor_ColorBlue"],
            1 => Loc["TmRecipientRoleEditor_ColorGreen"],
            2 => Loc["TmRecipientRoleEditor_ColorRed"],
            3 => Loc["TmRecipientRoleEditor_ColorPurple"],
            4 => Loc["TmRecipientRoleEditor_ColorCyan"],
            _ => Loc["TmRecipientRoleEditor_ColorAmber"]
        };
    }

    private static bool IsSwatchActive(SigningSubmitterRole role, string swatchColor)
    {
        return string.Equals(role.Color, swatchColor, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddClass(List<string> classes, bool condition, string cssClass)
    {
        if (condition)
        {
            classes.Add(cssClass);
        }
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    private IEnumerable<SigningSubmitterRole> GetOtherRoles(SigningSubmitterRole role)
    {
        return _roles.Where(candidate => !string.Equals(candidate.Uuid, role.Uuid, StringComparison.Ordinal));
    }

    private bool IsEmailMissing(SigningSubmitterRole role)
    {
        return Mode == TmRecipientRoleEditorMode.SubmissionRecipients
            && !role.IsOptional
            && string.IsNullOrWhiteSpace(role.Email);
    }

    private async Task AddRoleAsync()
    {
        if (Disabled)
        {
            return;
        }

        _roles.Add(CreateDefaultRole(_roles.Count));
        await NotifyChangedAsync();
    }

    private async Task RemoveRoleAsync(int index)
    {
        if (Disabled || !TryGetRole(index, out _))
        {
            return;
        }

        _roles.RemoveAt(index);
        if (_roles.Count == 0)
        {
            _roles.Add(CreateDefaultRole(0));
        }

        // The remove action deletes the row the user is acting on — the focus lands on the toggle
        // of the role that now occupies the removed position (the previous role when the last row
        // was removed), or on the Add action when the editor emptied. The move happens AFTER the
        // render that rebuilt the list (phase 7 review).
        RequestFocus(TryGetRole(Math.Min(index, _roles.Count - 1), out var survivor)
            ? $"[data-role-editor='{_editorId}'] [data-role-uuid='{survivor.Uuid}'] .tm-recipient-role-editor__toggle"
            : AddActionSelector);

        await NotifyChangedAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_interop is not null)
        {
            await _interop.DisposeAsync();
            _interop = null;
        }
    }

    private async Task MoveRoleAsync(int index, int delta)
    {
        if (Disabled || !TryGetRole(index, out var role))
        {
            return;
        }

        var target = index + delta;
        if (target < 0 || target >= _roles.Count)
        {
            return;
        }

        (_roles[index], _roles[target]) = (_roles[target], _roles[index]);

        // The screen reader hears the result; the keyboard keeps the focus INSIDE the moved card —
        // the same-direction button at the new position, or the opposite one when that direction
        // hit the edge and disabled itself (phase 7 review).
        _moveAnnouncement = string.Format(Loc["TmRecipientRoleEditor_RoleMoved"], role.Name, target + 1);
        var sameDirectionEnabled = delta < 0 ? target > 0 : target < _roles.Count - 1;
        var buttonClass = sameDirectionEnabled
            ? (delta < 0 ? "tm-recipient-role-editor__move-up" : "tm-recipient-role-editor__move-down")
            : (delta < 0 ? "tm-recipient-role-editor__move-down" : "tm-recipient-role-editor__move-up");
        RequestFocus($"[data-role-editor='{_editorId}'] [data-role-uuid='{role.Uuid}'] .{buttonClass}");

        await NotifyChangedAsync();
    }

    private void HandleDragStart(int index)
    {
        _dragSourceIndex = Disabled || !TryGetRole(index, out _)
            ? null
            : index;
    }

    private void HandleDragEnd()
    {
        _dragSourceIndex = null;
    }

    private async Task DropRoleAsync(int targetIndex)
    {
        if (Disabled || _dragSourceIndex is not { } sourceIndex || !TryGetRole(sourceIndex, out var role) || !TryGetRole(targetIndex, out _))
        {
            return;
        }

        _roles.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        _roles.Insert(targetIndex, role);
        _dragSourceIndex = null;
        await NotifyChangedAsync();
    }

    // ── Draft buffers (phase 7 review: keep the raw text while typing) ──────────────

    /// <summary>The displayed value of a free-text field: the live DRAFT while the user types, the
    /// committed value otherwise. Drafts are dropped on blur / Enter, where the commit normalizes.</summary>
    private string GetDraftValue(SigningSubmitterRole role, string field, string committed)
    {
        return _drafts.TryGetValue((role.Uuid, field), out var draft) ? draft : committed;
    }

    private Task HandleDraftChanged(SigningSubmitterRole role, string field, string? value)
    {
        if (Disabled)
        {
            return Task.CompletedTask;
        }

        // No normalization, no notification: the keystroke must not rebuild the role list (an
        // emptied name field would snap back to the default, and every keystroke would fire
        // RolesChanged).
        _drafts[(role.Uuid, field)] = value ?? string.Empty;
        return Task.CompletedTask;
    }

    /// <summary>Commits a field's draft through <paramref name="apply"/> (which normalizes and
    /// notifies). A field without a draft (focused and blurred untouched) is a no-op.</summary>
    private Task CommitDraftAsync(SigningSubmitterRole role, string field, Func<SigningSubmitterRole, string, Task> apply)
    {
        if (Disabled || !_drafts.Remove((role.Uuid, field), out var draft))
        {
            return Task.CompletedTask;
        }

        return apply(role, draft);
    }

    private Task CommitDraftOnEnterAsync(
        KeyboardEventArgs args,
        SigningSubmitterRole role,
        string field,
        Func<SigningSubmitterRole, string, Task> apply)
    {
        return string.Equals(args.Key, "Enter", StringComparison.Ordinal)
            ? CommitDraftAsync(role, field, apply)
            : Task.CompletedTask;
    }

    private Task ApplyNameAsync(SigningSubmitterRole role, string draft)
    {
        return UpdateRoleAsync(role.Uuid, target => target.Name = draft);
    }

    private Task ApplyFullNameAsync(SigningSubmitterRole role, string draft)
    {
        return UpdateRoleAsync(role.Uuid, target => target.FullName = NormalizeOptional(draft));
    }

    private Task ApplyEmailAsync(SigningSubmitterRole role, string draft)
    {
        return UpdateRoleAsync(role.Uuid, target => target.Email = NormalizeOptional(draft));
    }

    private Task ApplyPhoneAsync(SigningSubmitterRole role, string draft)
    {
        return UpdateRoleAsync(role.Uuid, target => target.Phone = NormalizeOptional(draft));
    }

    private Task HandleColorChangedAsync(int index, string? value)
    {
        return UpdateRoleAsync(index, role => role.Color = NormalizeColor(value, index));
    }

    private Task HandleInviteByRoleChangedAsync(int index, string? value)
    {
        return UpdateRoleAsync(index, role =>
        {
            role.InviteByRoleUuid = NormalizeRoleReference(role, value);
        });
    }

    private Task HandleOptionalInviteByRoleChangedAsync(int index, string? value)
    {
        return UpdateRoleAsync(index, role =>
        {
            role.OptionalInviteByRoleUuid = NormalizeRoleReference(role, value);
        });
    }

    private Task HandleInviteViaFieldChangedAsync(int index, string? value)
    {
        return UpdateRoleAsync(index, role =>
        {
            var normalized = NormalizeOptional(value);
            role.InviteViaFieldUuid = Fields.Any(field => string.Equals(field.Uuid, normalized, StringComparison.Ordinal))
                ? normalized
                : null;
        });
    }

    private async Task UpdateRoleAsync(int index, Action<SigningSubmitterRole> update)
    {
        if (Disabled || !TryGetRole(index, out var role))
        {
            return;
        }

        update(role);
        await NotifyChangedAsync();
    }

    /// <summary>Updates a role located by uuid (the commit handlers capture the role, not its
    /// volatile row index — a reorder between typing and the commit must not retarget).</summary>
    private Task UpdateRoleAsync(string roleUuid, Action<SigningSubmitterRole> update)
    {
        var index = _roles.FindIndex(role => string.Equals(role.Uuid, roleUuid, StringComparison.Ordinal));
        return index < 0 ? Task.CompletedTask : UpdateRoleAsync(index, update);
    }

    private bool TryGetRole(int index, out SigningSubmitterRole role)
    {
        if (index < 0 || index >= _roles.Count)
        {
            role = default!;
            return false;
        }

        role = _roles[index];
        return true;
    }

    private Task NotifyChangedAsync()
    {
        for (var index = 0; index < _roles.Count; index++)
        {
            _roles[index].Order = index;
        }

        var normalized = NormalizeRoles(_roles).ToArray();
        _roles.Clear();
        _roles.AddRange(normalized);
        return RolesChanged.InvokeAsync(normalized);
    }

    /// <summary>Drops the drafts of roles the host no longer carries, so a stale draft can never
    /// leak onto a different role.</summary>
    private void PruneDrafts()
    {
        var liveUuids = _roles.Select(role => role.Uuid).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _drafts.Keys.Where(key => !liveUuids.Contains(key.RoleUuid)).ToArray())
        {
            _drafts.Remove(key);
        }
    }

    private IEnumerable<SigningSubmitterRole> NormalizeRoles(IEnumerable<SigningSubmitterRole> roles)
    {
        var ordered = roles
            .Select(Clone)
            .OrderBy(role => role.Order)
            .ThenBy(role => role.Name, StringComparer.Ordinal)
            .ToList();
        var allRoleUuids = ordered.Select(role => role.Uuid).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < ordered.Count; index++)
        {
            var role = ordered[index];
            role.Order = index;
            role.Name = string.IsNullOrWhiteSpace(role.Name)
                ? GetDefaultName(index)
                : role.Name;
            role.Color = NormalizeColor(role.Color, index);
            role.InviteByRoleUuid = NormalizeRoleReference(role, role.InviteByRoleUuid, allRoleUuids);
            role.OptionalInviteByRoleUuid = NormalizeRoleReference(role, role.OptionalInviteByRoleUuid, allRoleUuids);
            role.InviteViaFieldUuid = Fields.Any(field => string.Equals(field.Uuid, role.InviteViaFieldUuid, StringComparison.Ordinal))
                ? role.InviteViaFieldUuid
                : null;
        }

        return ordered;
    }

    private static SigningSubmitterRole Clone(SigningSubmitterRole role)
    {
        return new SigningSubmitterRole
        {
            Uuid = role.Uuid,
            Name = role.Name,
            Email = role.Email,
            Phone = role.Phone,
            FullName = role.FullName,
            Color = role.Color,
            Order = role.Order,
            IsRequester = role.IsRequester,
            IsOptional = role.IsOptional,
            InviteByRoleUuid = role.InviteByRoleUuid,
            OptionalInviteByRoleUuid = role.OptionalInviteByRoleUuid,
            InviteViaFieldUuid = role.InviteViaFieldUuid
        };
    }

    private static SigningSubmitterRole CreateDefaultRole(int index)
    {
        return new SigningSubmitterRole
        {
            Name = GetDefaultName(index),
            Color = SigningRolePalette.ColorForOrder(index),
            Order = index
        };
    }

    private static string GetDefaultName(int index)
    {
        return $"Signer {index + 1}";
    }

    private static string NormalizeColor(string? value, int index)
    {
        return string.IsNullOrWhiteSpace(value)
            ? SigningRolePalette.ColorForOrder(index)
            : value;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private string? NormalizeRoleReference(SigningSubmitterRole role, string? value)
    {
        var allRoleUuids = _roles.Select(candidate => candidate.Uuid).ToHashSet(StringComparer.Ordinal);
        return NormalizeRoleReference(role, value, allRoleUuids);
    }

    private static string? NormalizeRoleReference(SigningSubmitterRole role, string? value, HashSet<string> allRoleUuids)
    {
        var normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, role.Uuid, StringComparison.Ordinal)
            || !allRoleUuids.Contains(normalized))
        {
            return null;
        }

        return normalized;
    }

    private static string GetFieldLabel(SigningField field)
    {
        if (!string.IsNullOrWhiteSpace(field.Name))
        {
            return field.Name;
        }

        if (!string.IsNullOrWhiteSpace(field.Title))
        {
            return field.Title;
        }

        return field.Uuid;
    }
}
