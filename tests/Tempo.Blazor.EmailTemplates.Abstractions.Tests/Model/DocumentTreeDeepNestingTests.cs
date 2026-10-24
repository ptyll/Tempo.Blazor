using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Model;

/// <summary>
/// <see cref="DocumentTree"/> must walk a hostile/deep document without recursion (an intermittent stack
/// overflow in <c>CollectBlockLists</c> took the test host down) and keep its documented pre-order.
/// </summary>
public class DocumentTreeDeepNestingTests
{
    private const int Depth = 50_000;

    private static EmailTemplateDocument DeepGroups(int depth, out EmailTextBlock leaf)
    {
        leaf = new EmailTextBlock { Content = "leaf" };
        EmailBlockBase block = leaf;
        for (var i = 0; i < depth; i++)
        {
            var group = new EmailGroupBlock();
            group.Columns.Add(new EmailColumn { Blocks = { block } });
            block = group;
        }

        var doc = new EmailTemplateDocument();
        doc.Sections.Add(new EmailSection { Columns = { new EmailColumn { Blocks = { block } } } });
        return doc;
    }

    [Fact]
    public void AllBlockLists_BlocksAndColumns_HandleAPathologicallyDeepTree()
    {
        var doc = DeepGroups(Depth, out var leaf);

        DocumentTree.AllBlockLists(doc).Count().Should().Be(Depth + 1);
        DocumentTree.AllBlocks(doc).Count().Should().Be(Depth + 1);
        DocumentTree.AllBlocks(doc).Should().Contain(leaf);
        DocumentTree.AllColumns(doc).Count().Should().Be(Depth + 1);
    }

    [Fact]
    public void AllSections_HandlesDeeplyNestedWrappers()
    {
        var inner = new EmailSection { Columns = { new EmailColumn { Blocks = { new EmailTextBlock() } } } };
        for (var i = 0; i < Depth; i++)
        {
            var wrapper = new EmailWrapperBlock();
            wrapper.Sections.Add(inner);
            inner = new EmailSection { Columns = { new EmailColumn { Blocks = { wrapper } } } };
        }

        var doc = new EmailTemplateDocument();
        doc.Sections.Add(inner);

        DocumentTree.AllSections(doc).Count().Should().Be(Depth + 1);
        DocumentTree.AllBlocks(doc).Count().Should().Be(Depth + 1);
    }

    [Fact]
    public void ReassignIds_HandlesADeepTree_AndRenewsEveryId()
    {
        var doc = DeepGroups(Depth, out var leaf);
        var before = leaf.Id;
        var section = doc.Sections[0];

        DocumentTree.ReassignIds(section);

        leaf.Id.Should().NotBe(before);
        DocumentTree.AllBlocks(doc).Select(b => b.Id).Distinct().Count().Should().Be(Depth + 1);
    }

    [Fact]
    public void Traversals_KeepDocumentOrder_AndTerminateOnACycle()
    {
        var a = new EmailTextBlock { Content = "a" };
        var b = new EmailTextBlock { Content = "b" };
        var c = new EmailTextBlock { Content = "c" };
        var d = new EmailTextBlock { Content = "d" };
        var hero = new EmailHeroBlock { Blocks = { b } };
        var group = new EmailGroupBlock();
        group.Columns.Add(new EmailColumn { Blocks = { c } });
        var doc = new EmailTemplateDocument();
        doc.Sections.Add(new EmailSection { Columns = { new EmailColumn { Blocks = { a, hero, group, d } } } });

        DocumentTree.AllBlocks(doc).Should().ContainInConsecutiveOrder(a, hero, group, d, b, c);
        DocumentTree.AllBlockLists(doc).Select(l => l.Count).Should().Equal(4, 1, 1);

        // A hand-built cycle: the group holds the very list that contains it.
        group.Columns[0].Blocks.Add(group);
        var act = () => DocumentTree.AllBlocks(doc).ToList();
        act.Should().NotThrow();
        DocumentTree.AllColumns(doc).Count().Should().BeGreaterThan(0);
    }
}
