using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Dialog for mapping the columns of an imported file to Gantt task properties. The columns are pre-mapped by
/// header name (case- and diacritics-insensitive, English / Czech / French and the active locale's own labels),
/// optionally seeded by <see cref="InitialMappings"/>. Import stays disabled until a task title, a start date and
/// an end date or a duration are mapped; two columns mapped to one field only raise a warning.
/// </summary>
public partial class TmGanttImportMappingDialog
{
    /// <summary>The fields a column can be mapped to, in the order the select lists them.</summary>
    private static readonly GanttColumnKey[] AvailableTargets =
    [
        GanttColumnKey.Title,
        GanttColumnKey.Start,
        GanttColumnKey.End,
        GanttColumnKey.Duration,
        GanttColumnKey.Progress,
        GanttColumnKey.Status,
        GanttColumnKey.Priority,
        GanttColumnKey.Deadline,
        GanttColumnKey.Assignees,
        GanttColumnKey.Predecessor,
        GanttColumnKey.WBS,
        GanttColumnKey.Estimation,
        GanttColumnKey.TimeLog,
    ];

    /// <summary>
    /// Header names (normalised: lower case, no diacritics, letters and digits only) that identify a field. This
    /// is matching vocabulary, not UI text - the labels of the active locale are added on top of it.
    /// </summary>
    private static readonly Dictionary<string, GanttColumnKey> HeaderAliases = BuildAliases();

    private readonly Dictionary<string, GanttColumnKey?> _mappings = new(StringComparer.Ordinal);
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private List<string> _columns = [];
    private string? _signature;
    private bool _wasOpen;

    /// <summary>Source column names from the imported file.</summary>
    [Parameter] public IEnumerable<string> SourceColumns { get; set; } = [];

    /// <summary>
    /// Mappings the dialog starts from (a remembered choice of a previous import, a host's own defaults). A source
    /// column is matched case-insensitively; the remaining columns are still mapped by header name, but never onto
    /// a field a host mapping already claimed.
    /// </summary>
    [Parameter] public IReadOnlyList<GanttColumnMapping>? InitialMappings { get; set; }

    /// <summary>Whether the dialog is visible.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Fires when the user confirms the mapping.</summary>
    [Parameter] public EventCallback<IReadOnlyList<GanttColumnMapping>> OnImport { get; set; }

    /// <summary>Fires when the dialog should close.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    private IReadOnlyList<SelectOption<GanttColumnKey?>> TargetOptions =>
        AvailableTargets.Select(key => new SelectOption<GanttColumnKey?>(key, GanttLabels.Column(Loc, key))).ToList();

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _columns = SourceColumns.Distinct(StringComparer.Ordinal).ToList();

