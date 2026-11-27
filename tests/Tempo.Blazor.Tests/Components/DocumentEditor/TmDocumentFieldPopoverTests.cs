using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>Redesign 146c7e1b, phase 5 (DE-1.6): the form-field popover - one component for content controls and signing fields.</summary>
public sealed class TmDocumentFieldPopoverTests : LocalizationTestBase
{
    private static DocumentFieldPopoverControl Control(
        DocumentContentControlKind kind,
        bool required = false,
        bool locked = false,
        IReadOnlyList<DocumentContentControlItem>? items = null)
        => new()
        {
            ControlId = "cc-1",
            Kind = kind,
            Title = "Signature date",
            IsRequired = required,
            LockContent = locked,
            DateIso = "2025-04-24",
            SelectedValue = "b",
            AssetId = "asset-1",
            Items = items ?? [new DocumentContentControlItem { Value = "a", DisplayText = "Alpha" }, new DocumentContentControlItem { Value = "b", DisplayText = "Beta" }],
        };

    private IRenderedComponent<TmDocumentFieldPopover> RenderControl(DocumentFieldPopoverControl control, Action<ComponentParameterCollectionBuilder<TmDocumentFieldPopover>>? configure = null)
        => Render<TmDocumentFieldPopover>(p =>
        {
            p.Add(x => x.ContentControl, control);
            configure?.Invoke(p);
        });

    [Theory]
    [InlineData(DocumentContentControlKind.Date, "document-canvas-content-control-date", "input")]
    [InlineData(DocumentContentControlKind.DropDown, "document-canvas-content-control-choice", "select")]
    [InlineData(DocumentContentControlKind.ComboBox, "document-canvas-content-control-choice", "select")]
    [InlineData(DocumentContentControlKind.Picture, "document-canvas-content-control-picture", "select")]
    public void EveryKind_GetsItsEditor_AsATmInput(DocumentContentControlKind kind, string testId, string tag)
    {
        var cut = RenderControl(Control(kind), p => p.Add(x => x.ImageAssets, [new DocumentImageAsset { Id = "asset-1", FileName = "logo.png" }]));

        var editor = cut.Find($"[data-testid='{testId}']");
        editor.TagName.ToLowerInvariant().Should().Be(tag);
        editor.ClassList.Should().Contain("tm-input");
        cut.Find("[data-testid='document-canvas-content-control-popover']").GetAttribute("data-control-kind").Should().Be(kind.ToString());
    }

    [Fact]
    public void Date_UsesTheNativeDateInput()
        => RenderControl(Control(DocumentContentControlKind.Date)).Find("[data-testid='document-canvas-content-control-date']").GetAttribute("type").Should().Be("date");

    [Fact]
    public void ComboBox_AddsAFreeTextField_ThatADropDownDoesNotHave()
    {
        RenderControl(Control(DocumentContentControlKind.ComboBox)).FindAll("[data-testid='document-canvas-content-control-combo-text']").Should().ContainSingle();
        RenderControl(Control(DocumentContentControlKind.DropDown)).FindAll("[data-testid='document-canvas-content-control-combo-text']").Should().BeEmpty();
    }

    [Theory]
    [InlineData(DocumentContentControlKind.PlainText)]
    [InlineData(DocumentContentControlKind.RichText)]
    [InlineData(DocumentContentControlKind.Checkbox)]
    [InlineData(DocumentContentControlKind.RepeatingSection)]
    public void MetadataOnlyKinds_ShowTheChipsAndNoEditor(DocumentContentControlKind kind)
    {
        var cut = RenderControl(Control(kind, required: true));

        cut.FindAll("[data-testid^='document-canvas-content-control-'][data-testid$='-date'], .tm-document-field-popover__field").Should().BeEmpty();
        cut.Find("[data-chip='kind']").Should().NotBeNull();
        cut.Find("[data-chip='required']").Should().NotBeNull();
    }

    [Fact]
    public void LockedContent_DisablesEveryEditor()
    {
        foreach (var kind in new[] { DocumentContentControlKind.Date, DocumentContentControlKind.DropDown, DocumentContentControlKind.ComboBox, DocumentContentControlKind.Picture })
        {
            var cut = RenderControl(Control(kind, locked: true), p => p.Add(x => x.ImageAssets, [new DocumentImageAsset { Id = "asset-1", FileName = "logo.png" }]));

            cut.FindAll(".tm-document-field-popover__field .tm-input").Should().OnlyContain(input => input.HasAttribute("disabled"), $"{kind} is locked");
            cut.Find("[data-chip='locked']").Should().NotBeNull();
        }
    }

    [Fact]
    public void MetaChips_Carry_AnIcon_AndTheLocalizedWords()
    {
        var cut = RenderControl(Control(DocumentContentControlKind.Date, required: true, locked: true));

        var chips = cut.FindAll(".tm-document-field-popover__chip");
        chips.Select(c => c.GetAttribute("data-chip")).Should().Equal("kind", "required", "locked");
        chips.Should().OnlyContain(chip => chip.QuerySelector("svg") != null, "every chip has an icon");
        cut.Find("[data-chip='required']").TextContent.Should().Contain("Required");
        cut.Find("[data-chip='locked']").TextContent.Should().Contain("Locked");
        cut.Find("[data-chip='kind']").TextContent.Should().Contain("Date");
    }

    [Fact]
    public void Header_ShowsTheFormFieldEditorTitle_AndTheFieldTitle()
    {
        var cut = RenderControl(Control(DocumentContentControlKind.Date));

        cut.Find("[data-testid='document-canvas-content-control-popover']").GetAttribute("data-floating-kind").Should().Be("content-control");
        cut.Find(".tm-document-floating-panel__header").TextContent.Should().Contain("Signature date").And.Contain("Form field");
        cut.Find("[data-testid='document-canvas-content-control-popover-close']").GetAttribute("aria-label").Should().Be("Close");
    }

