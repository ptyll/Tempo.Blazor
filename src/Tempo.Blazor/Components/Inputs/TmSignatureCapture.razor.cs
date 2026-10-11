using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>Captures signatures by drawing, typing, or uploading an image.</summary>
public partial class TmSignatureCapture
{
    private const string ScriptSignatureFontFamily = "\"Dancing Script\", \"Brush Script MT\", \"Snell Roundhand\", \"Apple Chancery\", \"Segoe Script\", \"Lucida Handwriting\", \"Z003\", \"URW Chancery L\", cursive";
    private readonly List<Stroke> _strokes = [];
    private Stroke? _currentStroke;
    private bool _isDrawing;
    private bool _pointerLeftCanvas;
    private long? _activePointerId;
    private string? _typedText;
    private string _typedFont = "script";
    private string? _lastTypedFontParameter;
    private string? _reason;
    private bool _rememberSignature;
    private ElementReference _canvasRef;

    /// <summary>JavaScript runtime used for optional PNG export.</summary>
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    /// <summary>Captured signature value.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Callback invoked when the captured signature value changes.</summary>
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>Callback invoked when the signature value or signing metadata changes.</summary>
    [Parameter] public EventCallback<TmSignatureCaptureChangedEventArgs> Changed { get; set; }

    /// <summary>Current capture mode. Defaults to draw mode.</summary>
    [Parameter] public TmSignatureCaptureMode Mode { get; set; } = TmSignatureCaptureMode.Draw;

    /// <summary>Callback invoked when the active capture mode changes.</summary>
    [Parameter] public EventCallback<TmSignatureCaptureMode> ModeChanged { get; set; }

    /// <summary>Capture modes shown in the mode selector.</summary>
    [Parameter] public IReadOnlyList<TmSignatureCaptureMode>? Modes { get; set; }

    /// <summary>Export format used when drawing signatures. Defaults to SVG.</summary>
    [Parameter] public TmSignatureCaptureExportFormat ExportFormat { get; set; } = TmSignatureCaptureExportFormat.Svg;

    /// <summary>Whether the signature is required.</summary>
    [Parameter] public bool Required { get; set; }

    /// <summary>Whether the component is disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether typed mode should collect initials instead of a full signature.</summary>
    [Parameter] public bool Initials { get; set; }

    /// <summary>Typed signature font key. Supported values are script, serif, and sans.</summary>
    [Parameter] public string TypedFont { get; set; } = "script";

    /// <summary>Stroke color used for draw mode. Defaults to the current text color.</summary>
    [Parameter] public string StrokeColor { get; set; } = "currentColor";

    /// <summary>Stroke width used for draw mode.</summary>
    [Parameter] public double StrokeWidth { get; set; } = 2.4;

    /// <summary>Canvas width used for SVG export.</summary>
    [Parameter] public int Width { get; set; } = 520;

    /// <summary>Canvas height used for SVG export.</summary>
    [Parameter] public int Height { get; set; } = 180;

    /// <summary>Optional SVG background color.</summary>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <summary>Whether a reason input should be shown.</summary>
    [Parameter] public bool RequireReason { get; set; }

    /// <summary>Whether to show the remember signature checkbox.</summary>
    [Parameter] public bool ShowRememberSignature { get; set; }

    /// <summary>Whether to show a QR/mobile signing button.</summary>
    [Parameter] public bool ShowQrSigningButton { get; set; }

    /// <summary>Whether to show an explicit confirmation button for the current signature.</summary>
    [Parameter] public bool ShowConfirmButton { get; set; }

    /// <summary>
    /// Where the clear button renders. Defaults to <see cref="TmSignatureClearButtonPlacement.Actions"/>,
    /// the row under the surface. <see cref="TmSignatureClearButtonPlacement.Overlay"/> moves the same
    /// button into the top-end corner of the surface; it stays one button, in the tab order.
    /// </summary>
    [Parameter] public TmSignatureClearButtonPlacement ClearButtonPlacement { get; set; } = TmSignatureClearButtonPlacement.Actions;

    /// <summary>Callback invoked when the current signature is explicitly confirmed.</summary>
    [Parameter] public EventCallback<TmSignatureCaptureChangedEventArgs> Confirmed { get; set; }

    /// <summary>Previously saved signature value that can be reused.</summary>
    [Parameter] public string? PreviousValue { get; set; }

    /// <summary>Callback invoked when the upload flow should be opened.</summary>
    [Parameter] public EventCallback OnUploadRequested { get; set; }

    /// <summary>Callback invoked when QR/mobile signing is requested.</summary>
    [Parameter] public EventCallback OnQrSigningRequested { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Whether the component currently has no signature value or strokes.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value)
        && string.IsNullOrWhiteSpace(_typedText)
        && _strokes.Count == 0;

