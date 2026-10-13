using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>Column pinning, resize, and layout persistence tests.</summary>
public class TmDataTablePinResizeTests : LocalizationTestBase
{
    public record Person(string Name, string Dept, int Age);

    private static void Col(RenderTreeBuilder b, ref int seq, string title, Func<Person, object?> field, bool resizable = false)
    {
        b.OpenComponent<TmDataTableColumn<Person>>(seq++);
        b.AddAttribute(seq++, "Title", title);
        b.AddAttribute(seq++, "PropertyName", title);
        b.AddAttribute(seq++, "Field", field);
        if (resizable) b.AddAttribute(seq++, "Resizable", true);
        b.CloseComponent();
    }

    private IRenderedComponent<TmDataTable<Person>> Render(IDataTableLayoutStore? store = null, string ctx = "pin-test")
        => Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, ctx);
            if (store is not null) p.Add(c => c.LayoutStore, store);
            p.Add(c => c.Items, new List<Person> { new("Ann", "A", 1), new("Bob", "B", 2) });
            p.AddChildContent(b =>
            {
                var seq = 0;
                Col(b, ref seq, "Name", x => x.Name, resizable: true);
                Col(b, ref seq, "Dept", x => x.Dept);
                Col(b, ref seq, "Age", x => x.Age);
            });
        });

    private static IElement HeaderFor(IRenderedComponent<TmDataTable<Person>> cut, string title)
        => cut.FindAll("thead th").First(h => h.TextContent.Contains(title));

    [Fact]
    public async Task PinColumn_AddsStickyClassAndReordersFirst()
    {
        var cut = Render();

        await cut.InvokeAsync(() => cut.Instance.SetColumnPinAsync("Dept", ColumnPin.Left));

        var first = cut.FindAll("thead th")[0];
        first.TextContent.Should().Contain("Dept");
        first.ClassList.Should().Contain("tm-col-pinned-left");
        first.GetAttribute("style").Should().Contain("position:sticky");
    }

    [Fact]
    public async Task PinColumn_PersistsToLayoutStore()
    {
        var store = new InMemoryDataTableLayoutStore();
        var cut = Render(store);

        await cut.InvokeAsync(() => cut.Instance.SetColumnPinAsync("Dept", ColumnPin.Right));

        var saved = await store.LoadLayoutAsync("pin-test");
        saved.Should().NotBeNull();
        saved!.ColumnPins["Dept"].Should().Be(ColumnPin.Right);
    }

    [Fact]
    public async Task PinColumn_FiresLayoutChanged()
    {
        DataTableLayout? captured = null;
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "pin-test");
            p.Add(c => c.LayoutChanged, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<DataTableLayout>(this, l => captured = l));
            p.Add(c => c.Items, new List<Person> { new("Ann", "A", 1) });
            p.AddChildContent(b =>
            {
                var seq = 0;
                Col(b, ref seq, "Name", x => x.Name);
                Col(b, ref seq, "Dept", x => x.Dept);
            });
        });

        await cut.InvokeAsync(() => cut.Instance.SetColumnPinAsync("Dept", ColumnPin.Left));

        captured.Should().NotBeNull();
        captured!.ColumnPins["Dept"].Should().Be(ColumnPin.Left);
    }

    [Fact]
    public async Task ResizeColumn_SetsWidthAndPersists()
    {
        var store = new InMemoryDataTableLayoutStore();
        var cut = Render(store);

        await cut.InvokeAsync(() => cut.Instance.SetColumnWidthAsync("Name", 250));

        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("width:250px");
        // The table layout is auto: without min-width the browser may keep the rendered
        // <th> narrower than the width the user (or the drag) set.
        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("min-width:250px");
        (await store.LoadLayoutAsync("pin-test"))!.ColumnWidths["Name"].Should().Be(250);
    }

    [Fact]
    public async Task ResizeColumn_Keeps_An_Explicit_MinWidth_Over_The_Pixel_Width()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "pin-test");
            p.Add(c => c.Items, new List<Person> { new("Ann", "A", 1) });
            p.AddChildContent(b =>
            {
                var seq = 0;
                b.OpenComponent<TmDataTableColumn<Person>>(seq++);
                b.AddAttribute(seq++, "Title", "Name");
                b.AddAttribute(seq++, "PropertyName", "Name");
                b.AddAttribute(seq++, "Field", (Func<Person, object?>)(x => x.Name));
                b.AddAttribute(seq++, "MinWidth", "80px");
                b.AddAttribute(seq++, "Resizable", true);
                b.CloseComponent();
            });
        });

        await cut.InvokeAsync(() => cut.Instance.SetColumnWidthAsync("Name", 250));

        var style = HeaderFor(cut, "Name").GetAttribute("style");
        style.Should().Contain("width:250px");
        style.Should().Contain("min-width:80px", "the column's own MinWidth wins");
        style.Should().NotContain("min-width:250px");
    }

    [Fact]
    public async Task ResizeColumn_ClampsToMinimum()
    {
        var cut = Render();

        await cut.InvokeAsync(() => cut.Instance.SetColumnWidthAsync("Name", 5));

        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("width:60px");
    }

    [Fact]
    public async Task LayoutStore_LoadedOnInit_AppliesPinAndWidth()
    {
        var store = new InMemoryDataTableLayoutStore();
        await store.SaveLayoutAsync("pin-test", new DataTableLayout
        {
            ColumnWidths = new() { ["Name"] = 300 },
            ColumnPins = new() { ["Dept"] = ColumnPin.Left }
        });

        var cut = Render(store);
        await cut.InvokeAsync(() => { });
        cut.Render();

        var headers = cut.FindAll("thead th");
        headers[0].TextContent.Should().Contain("Dept");
        headers[0].ClassList.Should().Contain("tm-col-pinned-left");
        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("width:300px");
    }

    [Fact]
    public void PinButton_Click_CyclesPin()
    {
        var cut = Render();

        cut.Find("[data-testid='pin-Dept']").Click();

        HeaderFor(cut, "Dept").ClassList.Should().Contain("tm-col-pinned-left");
    }

    [Fact]
    public void ResizeHandle_RenderedForResizableColumns()
    {
        var cut = Render();

        cut.FindAll("[data-testid='resize-Name']").Should().ContainSingle();
        cut.FindAll("[data-testid='resize-Dept']").Should().BeEmpty();
    }

    [Fact]
    public void OnColumnResized_JsCallback_AppliesWidth()
    {
        var cut = Render();

        cut.InvokeAsync(() => cut.Instance.OnColumnResized("Age", 180));
        cut.Render();

        HeaderFor(cut, "Age").GetAttribute("style").Should().Contain("width:180px");
    }

    [Fact]
    public void OnColumnResizeCancelled_AutoColumn_RemovesTheWidth_Again()
    {
        var cut = Render();

        cut.InvokeAsync(() => cut.Instance.OnColumnResized("Age", 180));
        cut.Render();
        HeaderFor(cut, "Age").GetAttribute("style").Should().Contain("width:180px", "precondition: the drag preview moved the width");

        cut.InvokeAsync(() => cut.Instance.OnColumnResizeCancelled());
        cut.Render();

        HeaderFor(cut, "Age").GetAttribute("style").Should().NotContain("width:",
            "the column had no width before the gesture — a cancelled drag must not persist one");
    }

    [Fact]
    public async Task OnColumnResizeCancelled_PresetColumn_RestoresTheSnapshotWidth()
    {
        var cut = Render();

        await cut.InvokeAsync(() => cut.Instance.SetColumnWidthAsync("Name", 250));
        cut.InvokeAsync(() => cut.Instance.OnColumnResized("Name", 400));
        cut.Render();
        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("width:400px", "precondition: the drag preview moved the width");

        cut.InvokeAsync(() => cut.Instance.OnColumnResizeCancelled());
        cut.Render();

        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("width:250px",
            "the cancelled drag restored the width the gesture started from");
    }

    [Fact]
    public void OnColumnResizeCancelled_WithoutAGesture_IsANoOp()
    {
        var cut = Render();

        cut.InvokeAsync(() => cut.Instance.OnColumnResizeCancelled());
        cut.Render();

        HeaderFor(cut, "Age").GetAttribute("style").Should().NotContain("width:");
    }

    [Fact]
    public async Task OnColumnResizeCommitted_ClearsTheSnapshot()
    {
        var cut = Render();

        cut.InvokeAsync(() => cut.Instance.OnColumnResized("Age", 180));
        await cut.InvokeAsync(() => cut.Instance.OnColumnResizeCommitted());

        // A cancel AFTER the commit is a stray late event, not part of the finished gesture:
        // the committed width must survive it.
        cut.InvokeAsync(() => cut.Instance.OnColumnResizeCancelled());
        cut.Render();

        HeaderFor(cut, "Age").GetAttribute("style").Should().Contain("width:180px");
    }

    [Fact]
    public async Task AriaValueMax_Tracks_Committed_Width()
    {
        var cut = Render();

        await cut.InvokeAsync(() => cut.Instance.SetColumnWidthAsync("Name", 1500));

        cut.Find("[data-testid='resize-Name']").GetAttribute("aria-valuemax").Should().Be("1500",
            "the announced maximum never sits below the separator's current value");
        cut.Find("[data-testid='resize-Name']").GetAttribute("aria-valuenow").Should().Be("1500");
    }

    [Fact]
    public void ResizeHandle_ArrowRight_Uses_The_Measured_Column_Width()
    {
        var cut = Render();
        JSInterop.Setup<double>("tmDataTable.measureColumn", _ => true).SetResult(137.6);

        cut.Find("[data-testid='resize-Name']").KeyDown("ArrowRight");

        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("width:146px",
            "the step starts from the real 138px <th>, not the 160 default");
    }

    // ── FZE-21: the handle is a keyboard-operable separator over the shared pointer-drag helper ──

    [Fact]
    public void ResizeHandle_Is_Focusable_Separator_With_Name()
    {
        var cut = Render();

        var handle = cut.Find("[data-testid='resize-Name']");
        handle.GetAttribute("role").Should().Be("separator");
        handle.GetAttribute("tabindex").Should().Be("0");
        handle.GetAttribute("aria-orientation").Should().Be("vertical");
        // A focusable separator announces its value and its bounds, always.
        handle.GetAttribute("aria-valuenow").Should().NotBeNullOrEmpty();
        handle.GetAttribute("aria-valuemin").Should().Be("60");
        handle.GetAttribute("aria-valuemax").Should().NotBeNullOrEmpty();
        handle.GetAttribute("aria-label").Should().Contain("Name");
        // The drag gesture reads these on press.
        handle.GetAttribute("data-col-key").Should().Be("Name");
        handle.GetAttribute("data-min-width").Should().Be("60");
    }

    [Fact]
    public async Task ResizeHandle_ArrowRight_Grows_By8_And_Persists()
    {
        var store = new InMemoryDataTableLayoutStore();
        var cut = Render(store);

        cut.Find("[data-testid='resize-Name']").KeyDown("ArrowRight");

        // No measured width yet: the step starts from the 160 default and lands on 168.
        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("width:168px");
        cut.Find("[data-testid='resize-Name']").GetAttribute("aria-valuenow").Should().Be("168");
        (await store.LoadLayoutAsync("pin-test"))!.ColumnWidths["Name"].Should().Be(168);
    }

    [Fact]
    public async Task ResizeHandle_ShiftArrowLeft_Shrinks_By32_Clamped_To_Min()
    {
        var cut = Render();

        await cut.InvokeAsync(() => cut.Instance.SetColumnWidthAsync("Name", 80));
        cut.Render();

        // One Shift step (32px) from 80 crosses the minimum: the width lands ON the clamp, not
        // below it. (bUnit fires keydowns without awaiting the async handler; a single press keeps
        // the dispatch deterministic.)
        cut.Find("[data-testid='resize-Name']").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs
        {
            Key = "ArrowLeft",
            ShiftKey = true,
        });

        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("width:60px");
    }

    [Fact]
    public void ResizeHandle_Home_Jumps_To_Minimum_And_End_To_Maximum()
    {
        var cut = Render();

        cut.Find("[data-testid='resize-Name']").KeyDown("End");
        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("width:1200px");

        cut.Find("[data-testid='resize-Name']").KeyDown("Home");
        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("width:60px");
    }

    [Fact]
    public void ResizeHandle_Keydown_Does_Not_Sort()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "pin-test");
            p.Add(c => c.Items, new List<Person> { new("Ann", "A", 1) });
            p.AddChildContent(b =>
            {
                var seq = 0;
                b.OpenComponent<TmDataTableColumn<Person>>(seq++);
                b.AddAttribute(seq++, "Title", "Name");
                b.AddAttribute(seq++, "PropertyName", "Name");
                b.AddAttribute(seq++, "Field", (Func<Person, object?>)(x => x.Name));
                b.AddAttribute(seq++, "Sortable", true);
                b.AddAttribute(seq++, "Resizable", true);
                b.CloseComponent();
            });
        });

        var handle = cut.Find("[data-testid='resize-Name']");
        handle.KeyDown("ArrowRight");
        handle.KeyDown("Home");
        handle.KeyDown("Enter");

        // The resize ran (the key was answered by the handle) but the keys never reached the
        // <th>'s sort handler: aria-sort is still "none".
        HeaderFor(cut, "Name").GetAttribute("style").Should().Contain("width:60px");
        cut.Find("thead th").GetAttribute("aria-sort").Should().Be("none");
    }

    [Fact]
    public void AttachColumnResize_Invoked_On_First_Render()
    {
        var cut = Render();

        JSInterop.VerifyInvoke("tmDataTable.attachColumnResize");
        var invocation = JSInterop.Invocations["tmDataTable.attachColumnResize"].Single();
        invocation.Arguments.Should().HaveCount(2);
    }
}
