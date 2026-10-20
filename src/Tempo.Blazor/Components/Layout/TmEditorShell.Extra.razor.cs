using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Components.Layout;

// The optional third mobile sheet tab (X2). Kept beside the markup to respect the .razor length ceiling.
public partial class TmEditorShell
{
    /// <summary>
    /// Optional extra content of the mobile panels sheet: a third tab after <see cref="Right"/>,
    /// so a host can present flat sheet tabs (e.g. Shapes | Properties | Layers) instead of
    /// nesting tabs inside the right panel. It opens and closes with the right panel
    /// (<see cref="RightOpen"/>), is selected through <see cref="ActiveMobilePanel"/> =
    /// <see cref="EditorShellPanel.Extra"/> and renders ONLY in the mobile
    /// <see cref="MobilePanelPresentation.Sheet"/> presentation — desktop, tablet and the Tabs
    /// presentation ignore it (render that content inside <see cref="Right"/> there).
    /// Null (default) keeps the two-panel sheet.
    /// </summary>
    [Parameter] public RenderFragment? Extra { get; set; }

    /// <summary>The extra panel title — its mobile sheet tab label. Null uses the localized default.</summary>
    [Parameter] public string? ExtraTitle { get; set; }

    private readonly string _extraPanelId = $"tm-editor-shell-extra-panel-{Guid.NewGuid():N}";

    /// <summary>The extra tab is shown: MOBILE Sheet presentation, right panel open with content, extra content set.</summary>
    private bool ExtraOpen => _resolved == TmLayoutMode.Mobile
        && MobilePanelPresentation == MobilePanelPresentation.Sheet
        && RightOpen && Right is not null && Extra is not null;

    /// <summary>The open sheet panels in tab order (Left, Right, Extra).</summary>
    private List<EditorShellPanel> SheetTabOrder()
    {
        var order = new List<EditorShellPanel>(3);
        if (LeftOpen && Left is not null) order.Add(EditorShellPanel.Left);
        if (RightOpen && Right is not null) order.Add(EditorShellPanel.Right);
        if (ExtraOpen) order.Add(EditorShellPanel.Extra);
        return order;
    }

    /// <summary>The panel the Sheet presentation shows: the selected tab while it is open, otherwise the first open panel.</summary>
    private EditorShellPanel SheetActive
    {
        get
        {
            var order = SheetTabOrder();
            if (order.Count == 0) return EditorShellPanel.Right;
            return order.Contains(_active) ? _active : order[0];
        }
    }

    private RenderFragment RenderExtraTab() => builder =>
    {
        if (ExtraOpen) builder.AddContent(0, RenderTab(EditorShellPanel.Extra));
    };

    /// <summary>The extra tabpanel: always a tabpanel (it only exists beside the right panel).</summary>
    private RenderFragment RenderSheetExtraPanel() => builder =>
    {
        if (!ExtraOpen) return;
        builder.OpenElement(0, "div");
        builder.SetKey(EditorShellPanel.Extra);
        builder.AddAttribute(1, "role", "tabpanel");
        builder.AddAttribute(2, "id", _extraPanelId);
        builder.AddAttribute(3, "aria-labelledby", TabId(EditorShellPanel.Extra));
        builder.AddAttribute(4, "class", "tm-editor-shell__tabpanel");
        builder.AddAttribute(5, "data-region", "extra");
        builder.AddAttribute(6, "hidden", SheetActive != EditorShellPanel.Extra);
        builder.AddContent(7, Extra);
        builder.CloseElement();
    };
}
