using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Base for the screenshot GENERATORS — classes whose purpose is to (re)write the PNG baselines
/// under <c>artifacts/baseline/</c> rather than to assert anything about them.
/// <para>
/// It covers ONE of the two ways a run reaches those PNGs, and not the larger one. Ordinary tests
/// also write baselines as a side effect of asserting something else — the Notion suite
/// does it for 765 files from 276 call sites — and those must not be skipped, because skipping them
/// would trade a working-tree problem for a coverage hole. They are handled by redirecting their
/// destination instead (<see cref="BaselineOutput"/>), and what actually holds the line for BOTH
/// routes is <see cref="BaselineWriteSweep"/>, which measures what the run touched rather than what
/// a class is called. Do not read this class as the complete protection.
/// </para>
/// <para>
/// They are off by default and only run when <see cref="EnvironmentVariable"/> is set. Before this
/// gate existed the only thing keeping them out of an ordinary run was a doc comment saying "run
/// this manually", which a plain <c>dotnet test</c> over the solution does not read — and the damage
/// is invisible in a test report, because a generator PASSES while it overwrites: it has nothing to
/// fail against. Measured with the gate in place, 58 test cases across the seven generators are
/// skipped that would otherwise have written into the working tree.
/// </para>
/// <para>
/// The gate is deliberately a SKIP and not a failure: an opt-in lane that turns an untouched
/// checkout red would just get the category filtered back out, which is where the protection was
/// last time. It is also in <c>TestInitialize</c> rather than in each capture helper, so a new
/// capture method in an existing generator is covered without anyone remembering to call anything.
/// </para>
/// <example>
/// Regenerating baselines on purpose, against a running WASM demo:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E \
///     --filter "TestCategory=BaselineGeneration"
/// </code>
/// </example>
/// </summary>
[TestCategory(BaselineGeneratorTestBase.Category)]
public abstract class BaselineGeneratorTestBase : WasmTestBase
{
    /// <summary>The category every generator carries, so an opt-in run can select exactly them.</summary>
    public const string Category = "BaselineGeneration";

    /// <summary>Set to <c>1</c>/<c>true</c>/<c>yes</c> to allow the generators to write.</summary>
    public const string EnvironmentVariable = "TM_WRITE_BASELINES";

    /// <summary>Whether the baseline root under <c>artifacts/baseline/</c> may be written by this run.</summary>
    public static bool WritesAllowed
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            value = value.Trim();
            return string.Equals(value, "1", StringComparison.Ordinal)
                   || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    [TestInitialize]
    public void RequireBaselineWriteOptIn()
    {
        if (!WritesAllowed)
        {
            Assert.Inconclusive(string.Create(
                CultureInfo.InvariantCulture,
                $"Baseline generation is off. This test REWRITES the PNG baselines under artifacts/baseline/, so it "
                + $"only runs when {EnvironmentVariable} is set to 1/true/yes."));
        }
    }
}
