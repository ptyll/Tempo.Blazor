namespace Tempo.Blazor.Reporting.Tests;

/// <summary>
/// N183 — proves this assembly's <c>[ModuleInitializer]</c> actually ran: the job-level
/// <c>TEMPO_BUNIT_WAIT_SECONDS</c> reaches every test project in the same <c>dotnet test</c>
/// process, but without a local initializer nothing in THIS assembly reads it and bUnit stays
/// on its 1 s default. Red before TestAssemblyInit existed: the value read 00:00:01 under
/// <c>TEMPO_BUNIT_WAIT_SECONDS=10</c>; with the initializer it follows the env/default contract.
/// </summary>
public sealed class WaitBudgetInitTests
{
    [Fact]
    public void DefaultWaitTimeout_HonorsTempoBunitWaitSeconds_OrTheSuiteDefault()
    {
        double expectedSeconds = 2;
        if (Environment.GetEnvironmentVariable("TEMPO_BUNIT_WAIT_SECONDS") is { Length: > 0 } raw)
        {
            expectedSeconds = double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        }

        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            Bunit.TestContext.DefaultWaitTimeout);
    }
}
