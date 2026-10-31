using System;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// The Insert Image dialog: one URL field and a Cancel / Apply footer. Applying raises the URL, which
/// the spreadsheet writes to the active cell's <c>ImageUrl</c>; the canvas paints it.
/// </summary>
/// <remarks>
/// The dialog contract — the overlay, <c>role="dialog"</c>, <c>aria-modal</c>, the initial focus on the
/// URL field, the document-level Escape and the focus restore to the grid — comes from the core
/// <c>TmModal</c>. The package only validates the URL and decides the words.
/// </remarks>
public partial class TmSpreadsheetInsertImageDialog
{
    private string _url = string.Empty;
    private bool _seeded;
    private string? _seededFor = string.Empty;

    /// <summary>The URL the field starts with (an existing cell image, when the host edits one).</summary>
    [Parameter] public string? ImageUrl { get; set; }

    /// <summary>Raised with the accepted image URL.</summary>
    [Parameter] public EventCallback<string> OnApply { get; set; }

    /// <summary>Raised when the dialog is dismissed.</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    /// <summary>
    /// The id of the element focus returns to on close — the host spreadsheet passes its grid root so
    /// focus lands on the grid. Null falls back to the element focused before the dialog opened.
    /// </summary>
    [Parameter] public string? RestoreFocusTargetId { get; set; }

    /// <summary>
    /// The layout the dialog renders. <see cref="TmLayoutMode.Auto"/> follows the viewport scope; a
    /// forced value pins the mode without measuring, so a test can render the mobile sheet.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    // Per instance: two spreadsheets on one page would otherwise share the id (and the label's
    // `for` would point at the first one). Same pattern as the other dialog ids.
    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];
    private string UrlInputDomId => $"spreadsheet-insert-image-url-{_idSuffix}";

    /// <summary>Only an absolute http(s) or data URL can be painted by the canvas.</summary>
    private bool IsValid => IsValidImageUrl(_url);

    /// <summary>
    /// The inline error appears only after something was typed: an empty field shows a disabled Apply
    /// instead of shouting at a user who has not started.
    /// </summary>
    private bool ShowsUrlError => _url.Length > 0 && !IsValid;

    /// <summary>
    /// An image URL the canvas can load: an absolute <c>http</c>/<c>https</c> address or a
    /// <c>data:</c> URI. Anything else (a bare path, a relative address, a javascript: URL) is refused
    /// before it reaches the <c>&lt;img&gt;</c> the canvas paints.
    /// </summary>
    internal static bool IsValidImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp
            || uri.Scheme == Uri.UriSchemeHttps
            || string.Equals(uri.Scheme, "data", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Seeds the field ONCE per starting URL. A Spreadsheet re-renders on its own (canvas sync callbacks,
    /// status aggregation) and re-sets this dialog's parameters with it; re-seeding on every parameter
    /// set would wipe what the user is typing mid-edit.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_seeded && _seededFor == ImageUrl)
        {
            return;
        }

        _seeded = true;
        _seededFor = ImageUrl;
        _url = ImageUrl ?? string.Empty;
    }

    private void OnUrlInput(ChangeEventArgs e) => _url = e.Value?.ToString() ?? string.Empty;

    private void Apply()
    {
        if (IsValid)
        {
            OnApply.InvokeAsync(_url.Trim());
        }
    }

    private void OnCancelClick() => OnCancel.InvokeAsync();

    private void OnUrlKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            Apply();
        }
    }
}
