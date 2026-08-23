namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// A <c>[Fact]</c> over a member that RUNS one of the <c>eng/*.sh</c> release scripts, skipped with a
/// named reason on Windows.
/// <para>
/// THE DEFECT, measured 2026-08-23: the members in <see cref="ReleaseScriptInputReadTests"/> and
/// <see cref="PackScriptManifestSweepTests"/> started their scripts through the absolute paths
/// <c>/usr/bin/env</c> and <c>/bin/chmod</c>, and carried no platform guard at all. On Windows neither
/// path exists, so every one of them died in <c>Process.Start</c> with a <c>Win32Exception</c> — a RED
/// that says nothing about the release scripts and everything about the machine. The ubuntu CI lane
/// never saw it, so the whole cost sat on a developer machine, where "the suite is red for me" is a
/// reason the suite stops being run.
/// </para>
/// <para>
/// WHAT THIS ATTRIBUTE CLAIMS, and it is deliberately narrower than the shape of the fix suggests:
/// a decorated member no longer TRIES TO START A BINARY THAT IS NOT THERE. That is a property of the
/// source and is visible without running anything. It is NOT a claim that these members pass on
/// Windows — nobody has run this suite there, and a skip is not a green. The read each decorated
/// member defends stays UNCHECKED on Windows; it is measured on the ubuntu lane, which is where both
/// publish workflows run.
/// </para>
/// <para>
/// WHY A NAMED SKIP RATHER THAN AN EARLY <c>return</c>. A member that returns early is reported as
/// PASSED, so a whole platform's worth of unmeasured behaviour would be indistinguishable in the
/// <c>.trx</c> from behaviour that was measured and agreed. A skip is a third outcome that a runner, a
/// <c>.trx</c> and a reviewer all read without being asked to, and it carries the reason with it.
/// </para>
/// <para>
/// THE MECHANISM IS BORROWED, NOT BUILT: an overridden <c>Skip</c> getter is the only skip this runner
/// honours, and the reasons — including why a throwing probe deliberately does not skip — live once in
/// <see cref="ProbeDecidedFactAttribute"/>. This class supplies only the question: is this Windows.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class BashScriptFactAttribute : ProbeDecidedFactAttribute
{
    /// <summary>
    /// The reason a Windows run reports instead of a result. It names what was not measured, because a
    /// skip whose reason is only "wrong OS" gets read as "nothing here matters on that OS".
    /// </summary>
    internal const string WindowsSkipReason =
        "[BashScript] skipped on Windows: this member starts `bash` and marks its fixture executable "
        + "with File.SetUnixFileMode, and the eng/*.sh scripts it runs are POSIX shell. This skip does "
        + "NOT claim the member would pass on Windows — nobody has run it there. It claims only that "
        + "the member no longer tries to start a binary that is not there. The script read this member "
        + "defends is therefore UNCHECKED on Windows and is measured on the ubuntu CI lane instead.";

    /// <inheritdoc/>
    protected override string? ProbeSkipReason() =>
        OperatingSystem.IsWindows() ? WindowsSkipReason : null;
}

/// <summary>
/// A <see cref="BashScriptFactAttribute"/> that ALSO needs nuget.org to have answered.
/// <para>
/// The two questions are asked in this order on purpose. Windows first means a Windows run never issues
/// the live flat-container request the feed probe costs, and a non-Windows run asks the feed exactly as
/// often as it did before this attribute existed — the count of live requests per discovery is
/// unchanged, which is the property the sibling's remark about <c>PublishedVersionSurvey.Take()</c> not
/// being cached makes worth stating.
/// </para>
/// <para>
/// The feed half is the SAME code <see cref="ReleaseContractTests.FeedReachableFactAttribute"/> runs —
/// <see cref="ReleaseContractTests.FeedUnreachableSkipReason"/> — rather than a copy of it. That is the
/// lesson <see cref="ProbeDecidedFactAttribute"/> was extracted from: the half two attributes hold in
/// common is declared once, so a defect in it cannot be fixed in one copy and left in the other.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class BashScriptFeedReachableFactAttribute : BashScriptFactAttribute
{
    /// <inheritdoc/>
    protected override string? ProbeSkipReason() =>
        base.ProbeSkipReason() ?? ReleaseContractTests.FeedUnreachableSkipReason();
}
