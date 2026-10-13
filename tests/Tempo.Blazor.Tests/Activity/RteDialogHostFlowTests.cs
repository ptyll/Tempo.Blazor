using AngleSharp.Html.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Activity;

/// <summary>
/// FZE-10 review: the dialog CLOSES FIRST and the caret restore + insertion run afterwards, from
/// <c>OnAfterRenderAsync</c>, once the render without the dialog completed (the TmModal focus
/// trap must release the editor before focus/selection move back into it). Pinned here with a
/// strict JS interop recording handler and a sequence counter — bUnit groups recorded
/// invocations per identifier and preserves no cross-identifier order, so the ORDER evidence is
/// the log entries the handler predicates append, including the dialog overlay count captured at
/// the moment each entry fired.
/// </summary>
public class RteDialogHostFlowTests : LocalizationTestBase
{
    public RteDialogHostFlowTests()
    {
        // Strict: every unprepared interop call fails the test, so the assertions below see the
        // FULL set of interop traffic and nothing hidden. The TmModal stack (probe, promote,
        // trap) calls into its own modules — those are stubbed per function here; everything on
        // window.tmRichEditor / window.tmMarkdownEditor stays explicit per test.
        JSInterop.Mode = JSRuntimeMode.Strict;
        var observer = JSInterop.SetupModule("./_content/Tempo.Blazor/js/layout-observer.js");
        observer.SetupVoid("observe", _ => true).SetVoidResult();
        observer.SetupVoid("disconnect", _ => true).SetVoidResult();
        var topLayer = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js");
        topLayer.SetupVoid("promote", _ => true).SetVoidResult();
        topLayer.SetupVoid("demote", _ => true).SetVoidResult();
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        trap.SetupVoid("activate", _ => true).SetVoidResult();
        trap.SetupVoid("deactivate", _ => true).SetVoidResult();
        trap.SetupVoid("syncScrollRegion", _ => true).SetVoidResult();
        trap.SetupVoid("stopScrollRegion", _ => true).SetVoidResult();
        trap.SetupVoid("updateScrollRegionLabel", _ => true).SetVoidResult();
    }

    /// <summary>Append-only log of interop events in firing order — the sequence evidence.</summary>
    private sealed class Sequence
    {
        private readonly List<string> _entries = new();
        public IReadOnlyList<string> Entries => _entries;
        public void Add(string entry) => _entries.Add(entry);
        public int FirstIndexOf(string prefix) => _entries.FindIndex(e => e.StartsWith(prefix, StringComparison.Ordinal));
        public bool Contains(string prefix) => FirstIndexOf(prefix) >= 0;
        public override string ToString() => string.Join(" | ", _entries);
    }

    /// <summary>Records through the bUnit handler predicate: it runs at invocation time, in order.</summary>
    private static void Record(BunitJSInterop js, string identifier, Sequence log, Func<string> entry)
    {
        js.SetupVoid(identifier, _ => { log.Add(entry()); return true; }).SetVoidResult();
    }

    /// <summary>
    /// The overlay count evaluated when the entry FIRES — ":overlays=0" proves the dialog had
    /// unmounted at the moment the interop call happened.
    /// </summary>
    private static Func<string> Overlays(Func<int> overlayCount) => () => $"overlays={overlayCount()}";

    // ── TmRichEditorFull ──────────────────────────────────────────────────────

