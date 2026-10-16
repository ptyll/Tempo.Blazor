using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Serialization;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Components.Diagram.Templates;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// DI1 read-only guard rails for <see cref="TmDiagramEditor"/>: the built-in Read-only/Edit
/// toggle flips an internal user state and NEVER writes back to the <c>[Parameter] ReadOnly</c>,
/// the toggle is hidden while the host pins <c>ReadOnly=true</c> (a hosted preview can no longer
/// be unlocked from the UI), and the effective read-only state blocks every editing path
/// (undo/redo, paste, SQL/CSV import, templates).
/// </summary>
public class TmDiagramEditorReadOnlyTests : LocalizationTestBase
{
    public TmDiagramEditorReadOnlyTests()
    {
        Services.GetRequiredService<DiagramStencilRegistry>()
            .RegisterProvider(new BuiltInDiagramStencilProvider());
        Services.GetRequiredService<DiagramTemplateRegistry>()
            .RegisterTemplate(new DiagramTemplate
            {
                Id = "tpl-readonly-test",
                Name = "Read-only Test Template",
                Category = "Test",
                DocumentJson = DiagramSerializer.Serialize(CreateTemplateDocument()),
            });
    }

    [Fact]
    public void HostReadOnly_True_HidesTheReadOnlyToggle()
    {
        using var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, true));

        cut.FindAll("[data-tm-group='readonly']").Should().BeEmpty(
            "a host-pinned read-only editor must not offer the unlock toggle");
        cut.Instance.IsReadOnly.Should().BeTrue();
        cut.Instance.ReadOnly.Should().BeTrue("the host parameter stays the source of truth");
    }

    [Fact]
    public void HostReadOnly_True_RootElementCarriesTheReadonlyClass()
    {
        using var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, true));

        cut.Find("[data-testid='diagram-editor']").ClassList
            .Should().Contain("tm-diagram-editor--readonly");
    }

    [Fact]
    public void ToggleClick_FlipsInternalState_WithoutWritingTheReadOnlyParameter()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        cut.Instance.ReadOnly.Should().BeFalse(
            "the [Parameter] ReadOnly is never assigned inside the component");
        cut.Instance.IsReadOnly.Should().BeTrue("the toggle flips the internal user state");
        cut.Find("[data-tm-group='readonly'] button").ClassList
            .Should().Contain("tm-toolbar-btn--active", "the toggle reflects the effective state");
        cut.Find("[data-testid='diagram-editor']").ClassList
            .Should().Contain("tm-diagram-editor--readonly");
    }

    [Fact]
    public void ToggleClick_SurvivesParentRerender_WithoutGoingInconsistent()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();
        cut.Instance.IsReadOnly.Should().BeTrue();

        // Parent re-renders with the same ReadOnly=false: the user's local lock persists, so the
        // editor never disagrees with what the toggle shows.
        cut.Render();
        cut.Instance.IsReadOnly.Should().BeTrue();
        cut.Instance.ReadOnly.Should().BeFalse();
        cut.FindAll("[data-tm-group='readonly']").Should().HaveCount(1);
        cut.Find("[data-tm-group='readonly'] button").ClassList
            .Should().Contain("tm-toolbar-btn--active");

        // And the user can still unlock via the same toggle.
        cut.Find("[data-tm-group='readonly'] button").Click();
        cut.Instance.IsReadOnly.Should().BeFalse();
        cut.Instance.ReadOnly.Should().BeFalse();
        cut.Find("[data-tm-group='readonly'] button").ClassList
            .Should().NotContain("tm-toolbar-btn--active");
    }

    [Fact]
    public void EffectiveReadOnly_BlocksUndoRedoKeyboardPaths()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        InvokePrivate(cut.Instance, "HandleQuickInsert", "general.rectangle");
        doc.Nodes.Should().HaveCount(1);

        cut.Find("[data-tm-group='readonly'] button").Click();
        cut.Instance.IsReadOnly.Should().BeTrue();

        // Simulated keyboard undo/redo (Ctrl+Z / Ctrl+Y arrive from diagram-editor.js) — must no-op.
        InvokePrivate(cut.Instance, "OnUndoClicked");
        InvokePrivate(cut.Instance, "OnRedoClicked");
        doc.Nodes.Should().HaveCount(1, "undo must not run in the effective read-only state");
    }

    [Fact]
    public void EffectiveReadOnly_BlocksPaste()
    {
        var doc = CreateDocument();
        DiagramClipboard.Nodes.Clear();
        DiagramClipboard.Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 10, Y = 10, W = 100, H = 50,
        });

        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        // bUnit raises the click event even on a disabled button — the handler must still no-op.
        cut.FindAll("button").First(b => b.GetAttribute("aria-label") == "Paste").Click();
        doc.Nodes.Should().BeEmpty("paste must not insert clipboard nodes in the effective read-only state");
    }

    [Fact]
    public void EffectiveReadOnly_BlocksSqlAndCsvImportDialogs()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        InvokePrivate(cut.Instance, "HandleImportSelect", "sql");
        InvokePrivate(cut.Instance, "HandleImportSelect", "csv");

        cut.FindAll(".tm-diagram-sql-import").Should().BeEmpty();
        cut.FindAll(".tm-diagram-csv-import").Should().BeEmpty();
    }

    [Fact]
    public async Task EffectiveReadOnly_TemplateSelection_DoesNotReplaceTheDocument()
    {
        var doc = CreateDocument();
        doc.Title = "Original";
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        // The gallery still opens (browsing is a read-only feature) but selecting must no-op.
        cut.FindAll("button").First(b => b.TextContent.Contains("Templates")).Click();
        cut.Find(".tm-diagram-template-gallery").Should().NotBeNull();
        cut.Find(".tm-diagram-template-card").Click();

        var create = cut.FindAll("button").First(b => b.TextContent.Contains("Create"));
        await cut.InvokeAsync(() => create.Click());

        cut.Instance.Document.Should().BeSameAs(doc,
            "a read-only editor must not swap to the template document");
        doc.Nodes.Should().BeEmpty();
    }

    [Fact]
    public async Task WritableEditor_TemplateSelection_StillReplacesTheDocument()
    {
        var doc = CreateDocument();
        doc.Title = "Original";
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.FindAll("button").First(b => b.TextContent.Contains("Templates")).Click();
        cut.Find(".tm-diagram-template-card").Click();
        var create = cut.FindAll("button").First(b => b.TextContent.Contains("Create"));
        await cut.InvokeAsync(() => create.Click());

        cut.Instance.Document.Should().NotBeSameAs(doc,
            "the template gallery keeps working when writable");
        cut.Instance.Document!.Title.Should().Be("Read-only Test Template");
    }

    private static DiagramDocument CreateDocument()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        return doc;
    }

    private static DiagramDocument CreateTemplateDocument()
    {
        var doc = new DiagramDocument { Title = "Read-only Test Template" };
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100, Y = 100, W = 120, H = 60,
        });
        return doc;
    }

    private static void InvokePrivate(TmDiagramEditor editor, string methodName, params object?[] args)
    {
        var method = typeof(TmDiagramEditor).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: args.Select(a => a?.GetType() ?? typeof(object)).ToArray(),
            modifiers: null);
        method.Should().NotBeNull($"'{methodName}' must exist");
        try
        {
            var result = method!.Invoke(editor, args);
            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
