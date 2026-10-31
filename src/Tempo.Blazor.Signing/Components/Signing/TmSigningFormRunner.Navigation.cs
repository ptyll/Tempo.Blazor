using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <content>
/// Step navigation of the wizard (phase 3 review split — a pure move from
/// <c>TmSigningFormRunner.razor.cs</c>): the Previous / Next / Complete gating, the Skip
/// affordance and the direct jump from the permanent field list. Every landing on a DIFFERENT
/// step bumps the focus signal so the step shell moves focus onto the new title — and (Signing
/// redesign phase 5) lands the mobile sheet in the FULL edit state and records the pending
/// auto-scroll that <c>OnAfterRenderAsync</c> performs once the render has applied the new state.
/// </content>
public partial class TmSigningFormRunner
{
    private bool IsPreviousDisabled => IsLoading || CurrentStepIndex == 0;

    private bool IsNextDisabled => IsLoading || IsCompleting || CurrentStepIndex >= Plan.Steps.Count - 1;

    /// <summary>
    /// Skip renders ONLY for an OPTIONAL current field that is NOT the last step (phase 3 review):
    /// skipping the final step has nowhere to go — with required work still missing the disabled
    /// Complete already explains what is left, so a "Skip" there would be a dead control.
    /// </summary>
    private bool ShowSkipButton => CurrentStep is not null
        && !CurrentStep.Field.Required
        && CurrentStepIndex < Plan.Steps.Count - 1;

    private async Task SelectStep(int index)
    {
        var clamped = Math.Clamp(index, 0, Math.Max(0, Plan.Steps.Count - 1));
        if (clamped != _currentStepIndex)
        {
            _stepFocusSignal.Version++;
        }

        _currentStepIndex = clamped;
        ClearValidationMessages();
        LandMobileSheetOnFull();
        RequestScrollToActiveField();
    }

    private async Task GoNextAsync()
    {
        if (IsLoading || CurrentStep is null)
        {
            return;
        }

        if (!IsStepComplete(CurrentStep))
        {
            _validationMessage = GetRequiredMessage(CurrentStep);
            var invalidIndex = FindFirstInvalidRequiredStep();
            if (invalidIndex != _currentStepIndex)
            {
                _stepFocusSignal.Version++;
            }

            _currentStepIndex = invalidIndex;
            LandMobileSheetOnFull();
            RequestScrollToActiveField();
            return;
        }

        await OnStepSubmit.InvokeAsync(CurrentStep);
        if (_currentStepIndex < Plan.Steps.Count - 1)
        {
            _currentStepIndex++;
            _stepFocusSignal.Version++;
            LandMobileSheetOnFull();
            RequestScrollToActiveField();
        }
    }

    private async Task GoPreviousAsync()
    {
        if (!IsPreviousDisabled)
        {
            _currentStepIndex--;
            _stepFocusSignal.Version++;
            ClearValidationMessages();
            LandMobileSheetOnFull();
            RequestScrollToActiveField();
        }
    }

    private async Task SkipCurrentAsync()
    {
        // The skip affordance renders only for an optional current field (phase 3); the guard
        // keeps a stale click (field became required between renders) a no-op.
        if (IsLoading || IsCompleting || CurrentStep is null || CurrentStep.Field.Required
            || _currentStepIndex >= Plan.Steps.Count - 1)
        {
            return;
        }

        _currentStepIndex++;
        _stepFocusSignal.Version++;
        ClearValidationMessages();
        LandMobileSheetOnFull();
        RequestScrollToActiveField();
    }

    private bool IsStepComplete(SigningStepItem step)
    {
        if (!step.Field.Required && step.Fields.All(field => !field.Required))
        {
            return true;
        }

        return step.Fields.Where(field => field.Required || step.Field.Required).All(IsFieldComplete);
    }

    private int FindFirstInvalidRequiredStep()
    {
        var index = Plan.Steps.FindIndex(step => step.Fields.Any(field => field.Required && !IsFieldComplete(field)));
        return index >= 0 ? index : CurrentStepIndex;
    }
}
