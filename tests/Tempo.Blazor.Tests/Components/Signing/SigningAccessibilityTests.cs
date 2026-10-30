using Bunit.Rendering;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

public class SigningAccessibilityTests : LocalizationTestBase
{
    [Fact]
    public void IconOnlyButtons_HaveAccessibleNames()
    {
        var fieldEditor = Render<TmSigningFieldEditorPanel>(parameters =>
            parameters.Add(p => p.Field, CreateChoiceField()));
        AssertIconOnlyButtonsHaveAccessibleNames(fieldEditor);

        var roles = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles()));
        AssertIconOnlyButtonsHaveAccessibleNames(roles);

        var conditions = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateConditionFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [
                          new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.NotEmpty },
                          new SigningFieldCondition { FieldUuid = "consent", Action = SigningConditionAction.Checked }
                      ]));
        AssertIconOnlyButtonsHaveAccessibleNames(conditions);
    }

    [Fact]
    public void FieldOverlay_InvalidState_UsesAriaInvalid()
    {
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateTextField())
                      .Add(p => p.Invalid, true));

        cut.Find(".tm-signing-field").GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void FieldOverlay_LocalizedLabelIsAccessibleName()
    {
        var field = CreateTextField();
        field.Labels.Translations["cs"] = "Celé jméno";

        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.Culture, "cs-CZ"));

        cut.Find(".tm-signing-field").GetAttribute("aria-label").Should().Be("Celé jméno");
    }

    [Fact]
    public void TextStep_LocalizedValidationMessageIsDescribedByInput()
    {
        var field = CreateTextField();
        field.Required = true;
        field.Validation = new SigningFieldValidation
        {
            Messages = { Translations = { ["cs"] = "Jméno je povinné." } }
        };

        var cut = Render<TmSigningTextStep>(parameters =>
            parameters.Add(p => p.Field, field)
                      .Add(p => p.Culture, "cs-CZ"));

        cut.Find(".tm-signing-text-step__input").Change(string.Empty);

        var input = cut.Find(".tm-signing-text-step__input");
        var describedBy = input.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{describedBy}").TextContent.Should().Be("Jméno je povinné.");
    }

    [Fact]
    public void FieldOverlay_EnterKey_InvokesSelection()
    {
        var invoked = false;
        var cut = Render<TmSigningFieldOverlay>(parameters =>
            parameters.Add(p => p.Field, CreateTextField())
                      .Add(p => p.OnClick, EventCallback.Factory.Create<TmSigningFieldOverlayPointerEventArgs>(this, _ => invoked = true)));

        cut.Find(".tm-signing-field").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        invoked.Should().BeTrue();
    }

    [Fact]
    public void RunnerProgress_HasAriaLabel()
    {
        var cut = Render<TmSigningFormRunner>(parameters =>
            parameters.Add(p => p.Fields, [CreateTextField()]));

        cut.Find(".tm-signing-form-runner__progress")
            .GetAttribute("aria-label")
            .Should()
            .Contain("Step 1 of 1");
    }

    /// <summary>
    /// The step shell title is the visible label of the step; every input of every step type must
    /// carry an accessible name — an <c>aria-label</c>, an <c>aria-labelledby</c> pointing at an
    /// EXISTING element (the title id), or a wrapping <c>label</c>. The runner is an end-user
    /// surface: all of its inputs are reachable through <c>getByLabel</c>.
    /// </summary>
    [Theory]
    [InlineData(SigningFieldType.Text)]
    [InlineData(SigningFieldType.Number)]
    [InlineData(SigningFieldType.Date)]
    [InlineData(SigningFieldType.Select)]
    [InlineData(SigningFieldType.Checkbox)]
    [InlineData(SigningFieldType.Multiple)]
    [InlineData(SigningFieldType.Image)]
    [InlineData(SigningFieldType.Signature)]
    [InlineData(SigningFieldType.Phone)]
    [InlineData(SigningFieldType.Kba)]
    public void Step_EveryInput_HasAccessibleName_AriaLabelledByExistingTitleId(SigningFieldType type)
    {
        var inputs = RenderStepInputsOfType(type);
        inputs.Should().NotBeEmpty($"{type} steps render form controls");

        foreach (var input in inputs)
        {
            HasAccessibleName(input).Should().BeTrue(
                $"the {type} step's <{input.TagName.ToLowerInvariant()}> must have an accessible name; markup: {input.OuterHtml}");
        }
    }

    /// <summary>The radio group is a real group: a <c>fieldset</c> with a legend, and radio names unique per instance.</summary>
    [Fact]
    public void ChoiceStep_UsesFieldsetLegend_AndUniqueRadioName()
    {
        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceFieldOfType(SigningFieldType.Radio)));

        var fieldset = cut.Find("fieldset.tm-signing-choice-step__group");
        fieldset.QuerySelector("legend").Should().NotBeNull("the group's accessible name comes from the legend");
        fieldset.QuerySelector("legend")!.TextContent.Should().NotBeEmpty();

        var radios = cut.FindAll("input[type='radio']");
        radios.Should().HaveCount(2);
        radios.Should().OnlyContain(radio => !string.IsNullOrWhiteSpace(radio.GetAttribute("name")));
        radios.Select(radio => radio.GetAttribute("name")).Distinct().Should().ContainSingle(
            "radios of ONE step share a name so arrow keys move within the group");
    }

    /// <summary>
    /// The checkbox-group hint is part of the GROUP's description: the hint carries an id and the
    /// fieldset references it (plus description / validation ids) through aria-describedby.
    /// </summary>
    [Fact]
    public void ChoiceStep_CheckboxGroupHint_IsPartOfTheGroupDescription()
    {
        var first = CreateTextField();
        first.Type = SigningFieldType.Checkbox;
        first.Name = "First consent";
        var second = CreateTextField();
        second.Type = SigningFieldType.Checkbox;
        second.Name = "Second consent";
        var group = CreateChoiceFieldOfType(SigningFieldType.Checkbox);
        group.Name = "Consents";

        var cut = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, group)
            .Add(p => p.Fields, [first, second]));

        var fieldset = cut.Find("fieldset.tm-signing-choice-step__group");
        var hintId = cut.Find(".tm-signing-choice-step__hint").GetAttribute("id");
        hintId.Should().NotBeNullOrWhiteSpace("the hint is referenced by the group, so it needs an id");
        fieldset.GetAttribute("aria-describedby")!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().Contain(hintId!);
    }

    /// <summary>Two choice steps with the SAME field must not share a radio name (B25 follow-up: ids/names per instance).</summary>
    [Fact]
    public void ChoiceStep_RadioName_IsUniquePerInstance()
    {
        var first = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceFieldOfType(SigningFieldType.Radio)));
        var second = Render<TmSigningChoiceStep>(parameters => parameters
            .Add(p => p.Field, CreateChoiceFieldOfType(SigningFieldType.Radio)));

        var firstName = first.Find("input[type='radio']").GetAttribute("name");
        var secondName = second.Find("input[type='radio']").GetAttribute("name");

        firstName.Should().NotBe(secondName,
            "two rendered radio groups must not merge into one browser-level group");
    }

    /// <summary>Phone inputs (country, number, OTP) are named — placeholders are not accessible names.</summary>
    [Fact]
    public void PhoneStep_InputsHaveLabels()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Uuid = "phone", Name = "Phone", Type = SigningFieldType.Phone, Required = true })
            .Add(p => p.CodeSent, true));

        var country = cut.Find("select.tm-signing-phone-step__country");
        var phone = cut.Find("input.tm-signing-phone-step__input");
        var otp = cut.Find("input.tm-signing-phone-step__otp");

        HasAccessibleName(country).Should().BeTrue($"country select needs a name; markup: {country.OuterHtml}");
        HasAccessibleName(phone).Should().BeTrue($"phone input needs a name; markup: {phone.OuterHtml}");
        HasAccessibleName(otp).Should().BeTrue($"OTP input needs a name; markup: {otp.OuterHtml}");
    }

    [Fact]
    public void SigningCss_UsesDesignTokensInsteadOfHardcodedPalette()
    {
        var root = FindRepositoryRoot();
        var cssDirectory = Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "css", "components");
        var signingCssFileNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "_audit-trail-viewer.css",
            "_condition-builder.css",
            "_document-page-viewer.css",
            "_formula-builder.css",
            "_pdf-signature-verification.css",
            "_pdf-template-designer.css",
            "_recipient-role-editor.css",
            "_share-link-panel.css",
            "_signature-capture.css",
            "_signing-completion-panel.css",
            "_signing-field-editor-panel.css",
            "_signing-field-overlay.css",
            "_signing-form-runner.css",
            "_signing-step-shell.css",
            "_submission-status-timeline.css"
        };
        var signingCssFiles = Directory.GetFiles(cssDirectory, "*.css")
            .Where(path => signingCssFileNames.Contains(Path.GetFileName(path)))
            .ToArray();

        var hardcodedColors = signingCssFiles
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { Path = path, Line = line, Number = index + 1 }))
            .Where(item => item.Line.Contains('#')
                || item.Line.Contains("rgb(", StringComparison.OrdinalIgnoreCase)
                || item.Line.Contains("hsl(", StringComparison.OrdinalIgnoreCase))
            .Select(item => $"{Path.GetFileName(item.Path)}:{item.Number}: {item.Line.Trim()}")
            .ToArray();

        hardcodedColors.Should().BeEmpty("signing CSS should stay token-driven and avoid a one-note hardcoded palette");
    }

    private static void AssertIconOnlyButtonsHaveAccessibleNames<TComponent>(IRenderedComponent<TComponent> fragment)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        var buttons = fragment.FindAll("button")
            .Where(button => string.IsNullOrWhiteSpace(button.TextContent)
                && button.QuerySelector("svg, .tm-icon") is not null);

        foreach (var button in buttons)
        {
            var accessibleName = button.GetAttribute("aria-label")
                ?? button.GetAttribute("title")
                ?? button.GetAttribute("aria-labelledby");

            accessibleName.Should().NotBeNullOrWhiteSpace(button.OuterHtml);
        }
    }

    /// <summary>
    /// Whether a form control resolves an accessible name: <c>aria-label</c>, an
    /// <c>aria-labelledby</c> whose every referenced id EXISTS in the fragment, or a wrapping
    /// <c>&lt;label&gt;</c> with content.
    /// </summary>
    private static bool HasAccessibleName(AngleSharp.Dom.IElement input)
    {
        var ariaLabel = input.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
        {
            return true;
        }

        var labelledBy = input.GetAttribute("aria-labelledby");
        if (!string.IsNullOrWhiteSpace(labelledBy))
        {
            return labelledBy
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(id => FindElementById(input, id)?.TextContent.Trim().Length > 0);
        }

        if (input.TagName.Equals("input", StringComparison.OrdinalIgnoreCase)
            && input.GetAttribute("type")?.Equals("hidden", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return input.Closest("label")?.TextContent.Trim().Length > 0;
    }

    /// <summary>
    /// Resolves an HTML id from the fragment the input renders in: the document's id index first,
    /// then a querySelector fallback (bUnit's parsed tree can miss ids on patched nodes).
    /// </summary>
    private static AngleSharp.Dom.IElement? FindElementById(AngleSharp.Dom.IElement input, string id)
    {
        var byId = input.Owner?.GetElementById(id);
        if (byId is not null)
        {
            return byId;
        }

        var root = input;
        while (root.ParentElement is not null)
        {
            root = root.ParentElement;
        }

        return root.QuerySelector($"[id='{id}']");
    }

    /// <summary>Renders one signing step of the requested type with a labeled field and returns its form controls.</summary>
    private IReadOnlyList<AngleSharp.Dom.IElement> RenderStepInputsOfType(SigningFieldType type)
    {
        var field = new SigningField
        {
            Uuid = $"field-{type.ToString().ToLowerInvariant()}",
            Name = $"{type} field",
            Type = type,
            Required = false
        };

        return type switch
        {
            SigningFieldType.Text => Render<TmSigningTextStep>(parameters => parameters
                .Add(p => p.Field, field)).FindAll("input, select, textarea"),
            SigningFieldType.Number => Render<TmSigningNumberStep>(parameters => parameters
                .Add(p => p.Field, field)).FindAll("input, select, textarea"),
            SigningFieldType.Date => Render<TmSigningDateStep>(parameters => parameters
                .Add(p => p.Field, field)).FindAll("input, select, textarea"),
            SigningFieldType.Select => Render<TmSigningChoiceStep>(parameters =>
            {
                parameters.Add(p => p.Field, CreateChoiceFieldOfType(SigningFieldType.Select));
            }).FindAll("input, select, textarea"),
            SigningFieldType.Checkbox => Render<TmSigningChoiceStep>(parameters => parameters
                .Add(p => p.Field, field)).FindAll("input, select, textarea"),
            SigningFieldType.Multiple => Render<TmSigningChoiceStep>(parameters =>
            {
                parameters.Add(p => p.Field, CreateChoiceFieldOfType(SigningFieldType.Multiple));
            }).FindAll("input, select, textarea"),
            SigningFieldType.Image => Render<TmSigningAttachmentStep>(parameters => parameters
                .Add(p => p.Field, field)).FindAll("input, select, textarea"),
            SigningFieldType.Signature => Render<TmSigningSignatureStep>(parameters => parameters
                .Add(p => p.Field, field)).FindAll("input, select, textarea"),
            SigningFieldType.Phone => Render<TmSigningPhoneStep>(parameters => parameters
                .Add(p => p.Field, field)).FindAll("input, select, textarea"),
            SigningFieldType.Kba => Render<TmSigningExternalStep>(parameters =>
            {
                parameters.Add(p => p.Field, field);
                parameters.Add(p => p.Questions, new[] { "First pet?", "Mothers maiden name?" });
            }).FindAll("input, select, textarea"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private static SigningField CreateChoiceFieldOfType(SigningFieldType type)
    {
        var field = CreateTextField();
        field.Type = type;
        field.Options =
        [
            new SigningFieldOption { Uuid = "option-a", Value = "Email" },
            new SigningFieldOption { Uuid = "option-b", Value = "Paper" }
        ];

        return field;
    }

    private static SigningField CreateTextField()
    {
        return new SigningField
        {
            Uuid = "name",
            Name = "Full name",
            Type = SigningFieldType.Text,
            Areas =
            [
                new SigningFieldArea
                {
                    AttachmentUuid = "doc",
                    Page = 0,
                    X = 0.1,
                    Y = 0.1,
                    Width = 0.3,
                    Height = 0.05
                }
            ]
        };
    }

    private static SigningField CreateChoiceField()
    {
        var field = CreateTextField();
        field.Type = SigningFieldType.Radio;
        field.Options =
        [
            new SigningFieldOption { Uuid = "option-a", Value = "Email" },
            new SigningFieldOption { Uuid = "option-b", Value = "Paper" }
        ];

        return field;
    }

    private static IReadOnlyList<SigningSubmitterRole> CreateRoles()
    {
        return
        [
            new SigningSubmitterRole
            {
                Uuid = "role-a",
                Name = "Signer A",
                Order = 0
            },
            new SigningSubmitterRole
            {
                Uuid = "role-b",
                Name = "Signer B",
                Order = 1
            }
        ];
    }

    private static IReadOnlyList<SigningField> CreateConditionFields()
    {
        return
        [
            new SigningField { Uuid = "target", Name = "Target", Type = SigningFieldType.Text },
            new SigningField { Uuid = "name", Name = "Name", Type = SigningFieldType.Text },
            new SigningField { Uuid = "consent", Name = "Consent", Type = SigningFieldType.Checkbox }
        ];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
