using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Page;

public partial class TmNotionPublicPage : TmComponentBase
{
    /// <summary>Public share token from the route.</summary>
    [Parameter] public string? Token { get; set; }

    /// <summary>Provider that resolves public share tokens.</summary>
    [Parameter, EditorRequired] public INotionPublicShareProvider PublicShareProvider { get; set; } = default!;

    /// <summary>Provider used to load the shared Notion page.</summary>
    [Parameter, EditorRequired] public INotionDataProvider DataProvider { get; set; } = default!;

    /// <summary>Aggregate provider used to load the shared page and all of its blocks.</summary>
    [Parameter, EditorRequired] public INotionAggregateProvider AggregateProvider { get; set; } = default!;

    /// <summary>Optional comments provider enabled only when the public share allows comments.</summary>
    [Parameter] public ITmCommentProvider? CommentProvider { get; set; }

    /// <summary>Additional CSS class on the public page shell.</summary>
    [Parameter] public string? Class { get; set; }

    private bool _loading;
    private string? _loadedToken;
    private PublicShareDto? _share;
    private string? _pageTitle;
    private string? _pageIconEmoji;
    private string? _pageIconImageUrl;

    private Task OnEditorPageChanged(INotionPage page)
    {
        _pageTitle = page.Title;
        _pageIconEmoji = page.IconEmoji;
        _pageIconImageUrl = page.IconImageUrl;
        return Task.CompletedTask;
    }

    protected override async Task OnParametersSetAsync()
    {
        var token = Token?.Trim();
        if (string.Equals(token, _loadedToken, StringComparison.Ordinal))
            return;

        _loadedToken = token;
        _share = null;

        if (string.IsNullOrWhiteSpace(token))
            return;

        _loading = true;
        try
        {
            _share = await PublicShareProvider.ResolveByTokenAsync(token);
        }
        finally
        {
            _loading = false;
        }
    }
}
