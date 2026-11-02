using System.Globalization;
using System.Text;
using System.Text.Json;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Demo.Api.Data;

public class MockNotionDatabaseStore : INotionDatabaseProvider
{
    // ── Stable IDs ────────────────────────────────────────────────────────────

    public static readonly Guid DbId = Guid.Parse("db000000-0000-0000-0000-000000000001");

    private static readonly Guid _fName     = Guid.Parse("f0000000-0000-0000-0000-000000000001");
    private static readonly Guid _fStatus   = Guid.Parse("f0000000-0000-0000-0000-000000000002");
    private static readonly Guid _fPriority = Guid.Parse("f0000000-0000-0000-0000-000000000003");
    private static readonly Guid _fDueDate  = Guid.Parse("f0000000-0000-0000-0000-000000000004");
    private static readonly Guid _fTags     = Guid.Parse("f0000000-0000-0000-0000-000000000005");
    private static readonly Guid _fProgress = Guid.Parse("f0000000-0000-0000-0000-000000000006");
    private static readonly Guid _fDone     = Guid.Parse("f0000000-0000-0000-0000-000000000007");
    private static readonly Guid _fAssignee = Guid.Parse("f0000000-0000-0000-0000-000000000008");

    private static readonly Guid _vTable    = Guid.Parse("e0000000-0000-0000-0000-000000000001");
    private static readonly Guid _vBoard    = Guid.Parse("e0000000-0000-0000-0000-000000000002");
    private static readonly Guid _vList     = Guid.Parse("e0000000-0000-0000-0000-000000000003");
    private static readonly Guid _vGallery  = Guid.Parse("e0000000-0000-0000-0000-000000000004");
    private static readonly Guid _vCalendar = Guid.Parse("e0000000-0000-0000-0000-000000000005");
    private static readonly Guid _vTimeline = Guid.Parse("e0000000-0000-0000-0000-000000000006");

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<Guid, List<DatabaseField>>          _fieldsByDb    = new();
    private readonly Dictionary<Guid, List<DatabaseView>>           _viewsByDb     = new();
    private readonly Dictionary<Guid, DatabaseRecord>               _records       = new();
    private readonly Dictionary<Guid, List<DatabaseRecordTemplate>> _templatesByDb = new();

    public MockNotionDatabaseStore()
    {
        Reset();
    }