        // The mapping is rebuilt when the dialog OPENS and when the file's columns or the host's seed change by
        // value - never on an unrelated parent render, which would throw the user's edits away.
        var signature = string.Join('\u001f', _columns) + '\u001e'
            + string.Join('\u001f', (InitialMappings ?? []).Select(m => $"{m.SourceColumn}>{m.TargetProperty}"));
        var opened = IsOpen && !_wasOpen;
        _wasOpen = IsOpen;
        if (opened || signature != _signature)
        {
            _signature = signature;
            BuildMapping();
        }
    }

    private void BuildMapping()
    {
        _mappings.Clear();
        foreach (var column in _columns)
        {
            _mappings[column] = null;
        }

        var claimed = new HashSet<GanttColumnKey>();
        foreach (var seed in InitialMappings ?? [])
        {
            var column = _columns.FirstOrDefault(c => string.Equals(c, seed.SourceColumn, StringComparison.OrdinalIgnoreCase));
            if (column is null || _mappings[column] is not null || !AvailableTargets.Contains(seed.TargetProperty)) continue;
            _mappings[column] = seed.TargetProperty;
            claimed.Add(seed.TargetProperty);
        }

        var aliases = AliasesWithLocale();
        foreach (var column in _columns)
        {
            if (_mappings[column] is not null) continue;
            if (aliases.TryGetValue(Normalize(column), out var key) && claimed.Add(key))
            {
                _mappings[column] = key;
            }
        }
    }

    /// <summary>The static header vocabulary plus the labels of the active locale ("Úkol", "Začátek", ...).</summary>
    private Dictionary<string, GanttColumnKey> AliasesWithLocale()
    {
        var aliases = new Dictionary<string, GanttColumnKey>(HeaderAliases, StringComparer.Ordinal);
        foreach (var key in AvailableTargets)
        {
            aliases.TryAdd(Normalize(GanttLabels.Column(Loc, key)), key);
        }

        return aliases;
    }

    /// <summary>Lower case, diacritics removed, only letters and digits: "Název úkolu" and "NAZEV_UKOLU" compare equal.</summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static Dictionary<string, GanttColumnKey> BuildAliases()
    {
        var aliases = new Dictionary<string, GanttColumnKey>(StringComparer.Ordinal);

        void Add(GanttColumnKey key, params string[] names)
        {
            foreach (var name in names)
            {
                aliases.TryAdd(Normalize(name), key);
            }
        }

        Add(GanttColumnKey.Title, "Title", "Name", "Task", "Task name", "Task title", "Summary",
            "Název", "Název úkolu", "Úkol", "Název úlohy", "Úloha", "Nom", "Nom de la tâche", "Tâche", "Titre", "Libellé");
        Add(GanttColumnKey.Start, "Start", "Start date", "Begin", "Begin date", "From", "Starts",
            "Začátek", "Datum zahájení", "Zahájení", "Od", "Début", "Date de début", "Commencement");
        Add(GanttColumnKey.End, "End", "End date", "Finish", "Finish date", "To", "Until",
            "Konec", "Datum ukončení", "Ukončení", "Do", "Fin", "Date de fin");
        Add(GanttColumnKey.Duration, "Duration", "Length", "Days",
            "Trvání", "Délka", "Doba", "Doba trvání", "Durée");
        Add(GanttColumnKey.Progress, "Progress", "Done", "Complete", "Completion", "Percent complete", "Percent done", "Pct complete",
            "Průběh", "Postup", "Dokončeno", "Hotovo", "Avancement", "Progression", "Pourcentage");
        Add(GanttColumnKey.Status, "Status", "State", "Stav", "Statut", "État");
        Add(GanttColumnKey.Priority, "Priority", "Prio", "Priorita", "Priorität", "Priorité");
        Add(GanttColumnKey.Deadline, "Deadline", "Due", "Due date", "Termín", "Datum termínu", "Échéance", "Date d'échéance");
        Add(GanttColumnKey.Assignees, "Assignee", "Assignees", "Assigned to", "Resource", "Resources", "Owner", "Responsible",
            "Zdroj", "Zdroje", "Přiřazeno", "Odpovědná osoba", "Ressource", "Ressources", "Responsable", "Affecté à");
        Add(GanttColumnKey.Predecessor, "Predecessor", "Predecessors", "Dependency", "Dependencies", "Depends on",
            "Předchůdce", "Předchůdci", "Závislost", "Závislosti", "Prédécesseur", "Prédécesseurs", "Dépendances");
        Add(GanttColumnKey.WBS, "WBS", "WBS code", "WBS id", "Outline", "Outline number");
        Add(GanttColumnKey.Estimation, "Estimate", "Estimation", "Estimated hours", "Odhad", "Odhad hodin", "Durée estimée");
        Add(GanttColumnKey.TimeLog, "Time log", "Time spent", "Logged hours", "Actual hours", "Vykázáno", "Odpracováno", "Temps passé");

        return aliases;
    }

    private string ValidationId => $"gantt-mapping-validation-{_instanceId}";

    private string SelectId(string column) => $"gantt-mapping-{_instanceId}-{_columns.IndexOf(column)}";

    private GanttColumnKey? GetMapping(string column) =>
        _mappings.TryGetValue(column, out var value) ? value : null;

    private void SetMapping(string column, GanttColumnKey? value) =>
        _mappings[column] = value;

    /// <summary>Fields that more than one column is mapped to.</summary>
    private HashSet<GanttColumnKey> DuplicateTargets() =>
        _mappings.Values
            .Where(value => value.HasValue)
            .GroupBy(value => value!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

    private bool IsMapped(GanttColumnKey key) => _mappings.Values.Any(value => value == key);

    /// <summary>The localized names of the required fields that are not mapped yet.</summary>
    private List<string> MissingRequired()
    {
        var missing = new List<string>();
        if (!IsMapped(GanttColumnKey.Title)) missing.Add(GanttLabels.Column(Loc, GanttColumnKey.Title));
        if (!IsMapped(GanttColumnKey.Start)) missing.Add(GanttLabels.Column(Loc, GanttColumnKey.Start));
        if (!IsMapped(GanttColumnKey.End) && !IsMapped(GanttColumnKey.Duration)) missing.Add(Loc["TmGantt_ImportMappingEndOrDuration"]);
        return missing;
    }

    private bool IsValid => MissingRequired().Count == 0;

    private async Task ConfirmAsync()
    {
        if (!IsValid) return;

        var result = _columns
            .Where(column => _mappings.TryGetValue(column, out var value) && value.HasValue)
            .Select(column => new GanttColumnMapping { SourceColumn = column, TargetProperty = _mappings[column]!.Value })
            .ToList();
        await OnImport.InvokeAsync(result);
        await OnClose.InvokeAsync();
    }
}
