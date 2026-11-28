namespace Tempo.Blazor.Components.DocumentEditor.Registry;

/// <summary>Central registry for named editor commands. Owns the forced-disable stack and read-only gating.</summary>
public sealed class DocumentEditorCommandRegistry
{
    private readonly Dictionary<string, IDocumentEditorCommandEntry> _commands =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, HashSet<string>> _forcedDisableReasons =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, DocumentEditorCommandState> _currentState =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _lastContextSignature;

    /// <summary>
    /// The commands of decision G2 that are tablet-and-up: page layout, equations, creating / renaming / deleting a style, the cell properties and
    /// comparing against sources. On a phone layout (<see cref="DocumentEditorCommandContext.IsPhoneLayout"/>) they are disabled whatever the rest of the
    /// context says, so the command palette and the keyboard cannot reach what the phone chrome hides.
    /// </summary>
    public static IReadOnlySet<string> PhoneRestrictedCommands { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "insertEquation", "pageLayout", "createStyle", "renameStyle", "deleteStyle", "cellProperties", "compareDocuments"
    };

    /// <summary>
    /// Monotonic counter incremented on every ACTUAL refresh (perf plan N7.3). Consumers can use it
    /// as a cheap change token for derived caches (e.g. the toolbar overflow menu groups).
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Registers a command. Throws if a command with the same name is already registered.</summary>
    public void Register(IDocumentEditorCommandEntry command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_commands.ContainsKey(command.Name))
        {
            throw new InvalidOperationException(
                $"A command named '{command.Name}' is already registered in this registry.");
        }

        _commands[command.Name] = command;
        _forcedDisableReasons[command.Name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Fáze 22 (code review): registrace mění množinu příkazů bez změny kontextu — signature
        // gate musí spadnout, jinak pozdě registrovaný příkaz nikdy nedostane stav (GetState null
        // → trvale skryté/disabled tlačítko, dokud se kontext nezmění).
        _lastContextSignature = null;
    }

    /// <summary>Tries to find a registered command by name. Returns <c>false</c> when not found.</summary>
    public bool TryGet(string name, out IDocumentEditorCommandEntry? command)
    {
        return _commands.TryGetValue(name, out command);
    }

    /// <summary>Returns a registered command by name. Throws when not found.</summary>
    public IDocumentEditorCommandEntry GetRequired(string name)
    {
        if (!_commands.TryGetValue(name, out var command))
        {
            throw new InvalidOperationException(
                $"Command '{name}' is not registered in this registry.");
        }

        return command;
    }

    /// <summary>Adds a forced-disable reason for the named command.
    /// The command stays disabled until all reasons are removed.</summary>
    public void AddForceDisableReason(string commandName, string reason)
    {
        if (_forcedDisableReasons.TryGetValue(commandName, out var reasons) && reasons.Add(reason))
        {
            // Forced reasons change command state without any context change — drop the signature
            // gate so the next refresh recomputes (perf plan N7.3).
            _lastContextSignature = null;
        }
    }

    /// <summary>Removes a previously added forced-disable reason.
    /// The command becomes eligible for re-enabling once no reasons remain.</summary>
    public void RemoveForceDisableReason(string commandName, string reason)
    {
        if (_forcedDisableReasons.TryGetValue(commandName, out var reasons) && reasons.Remove(reason))
        {
            _lastContextSignature = null;
        }
    }

