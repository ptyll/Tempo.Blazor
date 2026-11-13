namespace Tempo.Blazor.EmailTemplates.Tests;

/// <summary>
/// Proves this assembly's linked <c>[ModuleInitializer]</c> (tests/Shared/TestAssemblyInit.cs) actually ran: the
/// job-level <c>TEMPO_BUNIT_WAIT_SECONDS</c> reaches every test project in the same <c>dotnet test</c> process,
/// but without the initializer nothing in THIS assembly reads it and bUnit stays on its 1 s default (the cause of
/// the cs_CZ-lane <c>PreviewServerRendererTests</c> timeout, run 43382805365).
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

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Bunit.TestContext.DefaultWaitTimeout);
    }
}
