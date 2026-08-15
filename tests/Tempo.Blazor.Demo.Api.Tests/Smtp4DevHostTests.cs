using FluentAssertions;

namespace Tempo.Blazor.Demo.Api.Tests;

public class Smtp4DevHostTests
{
    [Fact]
    public async Task EnsureRunning_MakesRestReachable()
    {
        await Smtp4DevHost.EnsureRunningAsync();

        (await Smtp4DevHost.IsRestReachableAsync()).Should().BeTrue(
            "the two EmailTemplateSmtp4DevTests send through localhost:2525 and poll "
            + $"{Smtp4DevHost.RestBaseUrl}; a green 206/206 without this is the old 2/206 "
            + "waiting for someone to remember docker");
    }

    [Fact]
    public void FailureMessage_NamesTheSameDockerCommandTheHostRuns()
    {
        Smtp4DevHost.DockerRunCommand.Should().Contain(Smtp4DevHost.ContainerName);
        Smtp4DevHost.DockerRunCommand.Should().Contain(Smtp4DevHost.Image);
        Smtp4DevHost.DockerRunCommand.Should().Contain("-p 2525:25");
        Smtp4DevHost.DockerRunCommand.Should().Contain("-p 5000:80");

        var message = Smtp4DevHost.BuildFailureMessage();
        message.Should().Contain(Smtp4DevHost.RestBaseUrl);
        message.Should().Contain(Smtp4DevHost.DockerRunCommand);
    }
}
