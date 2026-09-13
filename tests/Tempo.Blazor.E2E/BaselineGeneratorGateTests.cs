using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Keeps every baseline GENERATOR behind <see cref="BaselineGeneratorTestBase"/>.
/// <para>
/// The gate only protects the generators that inherit it, and this sweep only sees classes NAMED
/// like one. Both limits are real and neither is hypothetical: <see cref="NotionE2ETestBase"/>
/// wrote 765 committed PNGs from 276 call sites while matching no convention and carrying no
/// category, so nothing here could ever have seen it — that residue existed in the repository at
/// the time this file was written, it was not a future risk. It is caught by
/// <see cref="BaselineWriteSweep"/>, whose predicate is the write TARGET.
/// </para>
/// <para>
/// What this file still buys is a fast, browser-free check on the naming convention itself, which
/// is how the earlier protection decayed: a doc comment plus a category on four of the seven
/// classes, with three (<c>DebtTokenBaselineScreenshots</c>, <c>ThemeTokenBaselineScreenshots</c>
/// and <c>SpreadsheetPhase6BaselineScreenshots</c>) carrying no category at all and running in
/// every ordinary <c>dotnet test</c>. This guard found the third; a hand-written list would not
/// have.
/// </para>
/// <para>
/// Deliberately reflection-only: no browser, no demo host, milliseconds. It is a normal test rather
/// than a generator, so it runs — and can fail — in the very lane it protects.
/// </para>
/// </summary>
[TestClass]
public sealed class BaselineGeneratorGateTests
{
    /// <summary>
    /// The naming convention the guard sweeps on. Types are matched by NAME, not by what they
    /// inherit — a guard that looked for subclasses of the gate could only ever find the classes
    /// that already pass it.
    /// </summary>
    private const string GeneratorSuffix = "BaselineScreenshots";

    private static List<Type> GeneratorTypes() =>
        typeof(BaselineGeneratorTestBase).Assembly
            .GetTypes()
            .Where(type => type.IsClass
                           && !type.IsAbstract
                           && type.Name.EndsWith(GeneratorSuffix, StringComparison.Ordinal))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

    [TestMethod]
    public void EveryBaselineGeneratorInheritsTheOptInGate()
    {
        var generators = GeneratorTypes();

        // Vacuity guard: a renamed convention would leave this sweeping an empty set and passing.
        Assert.IsTrue(
            generators.Count >= 7,
            $"expected at least 7 '*{GeneratorSuffix}' classes, found {generators.Count} — if the "
            + "convention was renamed, this guard is no longer looking at anything");

        var unguarded = generators
            .Where(type => !typeof(BaselineGeneratorTestBase).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToList();

        Assert.AreEqual(
            0,
            unguarded.Count,
            $"these classes rewrite baselines without inheriting "
            + $"{nameof(BaselineGeneratorTestBase)}, so an ordinary `dotnet test` would overwrite "
            + $"them: {string.Join(", ", unguarded)}");
    }

    /// <summary>
    /// The gate must default to OFF in THIS run. Asserted against the live environment rather than
    /// against a constant, so a CI lane that exports the opt-in for unrelated reasons is caught here
    /// instead of in a diff nobody reads.
    /// </summary>
    [TestMethod]
    public void BaselineWritesAreOffInAnOrdinaryRun()
    {
        var optIn = Environment.GetEnvironmentVariable(BaselineGeneratorTestBase.EnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(optIn))
        {
            Assert.Inconclusive(
                $"{BaselineGeneratorTestBase.EnvironmentVariable}='{optIn}' — this run is an explicit "
                + "baseline regeneration, which is the one case where writing is expected");
            return;
        }

        Assert.IsFalse(
            BaselineGeneratorTestBase.WritesAllowed,
            $"with {BaselineGeneratorTestBase.EnvironmentVariable} unset the generators must not run");
    }

    /// <summary>The opt-in must be an EXPLICIT affirmative, not "the variable exists".</summary>
    [DataTestMethod]
    [DataRow(null, false)]
    [DataRow("", false)]
    [DataRow("   ", false)]
    [DataRow("0", false)]
    [DataRow("false", false)]
    [DataRow("no", false)]
    [DataRow("maybe", false)]
    [DataRow("1", true)]
    [DataRow("true", true)]
    [DataRow("TRUE", true)]
    [DataRow(" yes ", true)]
    public void OptInIsReadAsAnExplicitAffirmative(string? value, bool expected)
    {
        var previous = Environment.GetEnvironmentVariable(BaselineGeneratorTestBase.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(BaselineGeneratorTestBase.EnvironmentVariable, value);
            Assert.AreEqual(expected, BaselineGeneratorTestBase.WritesAllowed);
        }
        finally
        {
            Environment.SetEnvironmentVariable(BaselineGeneratorTestBase.EnvironmentVariable, previous);
        }
    }
}
