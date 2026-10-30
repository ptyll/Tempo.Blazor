using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Signing;

public class TmSigningFieldOverlayTests : LocalizationTestBase
{
    [Fact]
    public void Render_BasicField_RendersRootAndPosition()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(name: "Signer name"))
                      .Add(p => p.Area, CreateArea(x: 0.1, y: 0.2, width: 0.3, height: 0.4)));

        var root = cut.Find(".tm-signing-field");
        root.ClassList.Should().Contain("tm-signing-field-overlay");
        root.GetAttribute("style").Should().Contain("left: 10%");
        root.GetAttribute("style").Should().Contain("top: 20%");
        root.GetAttribute("style").Should().Contain("width: 30%");
        root.GetAttribute("style").Should().Contain("height: 40%");
        root.TextContent.Should().Contain("Signer name");
    }

    [Fact]
    public void Render_LocalizedFieldAndOptionLabels_UsesRequestedCulture()
    {
        var field = CreateField(SigningFieldType.Select, "Country");
        field.Labels.Translations["cs"] = "Země";
        field.Options =
        [
            new SigningFieldOption
            {
                Uuid = "cz",
                Value = "CZ",
                Labels = { Translations = { ["cs"] = "Česká republika" } }
            }
        ];

        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.Culture, "cs-CZ"));

        cut.Find(".tm-signing-field__label").TextContent.Should().Be("Země");
        cut.Find(".tm-signing-field__option").TextContent.Should().Be("Česká republika");
        cut.Find(".tm-signing-field__option").GetAttribute("data-option-uuid").Should().Be("cz");
    }

    [Fact]
    public void Render_SignatureField_RendersTypeIcon()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(SigningFieldType.Signature, "Signature")));

        cut.Find(".tm-signing-field__icon").GetAttribute("data-icon").Should().Be("edit");
    }

    [Fact]
    public void Render_RequiredField_RendersRequiredIndicator()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(required: true)));

        var required = cut.Find(".tm-signing-field__required");
        required.TextContent.Should().Be("*");
        required.GetAttribute("aria-label").Should().Be("Required");
    }

    /// <summary>
    /// CSS contract of the overlay head (phase 3 review round 2 fix): the LABEL + PROMPT pair is a
    /// PLAIN flex pair — the head is NOT an inline-size container (a container has zero intrinsic
    /// width, so labels truncated to stubs no matter how wide the field is) and there is no
    /// width-based prompt hiding left: the runner passes the prompt only for the current field.
    /// The label keeps its natural width (may shrink, never below 6rem while there is room) and
    /// the prompt absorbs the remaining head room with an ellipsis. The prompt paints with the
    /// secondary text token, not an opacity wash. The FIELD ROOT must not be a container either —
    /// root containment wrapped the choice chips letter-by-letter.
    /// </summary>
    [Fact]
    public void FieldOverlay_CssContract_LabelKeepsItsWidth_PromptRidesTheCurrentFieldRule()
    {
        var css = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Signing", "wwwroot", "css", "components", "_signing-field-overlay.css"));

        css.Should().NotContain("container:",
            "neither the head nor any overlay element may be a size container: an inline-size "
            + "container has zero intrinsic width and truncated labels to stubs");
        css.Should().NotContain("@container",
            "there is no width-based prompt hiding — the runner passes the prompt only for the current field");
        css.Should().NotContain("max-width: 60%",
            "the label keeps its natural width instead of a hard cap that truncated it");
        css.Should().Contain("min-width: min(calc(100% - var(--tm-space-16)), 12rem)",
            "the head keeps the room past a 4rem value-area reserve (capped at 12rem) — without that "
            + "floor the head shrank as easily as the value area and the label's own percentage "
            + "floor had nothing to hold on to");
        css.Should().Contain("min-width: min(100%, 6rem)",
            "the label may shrink but never below 6rem while there is room, so wide fields read fully");
        css.Should().Contain("text-overflow: ellipsis",
            "both the label and the prompt ellipsize instead of overflowing");
        css.Should().Contain("color: var(--tm-text-secondary)",
            "the prompt reads through the secondary text token (no opacity wash)");
        css.Should().NotContain("opacity: 0.85");
    }

    [Fact]
    public void Render_ClassAndAdditionalAttributes_AreApplied()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.Class, "custom-field")
                      .AddUnmatched("data-testid", "field"));

        var root = cut.Find("[data-testid='field']");
        root.ClassList.Should().Contain("tm-signing-field");
        root.ClassList.Should().Contain("custom-field");
    }

    [Fact]
    public void RoleColor_RendersSigningRoleColorCustomProperty()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters => parameters
            .Add(p => p.Field, CreateField())
            .Add(p => p.Area, CreateArea())
            .Add(p => p.RoleColor, "#16a34a"));

        var root = cut.Find(".tm-signing-field");
        root.GetAttribute("style").Should().Contain("--tm-signing-role-color:#16a34a");
        root.ClassList.Should().Contain("tm-signing-field--role",
            "the role-coloured presentation derives border and background through color-mix()");
    }

    [Fact]
    public void RoleColor_WithoutValue_RendersNoRoleCustomProperty()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters => parameters
            .Add(p => p.Field, CreateField())
            .Add(p => p.Area, CreateArea()));

        var root = cut.Find(".tm-signing-field");
        root.GetAttribute("style").Should().NotContain("--tm-signing-role-color");
        root.ClassList.Should().NotContain("tm-signing-field--role");
    }

    [Theory]
    [InlineData("#16a34a")]
    [InlineData("#16A34AFF")]
    [InlineData("var(--tm-signing-role-2)")]
    [InlineData("rgb(22, 163, 74)")]
    [InlineData("rgba(22, 163, 74, 0.5)")]
    [InlineData("oklch(0.7 0.15 160)")]
    public void RoleColor_AllowListedValue_RendersRoleCustomProperty(string roleColor)
    {
        var cut = Render<TmSigningFieldOverlay>(parameters => parameters
            .Add(p => p.Field, CreateField())
            .Add(p => p.Area, CreateArea())
            .Add(p => p.RoleColor, roleColor));

        var root = cut.Find(".tm-signing-field");
        root.GetAttribute("style").Should().Contain($"--tm-signing-role-color:{roleColor}");
        root.ClassList.Should().Contain("tm-signing-field--role");
    }

    [Theory]
    [InlineData("red")]
    [InlineData("teal")]
    [InlineData("Fuchsia")]
    public void RoleColor_BasicColourKeyword_IsAllowListed(string roleColor)
    {
        var cut = Render<TmSigningFieldOverlay>(parameters => parameters
            .Add(p => p.Field, CreateField())
            .Add(p => p.Area, CreateArea())
            .Add(p => p.RoleColor, roleColor));

        var root = cut.Find(".tm-signing-field");
        root.GetAttribute("style").Should().Contain($"--tm-signing-role-color:{roleColor}");
        root.ClassList.Should().Contain("tm-signing-field--role");
    }

    [Theory]
    [InlineData("expression(alert(1))")]
    [InlineData("url(javascript:alert(1))")]
    [InlineData("red; } * { background: url(//evil.example) }")]
    [InlineData("var(--not-a-tempo-token)")]
    [InlineData("#123456789")]
    [InlineData("rgb(1;2;3)")]
    [InlineData(";;")]
    [InlineData("darkred")]
    [InlineData("palevioletred")]
    [InlineData("notacolor")]
    public void RoleColor_WithInjectionAttempt_IsDropped(string roleColor)
    {
        // darkred / palevioletred are extended CSS named colours: the sanitizer only admits the
        // basic keywords, so anything outside the closed set rides the inline style never.
        var cut = Render<TmSigningFieldOverlay>(parameters => parameters
            .Add(p => p.Field, CreateField())
            .Add(p => p.Area, CreateArea())
            .Add(p => p.RoleColor, roleColor));

        var root = cut.Find(".tm-signing-field");
        root.GetAttribute("style").Should().NotContain("--tm-signing-role-color");
        root.ClassList.Should().NotContain("tm-signing-field--role");
    }

    [Fact]
    public void Render_StateClassesAndAria_AreApplied()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.Selected, true)
                      .Add(p => p.Focused, true)
                      .Add(p => p.Invalid, true)
                      .Add(p => p.Completed, true)
                      .Add(p => p.ReadOnly, true)
                      .Add(p => p.Disabled, true)
                      .Add(p => p.Draggable, true));

        var root = cut.Find(".tm-signing-field");
        root.ClassList.Should().Contain("tm-signing-field--selected");
        root.ClassList.Should().Contain("tm-signing-field--focused");
        root.ClassList.Should().Contain("tm-signing-field--invalid");
        root.ClassList.Should().Contain("tm-signing-field--completed");
        root.ClassList.Should().Contain("tm-signing-field--read-only");
        root.ClassList.Should().Contain("tm-signing-field--disabled");
        root.ClassList.Should().Contain("tm-signing-field--draggable");
        root.GetAttribute("aria-invalid").Should().Be("true");
        root.GetAttribute("aria-disabled").Should().Be("true");
        root.GetAttribute("data-completed").Should().Be("true");
    }

    [Theory]
    [InlineData(SigningFieldType.Text, "Hello")]
    [InlineData(SigningFieldType.Number, "42")]
    public void Render_TextLikeValue_RendersPreview(SigningFieldType type, string expected)
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(type))
                      .Add(p => p.Value, expected));

        cut.Find(".tm-signing-field__value").TextContent.Should().Be(expected);
    }

    [Fact]
    public void Render_DateValue_RendersFormattedPreview()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(SigningFieldType.Date))
                      .Add(p => p.Value, new DateOnly(2026, 5, 8)));

        cut.Find(".tm-signing-field__value").TextContent.Should().Be("2026-05-08");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Render_CheckboxValue_RendersCheckedState(bool value)
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(SigningFieldType.Checkbox))
                      .Add(p => p.Value, value));

        cut.Find(".tm-signing-field__checkbox")
            .ClassList.Contains("tm-signing-field__checkbox--checked")
            .Should()
            .Be(value);
    }

    [Fact]
    public void Render_RadioField_ChecksAreaOptionUuid()
    {
        var field = CreateChoiceField(SigningFieldType.Radio);
        var area = CreateArea(optionUuid: "option-2");

        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.Area, area));

        cut.Find("[data-option-uuid='option-2']").ClassList.Should().Contain("tm-signing-field__option--checked");
    }

    [Fact]
    public void Render_MultipleField_ChecksSelectedOptions()
    {
        var field = CreateChoiceField(SigningFieldType.Multiple);

        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.Value, new[] { "option-1", "option-3" }));

        cut.Find("[data-option-uuid='option-1']").ClassList.Should().Contain("tm-signing-field__option--checked");
        cut.Find("[data-option-uuid='option-2']").ClassList.Should().NotContain("tm-signing-field__option--checked");
        cut.Find("[data-option-uuid='option-3']").ClassList.Should().Contain("tm-signing-field__option--checked");
    }

    [Fact]
    public void Render_CellsField_SplitsValueIntoCells()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(SigningFieldType.Cells))
                      .Add(p => p.Value, "AB12"));

        cut.FindAll(".tm-signing-field__cell")
            .Select(cell => cell.TextContent)
            .Should()
            .Equal("A", "B", "1", "2");
    }

    [Theory]
    [InlineData(SigningFieldType.Signature)]
    [InlineData(SigningFieldType.Initials)]
    [InlineData(SigningFieldType.Image)]
    public void Render_ImageLikeValue_RendersThumbnail(SigningFieldType type)
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(type))
                      .Add(p => p.Value, "data:image/png;base64,abc"));

        cut.Find("img.tm-signing-field__thumbnail")
            .GetAttribute("src")
            .Should()
            .Be("data:image/png;base64,abc");
    }

    [Theory]
    [InlineData(SigningFieldType.Signature)]
    [InlineData(SigningFieldType.Initials)]
    [InlineData(SigningFieldType.Image)]
    public void Render_ImageLikeValue_WithPlainText_DoesNotRenderBrokenThumbnail(SigningFieldType type)
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(type))
                      .Add(p => p.Value, "option-a"));

        cut.FindAll("img.tm-signing-field__thumbnail").Should().BeEmpty();
        cut.Find(".tm-signing-field__value").TextContent.Should().NotBeEmpty();
    }

    [Fact]
    public void Render_PaymentWithoutValue_RendersPaymentTypeName()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(SigningFieldType.Payment)));

        cut.Find(".tm-signing-field__value").TextContent.Should().Be("Payment");
    }

    [Fact]
    public void Render_StampWithoutValue_RendersPlaceholder()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(SigningFieldType.Stamp)));

        cut.Find(".tm-signing-field__stamp").TextContent.Should().Contain("Stamp");
    }

    [Theory]
    [InlineData(SigningFieldType.Signature)]
    [InlineData(SigningFieldType.Initials)]
    [InlineData(SigningFieldType.Image)]
    [InlineData(SigningFieldType.Stamp)]
    public void Render_ImageAndStampPlaceholdersUseLocalizedLabel(SigningFieldType type)
    {
        var field = CreateField(type, name: "Signature");
        field.Placeholders.Translations["cs"] = "Doplňte podpis";

        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.Culture, "cs-CZ"));

        cut.Markup.Should().Contain("Doplňte podpis");
    }

    [Fact]
    public void Render_Heading_RendersHeadingText()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(SigningFieldType.Heading, title: "Terms")));

        cut.Find(".tm-signing-field__heading").TextContent.Should().Be("Terms");
    }

    [Fact]
    public void Render_Strikethrough_RendersLine()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField(SigningFieldType.Strikethrough)));

        cut.Find(".tm-signing-field__strikethrough").Should().NotBeNull();
    }

    [Fact]
    public void Click_InvokesOnClick()
    {
        TmSigningFieldOverlayPointerEventArgs? captured = null;
        var field = CreateField();

        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.OnClick, EventCallback.Factory.Create<TmSigningFieldOverlayPointerEventArgs>(this, args => captured = args)));

        cut.Find(".tm-signing-field").Click();

        captured.Should().NotBeNull();
        captured!.Field.Should().BeSameAs(field);
    }

    [Fact]
    public void DoubleClick_InvokesOnDoubleClick()
    {
        var invoked = false;

        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.OnDoubleClick, EventCallback.Factory.Create<TmSigningFieldOverlayPointerEventArgs>(this, _ => invoked = true)));

        cut.Find(".tm-signing-field").DoubleClick();

        invoked.Should().BeTrue();
    }

    [Fact]
    public void ContextMenu_InvokesOnContextMenu()
    {
        TmSigningFieldOverlayPointerEventArgs? captured = null;

        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.OnContextMenu, EventCallback.Factory.Create<TmSigningFieldOverlayPointerEventArgs>(this, args => captured = args)));

        cut.Find(".tm-signing-field").ContextMenu(new MouseEventArgs { ClientX = 10 });

        captured.Should().NotBeNull();
        captured!.MouseEventArgs.ClientX.Should().Be(10);
    }

    [Fact]
    public void MouseDown_WhenDraggable_InvokesOnStartMove()
    {
        var invoked = false;

        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.Draggable, true)
                      .Add(p => p.OnStartMove, EventCallback.Factory.Create<TmSigningFieldOverlayPointerEventArgs>(this, _ => invoked = true)));

        cut.Find(".tm-signing-field").MouseDown();

        invoked.Should().BeTrue();
    }

    [Fact]
    public void ResizeHandleMouseDown_InvokesOnStartResize()
    {
        TmSigningFieldOverlayResizeEventArgs? captured = null;

        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.Editable, true)
                      .Add(p => p.OnStartResize, EventCallback.Factory.Create<TmSigningFieldOverlayResizeEventArgs>(this, args => captured = args)));

        cut.Find("[data-handle='SouthEast']").MouseDown();

        captured.Should().NotBeNull();
        captured!.Handle.Should().Be(SigningResizeHandle.SouthEast);
    }

    [Fact]
    public void Render_ResizeHandles_AreRenderedOnlyWhenEditable()
    {
        var notEditable = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.Editable, false));

        notEditable.FindAll(".tm-signing-field__resize-handle").Should().BeEmpty();

        var editable = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateField())
                      .Add(p => p.Editable, true));

        editable.FindAll(".tm-signing-field__resize-handle").Should().HaveCount(8);
    }

    private static SigningField CreateField(
        SigningFieldType type = SigningFieldType.Text,
        string? name = "Field label",
        string? title = null,
        bool required = false)
    {
        return new SigningField
        {
            Uuid = "field-1",
            Name = name,
            Title = title,
            Type = type,
            Required = required
        };
    }

    private static SigningField CreateChoiceField(SigningFieldType type)
    {
        var field = CreateField(type);
        field.Options =
        [
            new SigningFieldOption { Uuid = "option-1", Value = "One" },
            new SigningFieldOption { Uuid = "option-2", Value = "Two" },
            new SigningFieldOption { Uuid = "option-3", Value = "Three" }
        ];
        return field;
    }

    private static SigningFieldArea CreateArea(
        double x = 0.1,
        double y = 0.2,
        double width = 0.3,
        double height = 0.1,
        string? optionUuid = null)
    {
        return new SigningFieldArea
        {
            Uuid = "area-1",
            AttachmentUuid = "attachment-1",
            Page = 0,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            OptionUuid = optionUuid
        };
    }
}