    private IReadOnlyList<TmSignatureCaptureMode> AvailableModes => Modes is { Count: > 0 }
        ? Modes
        : [TmSignatureCaptureMode.Draw, TmSignatureCaptureMode.Typed, TmSignatureCaptureMode.Upload];

    private bool IsInvalid => Required && IsEmpty;

    private string RootClass
    {
        get
        {
            var classes = new List<string> { "tm-signature-capture" };
            AddClass(classes, Disabled, "tm-signature-capture--disabled");
            AddClass(classes, IsInvalid, "tm-signature-capture--invalid");
            AddClass(classes, ClearButtonPlacement == TmSignatureClearButtonPlacement.Overlay, "tm-signature-capture--overlay-clear");
            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes.Add(Class);
            }

            return string.Join(" ", classes);
        }
    }

    private string CanvasViewBox => string.Create(CultureInfo.InvariantCulture, $"0 0 {Width} {Height}");

    private string TypedAriaLabel => Initials
        ? Loc["TmSignatureCapture_InitialsInput"]
        : Loc["TmSignatureCapture_TypedInput"];

    private string TypedPlaceholder => Initials
        ? Loc["TmSignatureCapture_InitialsPlaceholder"]
        : Loc["TmSignatureCapture_TypedPlaceholder"];

    private string TypedPreviewText => string.IsNullOrWhiteSpace(_typedText)
        ? TypedPlaceholder
        : _typedText;

    private string TypedPreviewClass => string.Join(
        " ",
        "tm-signature-capture__typed-preview",
        $"tm-signature-capture__typed-preview--{_typedFont}");

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var normalizedTypedFont = NormalizeFont(TypedFont);
        if (_lastTypedFontParameter is null
            || !string.Equals(_lastTypedFontParameter, normalizedTypedFont, StringComparison.Ordinal))
        {
            _typedFont = normalizedTypedFont;
            _lastTypedFontParameter = normalizedTypedFont;
        }
    }

    private static void AddClass(List<string> classes, bool condition, string cssClass)
    {
        if (condition)
        {
            classes.Add(cssClass);
        }
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    private string GetModeButtonClass(TmSignatureCaptureMode mode)
    {
        return mode == Mode
            ? "tm-signature-capture__tab tm-signature-capture__tab--active"
            : "tm-signature-capture__tab";
    }

    private string GetModeLabel(TmSignatureCaptureMode mode)
    {
        return mode switch
        {
            TmSignatureCaptureMode.Draw => Loc["TmSigning_Draw"],
            TmSignatureCaptureMode.Typed => Loc["TmSigning_Type"],
            TmSignatureCaptureMode.Upload => Loc["TmSigning_Upload"],
            _ => mode.ToString()
        };
    }

    private async Task SetModeAsync(TmSignatureCaptureMode mode)
    {
        if (Disabled || Mode == mode)
        {
            return;
        }

        Mode = mode;
        await ModeChanged.InvokeAsync(mode);
        if (Mode == TmSignatureCaptureMode.Typed && string.IsNullOrWhiteSpace(_typedText))
        {
            _typedText = Value?.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) == true
                ? null
                : Value;
        }

        await Task.CompletedTask;
    }

    private async Task HandlePointerDownAsync(PointerEventArgs args)
    {
        if (Disabled || Mode != TmSignatureCaptureMode.Draw)
        {
            return;
        }

        var point = await GetPointerPointAsync(args);
        _isDrawing = true;
        _pointerLeftCanvas = false;
        _activePointerId = args.PointerId;
        _currentStroke = new Stroke(StrokeColor, StrokeWidth);
        _currentStroke.AddPoint(point.X, point.Y);
        _strokes.Add(_currentStroke);

        await CapturePointerAsync(args.PointerId);
    }

    private async Task HandlePointerMoveAsync(PointerEventArgs args)
    {
        if (Disabled || !_isDrawing || _currentStroke is null)
        {
            return;
        }

        if (ShouldFinishAbandonedPointer(args))
        {
            await FinishStrokeAsync(args, includePointerPosition: false);
            return;
        }

        _pointerLeftCanvas = false;
        var point = await GetPointerPointAsync(args);
        _currentStroke.AddPoint(point.X, point.Y);
    }

    private async Task HandlePointerUpAsync(PointerEventArgs args)
    {
        if (Disabled || !_isDrawing)
        {
            return;
        }

        await FinishStrokeAsync(args, includePointerPosition: true);
    }

    private void HandlePointerLeave(PointerEventArgs args)
    {
        if (_isDrawing)
        {
            _pointerLeftCanvas = true;
        }
    }

    private async Task FinishStrokeAsync(PointerEventArgs args, bool includePointerPosition)
    {
        if (includePointerPosition)
        {
            var point = await GetPointerPointAsync(args);
            _currentStroke?.AddPoint(point.X, point.Y);
        }

        _isDrawing = false;
        _pointerLeftCanvas = false;
        var pointerId = _activePointerId;
        _activePointerId = null;
        _currentStroke = null;
        await ReleasePointerAsync(pointerId);
        await CommitValueAsync(await ExportDrawValueAsync());
    }

    private bool ShouldFinishAbandonedPointer(PointerEventArgs args)
    {
        return _pointerLeftCanvas
            && !string.Equals(args.PointerType, "touch", StringComparison.OrdinalIgnoreCase)
            && args.Buttons == 0;
    }

    private async ValueTask<PointerPoint> GetPointerPointAsync(PointerEventArgs args)
    {
        try
        {
            var point = await JSRuntime.InvokeAsync<PointerPoint?>(
                "tmSignatureCapture.getPointerPoint",
                _canvasRef,
                args.ClientX,
                args.ClientY,
                Width,
                Height);

            if (point is not null && double.IsFinite(point.X) && double.IsFinite(point.Y))
            {
                return ClampPointerPoint(point.X, point.Y);
            }
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
        }

        return ClampPointerPoint(args.OffsetX, args.OffsetY);
    }

    private PointerPoint ClampPointerPoint(double x, double y)
    {
        return new PointerPoint(
            Math.Clamp(x, 0, Width),
            Math.Clamp(y, 0, Height));
    }

    private async Task HandleTypedInputAsync(ChangeEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        _typedText = args.Value?.ToString();
        await CommitValueAsync(BuildTypedSvgString());
    }

    private async Task HandleFontChangedAsync(ChangeEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        _typedFont = NormalizeFont(args.Value?.ToString());
        if (!string.IsNullOrWhiteSpace(_typedText))
        {
            await CommitValueAsync(BuildTypedSvgString());
        }
    }

    private void HandleReasonChanged(ChangeEventArgs args)
    {
        _reason = args.Value?.ToString();
    }

    private void HandleRememberChanged(ChangeEventArgs args)
    {
        _rememberSignature = args.Value is bool boolValue && boolValue;
    }

    private async Task HandleUploadChangedAsync(InputFileChangeEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        var fileName = args.File.Name;
        var value = string.Create(CultureInfo.InvariantCulture, $"upload://{Uri.EscapeDataString(fileName)}");
        await CommitValueAsync(value);
    }

    private Task HandleUploadRequestedAsync()
    {
        return Disabled || !OnUploadRequested.HasDelegate
            ? Task.CompletedTask
            : OnUploadRequested.InvokeAsync();
    }

    private Task HandleQrSigningRequestedAsync()
    {
        return Disabled || !OnQrSigningRequested.HasDelegate
            ? Task.CompletedTask
            : OnQrSigningRequested.InvokeAsync();
    }

    private async Task ConfirmAsync()
    {
        if (Disabled || IsEmpty)
        {
            return;
        }

        var value = await BuildCurrentValueAsync();
        if (!string.Equals(Value, value, StringComparison.Ordinal))
        {
            await CommitValueAsync(value);
        }

        if (Confirmed.HasDelegate)
        {
            await Confirmed.InvokeAsync(new TmSignatureCaptureChangedEventArgs(
                value,
                Mode,
                _reason,
                _rememberSignature));
        }
    }

    /// <summary>Clears the captured signature and notifies value callbacks.</summary>
    public async Task ClearAsync()
    {
        if (Disabled)
        {
            return;
        }

        _strokes.Clear();
        _currentStroke = null;
        _isDrawing = false;
        _pointerLeftCanvas = false;
        var pointerId = _activePointerId;
        _activePointerId = null;
        _typedText = null;
        await ReleasePointerAsync(pointerId);
        await CommitValueAsync(null);
        StateHasChanged();
    }

    private async Task ReusePreviousAsync()
    {
        if (Disabled || string.IsNullOrWhiteSpace(PreviousValue))
        {
            return;
        }

        await CommitValueAsync(PreviousValue);
    }

    private async Task CommitValueAsync(string? value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(value);
        if (Changed.HasDelegate)
        {
            await Changed.InvokeAsync(new TmSignatureCaptureChangedEventArgs(
                value,
                Mode,
                _reason,
                _rememberSignature));
        }
    }

    private async Task<string?> ExportDrawValueAsync()
    {
        var svg = BuildDrawSvgString();
        if (ExportFormat == TmSignatureCaptureExportFormat.Svg)
        {
            return svg;
        }

        try
        {
            var png = await JSRuntime.InvokeAsync<string>("tmSignatureCapture.exportPng", _canvasRef);
            return string.IsNullOrWhiteSpace(png) ? svg : png;
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            return svg;
        }
    }

    private async Task<string?> BuildCurrentValueAsync()
    {
        return Mode switch
        {
            TmSignatureCaptureMode.Draw when _strokes.Count > 0 => await ExportDrawValueAsync(),
            TmSignatureCaptureMode.Typed when !string.IsNullOrWhiteSpace(_typedText) => BuildTypedSvgString(),
            _ => Value
        };
    }

    private async Task CapturePointerAsync(long pointerId)
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("tmSignatureCapture.capturePointer", _canvasRef, pointerId);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
        }
    }

    private async Task ReleasePointerAsync(long? pointerId)
    {
        if (pointerId is null)
        {
            return;
        }

        try
        {
            await JSRuntime.InvokeVoidAsync("tmSignatureCapture.releasePointer", _canvasRef, pointerId.Value);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
        }
    }

    private string BuildDrawSvgString()
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height}\" width=\"{Width}\" height=\"{Height}\">");
        if (!string.IsNullOrWhiteSpace(BackgroundColor))
        {
            builder.Append(CultureInfo.InvariantCulture, $"<rect width=\"100%\" height=\"100%\" fill=\"{BackgroundColor}\"/>");
        }

        foreach (var stroke in _strokes)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<polyline points=\"{stroke.Points}\" fill=\"none\" stroke=\"{stroke.Color}\" stroke-width=\"{stroke.Width.ToString("0.0", CultureInfo.InvariantCulture)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        }

        builder.Append("</svg>");
        return builder.ToString();
    }

    private string? BuildTypedSvgString()
    {
        if (string.IsNullOrWhiteSpace(_typedText))
        {
            return null;
        }

        var escaped = System.Net.WebUtility.HtmlEncode(_typedText);
        var normalizedFont = NormalizeFont(_typedFont);
        var fontFamily = normalizedFont switch
        {
            "serif" => "Georgia, serif",
            "sans" => "Arial, sans-serif",
            _ => ScriptSignatureFontFamily
        };
        var escapedFontFamily = System.Net.WebUtility.HtmlEncode(fontFamily);
        var fontStyle = normalizedFont == "script" ? "italic" : "normal";
        var fontWeight = normalizedFont == "script" ? "500" : "400";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height}\" width=\"{Width}\" height=\"{Height}\"><text x=\"24\" y=\"{Height / 2}\" dominant-baseline=\"middle\" font-family=\"{escapedFontFamily}\" font-style=\"{fontStyle}\" font-weight=\"{fontWeight}\" font-size=\"48\" fill=\"currentColor\">{escaped}</text></svg>");
    }

    private RenderFragment RenderPreview(string value) => builder =>
    {
        if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            builder.OpenElement(0, "img");
            builder.AddAttribute(1, "class", "tm-signature-capture__preview-image");
            builder.AddAttribute(2, "src", value);
            builder.AddAttribute(3, "alt", Loc["TmSignatureCapture_CurrentPreview"]);
            builder.CloseElement();
            return;
        }

        if (value.StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
        {
            builder.OpenElement(4, "img");
            builder.AddAttribute(5, "class", "tm-signature-capture__preview-image tm-signature-capture__preview-svg");
            builder.AddAttribute(6, "src", "data:image/svg+xml," + Uri.EscapeDataString(value));
            builder.AddAttribute(7, "alt", Loc["TmSignatureCapture_CurrentPreview"]);
            builder.CloseElement();
            return;
        }

        builder.OpenElement(7, "span");
        builder.AddAttribute(8, "class", "tm-signature-capture__preview-text");
        builder.AddContent(9, value);
        builder.CloseElement();
    };

    private static string NormalizeFont(string? font)
    {
        return font?.Trim().ToLowerInvariant() switch
        {
            "serif" => "serif",
            "sans" => "sans",
            _ => "script"
        };
    }

    private sealed class Stroke(string color, double width)
    {
        private readonly StringBuilder _points = new();

        public string Color { get; } = color;

        public double Width { get; } = width;

        public int PointCount { get; private set; }

        public string Points => _points.ToString();

        public void AddPoint(double x, double y)
        {
            if (PointCount > 0)
            {
                _points.Append(' ');
            }

            _points.AppendFormat(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0}", x, y);
            PointCount++;
        }
    }

    private sealed class PointerPoint
    {
        public double X { get; set; }

        public double Y { get; set; }

        public PointerPoint()
        {
        }

        public PointerPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
}
