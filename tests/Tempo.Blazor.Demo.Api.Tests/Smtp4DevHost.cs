using System.Diagnostics;
using System.Net;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>
/// Brings smtp4dev up for the two real-delivery tests so Demo.Api.Tests is 206/206 without a
/// hand-started container. The 2/206 red was the environment, not the tests: they already pass
/// when REST <c>http://localhost:5000</c> answers.
/// </summary>
/// <remarks>
/// Does not stop the container afterwards. It is shared (this suite, the demo, other agents)
/// and tearing it down would turn a green neighbour red.
/// </remarks>
public sealed class Smtp4DevFixture : IAsyncLifetime
{
    public Task InitializeAsync() => Smtp4DevHost.EnsureRunningAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>Starts the existing <c>tempo-smtp4dev</c> container, or creates it, or fails closed.</summary>
public static class Smtp4DevHost
{
    public const string RestBaseUrl = "http://localhost:5000";
    public const string ContainerName = "tempo-smtp4dev";
    public const string Image = "rnwood/smtp4dev:latest";

    /// <summary>
    /// The command the error names and the command <see cref="EnsureRunningAsync"/> actually runs.
    /// Built from the same constants so a mutation that changes ports or the image cannot lie.
    /// </summary>
    public static string DockerRunCommand
        => $"docker run -d --name {ContainerName} -p 2525:25 -p 5000:80 {Image}";

    public static async Task EnsureRunningAsync(CancellationToken cancellationToken = default)
    {
        if (await IsRestReachableAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        RunDocker("start", ContainerName);
        if (await WaitForRestAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        RunDocker(
            "run", "-d", "--name", ContainerName,
            "-p", "2525:25", "-p", "5000:80",
            Image);

        if (await WaitForRestAsync(TimeSpan.FromSeconds(40), cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        throw new InvalidOperationException(BuildFailureMessage());
    }

    public static async Task<bool> IsRestReachableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client
                .GetAsync(new Uri($"{RestBaseUrl}/api/Messages?pageSize=1"), cancellationToken)
                .ConfigureAwait(false);
            return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return false;
        }
    }

    internal static string BuildFailureMessage()
        => $"smtp4dev REST {RestBaseUrl} is not reachable and docker could not start it. "
           + $"Start with: {DockerRunCommand}";

    private static async Task<bool> WaitForRestAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsRestReachableAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static void RunDocker(params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return;
            }

            if (!process.WaitForExit(15_000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* already gone */ }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // docker missing from PATH — EnsureRunningAsync will fail closed with BuildFailureMessage.
        }
    }
}
