using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;

namespace Tempo.Blazor.Tests.DocumentEditor;

public sealed class DocumentSearchServiceTests
{
    private static DocumentSearchService Create() => new();

    private static DocumentEditorDocument Doc(params DocumentBlock[] blocks) =>
        new() { Blocks = [.. blocks] };

    private static DocumentBlock Para(string text) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = DocumentBlockType.Paragraph,
        Content = new ParagraphBlockContent
        {
            Inlines = [new TextRun { Id = Guid.NewGuid().ToString("N"), Text = text }]
        }
    };

    private static DocumentBlock ParaMultiRun(params string[] runs) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = DocumentBlockType.Paragraph,
        Content = new ParagraphBlockContent
        {
            Inlines = [.. runs.Select(t => (InlineContent)new TextRun { Id = Guid.NewGuid().ToString("N"), Text = t })]
        }
    };

    private static DocumentBlock Heading(string text, int level = 1) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = DocumentBlockType.Heading,
        Content = new HeadingBlockContent
        {
            Level = level,
            Inlines = [new TextRun { Id = Guid.NewGuid().ToString("N"), Text = text }]
        }
    };

    private static DocumentBlock TableBlock(string[][] cells) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = DocumentBlockType.Table,
        Content = new TableBlockContent
        {
            Rows = cells.Select(row => new TableRowContent
            {
                Cells = row.Select(cellText => new TableCellContent
                {
                    Blocks =
                    [
                        new DocumentBlock
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Content = new ParagraphBlockContent
                            {
                                Inlines = [new TextRun { Id = Guid.NewGuid().ToString("N"), Text = cellText }]
                            }
                        }
                    ]
                }).ToList()
            }).ToList()
        }
    };

    private static DocumentBlock ContentControlBlock(params DocumentBlock[] blocks) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = DocumentBlockType.ContentControl,
        Content = new ContentControlBlockContent
        {
            Blocks = [.. blocks]
        }
    };

    // ─── Query model ─────────────────────────────────────────────────────────

    [Fact]
    public void Query_DefaultProperties_AreCorrect()
    {
        var q = new DocumentSearchQuery { Text = "hello" };
        Assert.Equal("hello", q.Text);
        Assert.False(q.CaseSensitive);
        Assert.False(q.WholeWord);
        Assert.Equal(DocumentSearchScope.Body, q.Scope);
    }

    // ─── Empty / no match ────────────────────────────────────────────────────

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var svc = Create();
        var results = svc.Search(Doc(Para("Hello world")), new DocumentSearchQuery { Text = "" });
        Assert.Empty(results);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var svc = Create();
        var results = svc.Search(Doc(Para("Hello world")), new DocumentSearchQuery { Text = "xyz" });
        Assert.Empty(results);
    }

    // ─── Basic single paragraph ───────────────────────────────────────────────

    [Fact]
    public void Search_SingleParagraph_FindsMatch()
    {
        var svc = Create();
        var results = svc.Search(Doc(Para("Hello world")), new DocumentSearchQuery { Text = "world" });
        Assert.Single(results);
        Assert.Equal(0, results[0].Index);
        Assert.Equal(6, results[0].BlockTextOffset);
        Assert.Equal(5, results[0].Length);
    }

    [Fact]
    public void Search_SingleParagraph_MultipleMatches()
    {
        var svc = Create();
        var results = svc.Search(Doc(Para("aaa bbb aaa")), new DocumentSearchQuery { Text = "aaa" });
        Assert.Equal(2, results.Count);
        Assert.Equal(0, results[0].BlockTextOffset);
        Assert.Equal(8, results[1].BlockTextOffset);
    }

    [Fact]
    public void Search_SingleParagraph_UsesNonOverlappingMatches()
    {
        var svc = Create();
        var results = svc.Search(Doc(Para("aaaa")), new DocumentSearchQuery { Text = "aa" });
        Assert.Equal(2, results.Count);
        Assert.Equal([0, 2], results.Select(result => result.BlockTextOffset));
    }

    [Fact]
    public void Search_MultipleBlocks_IndexIsGlobal()
    {
        var svc = Create();
        var doc = Doc(Para("First block"), Para("Second block"), Para("First block again"));
        var results = svc.Search(doc, new DocumentSearchQuery { Text = "block" });
        Assert.Equal(3, results.Count);
        Assert.Equal(0, results[0].Index);
        Assert.Equal(1, results[1].Index);
        Assert.Equal(2, results[2].Index);
    }

    // ─── Case sensitivity ────────────────────────────────────────────────────

    [Fact]
    public void Search_CaseInsensitive_FindsMixedCase()
    {
        var svc = Create();
        var results = svc.Search(Doc(Para("Hello WORLD")), new DocumentSearchQuery { Text = "world" });
        Assert.Single(results);
    }

    [Fact]
    public void Search_CaseSensitive_DoesNotFindMixedCase()
    {
        var svc = Create();
        var results = svc.Search(Doc(Para("Hello WORLD")),
            new DocumentSearchQuery { Text = "world", CaseSensitive = true });
        Assert.Empty(results);
    }

    [Fact]
    public void Search_CaseSensitive_FindsExactCase()
    {
        var svc = Create();
        var results = svc.Search(Doc(Para("Hello World")),
            new DocumentSearchQuery { Text = "World", CaseSensitive = true });
        Assert.Single(results);
    }

    // ─── Whole word ──────────────────────────────────────────────────────────

    [Fact]
    public void Search_WholeWord_MatchesIsolatedWord()
    {
        var svc = Create();
        var results = svc.Search(Doc(Para("cat and cats")),
            new DocumentSearchQuery { Text = "cat", WholeWord = true });
        Assert.Single(results);
        Assert.Equal(0, results[0].BlockTextOffset);
    }

    [Fact]
    public void Search_WholeWord_DoesNotMatchPartialWord()
    {
        var svc = Create();
        var results = svc.Search(Doc(Para("concatenate")),
            new DocumentSearchQuery { Text = "cat", WholeWord = true });
        Assert.Empty(results);
    }

    // ─── Multi-run inline ────────────────────────────────────────────────────

    [Fact]
    public void Search_MultipleInlines_FindsMatchSpanningRuns()
    {
        // "Hello " + "world" → "Hello world", search for "o w"
        var svc = Create();
        var block = ParaMultiRun("Hello ", "world");
        var results = svc.Search(Doc(block), new DocumentSearchQuery { Text = "o w" });
        Assert.Single(results);
        Assert.Equal(4, results[0].BlockTextOffset); // 'o' is at index 4
        Assert.Equal("o w", results[0].Preview);
    }

    [Fact]
    public void Search_MultipleInlines_CorrectBlockId()
    {
        var svc = Create();
        var block = ParaMultiRun("foo ", "bar");
        var results = svc.Search(Doc(block), new DocumentSearchQuery { Text = "bar" });
        Assert.Single(results);
        Assert.Equal(block.Id, results[0].BlockId);
    }

    // ─── Heading ─────────────────────────────────────────────────────────────

    [Fact]
    public void Search_HeadingBlock_FindsText()
    {
        var svc = Create();
        var results = svc.Search(Doc(Heading("Introduction")),
            new DocumentSearchQuery { Text = "intro" });
        Assert.Single(results);
    }

    // ─── Table traversal ─────────────────────────────────────────────────────

    [Fact]
    public void Search_TableCells_FindsTextInCell()
    {
        var svc = Create();
        var doc = Doc(TableBlock([["Alice", "Score"], ["Bob", "82"]]));
        var results = svc.Search(doc, new DocumentSearchQuery { Text = "Alice" });
        Assert.Single(results);
    }

    [Fact]
    public void Search_TableCells_FindsTextAcrossMultipleCells()
    {
        var svc = Create();
        var doc = Doc(TableBlock([["Name", "Score"], ["Alice", "95"]]));
        var results = svc.Search(doc, new DocumentSearchQuery { Text = "a" });
        // "Name" has 'a', "Alice" has 'a' and 'e' contains 'a' — "Name" → 1, "Alice" → 1
        Assert.True(results.Count >= 2);
    }

    [Fact]
    public void Search_ContentControlBlock_FindsTextInNestedBlock()
    {
        var svc = Create();
        var nested = Para("Locked template text");
        var doc = Doc(ContentControlBlock(nested));
        var results = svc.Search(doc, new DocumentSearchQuery { Text = "template" });

        Assert.Single(results);
        Assert.Equal(nested.Id, results[0].BlockId);
    }

    // ─── Preview ─────────────────────────────────────────────────────────────

    [Fact]
    public void Search_Result_PreviewContainsMatchText()
    {
        var svc = Create();
        var results = svc.Search(Doc(Para("The quick brown fox")),
            new DocumentSearchQuery { Text = "quick" });
        Assert.Contains("quick", results[0].Preview, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Scope: headers/footers ───────────────────────────────────────────────

    [Fact]
    public void Search_ScopeBody_DoesNotSearchHeaderFooter()
    {
        var svc = Create();
        var doc = new DocumentEditorDocument
        {
            Blocks = [Para("body text")],
            HeadersFooters =
            [
                new DocumentHeaderFooter
                {
                    Blocks = [Para("header secret")]
                }
            ]
        };
        var results = svc.Search(doc, new DocumentSearchQuery { Text = "secret", Scope = DocumentSearchScope.Body });
        Assert.Empty(results);
    }

    [Fact]
    public void Search_ScopeAll_SearchesHeaderFooter()
    {
        var svc = Create();
        var doc = new DocumentEditorDocument
        {
            Blocks = [Para("body text")],
            HeadersFooters =
            [
                new DocumentHeaderFooter
                {
                    Blocks = [Para("header secret")]
                }
            ]
        };
        var results = svc.Search(doc, new DocumentSearchQuery { Text = "secret", Scope = DocumentSearchScope.All });
        Assert.Single(results);
    }

    [Fact]
    public void Search_ScopeHeadersFooters_SearchesOnlyHeaderFooter()
    {
        var svc = Create();
        var doc = new DocumentEditorDocument
        {
            Blocks = [Para("body secret")],
            HeadersFooters =
            [
                new DocumentHeaderFooter
                {
                    Blocks = [Para("header secret")]
                }
            ]
        };

        var results = svc.Search(doc, new DocumentSearchQuery { Text = "secret", Scope = DocumentSearchScope.HeadersFooters });

        Assert.Single(results);
        Assert.Equal(DocumentSearchScope.HeadersFooters, results[0].Scope);
    }

    [Fact]
    public void Search_ScopeComments_SearchesCommentEntries()
    {
        var svc = Create();
        var doc = new DocumentEditorDocument
        {
            Blocks = [Para("body text")],
            Comments =
            [
                new DocumentComment
                {
                    Id = "c1",
                    Entries =
                    [
                        new DocumentCommentEntry { Id = "e1", Text = "comment secret" }
                    ]
                }
            ]
        };

        var results = svc.Search(doc, new DocumentSearchQuery { Text = "secret", Scope = DocumentSearchScope.Comments });

        Assert.Single(results);
        Assert.Equal(DocumentSearchScope.Comments, results[0].Scope);
        Assert.Equal("comment:c1:e1", results[0].BlockId);
    }

    [Fact]
    public void Search_Result_HasStableMarkerId()
    {
        var svc = Create();
        var block = Para("alpha alpha");

        var first = svc.Search(Doc(block), new DocumentSearchQuery { Text = "alpha" });
        var second = svc.Search(Doc(block), new DocumentSearchQuery { Text = "alpha" });

        Assert.Equal(first.Select(r => r.MarkerId), second.Select(r => r.MarkerId));
        Assert.All(first, r => Assert.StartsWith("search-", r.MarkerId));
    }
    // ─── Context around a match (redesign 146c7e1b, phase 6 / DE-2.3) ─────────

    [Fact]
    public void Search_Result_CarriesTheTextAroundTheMatch()
    {
        var svc = Create();

        var result = Assert.Single(svc.Search(
            Doc(Para("Project Aurora si klade za cil vybudovat moderni platformu")),
            new DocumentSearchQuery { Text = "Aurora" }));

        Assert.Equal("Project ", result.ContextBefore);
        Assert.Equal(" si klade za cil vybudovat mod", result.ContextAfter);
        Assert.Equal("Aurora", result.Preview);
    }

    [Fact]
    public void Search_Result_ContextStopsAtTheBlockBoundaries()
    {
        var svc = Create();

        var first = Assert.Single(svc.Search(Doc(Para("alpha beta")), new DocumentSearchQuery { Text = "alpha" }));
        Assert.Equal(string.Empty, first.ContextBefore);
        Assert.Equal(" beta", first.ContextAfter);

        var last = Assert.Single(svc.Search(Doc(Para("alpha beta")), new DocumentSearchQuery { Text = "beta" }));
        Assert.Equal("alpha ", last.ContextBefore);
        Assert.Equal(string.Empty, last.ContextAfter);
    }

    [Fact]
    public void Search_Result_ContextOfOneRunInAParagraphIgnoresTheNeighbouringBlocks()
    {
        var svc = Create();

        var results = svc.Search(Doc(Para("one needle"), Para("needle two")), new DocumentSearchQuery { Text = "needle" });

        Assert.Equal(2, results.Count);
        Assert.Equal("one ", results[0].ContextBefore);
        Assert.Equal(string.Empty, results[0].ContextAfter);
        Assert.Equal(string.Empty, results[1].ContextBefore);
        Assert.Equal(" two", results[1].ContextAfter);
    }

    [Fact]
    public void Search_Regex_ResultContextIsTakenAroundTheMatch()
    {
        var svc = Create();

        var result = Assert.Single(svc.Search(
            Doc(Para("order 4711 shipped")),
            new DocumentSearchQuery { Text = "\\d+", UseRegex = true }));

        Assert.Equal("4711", result.Preview);
        Assert.Equal("order ", result.ContextBefore);
        Assert.Equal(" shipped", result.ContextAfter);
    }

    [Fact]
    public void Search_WholeWord_SkipsTheWordInsideAnotherWord_AndKeepsContextOfTheRealOne()
    {
        var svc = Create();

        var result = Assert.Single(svc.Search(
            Doc(Para("category cat dog")),
            new DocumentSearchQuery { Text = "cat", WholeWord = true }));

        Assert.Equal("category ", result.ContextBefore);
        Assert.Equal(" dog", result.ContextAfter);
    }

    [Fact]
    public void Search_Result_ContextIsCappedAtThirtyCharactersEitherSide()
    {
        var svc = Create();
        var text = new string('a', 100) + "NEEDLE" + new string('b', 100);

        var result = Assert.Single(svc.Search(Doc(Para(text)), new DocumentSearchQuery { Text = "NEEDLE" }));

        Assert.Equal(30, result.ContextBefore.Length);
        Assert.Equal(30, result.ContextAfter.Length);
    }
}