    /// <summary>
    /// Recomputes and caches the state of every registered command against the given context.
    /// When <paramref name="contextSignature"/> is provided and matches the previous refresh's
    /// signature, the rebuild is skipped entirely (perf plan N7.3) — the caller guarantees the
    /// signature covers every input its command lambdas read. A null signature always refreshes.
    /// </summary>
    public Task RefreshAllAsync(DocumentEditorCommandContext context, string? contextSignature = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (contextSignature is not null && Version > 0 && string.Equals(contextSignature, _lastContextSignature, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        _lastContextSignature = contextSignature;
        foreach (var (name, command) in _commands)
        {
            _currentState[name] = BuildState(command, context, _forcedDisableReasons[name]);
        }

        Version += 1;
        return Task.CompletedTask;
    }

    /// <summary>Returns the last computed state for the named command, or <c>null</c> when not yet refreshed.</summary>
    public DocumentEditorCommandState? GetState(string name)
    {
        return _currentState.TryGetValue(name, out var state) ? state : null;
    }

    /// <summary>Read-only view of the last computed state for every registered command.</summary>
    public IReadOnlyDictionary<string, DocumentEditorCommandState> CurrentState => _currentState;

    /// <summary>Executes a command only when the current command state is visible and enabled.</summary>
    public async Task<bool> ExecuteAsync(string name, DocumentEditorCommandContext context, object? payload = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var command = GetRequired(name);
        var state = BuildState(command, context, _forcedDisableReasons[name]);
        _currentState[name] = state;
        // A single-command state write changes the observable registry state outside RefreshAllAsync:
        // bump the version (derived caches must rebuild) and drop the signature gate.
        Version += 1;
        _lastContextSignature = null;

        if (!state.IsVisible || !state.IsEnabled)
        {
            return false;
        }

        await command.ExecuteAsync(context, payload);
        return true;
    }

    private static DocumentEditorCommandState BuildState(
        IDocumentEditorCommandEntry command,
        DocumentEditorCommandContext context,
        HashSet<string> forcedDisableReasons)
    {
        var value = command.ComputeValue(context);
        var isVisible = command.ComputeVisible(context);

        if (forcedDisableReasons.Count > 0)
        {
            return new DocumentEditorCommandState
            {
                Name = command.Name,
                IsEnabled = false,
                IsVisible = isVisible,
                AffectsData = command.AffectsData,
                Value = value,
                DisabledReason = string.Join("; ", forcedDisableReasons),
                DisabledReasonKey = command.DisabledReasonKey,
                DescriptionKey = command.DescriptionKey,
                TooltipKey = command.TooltipKey,
                Category = command.Category,
                DefaultShortcut = command.DefaultShortcut,
                Icon = command.Icon
            };
        }

        if (context.IsReadOnly && command.AffectsData)
        {
            return new DocumentEditorCommandState
            {
                Name = command.Name,
                IsEnabled = false,
                IsVisible = isVisible,
                AffectsData = command.AffectsData,
                Value = value,
                DisabledReason = "read-only",
                DisabledReasonKey = "TmDocumentEditor_CommandDisabledReadOnly",
                DescriptionKey = command.DescriptionKey,
                TooltipKey = command.TooltipKey,
                Category = command.Category,
                DefaultShortcut = command.DefaultShortcut,
                Icon = command.Icon
            };
        }

        if (context.IsProtected
            && command.AffectsData
            && !context.IsInEditableRegion
            && !CanRunOutsideProtectedEditableRegion(command.Name))
        {
            return new DocumentEditorCommandState
            {
                Name = command.Name,
                IsEnabled = false,
                IsVisible = isVisible,
                AffectsData = command.AffectsData,
                Value = value,
                DisabledReason = "protected",
                DisabledReasonKey = command.DisabledReasonKey ?? "TmDocumentEditor_CommandDisabledUnavailable",
                DescriptionKey = command.DescriptionKey,
                TooltipKey = command.TooltipKey,
                Category = command.Category,
                DefaultShortcut = command.DefaultShortcut,
                Icon = command.Icon
            };
        }

        var isEnabled = command.ComputeEnabled(context) && !(context.IsPhoneLayout && PhoneRestrictedCommands.Contains(command.Name));
        return new DocumentEditorCommandState
        {
            Name = command.Name,
            IsEnabled = isVisible && isEnabled,
            IsVisible = isVisible,
            AffectsData = command.AffectsData,
            Value = value,
            DisabledReason = isVisible && isEnabled ? null : "unavailable",
            DisabledReasonKey = isVisible && isEnabled
                ? null
                : command.DisabledReasonKey ?? "TmDocumentEditor_CommandDisabledUnavailable",
            DescriptionKey = command.DescriptionKey,
            TooltipKey = command.TooltipKey,
            Category = command.Category,
            DefaultShortcut = command.DefaultShortcut,
            Icon = command.Icon
        };
    }

    private static bool CanRunOutsideProtectedEditableRegion(string commandName) =>
        string.Equals(commandName, "undo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(commandName, "redo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(commandName, "save", StringComparison.OrdinalIgnoreCase)
        // Protection administration must stay operable while the document is protected —
        // otherwise enabling protection locks out marking editable regions AND unprotecting.
        || string.Equals(commandName, "protectDocument", StringComparison.OrdinalIgnoreCase)
        || string.Equals(commandName, "markEditableRegion", StringComparison.OrdinalIgnoreCase);
}
