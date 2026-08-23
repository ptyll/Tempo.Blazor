namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// A <c>[Fact]</c> whose skip is decided by probing the world at DISCOVERY time, with the reason a
/// human wrote at the call site still winning over that probe.
/// <para>
/// WHY THIS TYPE EXISTS AT ALL, said as the measurement it came from rather than as a preference for
/// tidiness. Two attributes in this folder — <see cref="ReleaseContractTests.StagedPackagesFactAttribute"/>
/// and <see cref="ReleaseContractTests.FeedReachableFactAttribute"/> — were written by copying the first
/// into the second. Both carried <c>set =&gt; base.Skip = value;</c> and a getter that never read
/// <c>base.Skip</c> back, so <c>[FeedReachableFact(Skip = "flaky, see #123")]</c> stored the reason and
/// then ran the test anyway. That is one defect duplicated by copy, and what it settles is WHICH half
/// belongs where. The broken half was precisely the half both copies HELD IN COMMON, so that half is
/// declared once, here, where a fix lands in both. The half that legitimately differs — WHICH world is
/// probed — is the half that must NOT be shared, so it stays where it varies: an abstract method each
/// subclass answers for its own world.
/// </para>
/// <para>
/// THE GETTER IS SEALED, AND THE SEAL COVERS ONE OF THE TWO WAYS BACK IN. A subclass that re-declared
/// this property would reintroduce the swallowed skip silently: the cells over the shared shape
/// (<c>ProbeDecidedSkipTests</c>) would stay green, because they would go on measuring this class rather
/// than the copy. A property can be re-declared two ways, and one mechanism does not cover both.
/// <c>override</c> is refused while the getter is sealed — measured 2026-08-23 by compiling a subclass
/// that overrides <c>Skip</c>: <c>CS0239</c>, "cannot override inherited member ... because it is
/// sealed", alongside the companion arm in which the SAME subclass with the override taken out compiles
/// with 0 errors, so the red names the seal rather than a broken probe file. <c>new</c> is NOT refused:
/// a subclass carrying <c>public new string? Skip { get; set; }</c> compiles, measured the same way, 0
/// errors. What covers THAT one is the reflexive cell
/// <see cref="ProbeDecidedSkipTests.SharedShape_IsWhereEveryProbeDecidedAttributeGetsItsGetter"/>, which reads the
/// DECLARING type of each attribute's <c>Skip</c>: hiding with <c>new</c> moves that declaring type onto
/// the subclass (measured on a stand-in hierarchy the same day — <c>GetProperty("Skip").DeclaringType</c>
/// answered the shape for an honest subclass and the subclass itself for one that hid it), which the
/// cell reports as a red. Neither half says anything about an attribute that cell's list does not name.
/// </para>
/// <para>
/// WHY AN OVERRIDDEN GETTER IS THE MECHANISM. <c>FactAttribute.Skip</c> is virtual and xUnit v2 reads it
/// through <c>ReflectionAttributeInfo.GetNamedArgument</c>, which walks the type hierarchy for a property
/// of that name and calls its getter on a real instance rather than reading the attribute blob — so an
/// override is the supported way to decide a skip from the environment, and in this runner it is the ONLY
/// way, because xUnit v2 has no runtime skip (see the remark on
/// <see cref="ReleaseContractTests.PackedPackages_RecordTheCommitTheyWereBuiltFrom"/> for what was
/// measured about that). Walking the hierarchy is also why declaring the property HERE rather than on each
/// attribute changes nothing about discovery: the walk starts at the attribute's own type and finds it.
/// </para>
/// <para>
/// A BROKEN PROBE MUST NEVER BECOME A SKIP, which is why the catch returns null rather than a reason. Null
/// means "do not skip", so an exception in the probe hands the test to the runner, where the same
/// exception is thrown again inside the body and reported as a failure with its stack. The opposite
/// treatment — skip on error — would make every future breakage in these files look like the ordinary
/// state the probe exists to recognise.
/// </para>
/// </summary>
public abstract class ProbeDecidedFactAttribute : FactAttribute
{
    /// <summary>
    /// The reason written at the call site if there is one, otherwise the probe's verdict, otherwise
    /// null. The order is the whole point: a probe cannot overrule a human.
    /// </summary>
    public sealed override string? Skip
    {
        get
        {
            var written = base.Skip;
            if (written is not null)
            {
                return written;
            }

            try
            {
                return ProbeSkipReason();
            }
#pragma warning disable CA1031 // see the remark above: any failure here must NOT read as "skip"
            catch (Exception)
#pragma warning restore CA1031
            {
                return null;
            }
        }

        set => base.Skip = value;
    }

    /// <summary>
    /// Surveys the world and returns the reason this test cannot say anything today, or null when it can.
    /// Called once per discovery of each decorated member; it may throw, and a throw is deliberately NOT
    /// a skip.
    /// </summary>
    protected abstract string? ProbeSkipReason();
}
