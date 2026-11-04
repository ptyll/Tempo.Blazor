using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Plan 3e96f6d8, phase 6 — <c>PromptTemplateParser</c> (Tempo.Blazor.Abstractions, pure C#): a prompt template is
/// TEXT with <c>{{variable}}</c> placeholders. The parser finds the placeholders, honours the template's own
/// <see cref="PromptTemplate.Variables"/> declaration when it has one, and assembles the prompt by substituting
/// text — it never evaluates an expression, never resolves a path and never touches culture. Everything the
/// component needs to render a live preview (the segments) and to refuse an incomplete prompt (the validation
/// errors) comes from here, so the component itself contains no string parsing at all.
/// </summary>
public sealed class PromptTemplateParserTests
{
    private static PromptTemplate Template(string body, params PromptVariable[] variables)
        => new("tpl", "Template", body) { Variables = variables.Length > 0 ? variables : null };

    // ── Extraction ───────────────────────────────────────────────────────────

    [Fact]
    public void ExtractVariables_ReturnsEveryPlaceholder_InOrderOfFirstAppearance()
    {
        var variables = PromptTemplateParser.ExtractVariables("Summarise {{topic}} for {{audience}} in {{tone}}.");

        variables.Select(variable => variable.Name).Should().Equal("topic", "audience", "tone");
    }

    [Fact]
    public void ExtractVariables_CountsADuplicateOnce_AndKeepsTheFirstPosition()
    {
        var variables = PromptTemplateParser.ExtractVariables("{{b}} {{a}} {{b}} {{a}}");

        variables.Select(variable => variable.Name).Should().Equal("b", "a");
    }

    [Fact]
    public void ExtractVariables_TrimsWhitespaceInsideThePlaceholder()
    {
        var variables = PromptTemplateParser.ExtractVariables("Hello {{  name  }}!");

        variables.Should().ContainSingle().Which.Name.Should().Be("name");
    }

    [Fact]
    public void ExtractVariables_TreatsAnUnterminatedPlaceholderAsLiteralText()
    {
        var variables = PromptTemplateParser.ExtractVariables("A {{topic without a close");

        variables.Should().BeEmpty();
        PromptTemplateParser.Build(Template("A {{topic without a close"), null).Prompt
            .Should().Be("A {{topic without a close");
    }

    [Fact]
    public void ExtractVariables_DoesNotEvaluateExpressions()
    {
        // `1 + 2` is not a variable name, so the braces are plain text — the parser substitutes text and never
        // evaluates anything, so `{{1 + 2}}` cannot become `3`.
        var variables = PromptTemplateParser.ExtractVariables("{{1 + 2}} and {{ user.name }}");

        variables.Select(variable => variable.Name).Should().Equal("user.name");
        PromptTemplateParser.Build(Template("{{1 + 2}}"), null).Prompt.Should().Be("{{1 + 2}}");
    }

    [Fact]
    public void ExtractVariables_UnescapesTheBraces()
    {
        var variables = PromptTemplateParser.ExtractVariables("Use {{{{topic}}}} literally");

        variables.Should().BeEmpty();
        PromptTemplateParser.Build(Template("Use {{{{topic}}}} literally"), null).Prompt
            .Should().Be("Use {{topic}} literally",
                "the doubled braces are the documented escape, never a placeholder");
    }

    [Fact]
    public void ExtractVariables_DerivesRequiredTextVariables()
    {
        var variable = PromptTemplateParser.ExtractVariables("{{subject}}").Should().ContainSingle().Subject;

        variable.Name.Should().Be("subject");
        variable.Label.Should().Be("subject", "a derived variable has no better label than its own name");
        variable.IsRequired.Should().BeTrue("a placeholder the template author spelled out has to be filled in");
        variable.Kind.Should().Be(PromptVariableKind.Text);
        variable.Options.Should().BeNull();
    }

    [Fact]
    public void ExtractVariables_ReturnsEmptyForNullAndBlankBodies()
    {
        PromptTemplateParser.ExtractVariables(null).Should().BeEmpty();
        PromptTemplateParser.ExtractVariables(string.Empty).Should().BeEmpty();
        PromptTemplateParser.ExtractVariables("   ").Should().BeEmpty();
    }

    // ── Explicit variables win ───────────────────────────────────────────────

    [Fact]
    public void ExplicitVariables_WinOverTheBody()
    {
        var template = Template(
            "Write about {{topic}} in the style of {{style}} — {{stray}}",
            new PromptVariable("topic", "Topic") { IsRequired = true },
            new PromptVariable("style", "Style") { IsRequired = false, DefaultValue = "plain" });

        var resolved = PromptTemplateParser.ResolveVariables(template);

        resolved.Select(variable => variable.Name).Should().Equal("topic", "style");
        resolved[0].Label.Should().Be("Topic", "the host's own declaration is authoritative");
        PromptTemplateParser.ExtractVariables(template.Body).Select(variable => variable.Name)
            .Should().Equal("topic", "style", "stray");
    }

    [Fact]
    public void APlaceholderTheTemplateDoesNotDeclare_StaysLiteralText()
    {
        var template = Template("{{topic}} / {{stray}}", new PromptVariable("topic"));

        var result = PromptTemplateParser.Build(
            template,
            new Dictionary<string, string?> { ["topic"] = "Taxes", ["stray"] = "ignored" });

        result.Prompt.Should().Be("Taxes / {{stray}}");
        result.Errors.Should().BeEmpty("an undeclared placeholder is not a variable, so it cannot be missing");
        result.Values.Keys.Should().Equal("topic");
    }

    // ── Assembly ─────────────────────────────────────────────────────────────

    [Fact]
    public void Build_SubstitutesEveryOccurrence()
    {
        var template = Template("{{a}} then {{b}} then {{a}} again");

        var result = PromptTemplateParser.Build(
            template,
            new Dictionary<string, string?> { ["a"] = "one", ["b"] = "two" });

        result.Prompt.Should().Be("one then two then one again");
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Build_UsesTheDefaultWhenNoValueIsSupplied()
    {
        var template = Template("Tone: {{tone}}", new PromptVariable("tone") { DefaultValue = "neutral" });

        var result = PromptTemplateParser.Build(template, null);

        result.Prompt.Should().Be("Tone: neutral");
        result.Values["tone"].Should().Be("neutral");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Build_ReportsAMissingRequiredVariable_AndKeepsThePlaceholder()
    {
        var template = Template(
            "{{topic}} / {{style}}",
            new PromptVariable("topic", "Topic"),
            new PromptVariable("style", "Style") { IsRequired = false });

        var result = PromptTemplateParser.Build(template, new Dictionary<string, string?> { ["topic"] = "   " });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].VariableName.Should().Be("topic");
        result.Errors[0].Kind.Should().Be(PromptTemplateValidationErrorKind.MissingRequired);
        result.Prompt.Should().Be("{{topic}} / {{style}}",
            "an incomplete prompt must not silently swallow the placeholder it could not fill");
        result.Segments.Should().Contain(segment => segment.VariableName == "topic" && segment.IsEmpty);
    }

    [Fact]
    public void Build_AnUnfilledOptionalVariable_IsNotAnError()
    {
        var template = Template("{{topic}}{{suffix}}",
            new PromptVariable("topic"),
            new PromptVariable("suffix") { IsRequired = false });

        var result = PromptTemplateParser.Build(template, new Dictionary<string, string?> { ["topic"] = "x" });

        result.IsValid.Should().BeTrue();
        result.Prompt.Should().Be("x{{suffix}}");
        result.Segments.Single(segment => segment.VariableName == "suffix").IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Build_DoesNotReExpandASubstitutedValue()
    {
        var template = Template("{{greeting}}");

        var result = PromptTemplateParser.Build(
            template,
            new Dictionary<string, string?> { ["greeting"] = "Hello {{name}}" });

        result.Prompt.Should().Be("Hello {{name}}", "a host's value is text, not a template");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Build_KeepsTheValueAsTyped_ButTreatsWhitespaceAsEmpty()
    {
        var template = Template("[{{padded}}][{{blank}}]");

        var result = PromptTemplateParser.Build(
            template,
            new Dictionary<string, string?> { ["padded"] = "  keep me  ", ["blank"] = "  " });

        result.Prompt.Should().Be("[  keep me  ][{{blank}}]");
        result.Errors.Select(error => error.VariableName).Should().Equal("blank");
    }

    [Fact]
    public void Build_MatchesNamesCaseSensitively()
    {
        var template = Template("{{Topic}}");

        var result = PromptTemplateParser.Build(template, new Dictionary<string, string?> { ["topic"] = "x" });

        result.Prompt.Should().Be("{{Topic}}");
        result.Errors.Should().ContainSingle().Which.VariableName.Should().Be("Topic");
    }

    [Fact]
    public void Build_ReportsASelectValueThatIsNotAnOption()
    {
        var template = Template("{{tone}}",
            new PromptVariable("tone")
            {
                Kind = PromptVariableKind.Select,
                Options = ["formal", "casual"],
            });

        var result = PromptTemplateParser.Build(template, new Dictionary<string, string?> { ["tone"] = "shouty" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Kind.Should().Be(PromptTemplateValidationErrorKind.NotAnOption);
        result.Prompt.Should().Be("shouty",
            "a supplied value is still substituted; the host is told the value is not one of the options");
    }

    [Fact]
    public void Build_ANullTemplate_Throws()
    {
        var act = () => PromptTemplateParser.Build(null!, null);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Segments: what the live preview renders ──────────────────────────────

    [Fact]
    public void Build_Segments_CoverTheWholePrompt_AndMarkTheFilledVariables()
    {
        var template = Template("Hi {{name}}, welcome to {{place}}.");

        var result = PromptTemplateParser.Build(
            template,
            new Dictionary<string, string?> { ["name"] = "Ada", ["place"] = "Praha" });

        result.Segments.Select(segment => segment.Text)
            .Should().Equal("Hi ", "Ada", ", welcome to ", "Praha", ".");
        result.Segments.Select(segment => segment.VariableName)
            .Should().Equal(null, "name", null, "place", null);
        result.Segments.Where(segment => segment.IsVariable).Should().OnlyContain(segment => !segment.IsEmpty);
    }

    [Fact]
    public void Build_Segments_KeepThePlaceholderForAnEmptyVariable()
    {
        var template = Template("Hi {{name}}!");

        var segment = PromptTemplateParser.Build(template, null).Segments
            .Single(candidate => candidate.VariableName == "name");

        segment.Text.Should().Be("{{name}}");
        segment.IsEmpty.Should().BeTrue();
        segment.IsVariable.Should().BeTrue();
    }

    [Fact]
    public void Build_Segments_RenderAnEscapeAsLiteralText()
    {
        var result = PromptTemplateParser.Build(Template("{{{{literal}}}}"), null);

        result.Segments.Should().ContainSingle();
        result.Segments[0].Text.Should().Be("{{literal}}");
        result.Segments[0].VariableName.Should().BeNull();
        result.Segments[0].IsVariable.Should().BeFalse();
    }

    [Fact]
    public void Build_AnEmptyBody_YieldsAnEmptyPromptWithNoErrors()
    {
        var result = PromptTemplateParser.Build(new PromptTemplate("empty", "Empty"), null);

        result.Prompt.Should().BeEmpty();
        result.Segments.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Build_ReturnsTheEffectiveValuesOnly_IgnoringUnknownNames()
    {
        var template = Template("{{a}}{{b}}",
            new PromptVariable("a") { DefaultValue = "A" },
            new PromptVariable("b") { IsRequired = false });

        var result = PromptTemplateParser.Build(
            template,
            new Dictionary<string, string?> { ["b"] = "B", ["ghost"] = "x" });

        result.Values.Should().Equal(new Dictionary<string, string?> { ["a"] = "A", ["b"] = "B" });
    }

    // ── PromptTemplateUse: the payload of "Use" ──────────────────────────────

    [Fact]
    public void ToUse_CarriesTheAssembledPromptAndTheEffectiveValues()
    {
        var template = Template("{{topic}}", new PromptVariable("topic"));

        var use = PromptTemplateParser.Build(template, new Dictionary<string, string?> { ["topic"] = "Taxes" })
            .ToUse(template);

        use.TemplateId.Should().Be("tpl");
        use.Title.Should().Be("Template");
        use.Prompt.Should().Be("Taxes");
        use.Values.Should().Equal(new Dictionary<string, string> { ["topic"] = "Taxes" });
    }

    [Fact]
    public void ToUse_RefusesAnInvalidAssembly()
    {
        var template = Template("{{topic}}", new PromptVariable("topic"));
        var result = PromptTemplateParser.Build(template, null);

        var act = () => result.ToUse(template);

        act.Should().Throw<InvalidOperationException>("an incomplete prompt must never reach the host");
    }

    [Fact]
    public void TheUsePayload_DefaultValues_AreAReadOnlyEmptyMap()
    {
        var use = new PromptTemplateUse();

        use.Values.Should().BeEmpty();

        var mutate = () => ((IDictionary<string, string>)use.Values).Add("topic", "Taxes");

        mutate.Should().Throw<NotSupportedException>(
            "the default is ONE shared instance, so a cast back to a mutable dictionary would leak a value into every "
            + "later payload that carries no values");
    }
}
