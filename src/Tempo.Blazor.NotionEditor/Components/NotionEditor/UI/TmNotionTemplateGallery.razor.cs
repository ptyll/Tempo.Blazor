using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionTemplateGallery : ComponentBase
{
    private const string AllCategory = "__all";
    private const string BlankTemplateId = "blank";
    private static readonly IReadOnlyList<string> BaseCategories = ["blank", "team", "planning", "knowledge"];
    private IReadOnlyList<NotionTemplateDto> _templates = [];
    private string _selectedCategory = AllCategory;
    private string _query = string.Empty;
    private bool _loading;
    private string? _applyingId;
    private string? _error;
    private bool _loadedForVisible;

    /// <summary>Controls whether the template gallery dialog is rendered.</summary>
    [Parameter] public bool Visible { get; set; }

    /// <summary>Optional provider that supplies workspace page templates.</summary>
    [Parameter] public INotionTemplateProvider? TemplateProvider { get; set; }

    /// <summary>Raised when a template is selected.</summary>
    [Parameter] public EventCallback<NotionTemplateDto> OnTemplateSelected { get; set; }

    /// <summary>Raised when the dialog should close without selecting a template.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    /// <summary>Forced layout. Mobile presents the gallery fullscreen.</summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    private IReadOnlyList<string> Categories
    {
        get
        {
            var categories = _templates
                .Select(template => NormalizeCategory(template.Category))
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Concat(BaseCategories)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => string.Equals(category, "blank", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(GetCategoryLabel)
                .ToList();

            return [AllCategory, .. categories];
        }
    }

    private IReadOnlyList<NotionTemplateDto> FilteredTemplates
    {
        get
        {
            var query = _query.Trim();
            return _templates
                .Where(template => string.Equals(_selectedCategory, AllCategory, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(NormalizeCategory(template.Category), _selectedCategory, StringComparison.OrdinalIgnoreCase))
                .Where(template => string.IsNullOrWhiteSpace(query) ||
                                   Contains(template.Name, query) ||
                                   Contains(template.Description, query) ||
                                   Contains(GetCategoryLabel(template.Category), query))
                .ToList();
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!Visible)
        {
            _loadedForVisible = false;
            return;
        }

        if (_loadedForVisible) return;
        _loadedForVisible = true;
        await LoadTemplatesAsync();
    }

    private async Task LoadTemplatesAsync()
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            IReadOnlyList<NotionTemplateDto> templates = TemplateProvider is null
                ? []
                : await TemplateProvider.GetTemplatesAsync();

            _templates = NormalizeTemplates(templates);
            _selectedCategory = AllCategory;
            _query = string.Empty;
        }
        catch
        {
            _templates = [CreateBlankTemplate()];
            _error = Loc["Notion_Templates_LoadError"];
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private static IReadOnlyList<NotionTemplateDto> NormalizeTemplates(IReadOnlyList<NotionTemplateDto> templates)
    {
        var normalized = templates
            .Where(template => !string.IsNullOrWhiteSpace(template.Id))
            .GroupBy(template => template.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (normalized.All(template => !string.Equals(template.Id, BlankTemplateId, StringComparison.OrdinalIgnoreCase)))
        {
            normalized.Insert(0, CreateBlankTemplate());
        }

        return normalized;
    }

    private static NotionTemplateDto CreateBlankTemplate() => new()
    {
        Id = BlankTemplateId,
        Name = BlankTemplateId,
        Description = BlankTemplateId,
        IconEmoji = string.Empty,
        Category = "blank",
        Blocks = []
    };

    private void OnSearchInput(ChangeEventArgs args) => _query = args.Value?.ToString() ?? string.Empty;

    private void SelectCategory(string category) => _selectedCategory = category;

    private async Task SelectTemplateAsync(NotionTemplateDto template)
    {
        if (_applyingId is not null) return;
        _applyingId = template.Id;
        StateHasChanged();

        try
        {
            await OnTemplateSelected.InvokeAsync(template);
        }
        finally
        {
            _applyingId = null;
        }
    }

    private Task CloseAsync() => OnClosed.InvokeAsync();

    private string GetTemplateName(NotionTemplateDto template) =>
        string.Equals(template.Id, BlankTemplateId, StringComparison.OrdinalIgnoreCase)
            ? Loc["Notion_Templates_Blank"]
            : template.Name;

    private string GetTemplateDescription(NotionTemplateDto template) =>
        string.Equals(template.Id, BlankTemplateId, StringComparison.OrdinalIgnoreCase)
            ? Loc["Notion_Templates_BlankDescription"]
            : template.Description;

    private static string GetTemplateIcon(NotionTemplateDto template) =>
        string.IsNullOrWhiteSpace(template.IconEmoji) ? "+" : template.IconEmoji;

    private string GetCategoryLabel(string category)
    {
        var normalized = NormalizeCategory(category);
        if (string.Equals(normalized, AllCategory, StringComparison.OrdinalIgnoreCase))
            return Loc["Notion_Templates_Category_All"];

        var key = normalized.ToLowerInvariant() switch
        {
            "blank" => "Notion_Templates_Category_Blank",
            "team" => "Notion_Templates_Category_Team",
            "planning" => "Notion_Templates_Category_Planning",
            "knowledge" => "Notion_Templates_Category_Knowledge",
            _ => string.Empty
        };

        return string.IsNullOrEmpty(key) ? category : Loc[key];
    }

    private static string NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? "blank" : category.Trim();

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static IEnumerable<string> GetPreviewLines(NotionTemplateDto template)
    {
        var count = Math.Clamp(template.Blocks.Count, 1, 5);
        for (var i = 0; i < count; i++)
        {
            yield return i switch
            {
                0 => "tm-ntg__line--wide",
                1 => "tm-ntg__line--medium",
                2 => "tm-ntg__line--short",
                3 => "tm-ntg__line--medium",
                _ => "tm-ntg__line--short"
            };
        }
    }
}
