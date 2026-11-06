using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 8 — <see cref="ConflictMatrix"/>, the pure C# half of <c>TmConflictRadar</c>: which agents changed
/// which paths, which paths two agents claim (the conflict), how the rows are ordered and grouped, and which decisions a
/// merge still waits for.
/// <para>
/// Every fact drives the matrix directly — no renderer, no clock — so a conflict rule change is a name in a failure
/// message instead of a tinted row nobody measures. The model records are pinned here too (<see cref="ChangedPath"/>,
/// <see cref="AgentBranchChanges"/>, <see cref="ConflictResolution"/>), because the matrix's guarantees are only as good
/// as their normalisation: a path that folds two files into one row would hide the conflict the surface exists to show.
/// </para>
/// </summary>
public class ConflictMatrixTests
{
    private static AgentRef Agent(string name, string id) => new(name, id);

    private static AgentBranchChanges Branch(AgentRef agent, string branch, params ChangedPath[] changes)
        => new(agent, branch, changes);

    private static readonly AgentRef Ana = Agent("Ana", "a1");
    private static readonly AgentRef Bo = Agent("Bo", "a2");
    private static readonly AgentRef Cy = Agent("Cy", "a3");

    // ── ChangedPath ──────────────────────────────────────────────────────────

    [Fact]
    public void APathKeepsItsKindAndNamesItsDirectoryAndFile()
    {
        var change = new ChangedPath("src/AI/TmConflictRadar.razor", ChangeKind.Modified);

        change.Kind.Should().Be(ChangeKind.Modified);
        change.Directory.Should().Be("src/AI");
        change.FileName.Should().Be("TmConflictRadar.razor");
        change.PreviousPath.Should().BeNull();
        change.IsRename.Should().BeFalse();
    }

    [Fact]
    public void APathAtTheRepositoryRootHasAnEmptyDirectory()
    {
        var change = new ChangedPath("README.md", ChangeKind.Added);

        change.Directory.Should().BeEmpty();
        change.FileName.Should().Be("README.md");
    }

    [Fact]
    public void ABareFileNameIsPreservedAsTheFileName()
    {
        var change = new ChangedPath("Directory.Build.props", ChangeKind.Deleted);

        change.Directory.Should().BeEmpty();
        change.FileName.Should().Be("Directory.Build.props");
    }

    [Fact]
    public void BackslashesAreNormalisedSoOneFileIsOneRow()
    {
        var windows = new ChangedPath(@"src\AI\Foo.cs", ChangeKind.Modified);

        windows.Path.Should().Be("src/AI/Foo.cs");
        windows.Directory.Should().Be("src/AI");
        windows.FileName.Should().Be("Foo.cs");
    }

    [Fact]
    public void ARenameKeepsItsPreviousPathNormalised()
    {
        var change = new ChangedPath("src/New.cs", ChangeKind.Renamed, @"src\Old.cs");

        change.PreviousPath.Should().Be("src/Old.cs");
        change.IsRename.Should().BeTrue();
    }

    [Fact]
    public void APreviousPathOnlyMeansSomethingForARename()
    {
        var change = new ChangedPath("src/New.cs", ChangeKind.Modified, "src/Old.cs");

        change.PreviousPath.Should().BeNull("a modified file was not moved by anybody");
    }

    [Fact]
    public void ABarePathIsRejected()
    {
        var blank = () => new ChangedPath("   ", ChangeKind.Added);
        var missing = () => new ChangedPath(null!, ChangeKind.Added);

        blank.Should().Throw<ArgumentException>().WithParameterName("path");
        missing.Should().Throw<ArgumentException>().WithParameterName("path");
    }

    // ── AgentBranchChanges ───────────────────────────────────────────────────

    [Fact]
    public void ABranchKeepsItsAgentBranchAndChanges()
    {
        var branch = Branch(Ana, "feat/a", new ChangedPath("a.cs", ChangeKind.Added));

        branch.Agent.Should().BeSameAs(Ana);
        branch.Branch.Should().Be("feat/a");
        branch.HasBranch.Should().BeTrue();
        branch.HasChanges.Should().BeTrue();
        branch.ChangedPaths.Should().HaveCount(1);
    }