    public void SeedE2E(string seed)
    {
        Reset();
        if (string.Equals(seed, "empty", StringComparison.OrdinalIgnoreCase))
        {
            _records.Clear();
        }
        else if (string.Equals(seed, "one", StringComparison.OrdinalIgnoreCase))
        {
            var first = _records.Values
                .OrderBy(r => GetFieldText(r, _fName), StringComparer.OrdinalIgnoreCase)
                .Take(1)
                .ToList();
            _records.Clear();
            foreach (var record in first)
            {
                _records[record.Id] = record;
            }
        }
        else if (string.Equals(seed, "calendar-overflow", StringComparison.OrdinalIgnoreCase))
        {
            // Four events on one day of the current month, so the calendar's 3-chip cap
            // renders the +N overflow popover no matter which month the test runs in.
            var day = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 15, 12, 0, 0, DateTimeKind.Utc);
            var names = new[] { "Overflow alpha", "Overflow beta", "Overflow gamma", "Overflow delta" };
            for (var i = 0; i < names.Length; i++)
            {
                var record = new DatabaseRecord
                {
                    Id = Guid.NewGuid(),
                    DatabaseId = DbId,
                    CreatedAt = day,
                    LastEditedAt = day,
                    Fields = new Dictionary<string, object?>
                    {
                        [_fName.ToString()] = names[i],
                        [_fStatus.ToString()] = "Not Started",
                        [_fPriority.ToString()] = "Low",
                        [_fDueDate.ToString()] = day,
                        [_fTags.ToString()] = Array.Empty<string>(),
                        [_fProgress.ToString()] = 0d,
                        [_fDone.ToString()] = false,
                        [_fAssignee.ToString()] = "alice",
                    },
                };
                _records[record.Id] = record;
            }
        }
    }

    private void Reset()
    {
        _fieldsByDb.Clear();
        _viewsByDb.Clear();
        _records.Clear();
        _templatesByDb.Clear();

        _fieldsByDb[DbId]    = BuildFields();
        _viewsByDb[DbId]     = BuildViews();
        _templatesByDb[DbId] = BuildTemplates();
        foreach (var r in BuildRecords())
            _records[r.Id] = r;
    }

    // ── Fields ────────────────────────────────────────────────────────────────

    public Task<IEnumerable<IDatabaseField>> GetFieldsAsync(string databaseId)
    {
        var id     = Guid.Parse(databaseId);
        var fields = _fieldsByDb.TryGetValue(id, out var f)
            ? f.Cast<IDatabaseField>()
            : Enumerable.Empty<IDatabaseField>();
        return Task.FromResult(fields);
    }

    public Task<IDatabaseField> CreateFieldAsync(string databaseId, IDatabaseField field)
    {
        var id = Guid.Parse(databaseId);
        var nf = new DatabaseField
        {
            Id        = Guid.NewGuid(),
            Name      = field.Name,
            Type      = field.Type,
            Config    = field.Config,
            IsVisible = field.IsVisible,
            Width     = field.Width
        };
        if (!_fieldsByDb.ContainsKey(id)) _fieldsByDb[id] = new();
        _fieldsByDb[id].Add(nf);
        return Task.FromResult<IDatabaseField>(nf);
    }

    public Task<IDatabaseField> UpdateFieldAsync(string databaseId, IDatabaseField field)
    {
        var id   = Guid.Parse(databaseId);
        var list = _fieldsByDb.GetValueOrDefault(id) ?? new List<DatabaseField>();
        var idx  = list.FindIndex(f => f.Id == field.Id);
        if (idx < 0) throw new KeyNotFoundException($"Field {field.Id} not found");
        var uf = new DatabaseField
        {
            Id        = field.Id,
            Name      = field.Name,
            Type      = field.Type,
            IsPrimary = field.IsPrimary,
            Config    = field.Config,
            IsVisible = field.IsVisible,
            Width     = field.Width
        };
        list[idx] = uf;
        return Task.FromResult<IDatabaseField>(uf);
    }

    public Task DeleteFieldAsync(string databaseId, string fieldId)
    {
        var id  = Guid.Parse(databaseId);
        var fid = Guid.Parse(fieldId);
        _fieldsByDb.GetValueOrDefault(id)?.RemoveAll(f => f.Id == fid);
        return Task.CompletedTask;
    }

    public Task ReorderFieldsAsync(string databaseId, IEnumerable<string> orderedFieldIds)
    {
        var id    = Guid.Parse(databaseId);
        var list  = _fieldsByDb.GetValueOrDefault(id);
        if (list is null) return Task.CompletedTask;
        var order = orderedFieldIds.ToList();
        list.Sort((a, b) => order.IndexOf(a.Id.ToString()).CompareTo(order.IndexOf(b.Id.ToString())));
        return Task.CompletedTask;
    }

    // ── Views ─────────────────────────────────────────────────────────────────

    public Task<IEnumerable<IDatabaseView>> GetViewsAsync(string databaseId)
    {
        var id    = Guid.Parse(databaseId);
        var views = _viewsByDb.TryGetValue(id, out var v)
            ? v.Cast<IDatabaseView>()
            : Enumerable.Empty<IDatabaseView>();
        return Task.FromResult(views);
    }

    public Task<IDatabaseView> CreateViewAsync(string databaseId, IDatabaseView view)
    {
        var id = Guid.Parse(databaseId);
        var nv = new DatabaseView
        {
            Id              = Guid.NewGuid(),
            Name            = view.Name,
            Type            = view.Type,
            Filter          = view.Filter,
            Sorts           = view.Sorts,
            Grouping        = view.Grouping,
            VisibleFieldIds = view.VisibleFieldIds,
            Config          = view.Config
        };
        if (!_viewsByDb.ContainsKey(id)) _viewsByDb[id] = new();
        _viewsByDb[id].Add(nv);
        return Task.FromResult<IDatabaseView>(nv);
    }

    public Task<IDatabaseView> UpdateViewAsync(string databaseId, IDatabaseView view)
    {
        var id   = Guid.Parse(databaseId);
        var list = _viewsByDb.GetValueOrDefault(id) ?? new List<DatabaseView>();
        var idx  = list.FindIndex(v => v.Id == view.Id);
        if (idx < 0) throw new KeyNotFoundException($"View {view.Id} not found");
        var uv = new DatabaseView
        {
            Id              = view.Id,
            Name            = view.Name,
            Type            = view.Type,
            Filter          = view.Filter,
            Sorts           = view.Sorts,
            Grouping        = view.Grouping,
            VisibleFieldIds = view.VisibleFieldIds,
            Config          = view.Config
        };
        list[idx] = uv;
        return Task.FromResult<IDatabaseView>(uv);
    }

    public Task DeleteViewAsync(string databaseId, string viewId)
    {
        var id  = Guid.Parse(databaseId);
        var vid = Guid.Parse(viewId);
        _viewsByDb.GetValueOrDefault(id)?.RemoveAll(v => v.Id == vid);
        return Task.CompletedTask;
    }

    public Task<IDatabaseView> DuplicateViewAsync(string databaseId, string viewId)
    {
        var id   = Guid.Parse(databaseId);
        var vid  = Guid.Parse(viewId);
        var list = _viewsByDb.GetValueOrDefault(id) ?? new List<DatabaseView>();
        var src  = list.FirstOrDefault(v => v.Id == vid)
            ?? throw new KeyNotFoundException($"View {viewId} not found");
        var dup = new DatabaseView
        {
            Id              = Guid.NewGuid(),
            Name            = $"{src.Name} (copy)",
            Type            = src.Type,
            Filter          = src.Filter,
            Sorts           = src.Sorts,
            Grouping        = src.Grouping,
            VisibleFieldIds = src.VisibleFieldIds,
            Config          = src.Config
        };
        list.Add(dup);
        return Task.FromResult<IDatabaseView>(dup);
    }

    // ── Records ───────────────────────────────────────────────────────────────

    public Task<PagedResult<IDatabaseRecord>> GetRecordsAsync(
        string databaseId, INotionDatabaseFilter? filter,
        IEnumerable<NotionDatabaseSort>? sorts, NotionDatabaseGrouping? grouping,
        int page, int pageSize)
    {
        var dbId = Guid.Parse(databaseId);
        var all = _records.Values
            .Where(r => r.DatabaseId == dbId)
            .Where(r => MatchesFilter(r, filter))
            .ToList();

        all = ApplySorts(all, sorts).ToList();

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var total = all.Count;
        var items = all.Skip((safePage - 1) * safePageSize).Take(safePageSize).Cast<IDatabaseRecord>().ToList();
        return Task.FromResult(new PagedResult<IDatabaseRecord>
        {
            Items      = items,
            TotalCount = total,
            Page       = safePage,
            PageSize   = safePageSize
        });
    }

    private static IEnumerable<DatabaseRecord> ApplySorts(IEnumerable<DatabaseRecord> records, IEnumerable<NotionDatabaseSort>? sorts)
    {
        IOrderedEnumerable<DatabaseRecord>? ordered = null;

        foreach (var sort in sorts ?? [])
        {
            Func<DatabaseRecord, string> keySelector = record => GetFieldText(record, sort.FieldId);
            ordered = ordered is null
                ? sort.Direction == SortDirection.Descending
                    ? records.OrderByDescending(keySelector, StringComparer.OrdinalIgnoreCase)
                    : records.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase)
                : sort.Direction == SortDirection.Descending
                    ? ordered.ThenByDescending(keySelector, StringComparer.OrdinalIgnoreCase)
                    : ordered.ThenBy(keySelector, StringComparer.OrdinalIgnoreCase);
        }

        return ordered ?? records.OrderBy(record => GetFieldText(record, _fName), StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesFilter(DatabaseRecord record, INotionDatabaseFilter? filter)
    {
        if (filter is null)
        {
            return true;
        }

        var results = filter.Conditions
            .Select(condition => MatchesCondition(record, condition))
            .Concat(filter.NestedFilters.Select(nested => MatchesFilter(record, nested)))
            .ToList();

        if (results.Count == 0)
        {
            return true;
        }

        return filter.Logic == FilterLogic.Or
            ? results.Any(result => result)
            : results.All(result => result);
    }

    private static bool MatchesCondition(DatabaseRecord record, NotionFilterCondition condition)
    {
        record.Fields.TryGetValue(condition.FieldId.ToString(), out var rawValue);
        var value = NormalizeValue(rawValue);
        var expected = NormalizeValue(condition.Value);
        var valueText = ValueToText(value);
        var expectedText = ValueToText(expected);

        return condition.Operator switch
        {
            NotionFilterOperator.Equals => string.Equals(valueText, expectedText, StringComparison.OrdinalIgnoreCase),
            NotionFilterOperator.NotEquals => !string.Equals(valueText, expectedText, StringComparison.OrdinalIgnoreCase),
            NotionFilterOperator.Contains => valueText.Contains(expectedText, StringComparison.OrdinalIgnoreCase),
            NotionFilterOperator.NotContains => !valueText.Contains(expectedText, StringComparison.OrdinalIgnoreCase),
            NotionFilterOperator.StartsWith => valueText.StartsWith(expectedText, StringComparison.OrdinalIgnoreCase),
            NotionFilterOperator.EndsWith => valueText.EndsWith(expectedText, StringComparison.OrdinalIgnoreCase),
            NotionFilterOperator.IsEmpty => string.IsNullOrWhiteSpace(valueText),
            NotionFilterOperator.IsNotEmpty => !string.IsNullOrWhiteSpace(valueText),
            NotionFilterOperator.GreaterThan => TryCompare(value, expected, out var gt) && gt > 0,
            NotionFilterOperator.GreaterThanOrEqual => TryCompare(value, expected, out var gte) && gte >= 0,
            NotionFilterOperator.LessThan => TryCompare(value, expected, out var lt) && lt < 0,
            NotionFilterOperator.LessThanOrEqual => TryCompare(value, expected, out var lte) && lte <= 0,
            NotionFilterOperator.Before => TryCompareDate(value, expected, out var before) && before < 0,
            NotionFilterOperator.After => TryCompareDate(value, expected, out var after) && after > 0,
            NotionFilterOperator.OnOrBefore => TryCompareDate(value, expected, out var onOrBefore) && onOrBefore <= 0,
            NotionFilterOperator.OnOrAfter => TryCompareDate(value, expected, out var onOrAfter) && onOrAfter >= 0,
            NotionFilterOperator.IsChecked => value is bool checkedValue && checkedValue,
            NotionFilterOperator.IsUnchecked => value is bool uncheckedValue && !uncheckedValue,
            NotionFilterOperator.ThisWeek => IsDateInRange(value, DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek), 7),
            NotionFilterOperator.PastWeek => IsDateInRange(value, DateTime.UtcNow.Date.AddDays(-7), 7),
            NotionFilterOperator.PastMonth => IsDateInRange(value, DateTime.UtcNow.Date.AddMonths(-1), 31),
            NotionFilterOperator.NextWeek => IsDateInRange(value, DateTime.UtcNow.Date, 7),
            NotionFilterOperator.NextMonth => IsDateInRange(value, DateTime.UtcNow.Date, 31),
            _ => true
        };
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is JsonElement json)
        {
            return json.ValueKind switch
            {
                JsonValueKind.String => json.GetString(),
                JsonValueKind.Number => json.TryGetDouble(out var number) ? number : json.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => json.EnumerateArray().Select(item => NormalizeValue(item)).ToArray(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => json.GetRawText()
            };
        }

        return value;
    }

    private static string ValueToText(object? value)
    {
        value = NormalizeValue(value);
        return value switch
        {
            null => string.Empty,
            string text => text,
            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            string[] values => string.Join(" ", values),
            IEnumerable<object?> values => string.Join(" ", values.Select(ValueToText)),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static bool TryCompare(object? value, object? expected, out int comparison)
    {
        if (TryGetDouble(value, out var left) && TryGetDouble(expected, out var right))
        {
            comparison = left.CompareTo(right);
            return true;
        }

        comparison = string.Compare(ValueToText(value), ValueToText(expected), StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static bool TryCompareDate(object? value, object? expected, out int comparison)
    {
        comparison = 0;
        if (!TryGetDate(value, out var left) || !TryGetDate(expected, out var right))
        {
            return false;
        }

        comparison = left.Date.CompareTo(right.Date);
        return true;
    }

    private static bool TryGetDouble(object? value, out double number)
    {
        value = NormalizeValue(value);
        return value switch
        {
            double d => (number = d) == d,
            float f => (number = f) == f,
            decimal m => (number = (double)m) == (double)m,
            int i => (number = i) == i,
            long l => (number = l) == l,
            string text => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out number),
            _ => double.TryParse(ValueToText(value), NumberStyles.Any, CultureInfo.InvariantCulture, out number)
        };
    }

    private static bool TryGetDate(object? value, out DateTime date)
    {
        value = NormalizeValue(value);
        return value switch
        {
            DateTime dt => (date = dt) == dt,
            DateTimeOffset dto => (date = dto.UtcDateTime) == dto.UtcDateTime,
            string text => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date),
            _ => DateTime.TryParse(ValueToText(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date)
        };
    }

    private static bool IsDateInRange(object? value, DateTime start, int days)
    {
        if (!TryGetDate(value, out var date))
        {
            return false;
        }

        var end = start.AddDays(days);
        return date.Date >= start.Date && date.Date < end.Date;
    }

    private static string GetFieldText(DatabaseRecord record, Guid fieldId)
    {
        record.Fields.TryGetValue(fieldId.ToString(), out var value);
        return ValueToText(value);
    }

    public Task<IDatabaseRecord> GetRecordAsync(string databaseId, string recordId)
    {
        var rid = Guid.Parse(recordId);
        if (_records.TryGetValue(rid, out var r))
            return Task.FromResult<IDatabaseRecord>(r);
        throw new KeyNotFoundException($"Record {recordId} not found");
    }

    public Task<IDatabaseRecord> CreateRecordAsync(string databaseId, IDatabaseRecord record)
    {
        var dbId = Guid.Parse(databaseId);
        var nr   = new DatabaseRecord
        {
            Id             = Guid.NewGuid(),
            DatabaseId     = dbId,
            ParentRecordId = record.ParentRecordId,
            Fields         = record.Fields,
            CreatedAt      = DateTime.UtcNow,
            LastEditedAt   = DateTime.UtcNow
        };
        _records[nr.Id] = nr;
        return Task.FromResult<IDatabaseRecord>(nr);
    }

    public Task<IDatabaseRecord> UpdateRecordAsync(string databaseId, IDatabaseRecord record)
    {
        if (_records.TryGetValue(record.Id, out var existing))
        {
            existing.Fields       = record.Fields;
            existing.LastEditedAt = DateTime.UtcNow;
            return Task.FromResult<IDatabaseRecord>(existing);
        }
        throw new KeyNotFoundException($"Record {record.Id} not found");
    }

    public Task DeleteRecordAsync(string databaseId, string recordId)
    {
        var rid = Guid.Parse(recordId);
        _records.Remove(rid);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<IDatabaseRecord>> BatchUpdateRecordsAsync(string databaseId, IEnumerable<IDatabaseRecord> records)
    {
        var updated = new List<IDatabaseRecord>();
        foreach (var rec in records)
        {
            if (_records.TryGetValue(rec.Id, out var existing))
            {
                existing.Fields       = rec.Fields;
                existing.LastEditedAt = DateTime.UtcNow;
                updated.Add(existing);
            }
        }
        return Task.FromResult(updated.AsEnumerable());
    }

    public Task<IEnumerable<IDatabaseRecord>> GetSubItemsAsync(string parentRecordId)
    {
        var pid   = Guid.Parse(parentRecordId);
        var items = _records.Values.Where(r => r.ParentRecordId == pid).Cast<IDatabaseRecord>();
        return Task.FromResult(items);
    }

    public Task MoveRecordAsync(string recordId, string? newParentRecordId)
    {
        var rid = Guid.Parse(recordId);
        if (_records.TryGetValue(rid, out var r))
            r.ParentRecordId = newParentRecordId is null ? null : Guid.Parse(newParentRecordId);
        return Task.CompletedTask;
    }

    // ── Templates ─────────────────────────────────────────────────────────────

    public Task<IEnumerable<IDatabaseRecordTemplate>> GetTemplatesAsync(string databaseId)
    {
        var id        = Guid.Parse(databaseId);
        var templates = _templatesByDb.TryGetValue(id, out var t)
            ? t.Cast<IDatabaseRecordTemplate>()
            : Enumerable.Empty<IDatabaseRecordTemplate>();
        return Task.FromResult(templates);
    }

    public Task<IDatabaseRecordTemplate> CreateTemplateAsync(string databaseId, IDatabaseRecordTemplate template)
    {
        var id = Guid.Parse(databaseId);
        var nt = new DatabaseRecordTemplate
        {
            Id            = Guid.NewGuid(),
            DatabaseId    = id,
            Name          = template.Name,
            IconEmoji     = template.IconEmoji,
            DefaultFields = template.DefaultFields,
            TemplateBlocks = template.TemplateBlocks
        };
        if (!_templatesByDb.ContainsKey(id)) _templatesByDb[id] = new();
        _templatesByDb[id].Add(nt);
        return Task.FromResult<IDatabaseRecordTemplate>(nt);
    }

    public Task<IDatabaseRecordTemplate> UpdateTemplateAsync(string databaseId, IDatabaseRecordTemplate template)
    {
        var id   = Guid.Parse(databaseId);
        var list = _templatesByDb.GetValueOrDefault(id) ?? new List<DatabaseRecordTemplate>();
        var idx  = list.FindIndex(t => t.Id == template.Id);
        if (idx < 0) throw new KeyNotFoundException($"Template {template.Id} not found");
        var ut = new DatabaseRecordTemplate
        {
            Id             = template.Id,
            DatabaseId     = id,
            Name           = template.Name,
            IconEmoji      = template.IconEmoji,
            DefaultFields  = template.DefaultFields,
            TemplateBlocks = template.TemplateBlocks
        };
        list[idx] = ut;
        return Task.FromResult<IDatabaseRecordTemplate>(ut);
    }

    public Task DeleteTemplateAsync(string databaseId, string templateId)
    {
        var id  = Guid.Parse(databaseId);
        var tid = Guid.Parse(templateId);
        _templatesByDb.GetValueOrDefault(id)?.RemoveAll(t => t.Id == tid);
        return Task.CompletedTask;
    }

    public Task<IDatabaseRecord> CreateRecordFromTemplateAsync(string databaseId, string templateId)
    {
        var id   = Guid.Parse(databaseId);
        var tid  = Guid.Parse(templateId);
        var list = _templatesByDb.GetValueOrDefault(id) ?? new List<DatabaseRecordTemplate>();
        var tmpl = list.FirstOrDefault(t => t.Id == tid)
            ?? throw new KeyNotFoundException($"Template {templateId} not found");
        var nr = new DatabaseRecord
        {
            Id           = Guid.NewGuid(),
            DatabaseId   = id,
            Fields       = tmpl.DefaultFields,
            CreatedAt    = DateTime.UtcNow,
            LastEditedAt = DateTime.UtcNow
        };
        _records[nr.Id] = nr;
        return Task.FromResult<IDatabaseRecord>(nr);
    }

    // ── Import / Export ───────────────────────────────────────────────────────

    public Task ImportCsvAsync(string databaseId, Stream csv) => Task.CompletedTask;

    public Task<Stream> ExportCsvAsync(string databaseId, string? viewId)
    {
        var id     = Guid.Parse(databaseId);
        var fields = _fieldsByDb.GetValueOrDefault(id) ?? new List<DatabaseField>();
        var sb     = new StringBuilder();
        sb.AppendLine(string.Join(",", fields.Select(f => $"\"{f.Name}\"")));
        foreach (var rec in _records.Values.Where(r => r.DatabaseId == id))
        {
            var cells = fields.Select(f =>
            {
                if (!rec.Fields.TryGetValue(f.Id.ToString(), out var v)) return "\"\"";
                return $"\"{v?.ToString()?.Replace("\"", "\\\"") ?? ""}\"";
            });
            sb.AppendLine(string.Join(",", cells));
        }
        Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        return Task.FromResult(stream);
    }

    // ── Builders ──────────────────────────────────────────────────────────────

    private static List<DatabaseField> BuildFields() =>
    [
        new DatabaseField { Id = _fName,     Name = "Name",     Type = DatabaseFieldType.Text,        IsPrimary = true,  IsVisible = true, Width = 300 },
        new DatabaseField
        {
            Id = _fStatus, Name = "Status", Type = DatabaseFieldType.Status, IsVisible = true, Width = 140,
            Config = new StatusFieldConfig
            {
                Groups =
                [
                    new("Not Started", "#9ca3af",
                    [
                        new("status-todo",    "Not Started", "#9ca3af"),
                        new("status-backlog", "Backlog",     "#d1d5db")
                    ]),
                    new("In Progress", "#3b82f6",
                    [
                        new("status-progress", "In Progress", "#3b82f6"),
                        new("status-review",   "In Review",   "#8b5cf6")
                    ]),
                    new("Done", "#10b981",
                    [
                        new("status-done",    "Done",      "#10b981"),
                        new("status-wontfix", "Won't Fix", "#ef4444")
                    ])
                ]
            }
        },
        new DatabaseField
        {
            Id = _fPriority, Name = "Priority", Type = DatabaseFieldType.Select, IsVisible = true, Width = 120,
            Config = new SelectFieldConfig
            {
                Options =
                [
                    new("priority-low",      "Low",      "#10b981"),
                    new("priority-medium",   "Medium",   "#f59e0b"),
                    new("priority-high",     "High",     "#ef4444"),
                    new("priority-critical", "Critical", "#7c3aed")
                ]
            }
        },
        new DatabaseField { Id = _fDueDate,  Name = "Due Date", Type = DatabaseFieldType.Date,        IsVisible = true, Width = 140 },
        new DatabaseField
        {
            Id = _fTags, Name = "Tags", Type = DatabaseFieldType.MultiSelect, IsVisible = true, Width = 200,
            Config = new SelectFieldConfig
            {
                Options =
                [
                    new("tag-frontend", "Frontend", "#3b82f6"),
                    new("tag-backend",  "Backend",  "#8b5cf6"),
                    new("tag-api",      "API",      "#f59e0b"),
                    new("tag-design",   "Design",   "#ec4899"),
                    new("tag-testing",  "Testing",  "#10b981"),
                    new("tag-docs",     "Docs",     "#6b7280")
                ]
            }
        },
        new DatabaseField { Id = _fProgress, Name = "Progress", Type = DatabaseFieldType.Number,   IsVisible = true, Width = 100 },
        new DatabaseField { Id = _fDone,     Name = "Done",     Type = DatabaseFieldType.Checkbox, IsVisible = true, Width = 80  },
        new DatabaseField { Id = _fAssignee, Name = "Assignee", Type = DatabaseFieldType.Person,   IsVisible = true, Width = 160 }
    ];

    private static List<DatabaseView> BuildViews() =>
    [
        new DatabaseView
        {
            Id              = _vTable,
            Name            = "All Tasks",
            Type            = DatabaseViewType.Table,
            VisibleFieldIds = [_fName, _fStatus, _fPriority, _fDueDate, _fTags, _fProgress, _fDone, _fAssignee]
        },
        new DatabaseView
        {
            Id              = _vBoard,
            Name            = "Board",
            Type            = DatabaseViewType.Board,
            Grouping        = new NotionDatabaseGrouping(_fStatus, false, SortDirection.Ascending),
            VisibleFieldIds = [_fName, _fStatus, _fPriority, _fDueDate, _fAssignee]
        },
        new DatabaseView
        {
            Id              = _vList,
            Name            = "List",
            Type            = DatabaseViewType.List,
            VisibleFieldIds = [_fName, _fStatus, _fPriority, _fDone]
        },
        new DatabaseView
        {
            Id              = _vGallery,
            Name            = "Gallery",
            Type            = DatabaseViewType.Gallery,
            Config          = new GalleryViewConfig { CardSize = GalleryCardSize.Medium },
            VisibleFieldIds = [_fName, _fStatus, _fPriority, _fTags, _fProgress]
        },
        new DatabaseView
        {
            Id              = _vCalendar,
            Name            = "Calendar",
            Type            = DatabaseViewType.Calendar,
            Config          = new CalendarViewConfig { DateFieldId = _fDueDate },
            VisibleFieldIds = [_fName, _fStatus, _fPriority]
        },
        new DatabaseView
        {
            Id              = _vTimeline,
            Name            = "Timeline",
            Type            = DatabaseViewType.Timeline,
            Config          = new TimelineViewConfig { StartDateFieldId = _fDueDate, ShowTableArea = true },
            VisibleFieldIds = [_fName, _fStatus, _fPriority, _fAssignee]
        }
    ];

    private static List<DatabaseRecord> BuildRecords()
    {
        var now = new DateTime(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc);

        DatabaseRecord Make(string name, string status, string priority,
            DateTime? due, string[] tags, int progress, bool done, string assignee)
            => new()
            {
                Id           = Guid.NewGuid(),
                DatabaseId   = DbId,
                CreatedAt    = now.AddDays(-Random.Shared.Next(1, 60)),
                LastEditedAt = now.AddDays(-Random.Shared.Next(0, 10)),
                Fields       = new Dictionary<string, object?>
                {
                    [_fName.ToString()]     = name,
                    [_fStatus.ToString()]   = status,
                    [_fPriority.ToString()] = priority,
                    [_fDueDate.ToString()]  = due,
                    [_fTags.ToString()]     = tags,
                    [_fProgress.ToString()] = (double)progress,
                    [_fDone.ToString()]     = done,
                    [_fAssignee.ToString()] = assignee
                }
            };

        return
        [
            Make("Implement authentication flow",     "In Progress", "High",     now.AddDays(3),   ["tag-backend",  "tag-api"],                     75,  false, "alice"),
            Make("Design onboarding screens",         "In Review",   "Medium",   now.AddDays(1),   ["tag-design",   "tag-frontend"],                90,  false, "bob"),
            Make("Fix login page crash on mobile",    "Not Started", "Critical", now.AddDays(-1),  ["tag-frontend", "tag-testing"],                 0,   false, "charlie"),
            Make("Write API documentation",           "Done",        "Medium",   now.AddDays(-5),  ["tag-api",      "tag-docs"],                    100, true,  "alice"),
            Make("Set up CI/CD pipeline",             "Done",        "High",     now.AddDays(-10), ["tag-backend"],                                 100, true,  "dave"),
            Make("Optimize database queries",         "In Progress", "High",     now.AddDays(7),   ["tag-backend",  "tag-api"],                     40,  false, "charlie"),
            Make("Add dark mode support",             "Not Started", "Low",      now.AddDays(14),  ["tag-frontend", "tag-design"],                  0,   false, "bob"),
            Make("Implement search feature",          "Not Started", "Medium",   now.AddDays(10),  ["tag-frontend", "tag-backend"],                 0,   false, "alice"),
            Make("Security audit & penetration test", "Backlog",     "Critical", now.AddDays(21),  ["tag-testing",  "tag-backend"],                 0,   false, "dave"),
            Make("Migrate to .NET 9",                 "In Progress", "Medium",   now.AddDays(30),  ["tag-backend"],                                 20,  false, "charlie"),
            Make("Improve error messages for UX",     "Backlog",     "Low",      null,             ["tag-frontend", "tag-design", "tag-docs"],       0,   false, "bob"),
            Make("Add unit tests for payment module", "In Review",   "High",     now.AddDays(2),   ["tag-testing",  "tag-backend"],                 80,  false, "dave")
        ];
    }

    private static List<DatabaseRecordTemplate> BuildTemplates() =>
    [
        new DatabaseRecordTemplate
        {
            Id            = Guid.NewGuid(),
            DatabaseId    = DbId,
            Name          = "Bug Report",
            IconEmoji     = "🐛",
            DefaultFields = new Dictionary<string, object?>
            {
                [_fStatus.ToString()]   = "Not Started",
                [_fPriority.ToString()] = "High",
                [_fTags.ToString()]     = new[] { "tag-testing" },
                [_fProgress.ToString()] = 0.0,
                [_fDone.ToString()]     = false
            }
        }
    ];
}
