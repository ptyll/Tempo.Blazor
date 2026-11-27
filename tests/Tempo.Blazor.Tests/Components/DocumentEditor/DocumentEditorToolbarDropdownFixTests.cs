using System;
using System.Linq;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Fáze 13: nativní &lt;select&gt; v toolbaru nesmí mít @onpointerdown/@onmousedown:preventDefault —
/// preventDefault na pointerdown/mousedown ruší default akci, která otevírá popup selectu,
/// takže dropdown nejde otevřít myší. Tlačítka preventDefault mít musí (drží fokus v canvasu).
/// </summary>
public class DocumentEditorToolbarDropdownFixTests : LocalizationTestBase
{
    [Fact]
    public void FontFamilySelect_DoesNotPreventDefaultOnPointerOrMouseDown()
    {
        var cut = RenderToolbar();

        var select = cut.Find("[data-testid='document-font-family']");
        HasPreventDefault(select, "pointerdown").Should().BeFalse("preventDefault na pointerdown blokuje otevření nativního selectu");
        HasPreventDefault(select, "mousedown").Should().BeFalse("preventDefault na mousedown blokuje otevření nativního selectu");
    }

    [Fact]
    public void FontSizeSelect_DoesNotPreventDefaultOnPointerOrMouseDown()
    {
        var cut = RenderToolbar();

        var select = cut.Find("[data-testid='document-font-size']");
        HasPreventDefault(select, "pointerdown").Should().BeFalse("preventDefault na pointerdown blokuje otevření nativního selectu");
        HasPreventDefault(select, "mousedown").Should().BeFalse("preventDefault na mousedown blokuje otevření nativního selectu");
    }

    [Fact]
    public void BoldButton_StillPreventsDefault_ToKeepCanvasFocus()
    {
        // Pozitivní kontrola detekčního mechanismu: tlačítka preventDefault mít MUSÍ,
        // jinak by klik do toolbaru sebral canvasu fokus.
        var cut = RenderToolbar();

        var button = cut.Find("[data-testid='document-bold']");
        HasPreventDefault(button, "pointerdown").Should().BeTrue("tlačítka drží fokus v canvasu přes preventDefault");
        HasPreventDefault(button, "mousedown").Should().BeTrue("tlačítka drží fokus v canvasu přes preventDefault");
    }

    [Fact]
    public void LineSpacingSelect_ReferencePattern_HasNoPreventDefault()
    {
        // Regresní pojistka vzoru: lineSpacing select preventDefault nikdy neměl a funguje.
        var cut = RenderToolbar();

        var select = cut.Find("[data-testid='document-line-spacing']");
        HasPreventDefault(select, "pointerdown").Should().BeFalse();
        HasPreventDefault(select, "mousedown").Should().BeFalse();
    }

    private IRenderedComponent<TmDocumentEditorToolbar> RenderToolbar(bool showAdvancedCharacterFormatting = false)
    {
        var registry = BuildRegistry("fontFamily", "fontSize", "changeCase", "bold", "lineSpacing");
        return Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.ShowAdvancedCharacterFormatting, showAdvancedCharacterFormatting));
    }

    private static bool HasPreventDefault(IElement element, string eventName)
        => element.Attributes.Any(a =>
            a.Name.Contains("preventdefault", StringComparison.OrdinalIgnoreCase)
            && a.Name.Contains(eventName, StringComparison.OrdinalIgnoreCase));

    private static DocumentEditorCommandRegistry BuildRegistry(params string[] commands)
    {
        var registry = new DocumentEditorCommandRegistry();
        foreach (var name in commands)
        {
            registry.Register(new FuncDocumentEditorCommandEntry(
                name, affectsData: true,
                computeEnabled: _ => true,
                computeValue: _ => null,
                execute: (_, _) => Task.CompletedTask));
        }

        var ctx = new DocumentEditorCommandContext();
        registry.RefreshAllAsync(ctx).GetAwaiter().GetResult();
        return registry;
    }
}