    [Fact]
    public void AnAgentWithoutChangesIsNotAColumn()
    {
        var empty = Branch(Bo, "feat/b");
        var nothing = Branch(Cy, "feat/c", new ChangedPath("x.cs", ChangeKind.Added));

        empty.HasChanges.Should().BeFalse();
        new ConflictMatrix([empty, nothing]).ColumnCount.Should().Be(1);
    }

    [Fact]
    public void APathTheHostListedTwiceKeepsItsLastEntry()
    {
        var branch = Branch(
            Ana,
            "feat/a",
            new ChangedPath("a.cs", ChangeKind.Added),
            new ChangedPath("a.cs", ChangeKind.Modified));

        branch.ChangedPaths.Should().HaveCount(1);
        branch.Find("a.cs")!.Kind.Should().Be(ChangeKind.Modified,
            "a receipt that lists a file twice describes the newer fact second");
    }

    [Fact]
    public void AnUnnamedBranchReadsAsEmptyWithoutLosingTheAgent()
    {
        var branch = new AgentBranchChanges(Ana, null!, null);

        branch.Branch.Should().BeEmpty();
        branch.HasBranch.Should().BeFalse();
        branch.ChangedPaths.Should().BeEmpty();
        branch.Agent.Should().BeSameAs(Ana);
    }

    [Fact]
    public void ABranchNeedsAnAgent()
    {
        var act = () => new AgentBranchChanges(null!, "feat/a");

        act.Should().Throw<ArgumentNullException>().WithParameterName("agent");
    }

    [Fact]
    public void FindMatchesAPathWhoseSeparatorsDifferFromTheHosts()
    {
        var branch = Branch(Ana, "feat/a", new ChangedPath("src/AI/Foo.cs", ChangeKind.Modified));

        branch.Find(@"src\AI\Foo.cs").Should().NotBeNull();
        branch.Find("src/AI/Bar.cs").Should().BeNull();
        branch.Find(null).Should().BeNull();
    }

    // ── the matrix ───────────────────────────────────────────────────────────

    private static ConflictMatrix SampleMatrix() => new(
    [
        Branch(Ana, "feat/a",
            new ChangedPath("src/AI/TmConflictRadar.razor", ChangeKind.Modified),
            new ChangedPath("src/AI/ConflictMatrix.cs", ChangeKind.Added),
            new ChangedPath("README.md", ChangeKind.Modified)),
        Branch(Bo, "feat/b",
            new ChangedPath("src/AI/TmConflictRadar.razor", ChangeKind.Modified),
            new ChangedPath("docs/conflict-radar.md", ChangeKind.Added)),
        Branch(Cy, "feat/c",
            new ChangedPath("README.md", ChangeKind.Deleted),
            new ChangedPath("src/AI/TmConflictRadar.razor", ChangeKind.Renamed, "src/AI/Radar.razor")),
    ]);

    [Fact]
    public void TheMatrixHasOneColumnPerAgentThatChangedSomething()
    {
        var matrix = SampleMatrix();

        matrix.Columns.Select(column => column.Agent.Id).Should().Equal("a1", "a2", "a3");
        matrix.ColumnCount.Should().Be(3);
    }

