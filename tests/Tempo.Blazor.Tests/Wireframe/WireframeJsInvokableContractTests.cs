using System.Reflection;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// The JS -> .NET contract of <c>wireframe-designer.js</c> (wireframe plan ce19aa8c, phase 6 carry-forward). The script calls
/// the canvas through <c>invokeMethodAsync('Name', …)</c>; a name that no <c>[JSInvokable]</c> answers is only a runtime
/// interop error (it once left the context menu open after a click away, because <c>CloseContextMenu</c> had no
/// <c>[JSInvokable("CloseContextMenu")]</c> carrier). The test reads the script from the repo and the canvas by reflection.
/// </summary>
public class WireframeJsInvokableContractTests : LocalizationTestBase
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx"))) return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("TempoBlazor.slnx was not found above " + AppContext.BaseDirectory);
    }

    private static string ScriptSource() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "Tempo.Blazor.Wireframe", "wwwroot", "js", "wireframe-designer.js"));

    /// <summary>Every method name the script invokes on its .NET reference (directly or through the quiet wrapper).</summary>
    private static string[] CalledMethods() =>
        Regex.Matches(ScriptSource(), @"(?:invokeMethodAsync\(\s*|_invokeQuiet\(\s*inst\s*,\s*)'(?<name>\w+)'", RegexOptions.None, RegexTimeout)
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    /// <summary>The identifiers the canvas answers to: the explicit <c>[JSInvokable("X")]</c> name or the method name.</summary>
    private static HashSet<string> InvokableIdentifiers() =>
        typeof(TmWireframeDesignerCanvas)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => (Method: m, Attribute: m.GetCustomAttribute<JSInvokableAttribute>()))
            .Where(x => x.Attribute is not null)
            .Select(x => x.Attribute!.Identifier ?? x.Method.Name)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void ScriptSource_IsFoundAndCallsTheCanvas()
    {
        CalledMethods().Should().Contain(["OnElementMoved", "OnViewBoxChanged", "CloseContextMenu", "OnGestureCancelled"]);
    }

    [Fact]
    public void EveryMethodTheScriptInvokes_IsAJSInvokableOnTheCanvas()
    {
        var invokable = InvokableIdentifiers();

        CalledMethods().Where(name => !invokable.Contains(name))
            .Should().BeEmpty("wireframe-designer.js calls these through invokeMethodAsync and the canvas has no [JSInvokable] answering them");
    }

    [Fact]
    public void CloseContextMenu_IsExposedUnderItsScriptName()
    {
        var method = typeof(TmWireframeDesignerCanvas).GetMethod(nameof(TmWireframeDesignerCanvas.JsCloseContextMenu));

        method.Should().NotBeNull();
        method!.GetCustomAttribute<JSInvokableAttribute>()!.Identifier.Should().Be("CloseContextMenu");
    }

    [Fact]
    public async Task CloseContextMenu_RaisesOnCloseContextMenu()
    {
        Services.AddSingleton(new WireframeComponentRegistry());
        var closed = 0;
        var cut = Render<TmWireframeDesignerCanvas>(p => p
            .Add(c => c.OnCloseContextMenu, EventCallback.Factory.Create(this, () => closed++)));

        await cut.InvokeAsync(() => cut.Instance.JsCloseContextMenu());

        closed.Should().Be(1);
    }

    // ── gesture rollback announcement (live region) ───────────────────────────────

    [Fact]
    public void LiveRegion_IsAPoliteVisuallyHiddenStatus_AndStartsEmpty()
    {
        Services.AddSingleton(new WireframeComponentRegistry());

        var cut = Render<TmWireframeDesignerCanvas>();

        var status = cut.Find("[role=status]");
        status.GetAttribute("aria-live").Should().Be("polite");
        status.ClassList.Should().Contain("tm-sr-only");
        status.TextContent.Should().BeEmpty();
    }

    [Fact]
    public async Task OnGestureCancelled_RendersTheLocalizedMessage_AndReannouncesOnRepeat()
    {
        Services.AddSingleton(new WireframeComponentRegistry());
        var cut = Render<TmWireframeDesignerCanvas>();

        await cut.InvokeAsync(() => cut.Instance.JsOnGestureCancelled());
        var first = cut.Find("[role=status]").TextContent;
        await cut.InvokeAsync(() => cut.Instance.JsOnGestureCancelled());
        var second = cut.Find("[role=status]").TextContent;

        first.Trim().Should().Be("Move cancelled");
        second.Trim().Should().Be("Move cancelled");
        second.Should().NotBe(first, "an identical text would not be announced again; a trailing no-break space alternates");
    }

    [Fact]
    public async Task OnGestureCancelled_IsLocalized()
    {
        UseCzechLocalization();
        Services.AddSingleton(new WireframeComponentRegistry());
        var cut = Render<TmWireframeDesignerCanvas>();

        await cut.InvokeAsync(() => cut.Instance.JsOnGestureCancelled());

        cut.Find("[role=status]").TextContent.Trim().Should().Be("Přesun zrušen");
    }

    // ── context menu a11y (focus management itself lives in the script) ────────────

    [Fact]
    public void ContextMenu_ItemsAreMenuItemButtons_AndTheMenuCanTakeFocus()
    {
        var cut = Render<TmWireframeContextMenu>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MenuType, WireframeContextMenuType.Canvas)
            .Add(c => c.CanUndo, false)
            .Add(c => c.CanRedo, true));

        var menu = cut.Find(".tm-wd-editor__context-menu");
        menu.GetAttribute("role").Should().Be("menu");
        menu.GetAttribute("tabindex").Should().Be("-1", "the script falls back to focusing the menu when no item is enabled");
        var items = cut.FindAll(".tm-wd-editor__context-item");
        items.Should().OnlyContain(i => i.GetAttribute("role") == "menuitem");
        // the script skips disabled items (a disabled button cannot take focus), so the first focus target is the first enabled one
        items[0].HasAttribute("disabled").Should().BeTrue();
        items.First(i => !i.HasAttribute("disabled")).TextContent.Should().Contain("Redo");
    }
}
