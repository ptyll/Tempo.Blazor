using System.Text.Json;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Pure model operations on a <see cref="WireframeDocument"/>'s page list, shared by
/// <see cref="TmWireframePagesPanel"/>. They mutate the document and never raise events or touch the active page,
/// except <see cref="Delete"/> which re-targets <see cref="WireframeDocument.ActivePageId"/> when the active page goes.
/// </summary>
internal static class WireframePageOperations
{
    /// <summary>Appends a new empty page of the given size and returns it.</summary>
    public static WireframePage Add(WireframeDocument doc, string name, double width, double height)
    {
        var page = new WireframePage { Name = name, Width = width, Height = height };
        doc.Pages.Add(page);
        return page;
    }

    /// <summary>
    /// Deep-copies a page (JSON round trip, like the editor's page tab) under a new id and inserts the copy right
    /// after the source. The copy is named <c>"{name} ({copyWord})"</c>. Returns <c>null</c> for an unknown page.
    /// </summary>
    public static WireframePage? Duplicate(WireframeDocument doc, string pageId, string copyWord)
    {
        var index = doc.Pages.FindIndex(p => p.Id == pageId);
        if (index < 0) return null;

        var source = doc.Pages[index];
        var json = JsonSerializer.Serialize(source, WireframeJsonOptions.Default);
        var copy = JsonSerializer.Deserialize<WireframePage>(json, WireframeJsonOptions.Default)!;
        copy.Id = "p" + Guid.NewGuid().ToString("N")[..7];
        copy.Name = $"{source.Name} ({copyWord})";
        doc.Pages.Insert(index + 1, copy);
        return copy;
    }

    /// <summary>
    /// Removes a page. Returns the id of the page that is active afterwards, or <c>null</c> when nothing was deleted
    /// (unknown page, or it is the only page: a document always keeps at least one). When the deleted page was the
    /// active one, its previous neighbour (else its next one) becomes active.
    /// </summary>
    public static string? Delete(WireframeDocument doc, string pageId)
    {
        if (doc.Pages.Count <= 1) return null;
        var index = doc.Pages.FindIndex(p => p.Id == pageId);
        if (index < 0) return null;

        var wasActive = doc.ActivePage?.Id == pageId;
        doc.Pages.RemoveAt(index);
        if (wasActive)
            doc.ActivePageId = doc.Pages[Math.Max(0, index - 1)].Id;
        return doc.ActivePage?.Id;
    }

    /// <summary>Moves a page by <paramref name="delta"/> positions (clamped). Returns the new index, or -1 when nothing moved.</summary>
    public static int Move(WireframeDocument doc, string pageId, int delta)
    {
        var index = doc.Pages.FindIndex(p => p.Id == pageId);
        if (index < 0) return -1;
        var target = Math.Clamp(index + delta, 0, doc.Pages.Count - 1);
        return MoveTo(doc, pageId, target);
    }

    /// <summary>Moves a page to <paramref name="targetIndex"/> (the drag-and-drop landing spot). Returns the new index, or -1 when nothing moved.</summary>
    public static int MoveTo(WireframeDocument doc, string pageId, int targetIndex)
    {
        var index = doc.Pages.FindIndex(p => p.Id == pageId);
        if (index < 0 || targetIndex < 0 || targetIndex >= doc.Pages.Count || targetIndex == index) return -1;

        var page = doc.Pages[index];
        doc.Pages.RemoveAt(index);
        doc.Pages.Insert(targetIndex, page);
        return targetIndex;
    }

    /// <summary>Renames a page (trimmed). Returns <c>false</c> for an unknown page, a blank name or an unchanged one.</summary>
    public static bool Rename(WireframeDocument doc, string pageId, string? newName)
    {
        var page = doc.Pages.FirstOrDefault(p => p.Id == pageId);
        var name = newName?.Trim();
        if (page is null || string.IsNullOrEmpty(name) || string.Equals(page.Name, name, StringComparison.Ordinal)) return false;
        page.Name = name;
        return true;
    }
}
