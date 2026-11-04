using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Plan 3e96f6d8, phase 6 — <c>TmPromptLibrary</c> (Tempo.Blazor.AI): a library of prompt templates with
/// <c>{{variable}}</c> placeholders. The list is searchable and filtered by the host's categories, a picked
/// template opens a detail with a field per variable (a text box, a text area or a select), the preview is the
/// prompt assembled from what the reader typed, and "Use" is offered only while that prompt is complete — the
/// payload is a <see cref="PromptTemplateUse"/> and the component never sends anything itself.
/// <para>
/// The library is render-mode agnostic and contains no string parsing: every fact about the placeholders comes
/// from <see cref="PromptTemplateParser"/>, which has its own unit tests.
/// </para>
/// </summary>
public class TmPromptLibraryTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private PromptTemplateUse? _used;
    private string? _selected;
    private IReadOnlyDictionary<string, string?>? _values;
    private int _createCalls;
    private PromptTemplate? _edited;
    private PromptTemplate? _deleted;

    private int FocusCalls() => JSInterop.Invocations.Count(invocation => invocation.Identifier == FocusInvocation);

    private IReadOnlyList<string?> FocusTargets() => JSInterop.Invocations
        .Where(invocation => invocation.Identifier == FocusInvocation)
        .Select(invocation => ((ElementReference)invocation.Arguments[0]!).Id)
        .ToList();

    /// <summary>Four templates over two categories: every variable kind, a default, an optional variable, and a
    /// template with no category at all.</summary>
    private static PromptTemplate[] Templates() =>
    [
        new("brief", "Brief a topic", "Write a brief about {{topic}} for {{audience}} in a {{tone}} tone.")
        {
            Category = "Writing",
            Description = "Turns a topic into a short brief.",
            Variables =
            [
                new PromptVariable("topic", "Topic"),
                new PromptVariable("audience", "Audience") { IsRequired = false, DefaultValue = "general readers" },
                new PromptVariable("tone", "Tone")
                {
                    Kind = PromptVariableKind.Select,
                    Options = ["formal", "casual"],
                    DefaultValue = "formal",
                },
            ],
        },
        new("review", "Review a change", "Review this diff:\n{{diff}}")
        {
            Category = "Engineering",
            Description = "Reads a diff and lists the risks.",
            Variables = [new PromptVariable("diff", "Diff") { Kind = PromptVariableKind.Multiline }],
        },
        new("uvitani", "Uvítání", "Napiš uvítání pro {{jméno}}.") { Category = "Writing" },
        new("plain", "Plain prompt", "Hello {{name}}. Use {{{{braces}}}} literally."),
    ];

    /// <summary>The knobs a test overrides; every field has the value the default render uses.</summary>
    private sealed class Options
    {
        public IReadOnlyList<PromptTemplate>? Templates { get; set; } = TmPromptLibraryTests.Templates();
        public string? SelectedTemplateId { get; set; }
        public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Desktop;
        public string? Title { get; set; }
        public int HeadingLevel { get; set; } = 3;
        public IReadOnlyDictionary<string, string?>? Values { get; set; }
        public bool BindUse { get; set; } = true;
        public bool BindCreate { get; set; } = true;
        public bool BindEdit { get; set; } = true;
        public bool BindDelete { get; set; } = true;
    }

    private IRenderedComponent<TmPromptLibrary> RenderLibrary(Action<Options>? configure = null)
    {
        var options = new Options();
        configure?.Invoke(options);

        return Render<TmPromptLibrary>(parameters =>
        {
            parameters.Add(component => component.Templates, options.Templates);
            parameters.Add(component => component.LayoutMode, options.LayoutMode);
            parameters.Add(component => component.SelectedTemplateId, options.SelectedTemplateId);
            parameters.Add(component => component.SelectedTemplateIdChanged, value => _selected = value);
            parameters.Add(component => component.Values, options.Values);
            parameters.Add(component => component.ValuesChanged, value => _values = value);
            parameters.Add(component => component.Title, options.Title);
            parameters.Add(component => component.HeadingLevel, options.HeadingLevel);

            if (options.BindUse)
            {
                parameters.Add(component => component.OnUse, use => _used = use);
            }

            if (options.BindCreate)
            {
                parameters.Add(component => component.OnCreate, () => _createCalls++);
            }

            if (options.BindEdit)
            {
                parameters.Add(component => component.OnEdit, template => _edited = template);
            }

            if (options.BindDelete)
            {
                parameters.Add(component => component.OnDelete, template => _deleted = template);
            }
        });
    }

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<TmPromptLibrary> cut)
        => cut.FindAll(".tm-prompt-library__row").ToList();

    private static IReadOnlyList<string> RowIds(IRenderedComponent<TmPromptLibrary> cut)
        => Rows(cut).Select(row => row.GetAttribute("data-template-id")!).ToList();

    private static IElement Row(IRenderedComponent<TmPromptLibrary> cut, string id)
        => cut.Find($".tm-prompt-library__row[data-template-id='{id}']");

    private static IElement Search(IRenderedComponent<TmPromptLibrary> cut)
        => cut.Find("[data-testid='prompt-library-search']");

    private static string UncategorisedValue() => TmPromptLibrary.UncategorisedCategoryValue;

    private static IElement Variable(IRenderedComponent<TmPromptLibrary> cut, string name)
        => cut.Find($"[data-testid='prompt-library-variable'][data-variable='{name}']");

    private static string Preview(IRenderedComponent<TmPromptLibrary> cut)
        => cut.Find("[data-testid='prompt-library-preview']").TextContent;

    private static IReadOnlyList<string> AnnouncedSentences(IRenderedComponent<TmPromptLibrary> cut)
        => [cut.Find("[data-testid='prompt-library-announcement']").TextContent];

    // ── The list ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheList_ShowsEveryTemplate_AsOneRowPerTemplate()
    {
        var cut = RenderLibrary();

        RowIds(cut).Should().Equal("brief", "review", "uvitani", "plain");
        Row(cut, "brief").QuerySelector(".tm-prompt-library__row-title")!.TextContent.Should().Be("Brief a topic");
        Row(cut, "brief").TextContent.Should().Contain("Writing");
        Row(cut, "brief").TextContent.Should().Contain("Turns a topic into a short brief.");
    }

    [Fact]
    public void TheList_IsASemanticList_OfNativeButtons()
    {
        var cut = RenderLibrary();

        cut.Find("ul.tm-prompt-library__list").Should().NotBeNull();
        Rows(cut).Should().OnlyContain(row => row.TagName == "BUTTON" && row.GetAttribute("type") == "button");
    }

    [Fact]
    public void TheSelectedTemplate_IsMarkedAsCurrent_AndNamedByItsOwnText()
    {
        var cut = RenderLibrary(options => options.SelectedTemplateId = "review");

        Row(cut, "review").GetAttribute("aria-current").Should().Be("true");
        Row(cut, "brief").GetAttribute("aria-current").Should().BeNull();
        Row(cut, "review").GetAttribute("aria-current").Should().NotBe("page",
            "the row is one option in a library, not the page the reader is on");
    }

    [Fact]
    public void TheFirstTemplateIsSelected_SoTheDetailPaneIsNeverEmpty()
    {
        var cut = RenderLibrary();

        _selected.Should().Be("brief", "the library reports the selection it made, so a host binding stays in sync");
        cut.Find("[data-testid='prompt-library-detail']").Should().NotBeNull();
    }

    [Fact]
    public void ClickingARow_SelectsThatTemplate_AndRaisesTheBoundSelection()
    {
        var cut = RenderLibrary();

        Row(cut, "review").Click();

        _selected.Should().Be("review");
        cut.Find("[data-testid='prompt-library-detail'] .tm-prompt-library__detail-title").TextContent
            .Should().Be("Review a change");
    }

    [Fact]
    public void TheHostSelection_IsAdopted_WithoutTheComponentFightingIt()
    {
        var cut = RenderLibrary(options => options.SelectedTemplateId = "uvitani");

        cut.Find("[data-testid='prompt-library-detail'] .tm-prompt-library__detail-title").TextContent
            .Should().Be("Uvítání");
        Row(cut, "uvitani").GetAttribute("aria-current").Should().Be("true");
    }

    [Fact]
    public void AnEmptyLibrary_RendersTheLocalizedEmptyState_AndNoDetail()
    {
        var cut = RenderLibrary(options => options.Templates = []);

        cut.Find("[data-testid='prompt-library-empty']").TextContent.Should().Be("No prompt templates yet.");
        cut.FindAll("[data-testid='prompt-library-detail']").Should().BeEmpty();
        Rows(cut).Should().BeEmpty();
    }

    [Fact]
    public void ANullTemplateList_IsTreatedAsEmpty()
    {
        var cut = RenderLibrary(options => options.Templates = null);

        cut.Find("[data-testid='prompt-library-empty']").Should().NotBeNull();
    }

    // ── Search and categories ────────────────────────────────────────────────

    [Fact]
    public void Search_FiltersByTitle_IgnoringCaseAndAccents()
    {
        var cut = RenderLibrary();

        Search(cut).Input("uvitani");

        RowIds(cut).Should().Equal("uvitani");
    }

    [Fact]
    public void Search_FiltersByDescriptionAndCategory()
    {
        var cut = RenderLibrary();

        Search(cut).Input("risks");
        RowIds(cut).Should().Equal("review");

        Search(cut).Input("writing");
        RowIds(cut).Should().Equal("brief", "uvitani");
    }

    [Fact]
    public void Search_ThatMatchesNothing_RendersTheLocalizedNoResultsState_WithTheTerm()
    {
        var cut = RenderLibrary();

        Search(cut).Input("zzz");

        Rows(cut).Should().BeEmpty();
        cut.Find("[data-testid='prompt-library-no-results']").TextContent
            .Should().Contain("zzz").And.Contain("No templates match");
    }

    [Fact]
    public void TheSearchInput_IsNamedByItsLocalizedPlaceholder()
    {
        var cut = RenderLibrary();

        Search(cut).GetAttribute("aria-label").Should().Be("Search templates");
    }

    [Fact]
    public void TheCategories_OfferAllAndEveryHostCategory_AndFilterTheList()
    {
        var cut = RenderLibrary();

        var categories = cut.FindAll("[data-testid='prompt-library-categories'] [role='radio']").ToList();
        categories.Select(option => option.GetAttribute("data-value")).Should()
            .Equal(string.Empty, "Writing", "Engineering", UncategorisedValue());
        categories[0].TextContent.Should().Contain("All");

        cut.Find("[role='radio'][data-value='Engineering']").Click();
        RowIds(cut).Should().Equal("review");

        cut.Find("[role='radio'][data-value='']").Click();
        RowIds(cut).Should().HaveCount(4);
    }

    [Fact]
    public void TheUncategorisedCategory_OnlyExistsWhenATemplateHasNoCategory()
    {
        var cut = RenderLibrary(options => options.Templates = Templates().Where(template => template.Category is not null).ToArray());

        cut.FindAll("[role='radio']").Select(option => option.GetAttribute("data-value"))
            .Should().NotContain(UncategorisedValue());
    }

    [Fact]
    public void TheUncategorisedCategory_ShowsOnlyTheTemplatesWithoutACategory()
    {
        var cut = RenderLibrary();

        cut.Find($"[role='radio'][data-value='{UncategorisedValue()}']").Click();

        RowIds(cut).Should().Equal("plain");
        Row(cut, "plain").TextContent.Should().Contain("Uncategorised");
    }

    // ── The detail form ──────────────────────────────────────────────────────

    [Fact]
    public void TheDetail_RendersOneFieldPerVariable_WithTheControlItsKindAsksFor()
    {
        var cut = RenderLibrary();

        Variable(cut, "topic").TagName.Should().Be("INPUT");
        Variable(cut, "audience").TagName.Should().Be("INPUT");
        Variable(cut, "tone").TagName.Should().Be("SELECT");

        cut.Find("[data-testid='prompt-library-detail']").TextContent.Should()
            .Contain("Topic").And.Contain("Audience").And.Contain("Tone");
    }

    [Fact]
    public void AMultilineVariable_RendersATextArea()
    {
        var cut = RenderLibrary(options => options.SelectedTemplateId = "review");

        Variable(cut, "diff").TagName.Should().Be("TEXTAREA");
    }

    [Fact]
    public void ASelectVariable_OffersExactlyTheDeclaredOptions()
    {
        var cut = RenderLibrary();

        var options = Variable(cut, "tone").QuerySelectorAll("option").Select(option => option.GetAttribute("value")).ToList();

        options.Should().Contain(["formal", "casual"]);
    }

    [Fact]
    public void ADerivedVariable_IsRenderedWithoutAnExplicitDeclaration()
    {
        var cut = RenderLibrary(options => options.SelectedTemplateId = "uvitani");

        Variable(cut, "jméno").Should().NotBeNull();
        cut.Find("[data-testid='prompt-library-detail']").TextContent.Should().Contain("jméno");
    }

    [Fact]
    public void TheDefaultValue_PreFillsTheFieldAndThePreview()
    {
        var cut = RenderLibrary();

        Variable(cut, "audience").GetAttribute("value").Should().Be("general readers");
        Preview(cut).Should().Contain("general readers").And.Contain("formal");
    }

    [Fact]
    public void TypingAValue_UpdatesThePreviewImmediately()
    {
        var cut = RenderLibrary();

        Variable(cut, "topic").Input("Quantum taxes");

        Preview(cut).Should().Contain("Quantum taxes");
        _values.Should().ContainKey("topic").WhoseValue.Should().Be("Quantum taxes",
            "the draft is reported so a host can persist it");
    }

    [Fact]
    public void AnUnfilledVariable_IsMarkedInThePreview()
    {
        var cut = RenderLibrary();

        var token = cut.FindAll("[data-testid='prompt-library-preview'] mark").Cast<IElement>()
            .Single(mark => mark.TextContent.Contains("{{topic}}"));

        token.ClassList.Should().Contain("tm-prompt-library__token--empty");
        token.TextContent.Should().Contain("Unfilled");
        token.TextContent.Should().Contain("{{topic}}");
    }

    [Fact]
    public void AFilledVariable_IsMarkedAsATokenWithoutTheEmptyModifier()
    {
        var cut = RenderLibrary();

        Variable(cut, "topic").Input("Quantum taxes");

        var marks = cut.FindAll("[data-testid='prompt-library-preview'] mark").Cast<IElement>().ToList();
        marks.Should().Contain(mark => mark.TextContent == "Quantum taxes");
        marks.Should().NotContain(mark => mark.TextContent.Contains("Quantum taxes")
            && mark.ClassList.Contains("tm-prompt-library__token--empty"));
    }

    [Fact]
    public void ThePreview_ShowsAnEscapedPairAsLiteralBraces()
    {
        var cut = RenderLibrary(options => options.SelectedTemplateId = "plain");

        Preview(cut).Should().Contain("{{braces}}");
        cut.FindAll("[data-testid='prompt-library-preview'] mark").Cast<IElement>()
            .Should().NotContain(mark => mark.TextContent.Contains("braces"),
                "the escape is literal text, not a variable");
    }

    [Fact]
    public void SwitchingTemplates_ResetsTheDraft_SoValuesNeverLeakBetweenPrompts()
    {
        var cut = RenderLibrary();

        Variable(cut, "topic").Input("Quantum taxes");
        Row(cut, "review").Click();

        Variable(cut, "diff").GetAttribute("value").Should().BeNullOrEmpty();
        cut.FindAll("[data-variable='topic']").Should().BeEmpty();
        Preview(cut).Should().NotContain("Quantum taxes");
    }

    [Fact]
    public void SwitchingTemplates_ReportsTheResetValues_SoTheHostDraftFollowsTheSelection()
    {
        var cut = RenderLibrary();

        Variable(cut, "topic").Input("Quantum taxes");
        _values.Should().ContainKey("topic");

        Row(cut, "review").Click();

        _values.Should().NotBeNull();
        _values!.Keys.Should().BeEquivalentTo(["diff"],
            "the host keeps a draft per template, so the reset has to reach it — the old template's names are gone");
        _values["diff"].Should().BeNullOrEmpty();
    }

    /// <summary>
    /// Applies a host-driven selection the way a host does — bUnit 2 dropped <c>SetParametersAndRender</c>, so the
    /// same instance is fed a partial view through <c>SetParametersAsync</c> (every other parameter keeps its value).
    /// </summary>
    private static Task SetSelectionAsync(IRenderedComponent<TmPromptLibrary> cut, string? templateId)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmPromptLibrary.SelectedTemplateId)] = templateId })));

    [Fact]
    public async Task TheHostChangingTheSelection_AlsoReportsTheResetValues()
    {
        var cut = RenderLibrary();

        Variable(cut, "topic").Input("Quantum taxes");

        await SetSelectionAsync(cut, "uvitani");

        cut.WaitForAssertion(() => _values.Should().NotBeNull());
        _values!.Keys.Should().BeEquivalentTo(["jméno"],
            "the reset that follows a host-driven selection change is reported too, not only a row pick");
        _values["jméno"].Should().BeNullOrEmpty();
    }

    /// <summary>
    /// Applies a host-driven selection and (optionally) a draft for it in the SAME pass — the order the two parameters
    /// are assigned in is the order of the parent's markup, so both orders have to behave.
    /// </summary>
    private static Task SetSelectionWithDraftAsync(
        IRenderedComponent<TmPromptLibrary> cut,
        string? templateId,
        IReadOnlyDictionary<string, string?>? draft,
        bool selectionFirst)
    {
        var view = new Dictionary<string, object?>();
        if (selectionFirst)
        {
            view[nameof(TmPromptLibrary.SelectedTemplateId)] = templateId;
            view[nameof(TmPromptLibrary.Values)] = draft;
        }
        else
        {
            view[nameof(TmPromptLibrary.Values)] = draft;
            view[nameof(TmPromptLibrary.SelectedTemplateId)] = templateId;
        }

        return cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(view)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AHostSuppliedDraftForTheSelectionItNames_IsKeptAndNotReportedAsAReset(bool selectionFirst)
    {
        var cut = RenderLibrary();

        Variable(cut, "topic").Input("Quantum taxes");
        _values = null;

        await SetSelectionWithDraftAsync(
            cut,
            "uvitani",
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["jméno"] = "Adéla" },
            selectionFirst);

        Variable(cut, "jméno").GetAttribute("value").Should().Be("Adéla");
        Preview(cut).Should().Contain("Adéla");
        _values.Should().BeNull("the host named the selection AND handed over its draft — there was nothing to reset");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AHostClearingTheDraft_WhileChangingTheSelection_AppliesTheNewDefaults(bool selectionFirst)
    {
        PromptTemplate[] templates =
        [
            new("a", "Template A", "A {{x}}") { Variables = [new PromptVariable("x", "X")] },
            new("b", "Template B", "B {{y}}")
            {
                Variables = [new PromptVariable("y", "Y") { DefaultValue = "seeded" }],
            },
        ];

        var cut = RenderLibrary(options =>
        {
            options.Templates = templates;
            options.SelectedTemplateId = "a";
            options.Values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["x"] = "typed" };
        });

        Variable(cut, "x").GetAttribute("value").Should().Be("typed");

        await SetSelectionWithDraftAsync(cut, "b", null, selectionFirst);

        Variable(cut, "y").GetAttribute("value").Should().Be("seeded",
            "clearing Values is not a draft for the new selection, so the new template's defaults apply");
        Preview(cut).Should().Contain("seeded");
    }

    [Fact]
    public void TheInitialSelection_SeedsTheDefaultsAndReportsThem()
    {
        RenderLibrary();

        _values.Should().NotBeNull("the seeded draft is what the fields hold, so a host can persist it");
        _values!.Keys.Should().BeEquivalentTo(["topic", "audience", "tone"]);
        _values["audience"].Should().Be("general readers");
        _values["topic"].Should().BeNullOrEmpty();
    }

    [Fact]
    public void TheInitialSelection_WithHostSuppliedValues_ReportsNoReset()
    {
        var cut = RenderLibrary(options =>
            options.Values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["topic"] = "Quantum taxes" });

        cut.Find("[data-testid='prompt-library-detail']").Should().NotBeNull();
        _values.Should().BeNull("the host's own draft was kept, so there was nothing to reset or report");
        Variable(cut, "topic").GetAttribute("value").Should().Be("Quantum taxes");
    }

    // ── Validation and Use ───────────────────────────────────────────────────

    [Fact]
    public void Use_IsNotRenderedWithoutACallback()
    {
        var cut = RenderLibrary(options => options.BindUse = false);

        cut.FindAll("[data-testid='prompt-library-use']").Should().BeEmpty(
            "an action that has nowhere to go is not offered");
    }

    [Fact]
    public void Use_IsMarkedUnavailableWhileARequiredVariableIsEmpty()
    {
        var cut = RenderLibrary();

        var use = cut.Find("[data-testid='prompt-library-use']");
        use.GetAttribute("aria-disabled").Should().Be("true");
        use.HasAttribute("disabled").Should().BeFalse(
            "a natively disabled button leaves the tab order, so a keyboard user tabbing past it never learns why "
            + "Use is unavailable");
    }

    [Fact]
    public void Use_RaisesTheAssembledPrompt_OnceTheFormIsComplete()
    {
        var cut = RenderLibrary();

        Variable(cut, "topic").Input("Quantum taxes");
        cut.Find("[data-testid='prompt-library-use']").Click();

        _used.Should().NotBeNull();
        _used!.TemplateId.Should().Be("brief");
        _used.Title.Should().Be("Brief a topic");
        _used.Prompt.Should()
            .Be("Write a brief about Quantum taxes for general readers in a formal tone.");
        _used.Values["topic"].Should().Be("Quantum taxes");
        _used.Values["audience"].Should().Be("general readers");
        cut.Find("[data-testid='prompt-library-use']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Use_WithAMissingRequiredVariable_IsExplainedInsteadOfSendingThePrompt()
    {
        var cut = RenderLibrary();

        var use = cut.Find("[data-testid='prompt-library-use']");
        use.GetAttribute("aria-disabled").Should().Be("true");
        use.Click();

        _used.Should().BeNull("a prompt with a hole never reaches a host");
        AnnouncedSentences(cut).Should().Contain(sentence => sentence.Contains("Topic"),
            "pressing the unavailable action has to say what is missing");

        var summary = cut.Find("[data-testid='prompt-library-validation']");
        summary.TextContent.Should().Contain("Topic");
        cut.Find("[data-testid='prompt-library-use']").GetAttribute("aria-describedby").Should()
            .Contain(summary.Id, "the summary explains the action that is still unavailable");

        // Whitespace counts as empty for the parser, so the action stays unavailable.
        Variable(cut, "topic").Input("  ");
        cut.Find("[data-testid='prompt-library-use']").GetAttribute("aria-disabled").Should().Be("true");
        _used.Should().BeNull();
    }

    [Fact]
    public void TheFieldError_IsReachableFromTheControlItBelongsTo()
    {
        var cut = RenderLibrary();

        Variable(cut, "topic").Input("  ");
        cut.Find("[data-testid='prompt-library-validation']").TextContent.Should().Contain("Topic");

        var describedBy = Variable(cut, "topic").GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrWhiteSpace("the error has to be announced with the field, not only painted");
        cut.Find($"#{describedBy}").TextContent.Should().Contain("Topic");
    }

    [Fact]
    public void ARequiredFieldWithoutAnAttempt_ShowsNoErrorYet()
    {
        var cut = RenderLibrary();

        cut.FindAll(".tm-form-field-error").Should().BeEmpty(
            "a form the reader has not touched must not open covered in red");
        cut.FindAll("[data-testid='prompt-library-validation']").Should().BeEmpty();
    }

    [Fact]
    public void Use_ReportsAnEmptyPromptForATemplateWithoutVariables()
    {
        var cut = RenderLibrary(options =>
        {
            options.Templates = [new PromptTemplate("static", "Static", "You are a helpful assistant.")];
            options.SelectedTemplateId = "static";
        });

        cut.Find("[data-testid='prompt-library-use']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='prompt-library-use']").Click();

        _used!.Prompt.Should().Be("You are a helpful assistant.");
    }

    // ── Copy ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Copy_WritesTheAssembledPromptToTheClipboard_AndAnnouncesIt()
    {
        var cut = RenderLibrary();

        Variable(cut, "topic").Input("Quantum taxes");
        cut.Find("[data-testid='prompt-library-copy']").Click();

        var invocation = JSInterop.Invocations.Single(call => call.Identifier == "navigator.clipboard.writeText");
        invocation.Arguments[0].Should().Be(
            "Write a brief about Quantum taxes for general readers in a formal tone.");
        AnnouncedSentences(cut).Should().Contain(sentence => sentence.Contains("copied"));
    }

    [Fact]
    public void Copy_WhenTheClipboardRefuses_AnnouncesTheFailureAndNeverClaimsSuccess()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true)
            .SetException(new JSException("permission denied"));

        var cut = RenderLibrary();

        cut.Find("[data-testid='prompt-library-copy']").Click();

        AnnouncedSentences(cut).Should().Contain(sentence => sentence.Contains("could not"));
        AnnouncedSentences(cut).Should().NotContain(sentence => sentence.Contains("copied to the clipboard"));
    }

    // ── Optional CRUD ────────────────────────────────────────────────────────

    [Fact]
    public void CrudActions_AreNotRenderedWithoutTheirCallbacks()
    {
        var cut = RenderLibrary(options =>
        {
            options.BindCreate = false;
            options.BindEdit = false;
            options.BindDelete = false;
        });

        cut.FindAll("[data-testid='prompt-library-create']").Should().BeEmpty();
        cut.FindAll("[data-testid='prompt-library-edit']").Should().BeEmpty();
        cut.FindAll("[data-testid='prompt-library-delete']").Should().BeEmpty();
    }

    [Fact]
    public void CrudActions_RaiseThePickedTemplate()
    {
        var cut = RenderLibrary();

        cut.Find("[data-testid='prompt-library-create']").Click();
        cut.Find("[data-testid='prompt-library-edit']").Click();
        cut.Find("[data-testid='prompt-library-delete']").Click();

        _createCalls.Should().Be(1);
        _edited!.Id.Should().Be("brief");
        _deleted!.Id.Should().Be("brief");
    }

    // ── Mobile drill-down ────────────────────────────────────────────────────

    [Fact]
    public void Mobile_StartsOnTheList_WithoutADetailPane()
    {
        var cut = RenderLibrary(options => options.LayoutMode = TmLayoutMode.Mobile);

        cut.Find("[data-testid='prompt-library-list']").Should().NotBeNull();
        cut.FindAll("[data-testid='prompt-library-detail']").Should().BeEmpty();
        cut.FindAll("[data-testid='prompt-library-back']").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_TappingARow_OpensTheDetail_AndBackReturnsToTheList()
    {
        var cut = RenderLibrary(options => options.LayoutMode = TmLayoutMode.Mobile);

        Row(cut, "review").Click();

        cut.Find("[data-testid='prompt-library-detail']").Should().NotBeNull();
        cut.FindAll("[data-testid='prompt-library-list']").Should().BeEmpty("the detail replaces the list on a phone");
        var back = cut.Find("[data-testid='prompt-library-back']");
        back.GetAttribute("aria-label").Should().BeNull(
            "the visible text already is the accessible name; repeating it in an aria-label only risks a mismatch");
        back.TextContent.Trim().Should().Be("Back to templates");

        back.Click();

        cut.Find("[data-testid='prompt-library-list']").Should().NotBeNull();
        cut.FindAll("[data-testid='prompt-library-detail']").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_OpeningTheDetail_MovesTheFocus_AndBackRestoresItToTheRow()
    {
        var cut = RenderLibrary(options => options.LayoutMode = TmLayoutMode.Mobile);
        var before = FocusCalls();

        Row(cut, "review").Click();
        FocusCalls().Should().BeGreaterThan(before, "the opened detail takes the focus so the reader is not left behind");

        var backTarget = FocusTargets().Last();
        cut.Find("[data-testid='prompt-library-back']").Click();

        FocusCalls().Should().BeGreaterThan(before + 1);
        FocusTargets().Last().Should().NotBe(backTarget,
            "the focus returns to the row that opened the detail, not to a button that just disappeared");
    }

    [Fact]
    public void Desktop_HasNoBackButton_BecauseBothPanesAreVisible()
    {
        var cut = RenderLibrary();

        cut.FindAll("[data-testid='prompt-library-back']").Should().BeEmpty();
    }

    // ── Accessibility plumbing ───────────────────────────────────────────────

    [Fact]
    public void TheLibrary_IsAGroupNamedByItsVisibleHeading()
    {
        var cut = RenderLibrary(options => options.Title = "My prompts");

        var root = cut.Find("[data-testid='prompt-library']");
        root.GetAttribute("role").Should().Be("group");
        root.GetAttribute("aria-label").Should().Be("My prompts");
        cut.Find(".tm-prompt-library__title").TextContent.Should().Be("My prompts");
    }

    [Fact]
    public void TheLibrary_FallsBackToTheLocalizedName()
    {
        var cut = RenderLibrary();

        cut.Find("[data-testid='prompt-library']").GetAttribute("aria-label").Should().Be("Prompt library");
    }

    [Fact]
    public void TheHeadingLevel_IsHonouredInThePageOutline()
    {
        var cut = RenderLibrary(options => options.HeadingLevel = 2);

        cut.Find("h2.tm-prompt-library__title").Should().NotBeNull();
    }

    [Fact]
    public void TheComponent_OwnsExactlyOneLiveRegion()
    {
        var cut = RenderLibrary();

        cut.FindAll("[aria-live]").Should().ContainSingle(
            "the library speaks through one polite region; a second one would double every sentence");
        cut.Find("[aria-live]").GetAttribute("role").Should().Be("status");
    }

    [Fact]
    public void TheLiveRegion_IsSilentUntilSomethingHappens()
    {
        var cut = RenderLibrary();

        cut.Find("[data-testid='prompt-library-announcement']").TextContent.Should().BeEmpty();
    }

    [Fact]
    public void ThePreview_IsAGroupNamedByItsVisibleLabel()
    {
        var cut = RenderLibrary();

        var preview = cut.Find("[data-testid='prompt-library-preview']");
        preview.GetAttribute("role").Should().Be("group");

        var labelledBy = preview.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace("a preview floating beside the fields needs its own name");
        cut.Find($"#{labelledBy}").TextContent.Should().Be("Preview");
    }
}
