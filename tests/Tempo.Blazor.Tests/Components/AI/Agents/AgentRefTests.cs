using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 1 — <see cref="AgentRef"/>, the one way every agent visualization names an agent: the
/// identity a host keys its data by, the display name a reader sees, the optional role and avatar, and the
/// index into the eight-colour agent palette.
/// <para>
/// The two derived members are the contract the components depend on and neither may throw on host data:
/// <see cref="AgentRef.Initials"/> is what the avatar paints when no picture is supplied (so it must survive a
/// single-word name, diacritics and an empty name), and <see cref="AgentRef.ColorIndex"/> is normalised into the
/// palette so a host counting agents past eight — or a negative index out of its own arithmetic — still lands on a
/// declared <c>--tm-agent-color-{n}</c> token instead of a custom property that resolves to nothing.
/// </para>
/// </summary>
public sealed class AgentRefTests
{
    [Fact]
    public void ARef_KeepsTheHostIdentity_AndItsName()
    {
        var agent = new AgentRef("Data analyst", "agent-7") { Role = "Analyst" };

        agent.Id.Should().Be("agent-7");
        agent.Name.Should().Be("Data analyst");
        agent.Role.Should().Be("Analyst");
        agent.ColorIndex.Should().Be(0, "an agent that names no colour takes the first palette slot");
    }

    [Fact]
    public void AMissingId_IsGenerated_SoTwoSameNamedAgentsStayDistinct()
    {
        var first = new AgentRef("Planner");
        var second = new AgentRef("Planner");

        first.Id.Should().NotBeNullOrWhiteSpace();
        first.Id.Should().NotBe(second.Id,
            "the id is the @key of every agent surface — two agents called the same are still two agents");
    }

    [Fact]
    public void ANullName_IsStoredAsEmptyText()
        => new AgentRef(null!).Name.Should().BeEmpty("nothing may ever render the word 'null' as an agent");

    [Fact]
    public void Records_CompareByValue()
    {
        var one = new AgentRef("Planner", "p") { ColorIndex = 3 };
        var same = new AgentRef("Planner", "p") { ColorIndex = 3 };
        var other = new AgentRef("Planner", "p") { ColorIndex = 4 };

        one.Should().Be(same, "a host compares the agent it rendered with the agent in the next snapshot");
        one.Should().NotBe(other);
        one.GetHashCode().Should().Be(same.GetHashCode());
    }

    [Theory]
    [InlineData("Data analyst", "DA")]
    [InlineData("Planner", "P")]
    [InlineData("Bedřich Novák", "BN")]
    [InlineData("  Worker  ", "W")]
    [InlineData("tool-runner agent", "TA")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData("42", "4")]
    public void Initials_ComeFromTheFirstAndLastWord(string name, string expected)
        => new AgentRef(name).Initials.Should().Be(expected,
            "the avatar has no picture to show and must never paint an empty circle");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(8, 0)]
    [InlineData(9, 1)]
    [InlineData(-1, 7)]
    [InlineData(-8, 0)]
    [InlineData(int.MaxValue, 7)]
    public void ColorIndex_IsNormalisedIntoTheEightSlotPalette(int supplied, int expected)
    {
        new AgentRef("Planner") { ColorIndex = supplied }.ColorIndex.Should().Be(expected,
            "the palette is exactly --tm-agent-color-0 … --tm-agent-color-7, so every index has to land "
            + "on a declared token");
    }

    [Fact]
    public void ColorToken_NamesTheDeclaredPaletteEntry()
    {
        var agent = new AgentRef("Planner") { ColorIndex = 3 };

        agent.ColorToken.Should().Be("--tm-agent-color-3");
        agent.ColorToken.Should().Be(AgentRef.PaletteToken(3));
    }

    [Fact]
    public void ThePalette_IsEightSlots()
        => AgentRef.PaletteSize.Should().Be(8,
            "the token family the plan fixes is --tm-agent-color-0 … --tm-agent-color-7");

    [Fact]
    public void AnAvatarUrl_IsCarriedVerbatim()
        => new AgentRef("Planner") { AvatarUrl = "https://example.test/p.png" }.AvatarUrl
            .Should().Be("https://example.test/p.png");
}