    [Fact]
    public async Task Changes_AreImmediate_WithNoOkFooter()
    {
        string? date = null, choice = null;
        var cut = RenderControl(Control(DocumentContentControlKind.Date), p => p
            .Add(x => x.OnDateChanged, EventCallback.Factory.Create<string>(this, v => date = v)));

        await cut.Find("[data-testid='document-canvas-content-control-date']").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "2026-01-02" });
        date.Should().Be("2026-01-02");

        var drop = RenderControl(Control(DocumentContentControlKind.DropDown), p => p
            .Add(x => x.OnChoiceChanged, EventCallback.Factory.Create<string>(this, v => choice = v)));
        await drop.Find("[data-testid='document-canvas-content-control-choice']").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "a" });
        choice.Should().Be("a");

        cut.FindAll(".tm-document-floating-panel__footer button").Should().ContainSingle().Which.TextContent.Trim().Should().Be("Close", "only a visible Close, never OK/Apply");
    }

    [Fact]
    public void CloseButtons_RaiseOnClose()
    {
        var reasons = new List<DocumentFloatingCloseReason>();
        var cut = RenderControl(Control(DocumentContentControlKind.Date), p => p.Add(x => x.OnClose, EventCallback.Factory.Create<DocumentFloatingCloseReason>(this, r => reasons.Add(r))));

        cut.Find("[data-testid='document-canvas-content-control-popover-close']").Click();
        cut.Find("[data-testid='document-field-popover-close']").Click();

        reasons.Should().Equal(DocumentFloatingCloseReason.Button, DocumentFloatingCloseReason.Button);
    }

    [Fact]
    public void AnchorSpan_IsPlacedAtTheFieldRectangle_AndIsPassedAsTheExplicitAnchor()
    {
        var cut = RenderControl(Control(DocumentContentControlKind.Date), p => p.Add(x => x.AnchorRect, new TmDocumentCanvasEngineHost.CanvasEngineViewportRect { Left = 120, Top = 240.5, Width = 80, Height = 18 }));

        var anchor = cut.Find("[data-testid='document-field-popover-anchor']");
        anchor.GetAttribute("style").Should().Contain("left: 120px").And.Contain("top: 240.5px").And.Contain("position: fixed");
        // With no explicit Anchor the core overlay anchors to the panel's previous element sibling - the span.
        cut.Find("[data-testid='document-canvas-content-control-popover']").Should().NotBeNull();
        cut.FindComponent<Tempo.Blazor.Components.DocumentEditor.TmDocumentFloatingPanel>().Instance.Anchor.Id.Should().NotBeNullOrEmpty("the span's reference is passed explicitly");
    }

    // ─── signing field ───────────────────────────────────────────────────────

    private IRenderedComponent<TmDocumentFieldPopover> RenderSigning(Action<ComponentParameterCollectionBuilder<TmDocumentFieldPopover>>? configure = null, bool repeats = false)
        => Render<TmDocumentFieldPopover>(p =>
        {
            p.Add(x => x.SigningField, new TmDocumentCanvasEngineHost.CanvasEngineSigningFieldSelection
            {
                Uuid = "sf-1", FieldType = "signature", Label = "Customer", Required = true, SubmitterUuid = "r1", Repeats = repeats,
            });
            configure?.Invoke(p);
        });

    [Fact]
    public void Signing_RoleSelect_IsRenderedOnlyWhenThereAreRoles()
    {
        RenderSigning().FindAll("[data-testid='document-signing-field-role']").Should().BeEmpty();

        var cut = RenderSigning(p => p.Add(x => x.SigningRoles, [new SigningSubmitterRole { Uuid = "r1", Name = "Customer" }, new SigningSubmitterRole { Uuid = "r2", Name = "Seller" }]));
        cut.Find("[data-testid='document-signing-field-role']").QuerySelectorAll("option").Should().HaveCount(2);
    }

    [Fact]
    public void Signing_ShowsRequiredAndRepeatsChips_WithIcons()
    {
        var cut = RenderSigning(repeats: true);

        cut.Find("[data-chip='required'] svg").Should().NotBeNull();
        cut.Find("[data-chip='repeats'] svg").Should().NotBeNull();
        cut.Find("[data-testid='document-signing-repeats-badge']").TextContent.Should().Contain("Repeats on every page");
        cut.Find("[data-testid='document-canvas-signing-popover']").GetAttribute("data-signing-field-id").Should().Be("sf-1");
    }

    [Fact]
    public async Task Signing_Changes_AreImmediate()
    {
        string? label = null;
        bool? required = null;
        var cut = RenderSigning(p => p
            .Add(x => x.OnSigningLabelChanged, EventCallback.Factory.Create<string>(this, v => label = v))
            .Add(x => x.OnSigningRequiredChanged, EventCallback.Factory.Create<bool>(this, v => required = v)));

        await cut.Find("[data-testid='document-signing-field-label']").ChangeAsync(new ChangeEventArgs { Value = "Buyer" });
        await cut.Find("[data-testid='document-signing-field-required']").ChangeAsync(new ChangeEventArgs { Value = false });

        label.Should().Be("Buyer");
        required.Should().BeFalse();
    }

    [Fact]
    public void Nothing_IsRendered_WithoutAField()
        => Render<TmDocumentFieldPopover>().Markup.Trim().Should().BeEmpty();
}
