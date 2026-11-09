using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// TDD tests for <see cref="TmOtpInput"/> (plan „Nové komponenty 1/7", fáze 1). DOM input handling
/// (filtering, caret, composition, navigation, auto-advance, focus redirect) lives in
/// tm-otp-input.js and is covered by __tests__/tm-otp-input.test.mjs (npm run test:overlay);
/// these tests drive the component through its public [JSInvokable] model entry points.
/// </summary>
public class TmOtpInputTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-otp-input.js";

    private BunitJSModuleInterop SetupOtpModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.SetupVoid("setCellValue", _ => true).SetVoidResult();
        module.SetupVoid("setCellValues", _ => true).SetVoidResult();
        module.SetupVoid("restartShake", _ => true).SetVoidResult();
        module.SetupVoid("focusCell", _ => true).SetVoidResult();
        module.SetupVoid("selectCell", _ => true).SetVoidResult();
        return module;
    }

    private static IElement Cell(IRenderedComponent<TmOtpInput> cut, int index)
        => cut.FindAll(".tm-otp-input__cell")[index];

    private static int[] FocusedIndexes(BunitJSModuleInterop module)
        => module.Invocations
            .Where(i => i.Identifier == "focusCell" || i.Identifier == "selectCell")
            .Select(i => (int)i.Arguments[1]!)
            .ToArray();

    /// <summary>Re-renders with new parameter values — a parent re-render (this bUnit version has
    /// no SetParametersAndRender; same InvokeAsync pattern as TmChatViewportTests).</summary>
    private static Task ReRenderAsync(
        IRenderedComponent<TmOtpInput> cut, Dictionary<string, object?> parameters)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(parameters)));

    /// <summary>Drives the component the way the JS module would — on the renderer dispatcher.</summary>
    private static Task CellInputAsync(IRenderedComponent<TmOtpInput> cut, int index, string value)
        => cut.InvokeAsync(() => cut.Instance.OnCellInputAsync(index, value));

    /// <summary>Drives a multi-character insert (paste/autofill) on the renderer dispatcher.</summary>
    private static Task DistributeAsync(IRenderedComponent<TmOtpInput> cut, int index, string raw)
        => cut.InvokeAsync(() => cut.Instance.DistributeFromAsync(index, raw));

    [Fact]
    public void Renders_Length_Cells_Inside_Role_Group()
    {
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        var group = cut.Find("[role='group']");
        group.GetAttribute("aria-label").Should().Be("Verification code");
        cut.FindAll(".tm-otp-input__cell").Should().HaveCount(6);
    }

    [Fact]
    public void Each_Cell_Has_Localized_Aria_Label_With_Position()
    {
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        Cell(cut, 0).GetAttribute("aria-label").Should().Be("Digit 1 of 6");
        Cell(cut, 5).GetAttribute("aria-label").Should().Be("Digit 6 of 6");
    }

    [Fact]
    public void Alphanumeric_Kind_Uses_Character_Labels()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 4)
            .Add(c => c.Kind, OtpInputKind.Alphanumeric));
        Cell(cut, 0).GetAttribute("aria-label").Should().Be("Character 1 of 4");
    }

    [Fact]
    public void Autocomplete_OneTimeCode_On_First_Cell_And_Off_On_The_Rest()
    {
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        Cell(cut, 0).GetAttribute("autocomplete").Should().Be("one-time-code");
        for (var i = 1; i < 6; i++)
        {
            Cell(cut, i).GetAttribute("autocomplete").Should().Be("off");
        }
    }

    [Fact]
    public void InputMode_Follows_Kind()
    {
        var numeric = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        numeric.FindAll(".tm-otp-input__cell")
            .Should().OnlyContain(c => c.GetAttribute("inputmode") == "numeric");

        var alpha = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Kind, OtpInputKind.Alphanumeric));
        alpha.FindAll(".tm-otp-input__cell")
            .Should().OnlyContain(c => c.GetAttribute("inputmode") == "text");
    }

    [Fact]
    public void Numeric_Cells_Carry_Pattern_And_No_Autocapitalize()
    {
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(c =>
            c.GetAttribute("pattern") == "[0-9]*" && !c.HasAttribute("autocapitalize"));
    }

    [Fact]
    public void Alphanumeric_Cells_Carry_Autocapitalize_Characters_And_No_Pattern()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Kind, OtpInputKind.Alphanumeric));
        cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(c =>
            c.GetAttribute("autocapitalize") == "characters" && !c.HasAttribute("pattern"));
    }

    [Fact]
    public void Every_Cell_Disables_Autocorrect_And_Spellcheck()
    {
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(c =>
            c.GetAttribute("autocorrect") == "off" && c.GetAttribute("spellcheck") == "false");
    }

    [Fact]
    public void No_Cell_Renders_Maxlength()
    {
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(c => !c.HasAttribute("maxlength"));
    }

    [Fact]
    public void Cells_Use_Deterministic_Ids_And_InputId_Points_At_The_First()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Id, "otp"));
        for (var i = 0; i < 6; i++)
        {
            Cell(cut, i).GetAttribute("id").Should().Be($"otp-cell-{i}");
        }

        cut.Instance.InputId.Should().Be("otp-cell-0");
    }

    [Fact]
    public void AriaLabel_Overrides_The_Localized_Default()
    {
        var cut = Render<TmOtpInput>(p => p.Add(c => c.AriaLabel, "Two-factor code"));
        cut.Find("[role='group']").GetAttribute("aria-label").Should().Be("Two-factor code");
    }

    [Fact]
    public void AriaLabelledBy_Wins_Over_AriaLabel_And_Omits_It()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.AriaLabel, "Ignored")
            .Add(c => c.AriaLabelledBy, "otp-heading"));
        var group = cut.Find("[role='group']");
        group.GetAttribute("aria-labelledby").Should().Be("otp-heading");
        group.HasAttribute("aria-label").Should().BeFalse();
    }

    [Fact]
    public void AriaDescribedBy_Renders_On_The_Group()
    {
        var cut = Render<TmOtpInput>(p => p.Add(c => c.AriaDescribedBy, "otp-hint"));
        cut.Find("[role='group']").GetAttribute("aria-describedby").Should().Be("otp-hint");
    }

    [Fact]
    public async Task Numeric_Kind_Ignores_Letters_Without_Raising_ValueChanged()
    {
        SetupOtpModule();
        var raised = 0;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.ValueChanged, _ => raised++));
        await CellInputAsync(cut, 0, "a");
        Cell(cut, 0).GetAttribute("value").Should().BeEmpty();
        raised.Should().Be(0);
    }

    [Fact]
    public async Task Alphanumeric_Kind_Normalizes_To_Uppercase()
    {
        SetupOtpModule();
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Kind, OtpInputKind.Alphanumeric));
        await CellInputAsync(cut, 0, "a");
        Cell(cut, 0).GetAttribute("value").Should().Be("A");
    }

    [Fact]
    public async Task Clearing_A_Filled_Cell_Raises_ValueChanged_Without_Firing_OnComplete()
    {
        SetupOtpModule();
        var completions = 0;
        string? value = null;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 4)
            .Add(c => c.ValueChanged, v => value = v)
            .Add(c => c.OnComplete, _ => completions++));
        await CellInputAsync(cut, 0, "1");
        value.Should().Be("1");

        await CellInputAsync(cut, 0, string.Empty);
        Cell(cut, 0).GetAttribute("value").Should().BeEmpty();
        value.Should().BeEmpty();
        completions.Should().Be(0);
    }

    [Fact]
    public async Task Clearing_An_Already_Empty_Cell_Is_A_NoOp()
    {
        SetupOtpModule();
        var raised = 0;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.ValueChanged, _ => raised++));
        await CellInputAsync(cut, 0, string.Empty);
        raised.Should().Be(0);
    }

    [Fact]
    public void GroupSize_Renders_AriaHidden_Separators_Between_Groups()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.GroupSize, 2));
        var separators = cut.FindAll(".tm-otp-input__separator");
        separators.Should().HaveCount(2);
        separators.Should().OnlyContain(s => s.GetAttribute("aria-hidden") == "true");
    }

    [Fact]
    public void GroupSize_Zero_Renders_No_Separators()
    {
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        cut.FindAll(".tm-otp-input__separator").Should().BeEmpty();
    }

    [Fact]
    public void Masked_Renders_Password_Type_On_Every_Cell()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Masked, true));
        cut.FindAll(".tm-otp-input__cell")
            .Should().OnlyContain(c => c.GetAttribute("type") == "password");
    }

    [Fact]
    public void Default_Cells_Render_Text_Type()
    {
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        cut.FindAll(".tm-otp-input__cell")
            .Should().OnlyContain(c => c.GetAttribute("type") == "text");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    public void Length_Outside_4_To_10_Throws(int length)
    {
        var act = () => Render<TmOtpInput>(p => p.Add(c => c.Length, length));
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("Length");
    }

    [Fact]
    public async Task OnComplete_Fires_Exactly_Once_When_Code_Becomes_Complete()
    {
        SetupOtpModule();
        var completions = 0;
        string? completed = null;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 4)
            .Add(c => c.OnComplete, v => { completions++; completed = v; }));
        for (var i = 0; i < 4; i++)
        {
            await CellInputAsync(cut, i, $"{i + 1}");
        }

        completions.Should().Be(1);
        completed.Should().Be("1234");
    }

    [Fact]
    public async Task OnComplete_Fires_Again_After_Edit_Makes_Code_Incomplete_Then_Complete()
    {
        SetupOtpModule();
        var completions = 0;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 4)
            .Add(c => c.OnComplete, _ => completions++));
        for (var i = 0; i < 4; i++)
        {
            await CellInputAsync(cut, i, $"{i + 1}");
        }

        await CellInputAsync(cut, 3, string.Empty);
        await CellInputAsync(cut, 3, "9");
        completions.Should().Be(2);
    }

    [Fact]
    public async Task OnComplete_Handler_Resetting_Value_Through_Bound_Host_Fires_Again_On_Reentry()
    {
        SetupOtpModule();
        var completions = 0;
        var value = string.Empty;
        IRenderedComponent<TmOtpInput> cut = null!;
        cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 4)
            .Add(c => c.Value, value)
            .Add(c => c.ValueChanged, v => value = v)
            .Add(c => c.OnComplete, async _ =>
            {
                completions++;
                // The bound host resets the code inside the handler; the component re-renders with
                // Value="" while OnComplete is still on the stack. Completing the code afterwards
                // must fire OnComplete again.
                value = string.Empty;
                await ReRenderAsync(cut, new Dictionary<string, object?>
                {
                    [nameof(TmOtpInput.Value)] = value,
                    [nameof(TmOtpInput.ValueChanged)] =
                        EventCallback.Factory.Create<string>(cut.Instance, v => value = v),
                });
            }));

        for (var i = 0; i < 4; i++)
        {
            await CellInputAsync(cut, i, $"{i + 1}");
        }

        completions.Should().Be(1);
        value.Should().BeEmpty();

        for (var i = 0; i < 4; i++)
        {
            await CellInputAsync(cut, i, $"{i + 5}");
        }

        completions.Should().Be(2);
    }

    [Fact]
    public async Task Value_Prefill_Does_Not_Fire_OnComplete()
    {
        SetupOtpModule();
        var completions = 0;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Value, "123456")
            .Add(c => c.OnComplete, _ => completions++));

        completions.Should().Be(0);

        // Editing the pre-filled code behaves normally afterwards.
        await CellInputAsync(cut, 0, string.Empty);
        await CellInputAsync(cut, 0, "7");
        completions.Should().Be(1);
    }

    [Fact]
    public async Task ValueChanged_Raises_Concatenated_Value()
    {
        SetupOtpModule();
        string? value = null;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 4)
            .Add(c => c.ValueChanged, v => value = v));
        await CellInputAsync(cut, 0, "7");
        value.Should().Be("7");
        await CellInputAsync(cut, 1, "3");
        value.Should().Be("73");
    }

    [Fact]
    public void Value_Parameter_Populates_Cells()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Value, "123"));
        Cell(cut, 0).GetAttribute("value").Should().Be("1");
        Cell(cut, 2).GetAttribute("value").Should().Be("3");
        Cell(cut, 3).GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public async Task Length_Change_Preserves_Filled_Cells()
    {
        SetupOtpModule();
        var value = string.Empty;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Value, value)
            .Add(c => c.ValueChanged, v => value = v));
        await CellInputAsync(cut, 0, "1");
        await CellInputAsync(cut, 1, "2");

        await ReRenderAsync(cut, new Dictionary<string, object?>
        {
            [nameof(TmOtpInput.Length)] = 8,
            [nameof(TmOtpInput.Value)] = value,
            [nameof(TmOtpInput.ValueChanged)] =
                EventCallback.Factory.Create<string>(cut.Instance, v => value = v),
        });

        cut.FindAll(".tm-otp-input__cell").Should().HaveCount(8);
        Cell(cut, 0).GetAttribute("value").Should().Be("1");
        Cell(cut, 1).GetAttribute("value").Should().Be("2");
        Cell(cut, 7).GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public async Task DistributeFrom_Fills_Consecutive_Cells_And_Moves_Focus_Behind_Them()
    {
        var module = SetupOtpModule();
        string? value = null;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.ValueChanged, v => value = v));
        await DistributeAsync(cut, 0, "1234");
        value.Should().Be("1234");
        for (var i = 0; i < 4; i++)
        {
            Cell(cut, i).GetAttribute("value").Should().Be($"{i + 1}");
        }

        FocusedIndexes(module).Should().Equal(4);
    }

    [Fact]
    public async Task DistributeFrom_Filters_Per_Kind()
    {
        SetupOtpModule();
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        await DistributeAsync(cut, 0, "a1b2");
        Cell(cut, 0).GetAttribute("value").Should().Be("1");
        Cell(cut, 1).GetAttribute("value").Should().Be("2");
    }

    [Fact]
    public async Task DistributeFrom_Stops_At_The_Last_Cell()
    {
        SetupOtpModule();
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 4));
        await DistributeAsync(cut, 2, "56789");
        Cell(cut, 2).GetAttribute("value").Should().Be("5");
        Cell(cut, 3).GetAttribute("value").Should().Be("6");
    }

    [Fact]
    public void Disabled_Renders_Disabled_Cells_And_Root_Modifier()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Disabled, true));
        cut.Find(".tm-otp-input").ClassList.Should().Contain("tm-otp-input--disabled");
        cut.FindAll(".tm-otp-input__cell")
            .Should().OnlyContain(c => c.HasAttribute("disabled"));
    }

    [Fact]
    public void Root_Carries_TestId()
    {
        var cut = Render<TmOtpInput>();
        cut.Find("[data-testid='otp-input']").Should().NotBeNull();
    }

    [Fact]
    public void Czech_Localization_Uses_Czech_Labels()
    {
        UseCzechLocalization();
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        cut.Find("[role='group']").GetAttribute("aria-label").Should().Be("Ověřovací kód");
        Cell(cut, 0).GetAttribute("aria-label").Should().Be("Číslice 1 ze 6");
    }

    // ── Phase 2: paste, autofill, error and verified state ──────────────────

    [Fact]
    public async Task Paste_Into_Any_Cell_Distributes_Characters_From_That_Cell()
    {
        SetupOtpModule();
        string? value = null;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.ValueChanged, v => value = v));
        await DistributeAsync(cut, 2, "3456");
        value.Should().Be("3456");
        Cell(cut, 0).GetAttribute("value").Should().BeEmpty();
        for (var i = 0; i < 4; i++)
        {
            Cell(cut, i + 2).GetAttribute("value").Should().Be($"{i + 3}");
        }
    }

    [Fact]
    public async Task Paste_Cleans_Spaces_And_Dashes()
    {
        SetupOtpModule();
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        await DistributeAsync(cut, 0, "12 34-5");
        for (var i = 0; i < 5; i++)
        {
            Cell(cut, i).GetAttribute("value").Should().Be($"{i + 1}");
        }
    }

    [Fact]
    public async Task Autofill_Of_The_Whole_Code_Fires_OnComplete()
    {
        SetupOtpModule();
        var completions = 0;
        string? completed = null;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.OnComplete, v => { completions++; completed = v; }));
        await DistributeAsync(cut, 0, "123456");
        completions.Should().Be(1);
        completed.Should().Be("123456");
    }

    [Fact]
    public void Invalid_Sets_AriaInvalid_And_DescribedBy_On_The_Group()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Id, "otp")
            .Add(c => c.Invalid, true)
            .Add(c => c.ErrorText, "Wrong code"));
        cut.Find(".tm-otp-input").ClassList.Should().Contain("tm-otp-input--invalid");
        var group = cut.Find("[role='group']");
        group.GetAttribute("aria-invalid").Should().Be("true");
        group.GetAttribute("aria-describedby").Should().Be("otp-error");
        cut.Find("#otp-error").TextContent.Should().Be("Wrong code");
    }

    [Fact]
    public void Invalid_Combines_Host_DescribedBy_With_The_Error_Id()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Id, "otp")
            .Add(c => c.Invalid, true)
            .Add(c => c.ErrorText, "Wrong code")
            .Add(c => c.AriaDescribedBy, "otp-hint"));
        cut.Find("[role='group']").GetAttribute("aria-describedby").Should().Be("otp-hint otp-error");
    }

    [Fact]
    public void Invalid_Without_ErrorText_Renders_No_Error_Element_And_Keeps_Host_DescribedBy()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Id, "otp")
            .Add(c => c.Invalid, true)
            .Add(c => c.AriaDescribedBy, "otp-hint"));
        cut.FindAll(".tm-otp-input__error").Should().BeEmpty();
        cut.Find("[role='group']").GetAttribute("aria-describedby").Should().Be("otp-hint");
    }

    [Fact]
    public void Invalid_Announces_The_Error_Politely()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Invalid, true)
            .Add(c => c.ErrorText, "Wrong code"));
        var live = cut.Find("[role='status'][aria-live='polite']");
        live.TextContent.Should().Be("Wrong code");
        live.ClassList.Should().Contain("tm-sr-only");
    }

    [Fact]
    public async Task Verified_Blocks_Editing_And_Shows_The_Localized_Status()
    {
        SetupOtpModule();
        string? value = null;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Verified, true)
            .Add(c => c.ValueChanged, v => value = v));
        cut.Find(".tm-otp-input").ClassList.Should().Contain("tm-otp-input--verified");
        cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(c =>
            c.HasAttribute("readonly") && c.GetAttribute("aria-readonly") == "true");

        await CellInputAsync(cut, 0, "1");
        Cell(cut, 0).GetAttribute("value").Should().BeEmpty();
        value.Should().BeNull();

        cut.Find(".tm-otp-input__status span").TextContent.Should().Be("Code verified");
        cut.FindAll("[role='status'][aria-live='polite']")
            .Should().Contain(s => s.TextContent == "Code verified");
    }

    [Fact]
    public void Verified_Czech_Localization_Uses_Czech_Status()
    {
        UseCzechLocalization();
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Verified, true));
        cut.Find(".tm-otp-input__status span").TextContent.Should().Be("Kód ověřen");
    }

    [Fact]
    public void VerifiedText_Overrides_The_Localized_Status_In_The_Row_And_The_Live_Region()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Verified, true)
            .Add(c => c.VerifiedText, "Verified by the host"));
        cut.Find(".tm-otp-input__status span").TextContent.Should().Be("Verified by the host");
        cut.Find("[role='status'][aria-live='polite']").TextContent.Should().Be("Verified by the host",
            "the host wording is announced, not the localized default");
    }

    [Fact]
    public void ReadOnly_Renders_ReadOnly_Cells_And_Root_Modifier()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.ReadOnly, true));
        cut.Find(".tm-otp-input").ClassList.Should().Contain("tm-otp-input--readonly");
        cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(c =>
            c.HasAttribute("readonly") && c.GetAttribute("aria-readonly") == "true");
    }

    [Fact]
    public async Task ReadOnly_Blocks_Edits()
    {
        SetupOtpModule();
        string? value = null;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.ValueChanged, v => value = v));
        await CellInputAsync(cut, 0, "1");
        Cell(cut, 0).GetAttribute("value").Should().BeEmpty();
        value.Should().BeNull();
    }

    [Fact]
    public async Task Disabled_Blocks_Edits()
    {
        SetupOtpModule();
        string? value = null;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Disabled, true)
            .Add(c => c.ValueChanged, v => value = v));
        await CellInputAsync(cut, 0, "1");
        Cell(cut, 0).GetAttribute("value").Should().BeEmpty();
        value.Should().BeNull();
    }

    [Fact]
    public async Task FocusAsync_Focuses_The_First_Empty_Cell()
    {
        var module = SetupOtpModule();
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        await CellInputAsync(cut, 0, "1");
        await cut.InvokeAsync(() => cut.Instance.FocusAsync());
        FocusedIndexes(module).Should().Equal(1);
    }

    [Fact]
    public async Task FocusAsync_NoOp_When_Disabled()
    {
        var module = SetupOtpModule();
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Disabled, true));
        await cut.InvokeAsync(() => cut.Instance.FocusAsync());
        FocusedIndexes(module).Should().BeEmpty();
    }

    [Fact]
    public async Task ClearAsync_Clears_Cells_Syncs_Dom_And_ReArms_OnComplete()
    {
        var module = SetupOtpModule();
        var completions = 0;
        string? value = null;
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 4)
            .Add(c => c.ValueChanged, v => value = v)
            .Add(c => c.OnComplete, _ => completions++));
        for (var i = 0; i < 4; i++)
        {
            await CellInputAsync(cut, i, $"{i + 1}");
        }

        await cut.InvokeAsync(() => cut.Instance.ClearAsync());
        value.Should().BeEmpty();
        for (var i = 0; i < 4; i++)
        {
            Cell(cut, i).GetAttribute("value").Should().BeEmpty();
        }

        module.Invocations.Where(i => i.Identifier == "setCellValues").Should().HaveCount(1);
        module.Invocations.Where(i => i.Identifier == "setCellValues")
            .Single().Arguments[1].Should().BeEquivalentTo(new[] { "", "", "", "" });

        for (var i = 0; i < 4; i++)
        {
            await CellInputAsync(cut, i, $"{i + 5}");
        }
        completions.Should().Be(2);
    }

    [Fact]
    public void Live_Region_Exists_On_First_Render_Empty()
    {
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        var live = cut.Find("[role='status'][aria-live='polite']");
        live.ClassList.Should().Contain("tm-sr-only");
        live.TextContent.Should().BeEmpty();
        cut.FindAll("[role='status'][aria-live='polite']").Should().HaveCount(1);
    }

    [Fact]
    public void Invalid_Sets_AriaInvalid_And_Error_DescribedBy_On_Every_Cell()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Id, "otp")
            .Add(c => c.Invalid, true)
            .Add(c => c.ErrorText, "Wrong code"));
        cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(c =>
            c.GetAttribute("aria-invalid") == "true" && c.GetAttribute("aria-describedby") == "otp-error");
    }

    [Fact]
    public void Invalid_Cell_DescribedBy_Combines_Host_Reference_With_The_Error_Id()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Id, "otp")
            .Add(c => c.Invalid, true)
            .Add(c => c.ErrorText, "Wrong code")
            .Add(c => c.AriaDescribedBy, "otp-hint"));
        cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(c =>
            c.GetAttribute("aria-describedby") == "otp-hint otp-error");
    }

    [Fact]
    public void Invalid_Without_ErrorText_Keeps_Cells_Without_Error_DescribedBy()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Invalid, true));
        cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(c =>
            c.GetAttribute("aria-invalid") == "true" && !c.HasAttribute("aria-describedby"));
        // Exactly one live region, empty (a TmFormField-owned message is not duplicated here).
        var live = cut.Find("[role='status'][aria-live='polite']");
        live.TextContent.Should().BeEmpty();
    }

    [Fact]
    public void Verified_Adds_Status_Id_To_Every_Cell_DescribedBy()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Id, "otp")
            .Add(c => c.Verified, true));
        cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(c =>
            c.GetAttribute("aria-describedby") == "otp-status"
            && !c.HasAttribute("aria-invalid"));
    }

    [Fact]
    public void Verified_Status_Renders_Check_Icon_And_Span()
    {
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Id, "otp")
            .Add(c => c.Verified, true));
        var status = cut.Find(".tm-otp-input__status");
        status.GetAttribute("id").Should().Be("otp-status");
        status.QuerySelector("span")!.TextContent.Should().Be("Code verified");
        status.QuerySelector(".tm-icon").Should().NotBeNull();
        // Exactly one live region carries the announced status.
        cut.FindAll("[role='status'][aria-live='polite']").Should().HaveCount(1);
    }

    [Fact]
    public async Task ShowErrorAsync_Restarts_Shake_And_Refreshes_The_Live_Text()
    {
        var module = SetupOtpModule();
        var cut = Render<TmOtpInput>(p => p
            .Add(c => c.Length, 6)
            .Add(c => c.Invalid, true)
            .Add(c => c.ErrorText, "Wrong code"));
        cut.Find("[role='status'][aria-live='polite']").TextContent.Should().Be("Wrong code");

        await cut.InvokeAsync(() => cut.Instance.ShowErrorAsync());

        module.Invocations.Where(i => i.Identifier == "restartShake").Should().HaveCount(1);
        cut.Find("[role='status'][aria-live='polite']").TextContent.Should().Be("Wrong code");
    }

    [Fact]
    public async Task DistributeFrom_Syncs_The_Dom_With_One_Batched_setCellValues_Call()
    {
        var module = SetupOtpModule();
        var cut = Render<TmOtpInput>(p => p.Add(c => c.Length, 6));
        await DistributeAsync(cut, 0, "1234");
        module.Invocations.Where(i => i.Identifier == "setCellValues").Should().HaveCount(1);
        module.Invocations.Where(i => i.Identifier == "setCellValues")
            .Single().Arguments[1].Should().BeEquivalentTo(new[] { "1", "2", "3", "4", "", "" });
    }
}
