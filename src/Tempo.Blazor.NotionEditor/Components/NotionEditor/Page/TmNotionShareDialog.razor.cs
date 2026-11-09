using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Page;

public partial class TmNotionShareDialog : TmComponentBase
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    /// <summary>Controls whether the dialog is visible.</summary>
    [Parameter] public bool Visible { get; set; }

    /// <summary>Raised when dialog visibility changes.</summary>
    [Parameter] public EventCallback<bool> VisibleChanged { get; set; }

    /// <summary>Page identifier whose public share is managed.</summary>
    [Parameter, EditorRequired] public Guid PageId { get; set; }

    /// <summary>Provider used for creating, revoking, and reading public shares.</summary>
    [Parameter] public INotionPublicShareProvider? Provider { get; set; }

    /// <summary>Forced layout. Mobile presents the dialog as the core sheet.</summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The cascaded viewport. Auto sheets when the viewport is mobile.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] public TmLayoutContext? Layout { get; set; }

    private TmLayoutMode ResolvedLayout =>
        LayoutMode != TmLayoutMode.Auto ? LayoutMode : Layout?.Resolved ?? TmLayoutMode.Auto;

    /// <summary>
    /// Asks before revoking an active link. Defaults to true; a host that wants the old
    /// one-click revoke passes false.
    /// </summary>
    [Parameter] public bool ConfirmRevoke { get; set; } = true;

    private bool _loadedForVisible;
    private bool _loading;
    private bool _saving;
    private bool _confirmRevoke;
    private string _copied = string.Empty;
    private bool _allowComments;
    private DateOnly? _expiresDate;
    private string? _error;
    private PublicShareDto? _share;

    private bool IsShareUsable => _share is { IsEnabled: true } share && !IsExpired(share);
    private bool IsShareExpired => _share is { IsEnabled: true } share && IsExpired(share);
    private string ShareUrl => IsShareUsable ? $"{Navigation.BaseUri.TrimEnd('/')}/p/{Uri.EscapeDataString(_share!.Token)}" : string.Empty;
    private string ExpiresValue => _expiresDate?.ToString("yyyy-MM-dd") ?? string.Empty;
    private string StatusClass => IsShareUsable
        ? "tm-npsd__status--active"
        : IsShareExpired
            ? "tm-npsd__status--expired"
            : "tm-npsd__status--disabled";
    private string StatusTestId => IsShareUsable
        ? "notion-share-active"
        : IsShareExpired
            ? "notion-share-expired"
            : "notion-share-disabled";
    private string StatusText => IsShareUsable
        ? Loc["Notion_Share_Active"]
        : IsShareExpired
            ? Loc["Notion_Share_Expired"]
            : Loc["Notion_Share_Disabled"];

    protected override async Task OnParametersSetAsync()
    {
        if (!Visible)
        {
            _loadedForVisible = false;
            return;
        }

        if (!_loadedForVisible)
        {
            _loadedForVisible = true;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        if (Provider is null) return;

        _loading = true;
        _error = null;
        try
        {
            _share = await Provider.GetShareAsync(PageId);
            _allowComments = _share?.AllowComments ?? false;
            _expiresDate = _share?.ExpiresAt is null ? null : DateOnly.FromDateTime(_share.ExpiresAt.Value);
        }
        catch
        {
            _error = Loc["Notion_Share_LoadError"];
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task CreateAsync()
    {
        if (Provider is null || _saving) return;

        _saving = true;
        _error = null;
        try
        {
            DateTime? expiresAt = _expiresDate is null
                ? null
                : DateTime.SpecifyKind(_expiresDate.Value.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

            _share = await Provider.CreateShareAsync(PageId, new PublicShareOptions
            {
                AllowComments = _allowComments,
                ExpiresAt = expiresAt
            });
            _allowComments = _share.AllowComments;
            _expiresDate = _share.ExpiresAt is null ? null : DateOnly.FromDateTime(_share.ExpiresAt.Value);
        }
        catch
        {
            _error = Loc["Notion_Share_CreateError"];
        }
        finally
        {
            _saving = false;
        }
    }

    private Task AskRevoke()
    {
        if (!ConfirmRevoke)
            return RevokeAsync();
        _confirmRevoke = true;
        return Task.CompletedTask;
    }

    private async Task HandleRevokeResult(bool? confirmed)
    {
        _confirmRevoke = false;
        if (confirmed == true)
            await RevokeAsync();
    }

    private void AnnounceCopied() => _copied = Loc["Notion_Share_Copied"];

    private async Task RevokeAsync()
    {
        if (Provider is null || _saving) return;

        _saving = true;
        _error = null;
        try
        {
            await Provider.RevokeAsync(PageId);
            _share = await Provider.GetShareAsync(PageId);
        }
        catch
        {
            _error = Loc["Notion_Share_RevokeError"];
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task CloseAsync()
    {
        _error = null;
        await VisibleChanged.InvokeAsync(false);
    }

    private void HandleAllowCommentsChanged(bool value)
        => _allowComments = value;

    private void HandleExpiresChanged(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        _expiresDate = DateOnly.TryParse(value, out var date) ? date : null;
    }

    private static bool IsExpired(PublicShareDto share)
        => share.ExpiresAt is not null && share.ExpiresAt.Value.ToUniversalTime() <= DateTime.UtcNow;
}