    [Fact]
    public void Full_LinkConfirm_ClosesDialogFirst_ThenRestoresCaret_AndInsertsWithTheEditorElement()
    {
        var log = new Sequence();
        Record(JSInterop, "tmRichEditor.initKeyboardShortcuts", log, () => "init");

        var cut = Render<TmRichEditorFull>();
        var editorId = cut.Find(".tm-rte-editor-content").GetAttribute("id")!;

        JSInterop.Setup<string>("tmRichEditor.getSelectedText", _ => { log.Add("getSelectedText"); return true; })
            .SetResult("brave");
        Record(JSInterop, "tmRichEditor.saveCursorPosition", log, () => "save");
        Record(JSInterop, "tmRichEditor.restoreCursorPosition", log, () => $"restore:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        Record(JSInterop, "tmRichEditor.insertLink", log, () => $"insertLink:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        JSInterop.Setup<string>("tmRichEditor.getHtml", _ => { log.Add("getHtml"); return true; })
            .SetResult("<p>hello <a href=\"https://example.com\">brave</a> world</p>");

        // Toolbar Link click → save the caret, open the dialog, restore-target = the editor id.
        cut.Find(".tm-rte-btn[data-format='link']").Click();

        cut.Find(".tm-focus-scope").GetAttribute("data-restore-target").Should().Be(editorId,
            "the dialog's focus trap restores focus to the editor surface on close");
        JSInterop.VerifyInvoke("tmRichEditor.saveCursorPosition", 1);
        log.Contains("save").Should().BeTrue();

        // While the dialog is open: no getHtml/setHtml traffic at all, and nothing restored yet.
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "tmRichEditor.getHtml");
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "tmRichEditor.setHtml");
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "tmRichEditor.restoreCursorPosition");

        // Confirm through the form's submit button.
        cut.Find(".tm-rte-link-url-input").Input("https://example.com");
        ((IHtmlFormElement)cut.Find("form")).Submit(); // the browser's implicit submission path

        // The dialog is gone and the whole restore→insert→sync ran.
        cut.FindAll(".tm-rte-link-dialog").Should().BeEmpty("the dialog unmounts BEFORE the insertion runs");

        log.FirstIndexOf("restore").Should().BeGreaterThan(log.FirstIndexOf("save"),
            "the bookmark is replayed only after the dialog opened-and-closed");
        log.Entries[log.FirstIndexOf("restore")].Should().Be("restore:overlays=0",
            "the restore fires when no dialog overlay is rendered any more");
        log.Entries[log.FirstIndexOf("insertLink")].Should().Be("insertLink:overlays=0",
            "the insertion also runs after the dialog unmounted");
        log.FirstIndexOf("insertLink").Should().BeGreaterThan(log.FirstIndexOf("restore"),
            "insert lands at the restored caret, so restore comes first");

        var insert = JSInterop.VerifyInvoke("tmRichEditor.insertLink", 1).Single();
        insert.Arguments[1].Should().Be("https://example.com");
        insert.Arguments[2].Should().Be("brave");

        // bUnit ElementReference ids are opaque (not the DOM id attribute) — the element contract
        // is observable as "every step targets the SAME editor reference".
        var touchedRefs = JSInterop.Invocations
            .Where(i => i.Identifier is "tmRichEditor.restoreCursorPosition" or "tmRichEditor.insertLink" or "tmRichEditor.getHtml")
            .Select(i => ((ElementReference)i.Arguments[0]!).Id)
            .Distinct()
            .ToList();
        touchedRefs.Should().HaveCount(1, "restore, insert and sync all run against the SAME editor element");
        touchedRefs[0].Should().NotBeNullOrEmpty("an editor element reference was passed — never a document-wide heuristic");
    }

    [Fact]
    public async Task Full_LinkCancel_Escape_ClosesDialogAndRestores_WithoutInserting()
    {
        var log = new Sequence();
        Record(JSInterop, "tmRichEditor.initKeyboardShortcuts", log, () => "init");

        var cut = Render<TmRichEditorFull>();

        JSInterop.Setup<string>("tmRichEditor.getSelectedText", _ => true).SetResult("brave");
        Record(JSInterop, "tmRichEditor.saveCursorPosition", log, () => "save");
        Record(JSInterop, "tmRichEditor.restoreCursorPosition", log, () => $"restore:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        Record(JSInterop, "tmRichEditor.insertLink", log, () => "insertLink");
        JSInterop.Setup<string>("tmRichEditor.getHtml", _ => true).SetResult("<p>x</p>");

        cut.Find(".tm-rte-btn[data-format='link']").Click();
        cut.FindAll(".tm-rte-link-dialog").Should().NotBeEmpty();

        var scope = cut.FindComponent<TmFocusScope>().Instance;
        await cut.InvokeAsync(() => scope.HandleFocusTrapEscapeAsync());

        cut.FindAll(".tm-rte-link-dialog").Should().BeEmpty();
        log.Entries.Should().Contain(e => e.StartsWith("restore:overlays=0"),
            "the cancel path replays the caret after the dialog unmounted");
        log.Contains("insertLink").Should().BeFalse("cancel never inserts");
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "tmRichEditor.insertLink");
    }

    [Fact]
    public void Full_LinkConfirm_InsertLinkThrowingJSException_DoesNotEscape_AndDialogStaysClosed()
    {
        var log = new Sequence();
        Record(JSInterop, "tmRichEditor.initKeyboardShortcuts", log, () => "init");
        Record(JSInterop, "tmRichEditor.saveCursorPosition", log, () => "save");
        Record(JSInterop, "tmRichEditor.restoreCursorPosition", log, () => "restore");

        var cut = Render<TmRichEditorFull>();

        JSInterop.Setup<string>("tmRichEditor.getSelectedText", _ => true).SetResult("brave");
        // The stashed insert runs from OnAfterRenderAsync — on the render pipeline. A JSException
        // (dead JS runtime / torn-down interop) escaping there would kill a Blazor Server circuit.
        JSInterop.SetupVoid("tmRichEditor.insertLink", _ => true)
            .SetException(new JSException("interop is broken"));
        JSInterop.Setup<string>("tmRichEditor.getHtml", _ => true).SetResult("<p>x</p>");

        cut.Find(".tm-rte-btn[data-format='link']").Click();
        cut.Find(".tm-rte-link-url-input").Input("https://example.com");

        var act = () => ((IHtmlFormElement)cut.Find("form")).Submit(); // the browser's implicit submission path

        act.Should().NotThrow("a failing stashed insert must be swallowed, not crash the render");
        cut.FindAll(".tm-rte-link-dialog").Should().BeEmpty("the dialog closed before the failing insert ran");
        JSInterop.VerifyInvoke("tmRichEditor.insertLink", 1);
        cut.Find(".tm-rte-editor-content").Should().NotBeNull("the component survives and still renders");
    }

    [Fact]
    public void Full_FindReplace_ReplaceAll_PassesTheEditorElement_AndReportsTheRealCount()
    {
        var cut = Render<TmRichEditorFull>();
        var editorId = cut.Find(".tm-rte-editor-content").GetAttribute("id")!;

        JSInterop.SetupVoid("tmRichEditor.saveCursorPosition", _ => true).SetVoidResult();
        JSInterop.Setup<string>("tmRichEditor.getHtml", _ => true).SetResult("<p>beat</p>");
        JSInterop.Setup<int>("tmRichEditor.findAndReplace", _ => true).SetResult(2);

        cut.Find(".tm-rte-editor-content").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "f", CtrlKey = true });
        cut.FindAll(".tm-rte-findreplace-dialog").Should().NotBeEmpty();

        cut.Find(".tm-rte-findreplace-find-input").Input("tempo");
        cut.Find(".tm-rte-findreplace-replace-input").Input("beat");
        cut.Find(".tm-rte-btn-replaceall").Click();

        var call = JSInterop.VerifyInvoke("tmRichEditor.findAndReplace", 1).Single();
        ((ElementReference)call.Arguments[0]!).Id.Should().NotBeNullOrEmpty(
            "find/replace runs against the editor element reference, not a document-wide heuristic");
        call.Arguments[1].Should().Be("tempo");
        call.Arguments[2].Should().Be("beat");
        call.Arguments[3].Should().Be(true);

        cut.Find(".tm-rte-findreplace-result").TextContent.Trim().Should().Be("2 occurrence(s) replaced",
            "the real count from JS is reported back to the dialog");
    }

    [Fact]
    public void Full_FindReplace_ZeroMatches_ReportsNoMatches()
    {
        var cut = Render<TmRichEditorFull>();

        JSInterop.SetupVoid("tmRichEditor.saveCursorPosition", _ => true).SetVoidResult();
        JSInterop.Setup<string>("tmRichEditor.getHtml", _ => true).SetResult("<p>x</p>");
        JSInterop.Setup<int>("tmRichEditor.findAndReplace", _ => true).SetResult(0);

        cut.Find(".tm-rte-editor-content").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "f", CtrlKey = true });
        cut.Find(".tm-rte-findreplace-find-input").Input("tempo");
        cut.Find(".tm-rte-findreplace-replace-input").Input("beat");
        ((IHtmlFormElement)cut.Find("form")).Submit(); // Enter runs Replace (single)

        cut.Find(".tm-rte-findreplace-result").TextContent.Trim().Should().Be("No matches found");
    }

    [Fact]
    public async Task Full_FindReplaceCancel_Escape_RestoresTheCaret()
    {
        var log = new Sequence();
        Record(JSInterop, "tmRichEditor.initKeyboardShortcuts", log, () => "init");
        var cut = Render<TmRichEditorFull>();
        var editorId = cut.Find(".tm-rte-editor-content").GetAttribute("id")!;

        Record(JSInterop, "tmRichEditor.saveCursorPosition", log, () => "save");
        Record(JSInterop, "tmRichEditor.restoreCursorPosition", log, () => $"restore:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        JSInterop.Setup<string>("tmRichEditor.getHtml", _ => true).SetResult("<p>x</p>");

        cut.Find(".tm-rte-editor-content").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "f", CtrlKey = true });

        var scope = cut.FindComponent<TmFocusScope>().Instance;
        await cut.InvokeAsync(() => scope.HandleFocusTrapEscapeAsync());

        cut.FindAll(".tm-rte-findreplace-dialog").Should().BeEmpty();
        log.Entries.Should().Contain(e => e.StartsWith("restore:overlays=0"),
            "the find bar's close path replays the saved selection");
        var restored = JSInterop.VerifyInvoke("tmRichEditor.restoreCursorPosition", 1).Single();
        ((ElementReference)restored.Arguments[0]!).Id.Should().NotBeNullOrEmpty(
            "the replay targets the editor element reference");
    }

    // ── TmRichEditorSimple ────────────────────────────────────────────────────

    [Fact]
    public void Simple_LinkConfirm_ClosesDialogFirst_ThenRestoresCaret_AndInsertsWithTheEditorElement()
    {
        var log = new Sequence();
        Record(JSInterop, "tmRichEditor.initKeyboardShortcuts", log, () => "init");

        var cut = Render<TmRichEditorSimple>();
        var editorId = cut.Find(".tm-rte-editor-content").GetAttribute("id")!;

        JSInterop.Setup<string>("tmRichEditor.getSelectedText", _ => { log.Add("getSelectedText"); return true; })
            .SetResult("brave");
        Record(JSInterop, "tmRichEditor.saveCursorPosition", log, () => "save");
        Record(JSInterop, "tmRichEditor.restoreCursorPosition", log, () => $"restore:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        Record(JSInterop, "tmRichEditor.insertLink", log, () => $"insertLink:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        JSInterop.Setup<string>("tmRichEditor.getHtml", _ => { log.Add("getHtml"); return true; })
            .SetResult("<p>hello <a href=\"https://example.com\">brave</a> world</p>");

        cut.Find(".tm-rte-btn[data-format='link']").Click();

        cut.Find(".tm-focus-scope").GetAttribute("data-restore-target").Should().Be(editorId);
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "tmRichEditor.getHtml");
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "tmRichEditor.setHtml");

        cut.Find(".tm-rte-link-url-input").Input("https://example.com");
        ((IHtmlFormElement)cut.Find("form")).Submit(); // the browser's implicit submission path

        cut.FindAll(".tm-rte-link-dialog").Should().BeEmpty("the dialog unmounts BEFORE the insertion runs");
        log.Entries[log.FirstIndexOf("restore")].Should().Be("restore:overlays=0");
        log.Entries[log.FirstIndexOf("insertLink")].Should().Be("insertLink:overlays=0");
        log.FirstIndexOf("insertLink").Should().BeGreaterThan(log.FirstIndexOf("restore"));

        var insert = JSInterop.VerifyInvoke("tmRichEditor.insertLink", 1).Single();
        insert.Arguments[1].Should().Be("https://example.com");
        // The simple editor passes no PreselectedText — the dialog falls back to the URL as text.
        insert.Arguments[2].Should().Be("https://example.com");
        var refs = JSInterop.Invocations
            .Where(i => i.Identifier is "tmRichEditor.restoreCursorPosition" or "tmRichEditor.insertLink")
            .Select(i => ((ElementReference)i.Arguments[0]!).Id)
            .Distinct()
            .ToList();
        refs.Should().HaveCount(1, "restore and insert target the SAME editor element");
        refs[0].Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Simple_LinkCancel_Escape_ClosesDialogAndRestores_WithoutInserting()
    {
        var log = new Sequence();
        Record(JSInterop, "tmRichEditor.initKeyboardShortcuts", log, () => "init");

        var cut = Render<TmRichEditorSimple>();

        JSInterop.Setup<string>("tmRichEditor.getSelectedText", _ => true).SetResult("brave");
        Record(JSInterop, "tmRichEditor.saveCursorPosition", log, () => "save");
        Record(JSInterop, "tmRichEditor.restoreCursorPosition", log, () => $"restore:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        Record(JSInterop, "tmRichEditor.insertLink", log, () => "insertLink");
        JSInterop.Setup<string>("tmRichEditor.getHtml", _ => true).SetResult("<p>x</p>");

        cut.Find(".tm-rte-btn[data-format='link']").Click();

        var scope = cut.FindComponent<TmFocusScope>().Instance;
        await cut.InvokeAsync(() => scope.HandleFocusTrapEscapeAsync());

        cut.FindAll(".tm-rte-link-dialog").Should().BeEmpty();
        log.Entries.Should().Contain(e => e.StartsWith("restore:overlays=0"));
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "tmRichEditor.insertLink");
    }

    // ── TmMarkdownEditor ──────────────────────────────────────────────────────

    [Fact]
    public void Markdown_LinkConfirm_ClosesDialogFirst_ThenRestoresBookmark_AndInsertsWithTheEditorElement()
    {
        var log = new Sequence();
        Record(JSInterop, "tmMarkdownEditor.init", log, () => "init");

        var cut = Render<TmMarkdownEditor>();
        var editorId = cut.Find(".tm-mde-content").GetAttribute("id")!;

        JSInterop.Setup<string>("tmMarkdownEditor.getSelectedText", _ => { log.Add("getSelectedText"); return true; })
            .SetResult("brave");
        Record(JSInterop, "tmMarkdownEditor.saveCursorBookmark", log, () => "save");
        Record(JSInterop, "tmMarkdownEditor.restoreCursorBookmark", log, () => $"restore:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        Record(JSInterop, "tmMarkdownEditor.insertLink", log, () => $"insertLink:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        JSInterop.Setup<string>("tmMarkdownEditor.getValue", _ => true)
            .SetResult("hello [brave](https://example.com) world");
        JSInterop.Setup<string[]>("tmMarkdownEditor.getActiveFormats", _ => true).SetResult(Array.Empty<string>());

        cut.Find(".tm-mde-btn[aria-label='Link']").Click();

        cut.Find(".tm-focus-scope").GetAttribute("data-restore-target").Should().Be(editorId);
        JSInterop.VerifyInvoke("tmMarkdownEditor.saveCursorBookmark", 1);
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "tmMarkdownEditor.getValue");

        cut.Find(".tm-rte-link-url-input").Input("https://example.com");
        ((IHtmlFormElement)cut.Find("form")).Submit(); // the browser's implicit submission path

        cut.FindAll(".tm-rte-link-dialog").Should().BeEmpty("the dialog unmounts BEFORE the insertion runs");
        log.Entries[log.FirstIndexOf("restore")].Should().Be("restore:overlays=0");
        log.Entries[log.FirstIndexOf("insertLink")].Should().Be("insertLink:overlays=0");
        log.FirstIndexOf("insertLink").Should().BeGreaterThan(log.FirstIndexOf("restore"));

        var insert = JSInterop.VerifyInvoke("tmMarkdownEditor.insertLink", 1).Single();
        insert.Arguments[1].Should().Be("https://example.com");
        insert.Arguments[2].Should().Be("brave");
        var mdeRefs = JSInterop.Invocations
            .Where(i => i.Identifier is "tmMarkdownEditor.restoreCursorBookmark" or "tmMarkdownEditor.insertLink")
            .Select(i => ((ElementReference)i.Arguments[0]!).Id)
            .Distinct()
            .ToList();
        mdeRefs.Should().HaveCount(1, "the markdown restore and insert target the SAME editor element");
        mdeRefs[0].Should().NotBeNullOrEmpty();
        JSInterop.VerifyInvoke("tmMarkdownEditor.getValue", 1);
    }

    [Fact]
    public async Task Markdown_LinkCancel_Escape_ClosesDialogAndRestores_WithoutInserting()
    {
        var log = new Sequence();
        Record(JSInterop, "tmMarkdownEditor.init", log, () => "init");

        var cut = Render<TmMarkdownEditor>();

        JSInterop.Setup<string>("tmMarkdownEditor.getSelectedText", _ => true).SetResult("brave");
        Record(JSInterop, "tmMarkdownEditor.saveCursorBookmark", log, () => "save");
        Record(JSInterop, "tmMarkdownEditor.restoreCursorBookmark", log, () => $"restore:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        Record(JSInterop, "tmMarkdownEditor.insertLink", log, () => "insertLink");
        JSInterop.Setup<string>("tmMarkdownEditor.getValue", _ => true).SetResult("hello world");
        JSInterop.Setup<string[]>("tmMarkdownEditor.getActiveFormats", _ => true).SetResult(Array.Empty<string>());

        cut.Find(".tm-mde-btn[aria-label='Link']").Click();

        var scope = cut.FindComponent<TmFocusScope>().Instance;
        await cut.InvokeAsync(() => scope.HandleFocusTrapEscapeAsync());

        cut.FindAll(".tm-rte-link-dialog").Should().BeEmpty();
        log.Entries.Should().Contain(e => e.StartsWith("restore:overlays=0"));
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "tmMarkdownEditor.insertLink");
    }

    [Fact]
    public void Markdown_TableConfirm_ClosesDialogFirst_ThenRestoresBookmark_AndInsertsWithTheEditorElement()
    {
        var log = new Sequence();
        Record(JSInterop, "tmMarkdownEditor.init", log, () => "init");

        var cut = Render<TmMarkdownEditor>();
        var editorId = cut.Find(".tm-mde-content").GetAttribute("id")!;

        Record(JSInterop, "tmMarkdownEditor.saveCursorBookmark", log, () => "save");
        Record(JSInterop, "tmMarkdownEditor.restoreCursorBookmark", log, () => $"restore:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        Record(JSInterop, "tmMarkdownEditor.insertTable", log, () => $"insertTable:overlays={cut.FindAll(".tm-modal-overlay").Count}");
        JSInterop.Setup<string>("tmMarkdownEditor.getValue", _ => true)
            .SetResult("| a | b |\n| - | - |\n| 1 | 2 |");
        JSInterop.Setup<string[]>("tmMarkdownEditor.getActiveFormats", _ => true).SetResult(Array.Empty<string>());

        cut.Find(".tm-mde-btn[aria-label='Table']").Click();

        cut.Find(".tm-focus-scope").GetAttribute("data-restore-target").Should().Be(editorId);

        ((IHtmlFormElement)cut.Find("form")).Submit(); // defaults 3×3 are valid

        cut.FindAll(".tm-rte-table-dialog").Should().BeEmpty("the dialog unmounts BEFORE the insertion runs");
        log.Entries[log.FirstIndexOf("restore")].Should().Be("restore:overlays=0");
        log.Entries[log.FirstIndexOf("insertTable")].Should().Be("insertTable:overlays=0");

        var insert = JSInterop.VerifyInvoke("tmMarkdownEditor.insertTable", 1).Single();
        insert.Arguments[1].Should().Be(3);
        insert.Arguments[2].Should().Be(3);
        var mdeTableRefs = JSInterop.Invocations
            .Where(i => i.Identifier is "tmMarkdownEditor.restoreCursorBookmark" or "tmMarkdownEditor.insertTable")
            .Select(i => ((ElementReference)i.Arguments[0]!).Id)
            .Distinct()
            .ToList();
        mdeTableRefs.Should().HaveCount(1, "the markdown restore and insert target the SAME editor element");
        mdeTableRefs[0].Should().NotBeNullOrEmpty("the insert runs against the editor ELEMENT");
    }
}