    [Fact]
    public void TheMatrixHasOneRowPerTouchedPath()
    {
        var matrix = SampleMatrix();

        matrix.Rows.Select(row => row.Path).Should().BeEquivalentTo(
            "src/AI/TmConflictRadar.razor",
            "src/AI/ConflictMatrix.cs",
            "README.md",
            "docs/conflict-radar.md");
        matrix.RowCount.Should().Be(4);
        matrix.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void OneAgentOnAFileIsNotAConflict_AndTwoAgentsAre()
    {
        var matrix = SampleMatrix();

        matrix.Conflicts.Select(row => row.Path).Should().Equal("README.md", "src/AI/TmConflictRadar.razor");
        matrix.ConflictCount.Should().Be(2);
        matrix.HasConflicts.Should().BeTrue();
        matrix.Find("src/AI/ConflictMatrix.cs")!.IsConflict.Should().BeFalse();
        matrix.Find("src/AI/TmConflictRadar.razor")!.IsConflict.Should().BeTrue();
    }

    [Fact]
    public void ConflictingRowsComeFirst_AndEachGroupIsSortedByPath()
    {
        var matrix = SampleMatrix();

        matrix.Rows.Select(row => row.Path).Should().Equal(
            "README.md",
            "src/AI/TmConflictRadar.razor",
            "docs/conflict-radar.md",
            "src/AI/ConflictMatrix.cs");
    }

    [Fact]
    public void RowsAreGroupedByDirectoryInTheRowOrder()
    {
        var matrix = SampleMatrix();

        matrix.Directories.Select(directory => directory.Directory).Should().Equal(
            string.Empty, "src/AI", "docs");
        matrix.Directories[0].Rows.Select(row => row.Path).Should().Equal("README.md");
        matrix.Directories[1].Rows.Select(row => row.Path).Should().Equal(
            "src/AI/TmConflictRadar.razor", "src/AI/ConflictMatrix.cs");
        matrix.Directories[1].ConflictCount.Should().Be(1);
        matrix.Directories[1].HasConflicts.Should().BeTrue();
        matrix.Directories[2].HasConflicts.Should().BeFalse();
    }

    [Fact]
    public void ACellIsAlignedWithItsColumn_AndMissingChangesStayEmpty()
    {
        var matrix = SampleMatrix();
        var row = matrix.Find("src/AI/TmConflictRadar.razor")!;

        row.Cells.Should().HaveCount(3);
        row.Cells[0]!.Agent.Id.Should().Be("a1");
        row.Cells[0]!.Kind.Should().Be(ChangeKind.Modified);
        row.Cells[1]!.Agent.Id.Should().Be("a2");
        row.Cells[2]!.Agent.Id.Should().Be("a3");
        row.Cells[2]!.IsRename.Should().BeTrue();
        row.Cells[2]!.PreviousPath.Should().Be("src/AI/Radar.razor");
        matrix.Find("docs/conflict-radar.md")!.Cells[0].Should().BeNull("Ana never touched that file");
        matrix.Find("docs/conflict-radar.md")!.Cells[2].Should().BeNull("Cy never touched it either");
    }

    [Fact]
    public void TheRowsChangedCellsAreTheAgentsThatClaimIt()
    {
        var row = SampleMatrix().Find("README.md")!;

        row.Changed.Select(cell => cell.Agent.Id).Should().Equal("a1", "a3");
        row.ConflictingAgents.Select(agent => agent.Id).Should().Equal("a1", "a3");
        row.AgentCount.Should().Be(2);
    }

    [Fact]
    public void TheSameAgentReportedTwiceIsOneColumnWithBothListsMerged()
    {
        var matrix = new ConflictMatrix(
        [
            Branch(Ana, "feat/a", new ChangedPath("a.cs", ChangeKind.Added)),
            Branch(Ana, "feat/a-again", new ChangedPath("b.cs", ChangeKind.Added)),
        ]);

        matrix.ColumnCount.Should().Be(1, "an agent id names one agent, whichever branch it reports from");
        matrix.RowCount.Should().Be(2, "inventing a second column would hide the files the second receipt names");
        matrix.Rows.Select(row => row.Path).Should().Equal("a.cs", "b.cs");

        var sameAgent = new ConflictMatrix(
        [
            Branch(Ana, "feat/a", new ChangedPath("a.cs", ChangeKind.Added)),
            Branch(Agent("Ana again", "a1"), "feat/other", new ChangedPath("a.cs", ChangeKind.Modified)),
        ]);

        sameAgent.ColumnCount.Should().Be(1);
        sameAgent.RowCount.Should().Be(1);
        sameAgent.Find("a.cs")!.Cells[0]!.Kind.Should().Be(ChangeKind.Modified, "the later receipt is the newer fact");
    }

    [Fact]
    public void APathReportedWithBothSeparatorStylesIsOneRow()
    {
        var matrix = new ConflictMatrix(
        [
            Branch(Ana, "feat/a", new ChangedPath("src/AI/Foo.cs", ChangeKind.Modified)),
            Branch(Bo, "feat/b", new ChangedPath(@"src\AI\Foo.cs", ChangeKind.Modified)),
        ]);

        matrix.RowCount.Should().Be(1);
        matrix.ConflictCount.Should().Be(1);
        matrix.Conflicts[0].Path.Should().Be("src/AI/Foo.cs");
    }

    [Fact]
    public void ACaseDifferenceIsTwoDistinctFiles()
    {
        var matrix = new ConflictMatrix(
        [
            Branch(Ana, "feat/a", new ChangedPath("src/Foo.cs", ChangeKind.Modified)),
            Branch(Bo, "feat/b", new ChangedPath("src/foo.cs", ChangeKind.Modified)),
        ]);

        matrix.RowCount.Should().Be(2, "collapsing a case difference would hide a change nobody resolved");
        matrix.ConflictCount.Should().Be(0);
    }

    [Fact]
    public void AnEmptyMatrixIsEmpty()
    {
        ConflictMatrix.Empty.IsEmpty.Should().BeTrue();
        ConflictMatrix.Empty.HasConflicts.Should().BeFalse();
        ConflictMatrix.Empty.Directories.Should().BeEmpty();
        new ConflictMatrix(null).IsEmpty.Should().BeTrue();
        new ConflictMatrix([null, Branch(Ana, "feat/a")]).IsEmpty.Should().BeTrue();
        ConflictMatrix.Empty.Find("anything").Should().BeNull();
        ConflictMatrix.Empty.Find(null).Should().BeNull();
    }

    // ── resolutions ──────────────────────────────────────────────────────────

    [Fact]
    public void AnAgentResolutionNeedsTheAgentWhoseVersionWins()
    {
        var act = () => new ConflictResolution("a.cs", ConflictResolutionKind.Agent);
        var blank = () => new ConflictResolution("a.cs", ConflictResolutionKind.Agent, "  ");
        var noPath = () => new ConflictResolution(" ", ConflictResolutionKind.Manual);

        act.Should().Throw<ArgumentException>().WithParameterName("agentId");
        blank.Should().Throw<ArgumentException>().WithParameterName("agentId");
        noPath.Should().Throw<ArgumentException>().WithParameterName("path");
    }

    [Fact]
    public void AManualResolutionNeedsNoAgent()
    {
        var resolution = new ConflictResolution("a.cs", ConflictResolutionKind.Manual, "ignored");

        resolution.AgentId.Should().BeNull();
        resolution.IsManual.Should().BeTrue();
        resolution.IsAgentSide.Should().BeFalse();
        resolution.Decides(@"a.cs").Should().BeTrue();
        resolution.Decides("b.cs").Should().BeFalse();
    }

    [Fact]
    public void TheMatrixWaitsForEveryConflictToBeDecided()
    {
        var matrix = SampleMatrix();

        matrix.IsReadyToMerge(null).Should().BeFalse();
        matrix.Unresolved(null).Select(row => row.Path).Should().Equal("README.md", "src/AI/TmConflictRadar.razor");

        ConflictResolution[] onlyOne =
        [
            new("README.md", ConflictResolutionKind.Agent, "a1"),
        ];
        matrix.IsReadyToMerge(onlyOne).Should().BeFalse();
        matrix.Unresolved(onlyOne).Select(row => row.Path).Should().Equal("src/AI/TmConflictRadar.razor");

        ConflictResolution[] both =
        [
            new("README.md", ConflictResolutionKind.Agent, "a1"),
            new("src/AI/TmConflictRadar.razor", ConflictResolutionKind.Manual),
        ];
        matrix.IsReadyToMerge(both).Should().BeTrue();
        matrix.Unresolved(both).Should().BeEmpty();
    }

    [Fact]
    public void ADecisionForAnAgentThatDoesNotChangeTheFileDoesNotSettleIt()
    {
        var matrix = SampleMatrix();
        ConflictResolution[] wrong =
        [
            new("README.md", ConflictResolutionKind.Agent, "a2"),
            new("src/AI/TmConflictRadar.razor", ConflictResolutionKind.Agent, "a2"),
            new("src/AI/TmConflictRadar.razor", ConflictResolutionKind.Agent, "a1"),
        ];

        matrix.IsReadyToMerge(wrong).Should().BeFalse("Bo does not change README.md, so his side cannot win it");
        matrix.Unresolved(wrong).Select(row => row.Path).Should().Equal("README.md");
    }

    [Fact]
    public void AMergeWithNoConflictsIsAlwaysReady()
    {
        var matrix = new ConflictMatrix(
        [
            Branch(Ana, "feat/a", new ChangedPath("a.cs", ChangeKind.Added)),
            Branch(Bo, "feat/b", new ChangedPath("b.cs", ChangeKind.Modified)),
        ]);

        matrix.IsReadyToMerge(null).Should().BeTrue();
        matrix.BuildMergeRequest(null).Files.Should().Equal("a.cs", "b.cs");
        matrix.BuildMergeRequest(null).Resolutions.Should().BeEmpty();
    }

    [Fact]
    public void AMergeRequestCarriesEveryFileAndOnlyAcceptedDecisions()
    {
        var matrix = SampleMatrix();
        ConflictResolution[] decisions =
        [
            new("README.md", ConflictResolutionKind.Agent, "a3"),
            new("src/AI/TmConflictRadar.razor", ConflictResolutionKind.Manual),
            new("src/AI/ConflictMatrix.cs", ConflictResolutionKind.Agent, "a1"),
        ];

        var request = matrix.BuildMergeRequest(decisions);

        request.Files.Should().Equal(matrix.Rows.Select(row => row.Path));
        request.FileCount.Should().Be(4);
        request.Resolutions.Should().HaveCount(2, "a conflict-free file needs no side choice");
        request.ResolutionOf("README.md")!.AgentId.Should().Be("a3");
        request.ResolutionOf("src/AI/TmConflictRadar.razor")!.IsManual.Should().BeTrue();
        request.ResolutionOf("src/AI/ConflictMatrix.cs").Should().BeNull();
        request.ResolutionOf(null).Should().BeNull();
    }

    [Fact]
    public void AResolutionForAPathThatIsNotInConflictIsAcceptedButNotReportedAsOne()
    {
        var matrix = SampleMatrix();
        ConflictResolution[] decisions =
        [
            new("src/AI/ConflictMatrix.cs", ConflictResolutionKind.Agent, "a1"),
        ];

        matrix.AcceptedResolution("src/AI/ConflictMatrix.cs", decisions).Should().BeNull();
        matrix.AcceptedResolution("nothing.cs", decisions).Should().BeNull();
        matrix.AcceptedResolution("README.md", decisions).Should().BeNull();
        matrix.AcceptedResolution(null, decisions).Should().BeNull();
    }

    [Fact]
    public void TheMergeRequestDropsBlankPathsAndNullDecisions()
    {
        var request = new ConflictMergeRequest(["a.cs", " ", null!, "b.cs"], [null!, new("a.cs", ConflictResolutionKind.Manual)]);

        request.Files.Should().Equal("a.cs", "b.cs");
        request.Resolutions.Should().HaveCount(1);
        request.ResolvedConflictCount.Should().Be(1);
    }

    [Fact]
    public void TheMergeRequestNormalisesItsPathsAndKeepsEachFileOnce()
    {
        var request = new ConflictMergeRequest(
            ["src\\AI\\Foo.cs", "src/AI/Foo.cs", " src/AI/Bar.cs "],
            [new("src\\AI\\Foo.cs", ConflictResolutionKind.Manual)]);

        request.Files.Should().Equal(
            "src/AI/Foo.cs",
            "src/AI/Bar.cs");
        request.FileCount.Should().Be(2, "a file the host reported twice is still ONE file to merge");
        request.ResolutionOf("src/AI/Foo.cs").Should().NotBeNull("the decision is stored under the same normalised path");
    }
}
