using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Pins the versioning and feedback half of <see cref="AiMessage"/> — the state
/// <c>TmAiMessage</c> renders its version switcher and its thumb buttons from: a message keeps the
/// superseded regenerations in <see cref="AiMessage.Versions"/>, the live content stays in
/// <see cref="AiMessage.Parts"/> (the last version), and <see cref="AiMessage.ActiveVersionIndex"/>
/// selects which one the UI shows. Out-of-range indices clamp instead of throwing, because a host
/// that swaps the version list and the index in two renders must never crash the conversation.
/// </summary>
public sealed class AiMessageVersioningTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private static AiMessage Assistant(params string[] texts)
    {
        var message = new AiMessage { Id = "assistant-1", Role = AiMessageRole.Assistant, CreatedAt = T0 };
        foreach (var text in texts)
        {
            message.Parts.Add(new AiTextPart(text));
        }

        return message;
    }

    [Fact]
    public void FreshMessage_HasOneVersion_AndRendersItsOwnParts()
    {
        var message = Assistant("answer");

        message.VersionCount.Should().Be(1);
        message.ActiveVersionIndex.Should().BeNull("null means the newest version");
        message.EffectiveVersionIndex.Should().Be(0);
        message.ActiveParts.Should().ContainSingle().Which.Should().BeOfType<AiTextPart>();
        message.ActiveText.Should().Be("answer");
    }

    [Fact]
    public void SupersededVersions_AppendAndTheLivePartsStayTheLastVersion()
    {
        var message = Assistant("third");
        message.Versions.Add(new AiMessageVersion([new AiTextPart("first")]));
        message.Versions.Add(new AiMessageVersion([new AiTextPart("second")]));

        message.VersionCount.Should().Be(3);
        message.EffectiveVersionIndex.Should().Be(2, "the live parts are the newest version");
        message.ActiveText.Should().Be("third", "a message that gained a generation keeps showing it");
        message.Text.Should().Be("third", "the live parts are always the newest generation");
    }

    [Fact]
    public void ActiveVersionIndex_SelectsTheVersion_AndTheLivePartsAtTheLastIndex()
    {
        var message = Assistant("live");
        message.Versions.Add(new AiMessageVersion([new AiTextPart("old")]));

        message.ActiveVersionIndex = 1;
        message.ActiveText.Should().Be("live", "the last index is the live Parts, not a snapshot");
        message.EffectiveVersionIndex.Should().Be(1);

        message.ActiveVersionIndex = 0;
        message.ActiveText.Should().Be("old");

        message.Versions.Clear();
        message.ActiveVersionIndex = 5;
        message.ActiveText.Should().Be("live", "an out-of-range index clamps to the live version");
        message.ActiveParts.Should().NotBeNull();
    }

    [Fact]
    public void VersionCount_AllowsIndexingTheLiveVersionAtTheEnd()
    {
        var message = Assistant("live");
        message.Versions.Add(new AiMessageVersion([new AiTextPart("old")]));

        var last = message.VersionCount - 1;
        last.Should().Be(1);

        message.ActiveVersionIndex = last;
        message.ActiveText.Should().Be("live");
    }

    [Fact]
    public void ActiveVersionIndicesBelowZero_ClampToTheOldestVersion()
    {
        var message = Assistant("live");
        message.Versions.Add(new AiMessageVersion([new AiTextPart("old")]));

        message.ActiveVersionIndex = -3;

        message.EffectiveVersionIndex.Should().Be(0);
        message.ActiveText.Should().Be("old");
    }

    [Fact]
    public void SourcesAndCitationLookup_FollowTheActiveVersion()
    {
        var message = Assistant("live answer");
        message.Parts.Add(new AiSourcesPart([new AiSource { Id = "live-source", Title = "Live" }]));

        var older = new AiMessageVersion([
            new AiTextPart("older answer"),
            new AiSourcesPart([new AiSource { Id = "old-source", Title = "Older" }]),
        ]);
        message.Versions.Add(older);

        message.Sources.Select(source => source.Id).Should().Equal("live-source");
        message.FindSource("live-source").Should().NotBeNull();
        message.FindSource("old-source").Should().BeNull();

        message.ActiveVersionIndex = 0;

        message.Sources.Select(source => source.Id).Should().Equal("old-source");
        message.FindSource("old-source").Should().NotBeNull();
        message.FindSource("live-source").Should().BeNull();
    }

    [Fact]
    public void AiMessageVersion_CopiesItsParts_AndExposesTheirText()
    {
        var parts = new AiMessagePart[] { new AiTextPart("one "), new AiTextPart("two") };

        var version = new AiMessageVersion(parts, T0, "model-a");

        version.Parts.Should().HaveCount(2);
        version.Text.Should().Be("one two");
        version.CreatedAt.Should().Be(T0);
        version.Model.Should().Be("model-a");
        version.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AiMessageVersion_RejectsANullPartList()
    {
        var create = () => new AiMessageVersion(null!);

        create.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FeedbackDefaultsToNone_AndFollowUpsStartEmpty()
    {
        var message = Assistant("answer");

        message.Feedback.Should().BeNull();
        message.FollowUps.Should().BeEmpty();
        new AiFeedback().Rating.Should().Be(AiFeedbackRating.None);
    }

    [Fact]
    public void Feedback_KeepsTheRatingAndTheOptionalReason()
    {
        var feedback = new AiFeedback
        {
            Rating = AiFeedbackRating.Negative,
            Reason = "The second paragraph is wrong.",
            CreatedAt = T0,
        };

        feedback.Rating.Should().Be(AiFeedbackRating.Negative);
        feedback.Reason.Should().Be("The second paragraph is wrong.");
        feedback.CreatedAt.Should().Be(T0);

        var message = Assistant("answer");
        message.Feedback = feedback;
        message.FollowUps.Add("Why?");
        message.FollowUps.Add("Show the source.");

        message.Feedback.Should().BeSameAs(feedback);
        message.FollowUps.Should().Equal("Why?", "Show the source.");
    }
}
