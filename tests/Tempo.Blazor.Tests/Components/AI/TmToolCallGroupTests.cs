using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of <see cref="TmToolCallGroup"/> (plan 7f21f26b, Fáze 4): the collapsed summary
/// ("3 nástroje · 1 selhal · 4,2 s"), the plural-correct wording, the omitted failure segment when
/// nothing failed, the omitted duration while a call still runs, and the disclosure that renders the
/// cards.
/// </summary>
public sealed class TmToolCallGroupTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static AiToolCall Call(
        string id,
        AiToolCallStatus status,
        double? durationSeconds = null,
        string name = "search")
    {
        var call = new AiToolCall
        {
            Id = id,
            Name = name,
            DisplayName = name,
            Status = status,
        };

        call.StartedAt = Start;
        if (durationSeconds is { } duration)
        {
            call.CompletedAt = Start.AddSeconds(duration);
        }

        return call;
    }

    private FakeTimeProvider UseFakeClock()
    {
        var time = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(time);
        return time;
    }

    private static string Summary(IRenderedComponent<TmToolCallGroup> cut)
        => cut.Find(".tm-tool-call-group__summary").TextContent.Trim();

    [Fact]
    public void Summary_CountsTheTools_TheFailuresAndTheTotalDuration()
    {
        UseFakeClock();
        var calls = new[]
        {
            Call("c1", AiToolCallStatus.Succeeded, 1.0),
            Call("c2", AiToolCallStatus.Succeeded, 1.2),
            Call("c3", AiToolCallStatus.Failed, 2.0),
        };

        var cut = Render<TmToolCallGroup>(parameters => parameters.Add(group => group.ToolCalls, calls));

        Summary(cut).Should().Be("3 tools · 1 failed · 4.2 s");
    }

    [Fact]
    public void Summary_OmitsTheFailureSegment_WhenNothingFailed()
    {
        UseFakeClock();
        var calls = new[]
        {
            Call("c1", AiToolCallStatus.Succeeded, 1.0),
            Call("c2", AiToolCallStatus.Succeeded, 1.5),
        };

        var cut = Render<TmToolCallGroup>(parameters => parameters.Add(group => group.ToolCalls, calls));

        Summary(cut).Should().Be("2 tools · 2.5 s");
    }

    [Fact]
    public void Summary_OmitsTheDuration_WhileACallIsStillRunning()
    {
        UseFakeClock();
        var calls = new[]
        {
            Call("c1", AiToolCallStatus.Succeeded, 1.0),
            Call("c2", AiToolCallStatus.Running),
        };

        var cut = Render<TmToolCallGroup>(parameters => parameters.Add(group => group.ToolCalls, calls));

        Summary(cut).Should().Be("2 tools",
            "a total that still grows is not a duration — the running card carries its own live timer");
    }

    [Fact]
    public void Summary_UsesTheCzechPluralForms_AndTheCzechDecimalSeparator()
    {
        UseFakeClock();
        Services.AddSingleton<ITmLocalizer>(new MockTmLocalizer(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TmToolCallGroup_Tools_One"] = "{0} nástroj",
                ["TmToolCallGroup_Tools_Few"] = "{0} nástroje",
                ["TmToolCallGroup_Tools_Many"] = "{0} nástroje",
                ["TmToolCallGroup_Tools_Other"] = "{0} nástrojů",
                ["TmToolCallGroup_Failed_One"] = "{0} selhal",
                ["TmToolCallGroup_Failed_Few"] = "{0} selhaly",
                ["TmToolCallGroup_Failed_Many"] = "{0} selhalo",
                ["TmToolCallGroup_Failed_Other"] = "{0} selhalo",
                ["TmAiDuration_Seconds"] = "{0} s",
                ["TmAiDuration_Minutes"] = "{0} min {1} s",
                ["TmAiDuration_Hours"] = "{0} h {1} min",
                ["TmToolCallCard_Status_Queued"] = "Ve frontě",
                ["TmToolCallCard_Status_Running"] = "Probíhá",
                ["TmToolCallCard_Status_Succeeded"] = "Hotovo",
                ["TmToolCallCard_Status_Failed"] = "Selhalo",
                ["TmToolCallCard_Status_Cancelled"] = "Zrušeno",
                ["TmToolCallCard_Arguments"] = "Argumenty",
                ["TmToolCallCard_Result"] = "Výsledek",
                ["TmToolCallCard_Error"] = "Chyba",
                ["TmToolCallCard_Redacted"] = "Podrobnosti jsou skryté",
                ["TmToolCallCard_CopyFor"] = "Kopírovat podrobnosti nástroje {0}",
                ["TmToolCallCard_Copied"] = "Zkopírováno",
                ["TmToolCallCard_Retry"] = "Zkusit znovu",
                ["TmToolCallCard_Announcement"] = "{0}: {1}",
            },
            System.Globalization.CultureInfo.GetCultureInfo("cs")));

        var calls = new[]
        {
            Call("c1", AiToolCallStatus.Succeeded, 1.0),
            Call("c2", AiToolCallStatus.Succeeded, 1.2),
            Call("c3", AiToolCallStatus.Failed, 2.0),
        };

        var cut = Render<TmToolCallGroup>(parameters => parameters.Add(group => group.ToolCalls, calls));

        Summary(cut).Should().Be("3 nástroje · 1 selhal · 4,2 s");
    }

    [Fact]
    public void TheCards_AreRenderedOnlyWhenExpanded()
    {
        UseFakeClock();
        var calls = new[]
        {
            Call("c1", AiToolCallStatus.Succeeded, 1.0),
            Call("c2", AiToolCallStatus.Succeeded, 1.0),
        };

        var cut = Render<TmToolCallGroup>(parameters => parameters.Add(group => group.ToolCalls, calls));

        var toggle = cut.Find(".tm-tool-call-group__toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        toggle.GetAttribute("aria-controls").Should().BeNull(
            "the cards are not rendered while the run is collapsed, so the id would dangle");
        cut.FindAll(".tm-tool-call").Should().BeEmpty();

        toggle.Click();

        cut.Find(".tm-tool-call-group__toggle").GetAttribute("aria-expanded").Should().Be("true");
        var bodyId = cut.Find(".tm-tool-call-group__toggle").GetAttribute("aria-controls");
        bodyId.Should().NotBeNullOrWhiteSpace("an open disclosure names the region it owns");
        cut.Find($"[id='{bodyId}']").ClassList.Should().Contain("tm-tool-call-group__body");
        cut.FindAll(".tm-tool-call").Should().HaveCount(2, "one card per call, in order");
    }

    [Fact]
    public void DefaultExpanded_RendersTheCardsImmediately()
    {
        UseFakeClock();
        var cut = Render<TmToolCallGroup>(parameters => parameters
            .Add(group => group.ToolCalls, new[] { Call("c1", AiToolCallStatus.Succeeded, 1.0) })
            .Add(group => group.DefaultExpanded, true));

        cut.FindAll(".tm-tool-call").Should().HaveCount(1);
    }

    [Fact]
    public void Retry_IsForwardedToTheCards_WithTheCallThatFailed()
    {
        UseFakeClock();
        AiToolCall? retried = null;
        var calls = new[]
        {
            Call("c1", AiToolCallStatus.Failed, 1.0, "search"),
            Call("c2", AiToolCallStatus.Succeeded, 1.0, "fetch"),
        };

        var cut = Render<TmToolCallGroup>(parameters => parameters
            .Add(group => group.ToolCalls, calls)
            .Add(group => group.DefaultExpanded, true)
            .Add(group => group.OnRetry, (AiToolCall call) => retried = call));

        cut.Find(".tm-tool-call__retry").Click();

        retried.Should().BeSameAs(calls[0], "the group tells the host WHICH call to run again");
    }

    [Fact]
    public void AnEmptyList_RendersNothing()
    {
        UseFakeClock();
        var cut = Render<TmToolCallGroup>(parameters => parameters
            .Add(group => group.ToolCalls, Array.Empty<AiToolCall>()));

        cut.FindAll(".tm-tool-call-group").Should().BeEmpty();
    }
}
