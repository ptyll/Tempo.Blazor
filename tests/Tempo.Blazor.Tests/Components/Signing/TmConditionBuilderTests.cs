using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// Condition builder (Signing redesign phase 7, S1-2): the row controls are Tm
/// <c>TmSelect</c>/<c>TmTextInput</c> components (SI1), rows share ONE card with separators, the
/// add action is the dashed full-width row, and rows reorder with up/down buttons (Q-S7 — the
/// first row never carries an operation).
/// </summary>
public class TmConditionBuilderTests : LocalizationTestBase
{
    private const string FieldSelector = ".tm-condition-builder__field select";
    private const string ActionSelector = ".tm-condition-builder__action select";
    private const string OperationSelector = ".tm-condition-builder__operation select";

    [Fact]
    public void Render_EmptyConditions_RendersEmptyConditionRow()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target"));

        cut.Find(".tm-condition-builder__row").Should().NotBeNull();
        (cut.Find(FieldSelector).GetAttribute("value") ?? "").Should().BeEmpty();
    }

    [Fact]
    public void Condition_UsesTmSelect()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target"));

        cut.FindComponents<Tempo.Blazor.Components.Inputs.TmSelect<string>>().Should().NotBeEmpty(
            "the field/action/operation/value dropdowns are TmSelect components, not native selects");
        cut.Find(FieldSelector).Should().NotBeNull();
    }

    [Fact]
    public void Render_Fields_ShowsSupportedSourceFieldSelectOptions()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target"));

        cut.FindAll($"{FieldSelector} option")
            .Select(option => option.TextContent)
            .Should()
            .Contain(["Consent", "Delivery", "Country", "Approvals", "Amount", "Name"]);
    }

    [Fact]
    public void Render_Fields_FiltersCurrentField()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target"));

        cut.FindAll($"{FieldSelector} option")
            .Select(option => option.GetAttribute("value"))
            .Should()
            .NotContain("target");
    }

    [Fact]
    public void Render_Fields_FiltersUnsupportedStaticFieldTypes()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target"));

        cut.FindAll($"{FieldSelector} option")
            .Select(option => option.GetAttribute("value"))
            .Should()
            .NotContain(["heading", "strikethrough"]);
    }

    [Fact]
    public void Change_Field_InvokesConditionsChanged()
    {
        IReadOnlyList<SigningFieldCondition>? captured = null;
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.ConditionsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningFieldCondition>>(this, value => captured = value)));

        cut.Find(FieldSelector).Change("name");

        captured.Should().NotBeNull();
        captured!.Should().ContainSingle().Which.FieldUuid.Should().Be("name");
    }

    [Theory]
    [InlineData("consent", SigningConditionAction.Checked, SigningConditionAction.Unchecked)]
    [InlineData("delivery", SigningConditionAction.Equal, SigningConditionAction.NotEqual)]
    [InlineData("country", SigningConditionAction.Equal, SigningConditionAction.NotEqual)]
    [InlineData("approvals", SigningConditionAction.Contains, SigningConditionAction.DoesNotContain)]
    [InlineData("name", SigningConditionAction.Empty, SigningConditionAction.NotEmpty)]
    public void Actions_FieldType_RendersExpectedActions(string fieldUuid, params SigningConditionAction[] expectedActions)
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition { FieldUuid = fieldUuid }]));

        cut.FindAll($"{ActionSelector} option")
            .Select(option => Enum.Parse<SigningConditionAction>(option.GetAttribute("value")!))
            .Should()
            .Equal(expectedActions);
    }

    [Fact]
    public void Actions_NumberField_RendersNumericActions()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition { FieldUuid = "amount" }]));

        cut.FindAll($"{ActionSelector} option")
            .Select(option => Enum.Parse<SigningConditionAction>(option.GetAttribute("value")!))
            .Should()
            .Equal(
                SigningConditionAction.Empty,
                SigningConditionAction.NotEmpty,
                SigningConditionAction.Equal,
                SigningConditionAction.NotEqual,
                SigningConditionAction.GreaterThan,
                SigningConditionAction.LessThan);
    }

    [Theory]
    [InlineData("delivery")]
    [InlineData("country")]
    [InlineData("approvals")]
    public void Value_ChoiceField_RendersOptionDropdown(string fieldUuid)
    {
        var action = fieldUuid == "approvals"
            ? SigningConditionAction.Contains
            : SigningConditionAction.Equal;

        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition { FieldUuid = fieldUuid, Action = action }]));

        cut.Find(".tm-condition-builder__value-select select").Should().NotBeNull();
        cut.FindAll(".tm-condition-builder__value-select select option")
            .Select(option => option.TextContent)
            .Should()
            .Contain(["One", "Two"]);
    }

    [Fact]
    public void Value_NumberField_RendersNumberInput()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition
                      {
                          FieldUuid = "amount",
                          Action = SigningConditionAction.GreaterThan
                      }]));

        cut.Find("input.tm-condition-builder__value-input[type='number']").Should().NotBeNull();
    }

    [Fact]
    public void Value_EmptyAction_DoesNotRenderValueInput()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition
                      {
                          FieldUuid = "name",
                          Action = SigningConditionAction.Empty
                      }]));

        cut.FindAll("input.tm-condition-builder__value-input").Should().BeEmpty();
        cut.FindAll(".tm-condition-builder__value-select").Should().BeEmpty();
        cut.FindAll(".tm-condition-builder__validation").Should().BeEmpty();
    }

    [Fact]
    public void Value_RequiredActionWithoutValue_ShowsValidation()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition
                      {
                          FieldUuid = "country",
                          Action = SigningConditionAction.Equal
                      }]));

        cut.Find(".tm-condition-builder").ClassList.Should().Contain("tm-condition-builder--invalid");
        cut.Find(".tm-condition-builder__validation").TextContent.Should().Contain("Choose a value");
    }

    [Fact]
    public void Condition_AddRow()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target"));

        cut.Find(".tm-condition-builder__add").Click();

        cut.FindAll(".tm-condition-builder__row").Should().HaveCount(2);
        // The dashed add row is the builder's only add affordance (the reference's "+ Přidat podmínku").
        cut.Find(".tm-condition-builder__add").ClassList.Should().Contain("tm-condition-builder__add--dashed");
    }

    [Fact]
    public void Condition_MoveUpDown_KeepsFirstRowWithoutOperation()
    {
        IReadOnlyList<SigningFieldCondition>? captured = null;
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions,
                      [
                          new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.NotEmpty },
                          new SigningFieldCondition { FieldUuid = "consent", Action = SigningConditionAction.Checked, Operation = SigningConditionOperation.Or }
                      ])
                      .Add(p => p.ConditionsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningFieldCondition>>(this, value => captured = value)));

        // Row 0 never renders an operation select; row 1 does.
        cut.FindAll(OperationSelector).Should().HaveCount(1);
        cut.Find(".tm-condition-builder__move-up").HasAttribute("disabled").Should().BeTrue("the first row cannot move up");

        cut.FindAll(".tm-condition-builder__move-up")[1].Click();

        captured.Should().NotBeNull();
        captured!.Select(condition => condition.FieldUuid)
            .Should().ContainInOrder("consent", "name");
        // After the move the FIRST row still carries no operation select — the join operator stays
        // positional, never on the leading row.
        cut.FindAll(OperationSelector).Should().HaveCount(1);
        cut.FindAll(OperationSelector)[0].GetAttribute("value").Should().Be(SigningConditionOperation.And.ToString(),
            "the hidden operation of the leading row normalizes back to And");

        // The last row cannot move down; the (now second) row can.
        cut.FindAll(".tm-condition-builder__move-down")[1].HasAttribute("disabled").Should().BeTrue();
        cut.FindAll(".tm-condition-builder__move-down")[0].Click();
        captured!.Select(condition => condition.FieldUuid)
            .Should().ContainInOrder("name", "consent");
    }

    [Fact]
    public void Operation_SecondCondition_CanSwitchToOr()
    {
        IReadOnlyList<SigningFieldCondition>? captured = null;
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [
                          new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.NotEmpty },
                          new SigningFieldCondition { FieldUuid = "consent", Action = SigningConditionAction.Checked }
                      ])
                      .Add(p => p.ConditionsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningFieldCondition>>(this, value => captured = value)));

        cut.Find(OperationSelector).Change(SigningConditionOperation.Or.ToString());

        captured.Should().NotBeNull();
        captured![1].Operation.Should().Be(SigningConditionOperation.Or);
    }

    [Fact]
    public void Operation_DraftCondition_DoesNotNotifyBeforeFieldIsSelected()
    {
        IReadOnlyList<SigningFieldCondition>? captured = null;
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.ConditionsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningFieldCondition>>(this, value => captured = value)));

        cut.Find(".tm-condition-builder__add").Click();
        cut.Find(OperationSelector).Change(SigningConditionOperation.Or.ToString());

        cut.FindAll(".tm-condition-builder__row").Should().HaveCount(2);
        captured.Should().BeNull();
    }

    [Fact]
    public void Operation_DraftCondition_IsPreservedWhenFieldIsSelected()
    {
        IReadOnlyList<SigningFieldCondition>? captured = null;
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.ConditionsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningFieldCondition>>(this, value => captured = value)));

        cut.Find(".tm-condition-builder__add").Click();
        cut.Find(OperationSelector).Change(SigningConditionOperation.Or.ToString());
        cut.FindAll(FieldSelector)[1].Change("consent");

        captured.Should().NotBeNull();
        captured!.Should().ContainSingle();
        captured[0].FieldUuid.Should().Be("consent");
        captured[0].Operation.Should().Be(SigningConditionOperation.Or);
    }

    [Fact]
    public void RemoveCondition_RemovesConditionAndNotifies()
    {
        IReadOnlyList<SigningFieldCondition>? captured = null;
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [
                          new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.NotEmpty },
                          new SigningFieldCondition { FieldUuid = "consent", Action = SigningConditionAction.Checked }
                      ])
                      .Add(p => p.ConditionsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningFieldCondition>>(this, value => captured = value)));

        cut.FindAll(".tm-condition-builder__remove")[0].Click();

        captured.Should().NotBeNull();
        captured!.Should().ContainSingle().Which.FieldUuid.Should().Be("consent");
    }

    [Fact]
    public void CultureChange_DoesNotChangeExistingConditionFieldUuid()
    {
        IReadOnlyList<SigningFieldCondition>? captured = null;
        var fields = CreateFields();
        fields.First(field => field.Uuid == "country").Labels.Translations["cs"] = "Země";
        var conditions = new[]
        {
            new SigningFieldCondition
            {
                FieldUuid = "country",
                Action = SigningConditionAction.Equal,
                Value = "one"
            }
        };

        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, fields)
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, conditions)
                      .Add(p => p.Culture, "en-US")
                      .Add(p => p.ConditionsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningFieldCondition>>(this, value => captured = value)));

        cut.Render(parameters => parameters.Add(p => p.Culture, "cs-CZ"));

        captured.Should().BeNull();
        cut.Find(FieldSelector).GetAttribute("value").Should().Be("country");
        cut.Find(FieldSelector).TextContent.Should().Contain("Země");
    }

    [Fact]
    public void Cycle_DirectDependencyOnCurrentField_ShowsValidation()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition
                      {
                          FieldUuid = "target",
                          Action = SigningConditionAction.NotEmpty
                      }]));

        cut.Find(".tm-condition-builder__validation").TextContent.Should().Contain("cycle");
    }

    [Fact]
    public void Cycle_IndirectDependencyAcrossFields_ShowsValidation()
    {
        var fields = CreateFields();
        fields.First(field => field.Uuid == "name").Conditions.Add(new SigningFieldCondition
        {
            FieldUuid = "amount",
            Action = SigningConditionAction.NotEmpty
        });
        fields.First(field => field.Uuid == "amount").Conditions.Add(new SigningFieldCondition
        {
            FieldUuid = "target",
            Action = SigningConditionAction.NotEmpty
        });

        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, fields)
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition
                      {
                          FieldUuid = "name",
                          Action = SigningConditionAction.NotEmpty
                      }]));

        cut.Find(".tm-condition-builder__validation").TextContent.Should().Contain("cycle");
    }

    // ── Value drafts (phase 7 review, MAJOR 3) ──────────────────────────────────────

    [Fact]
    public void ValueInput_TypingStaysRaw_AndDoesNotNotifyPerKeystroke()
    {
        IReadOnlyList<SigningFieldCondition>? captured = null;
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition
                      {
                          FieldUuid = "amount",
                          Action = SigningConditionAction.GreaterThan,
                          Value = "10"
                      }])
                      .Add(p => p.ConditionsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningFieldCondition>>(this, value => captured = value)));

        var input = cut.Find("input.tm-condition-builder__value-input");
        input.Input("10"); // clearing through the backspace path — raw text must stay
        input.Input("1");

        input.GetAttribute("value").Should().Be("1", "the raw keystrokes stay on screen");
        captured.Should().BeNull("a keystroke must not raise ConditionsChanged");
    }

    [Fact]
    public void ValueInput_BlurCommitsTheDraft()
    {
        IReadOnlyList<SigningFieldCondition>? captured = null;
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition
                      {
                          FieldUuid = "amount",
                          Action = SigningConditionAction.GreaterThan,
                          Value = "10"
                      }])
                      .Add(p => p.ConditionsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningFieldCondition>>(this, value => captured = value)));

        var input = cut.Find("input.tm-condition-builder__value-input");
        input.Input("42");
        input.Blur();

        captured.Should().NotBeNull("the blur is the commit");
        captured!.Should().ContainSingle().Which.Value.Should().Be("42");
    }

    // ── Stable row identity (phase 7 review, MAJOR 2) ───────────────────────────────

    [Fact]
    public void Rows_CarryStableGuidIds()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions,
                      [
                          new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.NotEmpty },
                          new SigningFieldCondition { FieldUuid = "consent", Action = SigningConditionAction.Checked }
                      ]));

        var ids = cut.FindAll(".tm-condition-builder__row")
            .Select(row => row.GetAttribute("data-row-id"))
            .ToArray();
        ids.Should().HaveCount(2);
        Guid.TryParse(ids[0], out _).Should().BeTrue("every row carries a stable Guid the @key rides on");
        Guid.TryParse(ids[1], out _).Should().BeTrue();
    }

    /// <summary>
    /// Hosts (the demo page and <c>TmSigningFieldEditorPanel</c>) echo a NEW-but-equal conditions
    /// list after every change. Rebuilding <c>_rows</c> for that echo minted fresh Guids, so
    /// <c>@key</c> recreated every row and the queued focus landed on <c>&lt;body&gt;</c>.
    /// </summary>
    [Fact]
    public void EchoedEqualConditions_KeepRowIds_SoRemoveFocusTargetsTheSurvivingRow()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.Signing/js/signing-editor.js");
        module.Setup<bool>("focusElement", _ => true).SetResult(true);

        IReadOnlyList<SigningFieldCondition>? captured = null;
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions,
                      [
                          new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.NotEmpty },
                          new SigningFieldCondition { FieldUuid = "consent", Action = SigningConditionAction.Checked },
                          new SigningFieldCondition { FieldUuid = "amount", Action = SigningConditionAction.NotEmpty }
                      ])
                      .Add(p => p.ConditionsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningFieldCondition>>(this, value => captured = value)));

        var idsBefore = RowIds(cut);
        cut.FindAll(".tm-condition-builder__remove")[1].Click();

        captured.Should().NotBeNull();
        // A new list instance with the same values — the host echo, not the list the builder just sent.
        cut.Render(parameters => parameters.Add(p => p.Conditions, CloneConditions(captured!)));

        var idsAfter = RowIds(cut);
        idsAfter.Should().Equal(new[] { idsBefore[0], idsBefore[2] }.AsEnumerable(),
            "an equal echo must not mint new row ids — the surviving rows keep theirs");

        module.Invocations.Should().Contain(invocation =>
            invocation.Identifier == "focusElement"
            && invocation.Arguments[0].ToString()!.Contains($"data-row-id='{idsBefore[2]}'")
            && invocation.Arguments[0].ToString()!.Contains(".tm-condition-builder__remove"),
            "the focus selector must name the surviving row's id, which only exists if the echo kept it");
    }

    [Fact]
    public void EchoedEqualConditions_AfterSelectChange_KeepTheChangedRowId()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.Signing/js/signing-editor.js");
        module.Setup<bool>("focusElement", _ => true).SetResult(true);

        IReadOnlyList<SigningFieldCondition>? captured = null;
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.NotEmpty }])
                      .Add(p => p.ConditionsChanged, EventCallback.Factory.Create<IReadOnlyList<SigningFieldCondition>>(this, value => captured = value)));

        var idBefore = RowIds(cut).Single();
        cut.Find(FieldSelector).Change("consent");

        captured.Should().NotBeNull();
        captured.Should().ContainSingle().Which.FieldUuid.Should().Be("consent");
        cut.Render(parameters => parameters.Add(p => p.Conditions, CloneConditions(captured!)));

        RowIds(cut).Should().Equal(new[] { idBefore }.AsEnumerable(),
            "a select change echoed as a new equal list must not recreate the row");
    }

    [Fact]
    public void IncomingConditions_ThatDiffer_RebuildReusingIdsByPosition()
    {
        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions,
                      [
                          new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.NotEmpty },
                          new SigningFieldCondition { FieldUuid = "consent", Action = SigningConditionAction.Checked }
                      ]));

        var idsBefore = RowIds(cut);
        cut.Render(parameters => parameters.Add(p => p.Conditions,
        [
            new SigningFieldCondition { FieldUuid = "amount", Action = SigningConditionAction.GreaterThan, Value = "5" },
            new SigningFieldCondition { FieldUuid = "consent", Action = SigningConditionAction.Unchecked },
            new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.Empty }
        ]));

        var idsAfter = RowIds(cut);
        idsAfter.Should().HaveCount(3);
        idsAfter[0].Should().Be(idsBefore[0]);
        idsAfter[1].Should().Be(idsBefore[1], "a real change rebuilds, but existing positions keep their ids");
        Guid.TryParse(idsAfter[2], out _).Should().BeTrue();
        idsAfter[2].Should().NotBe(idsBefore[0]).And.NotBe(idsBefore[1]);
    }

    private static string[] RowIds(IRenderedComponent<TmConditionBuilder> cut)
    {
        return cut.FindAll(".tm-condition-builder__row")
            .Select(row => row.GetAttribute("data-row-id")!)
            .ToArray();
    }

    private static IReadOnlyList<SigningFieldCondition> CloneConditions(IReadOnlyList<SigningFieldCondition> conditions)
    {
        return conditions.Select(condition => new SigningFieldCondition
        {
            FieldUuid = condition.FieldUuid,
            Action = condition.Action,
            Value = condition.Value,
            Operation = condition.Operation
        }).ToArray();
    }

    // ── Focus hand-off + announcements (phase 7 review, MAJOR 2) ────────────────────

    [Fact]
    public void RemoveMiddleRow_FocusesTheNextRowsRemove()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.Signing/js/signing-editor.js");
        module.Setup<bool>("focusElement", _ => true).SetResult(true);

        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions,
                      [
                          new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.NotEmpty },
                          new SigningFieldCondition { FieldUuid = "consent", Action = SigningConditionAction.Checked },
                          new SigningFieldCondition { FieldUuid = "amount", Action = SigningConditionAction.NotEmpty }
                      ]));

        cut.FindAll(".tm-condition-builder__remove")[1].Click();
        cut.Render(); // the host re-render that follows ConditionsChanged

        // The row that now occupies the removed position is "amount" — its remove takes the focus.
        module.Invocations.Should().Contain(invocation =>
            invocation.Identifier == "focusElement"
            && invocation.Arguments[0].ToString()!.Contains(".tm-condition-builder__remove"),
            "the focus lands on the next row's remove, not on <body>");
    }

    [Fact]
    public void RemoveLastContentRow_FocusesTheDraftRowsRemove()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.Signing/js/signing-editor.js");
        module.Setup<bool>("focusElement", _ => true).SetResult(true);

        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions, [new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.NotEmpty }]));

        cut.Find(".tm-condition-builder__remove").Click();
        cut.Render();

        module.Invocations.Where(invocation => invocation.Identifier == "focusElement")
            .Select(invocation => invocation.Arguments[0].ToString())
            .Should().Contain(selector => selector!.Contains(".tm-condition-builder__remove"),
                "removing the only content row collapses into the fresh draft — the draft row's remove takes the focus");
    }

    [Fact]
    public void MoveRowUp_FocusesTheMovedRow_AndAnnouncesTheNewPosition()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.Signing/js/signing-editor.js");
        module.Setup<bool>("focusElement", _ => true).SetResult(true);

        var cut = Render<TmConditionBuilder>(parameters =>
            parameters.Add(p => p.Fields, CreateFields())
                      .Add(p => p.CurrentFieldUuid, "target")
                      .Add(p => p.Conditions,
                      [
                          new SigningFieldCondition { FieldUuid = "name", Action = SigningConditionAction.NotEmpty },
                          new SigningFieldCondition { FieldUuid = "consent", Action = SigningConditionAction.Checked }
                      ]));

        cut.FindAll(".tm-condition-builder__move-up")[1].Click();
        cut.Render();

        // The moved row landed in the TOP slot, where its move-up disabled itself — the focus
        // moves to the opposite (still enabled) button inside the moved row.
        module.Invocations.Should().Contain(invocation =>
            invocation.Identifier == "focusElement"
            && invocation.Arguments[0].ToString()!.Contains(".tm-condition-builder__move-down"));

        cut.Find(".tm-condition-builder .tm-sr-only[aria-live='polite']").TextContent
            .Should().Be("Consent moved to position 1");
    }

    private static List<SigningField> CreateFields()
    {
        return
        [
            CreateField("target", "Target", SigningFieldType.Text),
            CreateField("name", "Name", SigningFieldType.Text),
            CreateField("consent", "Consent", SigningFieldType.Checkbox),
            CreateChoiceField("delivery", "Delivery", SigningFieldType.Radio),
            CreateChoiceField("country", "Country", SigningFieldType.Select),
            CreateChoiceField("approvals", "Approvals", SigningFieldType.Multiple),
            CreateField("amount", "Amount", SigningFieldType.Number),
            CreateField("heading", "Heading", SigningFieldType.Heading),
            CreateField("strikethrough", "Strikethrough", SigningFieldType.Strikethrough)
        ];
    }

    private static SigningField CreateField(string uuid, string name, SigningFieldType type)
    {
        return new SigningField
        {
            Uuid = uuid,
            Name = name,
            Type = type
        };
    }

    private static SigningField CreateChoiceField(string uuid, string name, SigningFieldType type)
    {
        var field = CreateField(uuid, name, type);
        field.Options =
        [
            new SigningFieldOption { Uuid = "one", Value = "One" },
            new SigningFieldOption { Uuid = "two", Value = "Two" }
        ];

        return field;
    }
}
