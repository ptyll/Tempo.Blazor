using System.Linq.Expressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// Plan 1c95309c Fáze 6 — ValueExpression, Id, InputId and AriaDescribedBy.
/// Segments without the new parameters keep today's generated ids.
/// </summary>
public class TmTimePickerIdentityTests : LocalizationTestBase
{
    private sealed class AppointmentModel
    {
        public TimeOnly? Start { get; set; }
    }

    [Fact]
    public void Segments_WithoutId_KeepGeneratedInputIdPrefix()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Label, "Meeting"));

        var hoursId = cut.Find(".tm-time-seg--hours").GetAttribute("id");
        hoursId.Should().NotBeNullOrWhiteSpace();
        hoursId.Should().StartWith("tm-time-picker-input-");
        cut.Find(".tm-time-picker").GetAttribute("id").Should().BeNull();
    }

    [Fact]
    public void Id_SetsRootAndDefaultInputId()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Id, "start")
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Label, "Meeting"));

        cut.Find(".tm-time-picker").GetAttribute("id").Should().Be("start");
        cut.Find(".tm-time-seg--hours").GetAttribute("id").Should().Be("start-input");
        cut.Find(".tm-picker-label").GetAttribute("for").Should().Be("start-input");
    }

    [Fact]
    public void InputId_WinsOverGeneratedDefault()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Id, "start")
            .Add(c => c.InputId, "hours-field")
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Label, "Meeting"));

        cut.Find(".tm-time-seg--hours").GetAttribute("id").Should().Be("hours-field");
    }

    [Fact]
    public void AriaDescribedBy_IsForwardedToSegments()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.AriaDescribedBy, "hint-id")
            .Add(c => c.Label, "Meeting"));

        cut.Find(".tm-time-seg--hours").GetAttribute("aria-describedby").Should().Contain("hint-id");
    }

    [Fact]
    public void ValueExpression_NotifiesEditContext()
    {
        var model = new AppointmentModel { Start = new TimeOnly(10, 0) };
        var cut = Render(builder =>
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.Model), model);
            builder.AddAttribute(2, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(_ =>
                (RenderTreeBuilder b) =>
                {
                    b.OpenComponent<TmTimePicker>(0);
                    b.AddAttribute(1, nameof(TmTimePicker.HourCycle), TimeHourCycle.H23);
                    b.AddAttribute(2, nameof(TmTimePicker.Value), model.Start);
                    b.AddAttribute(3, nameof(TmTimePicker.ValueChanged),
                        EventCallback.Factory.Create<TimeOnly?>(this, v => model.Start = v));
                    b.AddAttribute(4, nameof(TmTimePicker.ValueExpression),
                        (Expression<Func<TimeOnly?>>)(() => model.Start));
                    b.CloseComponent();
                }));
            builder.CloseComponent();
        });

        var changed = new List<string>();
        cut.FindComponent<EditForm>().Instance.EditContext!.OnFieldChanged +=
            (_, e) => changed.Add(e.FieldIdentifier.FieldName);

        cut.Find(".tm-time-seg--hours").Change("11");

        model.Start.Should().Be(new TimeOnly(11, 0));
        changed.Should().ContainSingle().Which.Should().Be(nameof(AppointmentModel.Start));
    }
}
