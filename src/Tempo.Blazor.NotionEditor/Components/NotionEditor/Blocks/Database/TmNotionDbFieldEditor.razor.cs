using Microsoft.AspNetCore.Components;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbFieldEditor : ComponentBase
{
    // ── Parameters ────────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IDatabaseField                Field   { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField> Fields  { get; set; } = [];
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Whether the editor dialog is open. Defaults to <c>true</c> — the host renders the
    /// component only while editing, and tests render it directly.</summary>
    [Parameter] public bool Show { get; set; } = true;

    [Parameter] public EventCallback<IDatabaseField> OnFieldChanged    { get; set; }
    [Parameter] public EventCallback<IDatabaseField> OnFieldDuplicated { get; set; }
    [Parameter] public EventCallback<Guid>           OnFieldDeleted    { get; set; }
    [Parameter] public EventCallback                 OnClose           { get; set; }

    // ── General state ─────────────────────────────────────────────────────────

    /// <summary>Suffixes every input id so two editors on one page do not share an id.</summary>
    private readonly string _idSuffix = Guid.NewGuid().ToString("N");

    private string IdFor(string name) => $"tm-dbfe-{name}-{_idSuffix}";

    private string            _name        = string.Empty;
    private DatabaseFieldType _type;
    private bool              _typeChanged;

    // ── Select / MultiSelect options ──────────────────────────────────────────

    private List<OptionRowModel> _options = [];
    private OptionRowModel?      _dragging;
    private OptionRowModel?      _dragOver;

    // ── Status groups ─────────────────────────────────────────────────────────

    private List<StatusGroupModel> _statusGroups = [];

    // ── Number ────────────────────────────────────────────────────────────────

    private NumberFormat _numberFormat = NumberFormat.Number;

    // ── Formula ───────────────────────────────────────────────────────────────

    private string _formulaExpression = string.Empty;

    // ── Relation ──────────────────────────────────────────────────────────────

    private string _relationTargetDbId = string.Empty;
    private bool   _isBidirectional;

    // ── Rollup ────────────────────────────────────────────────────────────────

    private Guid?             _rollupRelationFieldId;
    private Guid?             _rollupTargetFieldId;
    private RollupAggregation _rollupAggregation = RollupAggregation.Count;

    // ── Date / DateRange ─────────────────────────────────────────────────────

    private string _dateFormat  = "MMMM D, YYYY";
    private bool   _includeTime;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private bool _initialized;

    protected override void OnParametersSet()
    {
        if (_initialized) return;
        _initialized = true;

        _name        = Field.Name;
        _type        = Field.Type;
        _typeChanged = false;

        switch (Field.Config)
        {
            case ISelectFieldConfig sel:
                _options = sel.Options.Select(o => new OptionRowModel(o.Id, o.Name, o.Color)).ToList();
                break;

            case IStatusFieldConfig st:
                _statusGroups = st.Groups
                    .Select(g => new StatusGroupModel(g.Name, g.Color,
                        g.Options.Select(o => new OptionRowModel(o.Id, o.Name, o.Color)).ToList()))
                    .ToList();
                break;

            case INumberFieldConfig num:
                _numberFormat = num.Format;
                break;

            case IFormulaFieldConfig form:
                _formulaExpression = form.Expression;
                break;

            case IRelationFieldConfig rel:
                _relationTargetDbId = rel.TargetDatabaseId.ToString();
                _isBidirectional    = rel.IsBidirectional;
                break;

            case IRollupFieldConfig roll:
                _rollupRelationFieldId = roll.RelationFieldId;
                _rollupTargetFieldId   = roll.TargetFieldId;
                _rollupAggregation     = roll.Aggregation;
                break;

            case IDateFieldConfig date:
                _dateFormat  = date.DateFormat;
                _includeTime = date.IncludeTime;
                break;
        }
    }

    private void OnTypeChanged(ChangeEventArgs e)
    {
        if (!Enum.TryParse<DatabaseFieldType>(e.Value?.ToString(), out var t)) return;
        if (t == _type) return;
        _type        = t;
        _typeChanged = true;
    }

    // ── Select / MultiSelect option management ────────────────────────────────

    private void AddOption()
        => _options.Add(new OptionRowModel(Guid.NewGuid().ToString(), string.Empty, "gray"));

    private void RemoveOption(OptionRowModel opt) => _options.Remove(opt);

    /// <summary>Non-drag alternative to the DnD reorder (WCAG 2.5.7): moves the option by delta.</summary>
    private void MoveOption(OptionRowModel opt, int delta)
    {
        var index = _options.IndexOf(opt);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _options.Count)
            return;
        _options.RemoveAt(index);
        _options.Insert(target, opt);
    }

    /// <summary>Localized select-option color name (key per color).</summary>
    private string ColorLabel(string color)
    {
        var key = $"TmNotionDbFieldEditor_Color_{color switch
        {
            "gray" => "Gray",
            "red" => "Red",
            "orange" => "Orange",
            "yellow" => "Yellow",
            "green" => "Green",
            "blue" => "Blue",
            "purple" => "Purple",
            "pink" => "Pink",
            "brown" => "Brown",
            _ => color,
        }}";
        var label = Loc[key];
        return string.IsNullOrEmpty(label) ? color : label;
    }

    private void StartDrag(OptionRowModel opt)  => _dragging = opt;
    private void SetDragOver(OptionRowModel opt) => _dragOver = opt;
    private void EndDrag() { _dragging = null; _dragOver = null; }

    private void DropOn(OptionRowModel target)
    {
        if (_dragging is null || _dragging == target) { EndDrag(); return; }
        var from = _options.IndexOf(_dragging);
        var to   = _options.IndexOf(target);
        if (from >= 0 && to >= 0) { _options.RemoveAt(from); _options.Insert(to, _dragging); }
        EndDrag();
    }

    // ── Status option management ──────────────────────────────────────────────

    private void AddStatusOption(StatusGroupModel group)
        => group.Options.Add(new OptionRowModel(Guid.NewGuid().ToString(), string.Empty, group.Color));

    private void RemoveStatusOption(StatusGroupModel group, OptionRowModel opt)
        => group.Options.Remove(opt);

    // ── Rollup helpers ────────────────────────────────────────────────────────

    private IEnumerable<IDatabaseField> RelationFields
        => Fields.Where(f => f.Type == DatabaseFieldType.Relation);

    private IDatabaseField? RollupRelationField
        => _rollupRelationFieldId.HasValue
            ? Fields.FirstOrDefault(f => f.Id == _rollupRelationFieldId.Value)
            : null;

    private void SetRollupRelationField(ChangeEventArgs e)
    {
        _rollupRelationFieldId = Guid.TryParse(e.Value?.ToString(), out var id) ? id : (Guid?)null;
        _rollupTargetFieldId   = null;
    }

    private void SetRollupTargetField(ChangeEventArgs e)
        => _rollupTargetFieldId = Guid.TryParse(e.Value?.ToString(), out var id) ? id : (Guid?)null;

    // ── Actions ───────────────────────────────────────────────────────────────

    private async Task SaveAsync()
    {
        IFieldConfig? config = BuildConfig();
        var updated = new DatabaseField
        {
            Id        = Field.Id,
            Name      = _name.Trim() is { Length: > 0 } n ? n : Field.Name,
            Type      = _type,
            IsPrimary = Field.IsPrimary,
            Config    = config,
            IsVisible = Field.IsVisible,
            Width     = Field.Width
        };
        await OnFieldChanged.InvokeAsync(updated);
    }

    private IFieldConfig? BuildConfig() => _type switch
    {
        DatabaseFieldType.Select or DatabaseFieldType.MultiSelect =>
            new SelectFieldConfig
            {
                Options = _options.Select(o => new SelectFieldOption(o.Id, o.Name, o.Color)).ToList()
            },

        DatabaseFieldType.Status =>
            new StatusFieldConfig
            {
                Groups = _statusGroups
                    .Select(g => new StatusGroup(g.Name, g.Color,
                        g.Options.Select(o => new SelectFieldOption(o.Id, o.Name, o.Color)).ToList()))
                    .ToList()
            },

        DatabaseFieldType.Number =>
            new NumberFieldConfig { Format = _numberFormat },

        DatabaseFieldType.Formula =>
            new FormulaFieldConfig { Expression = _formulaExpression },

        DatabaseFieldType.Relation when Guid.TryParse(_relationTargetDbId, out var tid) =>
            new RelationFieldConfig
            {
                TargetDatabaseId = tid,
                IsBidirectional  = _isBidirectional,
                InverseFieldId   = null
            },

        DatabaseFieldType.Rollup when _rollupRelationFieldId.HasValue && _rollupTargetFieldId.HasValue =>
            new RollupFieldConfig
            {
                RelationFieldId = _rollupRelationFieldId.Value,
                TargetFieldId   = _rollupTargetFieldId.Value,
                Aggregation     = _rollupAggregation
            },

        DatabaseFieldType.Date or DatabaseFieldType.DateRange =>
            new DateFieldConfig
            {
                DateFormat  = _dateFormat,
                TimeFormat  = _includeTime ? "HH:mm" : null,
                IncludeTime = _includeTime
            },

        _ => null
    };

    private async Task DuplicateAsync()
        => await OnFieldDuplicated.InvokeAsync(Field);

    private async Task DeleteAsync()
    {
        if (Field.IsPrimary) return;
        await OnFieldDeleted.InvokeAsync(Field.Id);
    }

    // ── Labels ────────────────────────────────────────────────────────────────

    private string TypeLabel(DatabaseFieldType t) => t switch
    {
        DatabaseFieldType.Text           => Loc["TmNotionDbFieldEditor_Type_Text"],
        DatabaseFieldType.Number         => Loc["TmNotionDbFieldEditor_Type_Number"],
        DatabaseFieldType.Select         => Loc["TmNotionDbFieldEditor_Type_Select"],
        DatabaseFieldType.MultiSelect    => Loc["TmNotionDbFieldEditor_Type_MultiSelect"],
        DatabaseFieldType.Status         => Loc["TmNotionDbFieldEditor_Type_Status"],
        DatabaseFieldType.Date           => Loc["TmNotionDbFieldEditor_Type_Date"],
        DatabaseFieldType.DateRange      => Loc["TmNotionDbFieldEditor_Type_DateRange"],
        DatabaseFieldType.Person         => Loc["TmNotionDbFieldEditor_Type_Person"],
        DatabaseFieldType.Files          => Loc["TmNotionDbFieldEditor_Type_Files"],
        DatabaseFieldType.Checkbox       => Loc["TmNotionDbFieldEditor_Type_Checkbox"],
        DatabaseFieldType.Url            => Loc["TmNotionDbFieldEditor_Type_Url"],
        DatabaseFieldType.Email          => Loc["TmNotionDbFieldEditor_Type_Email"],
        DatabaseFieldType.Phone          => Loc["TmNotionDbFieldEditor_Type_Phone"],
        DatabaseFieldType.Formula        => Loc["TmNotionDbFieldEditor_Type_Formula"],
        DatabaseFieldType.Relation       => Loc["TmNotionDbFieldEditor_Type_Relation"],
        DatabaseFieldType.Rollup         => Loc["TmNotionDbFieldEditor_Type_Rollup"],
        DatabaseFieldType.CreatedTime    => Loc["TmNotionDbFieldEditor_Type_CreatedTime"],
        DatabaseFieldType.CreatedBy      => Loc["TmNotionDbFieldEditor_Type_CreatedBy"],
        DatabaseFieldType.LastEditedTime => Loc["TmNotionDbFieldEditor_Type_LastEditedTime"],
        DatabaseFieldType.LastEditedBy   => Loc["TmNotionDbFieldEditor_Type_LastEditedBy"],
        _                                => t.ToString()
    };

    private string NumberFormatLabel(NumberFormat f) => f switch
    {
        NumberFormat.Number           => Loc["TmNotionDbFieldEditor_NF_Number"],
        NumberFormat.NumberWithCommas => Loc["TmNotionDbFieldEditor_NF_NumberWithCommas"],
        NumberFormat.Percent          => Loc["TmNotionDbFieldEditor_NF_Percent"],
        NumberFormat.Dollar           => Loc["TmNotionDbFieldEditor_NF_Dollar"],
        NumberFormat.Euro             => Loc["TmNotionDbFieldEditor_NF_Euro"],
        NumberFormat.Pound            => Loc["TmNotionDbFieldEditor_NF_Pound"],
        NumberFormat.Yen              => Loc["TmNotionDbFieldEditor_NF_Yen"],
        NumberFormat.Rupee            => Loc["TmNotionDbFieldEditor_NF_Rupee"],
        NumberFormat.Won              => Loc["TmNotionDbFieldEditor_NF_Won"],
        NumberFormat.Yuan             => Loc["TmNotionDbFieldEditor_NF_Yuan"],
        _                             => f.ToString()
    };

    private string AggregationLabel(RollupAggregation a) => a switch
    {
        RollupAggregation.Count             => Loc["TmNotionDbFieldEditor_RA_Count"],
        RollupAggregation.CountValues       => Loc["TmNotionDbFieldEditor_RA_CountValues"],
        RollupAggregation.CountUniqueValues => Loc["TmNotionDbFieldEditor_RA_CountUniqueValues"],
        RollupAggregation.Sum               => Loc["TmNotionDbFieldEditor_RA_Sum"],
        RollupAggregation.Average           => Loc["TmNotionDbFieldEditor_RA_Average"],
        RollupAggregation.Min               => Loc["TmNotionDbFieldEditor_RA_Min"],
        RollupAggregation.Max               => Loc["TmNotionDbFieldEditor_RA_Max"],
        RollupAggregation.Median            => Loc["TmNotionDbFieldEditor_RA_Median"],
        RollupAggregation.Range             => Loc["TmNotionDbFieldEditor_RA_Range"],
        RollupAggregation.ShowOriginal      => Loc["TmNotionDbFieldEditor_RA_ShowOriginal"],
        RollupAggregation.PercentEmpty      => Loc["TmNotionDbFieldEditor_RA_PercentEmpty"],
        RollupAggregation.PercentNotEmpty   => Loc["TmNotionDbFieldEditor_RA_PercentNotEmpty"],
        _                                   => a.ToString()
    };

    // ── Inner models ──────────────────────────────────────────────────────────

    internal static readonly string[] SelectColors =
        ["gray", "red", "orange", "yellow", "green", "blue", "purple", "pink", "brown"];

    internal sealed class OptionRowModel(string id, string name, string color)
    {
        public string Id    { get; }      = id;
        public string Name  { get; set; } = name;
        public string Color { get; set; } = color;
    }

    internal sealed class StatusGroupModel(string name, string color, List<OptionRowModel> options)
    {
        public string               Name    { get; }      = name;
        public string               Color   { get; }      = color;
        public List<OptionRowModel> Options { get; }      = options;
    }
}
