using System.Security.Cryptography;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.DocumentEditor.Interfaces;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.DocumentEditor.Services;

/// <summary>In-memory document editor provider intended for tests and demos.</summary>
public class InMemoryDocumentEditorProvider : IDocumentEditorProvider, ITmActivityProvider
{
    /// <summary>Stable document id for the 2026-05-23 Google Docs engine recovery baseline.</summary>
    public const string Recovery20260523DocumentId = "recovery-2026-05-23";

    /// <summary>Stable document id for the 2026-05-24 ONLYOFFICE parity baseline.</summary>
    public const string OnlyOfficeParity20260524DocumentId = "onlyoffice-parity-2026-05-24";

    /// <summary>Stable document id for canvas search, bookmarks, outline, and table-of-contents coverage.</summary>
    public const string CanvasSearchOutlineTocDocumentId = "phase-18-canvas-search-outline-toc";

    private const string RecoverySectionId = "recovery-section-main";
    private const string RecoveryUrlImageUrl = "/document-editor-evidence.svg";
    private const string RecoveryProviderAssetId = "contract-evidence-asset";
    private readonly Dictionary<string, StoredDocument> _documents = [];
    private readonly Dictionary<string, List<DocumentVersion>> _versions = [];
    private readonly List<TmActivityEntry> _activityEntries = [];

    /// <summary>Recorded activity entries.</summary>
    public IReadOnlyList<TmActivityEntry> ActivityEntries => _activityEntries;

    /// <inheritdoc />
    public virtual TmCommentProviderCapabilities Capabilities
        => TmCommentProviderCapabilities.Read
        | TmCommentProviderCapabilities.CreateThread
        | TmCommentProviderCapabilities.Reply
        | TmCommentProviderCapabilities.EditEntry
        | TmCommentProviderCapabilities.Delete
        | TmCommentProviderCapabilities.Resolve
        | TmCommentProviderCapabilities.RichText;

    TmActivityProviderCapabilities ITmActivityProvider.Capabilities
        => TmActivityProviderCapabilities.Read
        | TmActivityProviderCapabilities.Query
        | TmActivityProviderCapabilities.Append;

    TmActivityProviderCapabilities ITmCapabilityProvider<TmActivityProviderCapabilities>.Capabilities
        => ((ITmActivityProvider)this).Capabilities;

    /// <summary>Clears all in-memory documents, versions, and audit events.</summary>
    protected void ClearStore()
    {
        _documents.Clear();
        _versions.Clear();
        _activityEntries.Clear();
    }

    /// <summary>Seeds a new empty document.</summary>
    public DocumentEditorDocument SeedEmptyDocument(string documentId = "empty-document")
    {
        var document = DocumentEditorDocument.Empty(documentId);
        document.Metadata.Title = "Empty document";
        StoreDocument(document);
        return Clone(document);
    }

    /// <summary>Seeds a representative contract document.</summary>
    public DocumentEditorDocument SeedContractDocument(string documentId = "contract-demo")
    {
        var document = DocumentEditorDocument.Empty(documentId);
        document.Metadata.Title = "Service agreement";
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.25,
            ParagraphSpacingAfter = 9
        };
        document.Sections[0].Id = "contract-section-main";
        document.Sections[0].Title = "Agreement";
        document.Sections[0].Properties.HeaderFooterReferences =
        [
            new DocumentHeaderFooterReference
            {
                HeaderFooterId = "contract-header-primary",
                Type = DocumentHeaderFooterType.Header,
                Scope = DocumentHeaderFooterScope.Primary
            },
            new DocumentHeaderFooterReference
            {
                HeaderFooterId = "contract-footer-primary",
                Type = DocumentHeaderFooterType.Footer,
                Scope = DocumentHeaderFooterScope.Primary
            }
        ];
        document.Blocks.Add(new DocumentBlock
        {
            Id = "contract-heading",
            SectionId = "contract-section-main",
            Type = DocumentBlockType.Heading,
            Order = 10,
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines =
                [
                    new TextRun
                    {
                        Id = "contract-heading-text",
                        Text = "Service agreement",
                        Marks =
                        [
                            new InlineMark { Type = InlineMarkType.FontFamily, Value = "Aptos Display, Aptos, Arial, sans-serif" },
                            new InlineMark { Type = InlineMarkType.FontSize, Value = "24pt" }
                        ]
                    }
                ]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "contract-intro",
            SectionId = "contract-section-main",
            Type = DocumentBlockType.Paragraph,
            Order = 20,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = DocumentTextAlignment.Justify,
                LineSpacing = 1.25,
                SpacingAfter = 10
            },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = "contract-intro-prefix",
                        Text = "This agreement is made with ",
                        Marks = [new InlineMark { Type = InlineMarkType.Bold }]
                    },
                    new TokenRun
                    {
                        Id = "contract-client-token",
                        Key = "client.name",
                        DisplayName = "Client name",
                        TokenType = "text",
                        TypeLabel = "CRM",
                        FallbackText = "Acme s.r.o."
                    },
                    new TextRun { Id = "contract-intro-suffix", Text = "." }
                ]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "contract-scope",
            SectionId = "contract-section-main",
            Type = DocumentBlockType.Paragraph,
            Order = 25,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = DocumentTextAlignment.Left,
                LineSpacing = 1.25,
                SpacingAfter = 12
            },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = "contract-scope-approved",
                        Text = "The provider will deliver implementation, training, and documentation services.",
                        Marks = [new InlineMark { Type = InlineMarkType.TextColor, Value = "#1f2937" }]
                    },
                    new TextRun
                    {
                        Id = "contract-scope-revision",
                        Text = " Priority support is included during the first thirty days.",
                        Marks =
                        [
                            new InlineMark
                            {
                                Type = InlineMarkType.Revision,
                                RevisionId = "contract-revision-scope",
                                Value = "Insertion"
                            }
                        ]
                    }
                ]
            }
        });
        document.HeadersFooters.Add(CreateSeedHeaderFooter(
            "contract-header-primary",
            DocumentHeaderFooterType.Header,
            "Tempo Legal - Service agreement"));
        document.HeadersFooters.Add(CreateSeedFooterWithPageFields("contract-footer-primary"));
        document.Revisions.Add(new DocumentRevision
        {
            Id = "contract-revision-scope",
            Type = DocumentRevisionType.Insertion,
            Range = new DocumentRevisionRange
            {
                BlockId = "contract-scope",
                StartInlineIndex = 1,
                EndInlineIndex = 1,
                StartOffset = 0,
                EndOffset = 60
            },
            Author = new DocumentRevisionAuthor
            {
                Id = "demo-reviewer",
                DisplayName = "Demo Reviewer",
                Email = "reviewer@example.local"
            },
            CreatedAt = new DateTimeOffset(2026, 5, 14, 8, 30, 0, TimeSpan.Zero),
            Action = DocumentRevisionAction.Pending,
            PayloadJson = "Priority support is included during the first thirty days."
        });

        StoreDocument(document);
        return Clone(document);
    }

    /// <summary>Seeds the deterministic recovery document used by Google Docs engine regression E2E tests.</summary>
    public DocumentEditorDocument SeedRecoveryDocument(string documentId = Recovery20260523DocumentId)
    {
        var document = DocumentEditorDocument.Empty(documentId);
        document.Metadata.Title = "Google Docs engine recovery baseline";
        document.Metadata.Description = "Deterministic baseline for the 2026-05-23 document editor recovery plan.";
        document.Metadata.Status = DocumentEditorStatus.Review;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11,
            BodyLineHeight = 1.25,
            ParagraphSpacingAfter = 9
        };
        document.Sections[0].Id = RecoverySectionId;
        document.Sections[0].Title = "Recovery";
        document.Sections[0].Properties.HeaderFooterReferences =
        [
            new DocumentHeaderFooterReference
            {
                HeaderFooterId = "recovery-header-primary",
                Type = DocumentHeaderFooterType.Header,
                Scope = DocumentHeaderFooterScope.Primary
            },
            new DocumentHeaderFooterReference
            {
                HeaderFooterId = "recovery-footer-primary",
                Type = DocumentHeaderFooterType.Footer,
                Scope = DocumentHeaderFooterScope.Primary
            }
        ];
        document.HeadersFooters.Add(CreateRecoveryHeader());
        document.HeadersFooters.Add(CreateRecoveryFooter());

        document.Blocks.Add(new DocumentBlock
        {
            Id = "recovery-heading",
            SectionId = RecoverySectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "recovery-heading-text", Text = "Recovery baseline" }]
            }
        });
        document.Blocks.Add(CreateParagraph(
            "recovery-comment-paragraph",
            20,
            [
                new TextRun { Id = "recovery-comment-prefix", Text = "This paragraph contains a " },
                new TextRun
                {
                    Id = "recovery-comment-anchor-run",
                    Text = "visible comment anchor",
                    Marks =
                    [
                        new InlineMark
                        {
                            Type = InlineMarkType.CommentAnchor,
                            CommentAnchor = new CommentAnchorMarkData
                            {
                                CommentId = "recovery-comment-visible",
                                AnchorId = "recovery-comment-visible-anchor"
                            }
                        }
                    ]
                },
                new TextRun { Id = "recovery-comment-suffix", Text = " for marker checks." }
            ]));
        document.Blocks.Add(CreateParagraph(
            "recovery-insertion-revision-paragraph",
            30,
            [
                new TextRun { Id = "recovery-insertion-prefix", Text = "Pending inserted text should be decorated: " },
                new TextRun
                {
                    Id = "recovery-insertion-run",
                    Text = "inserted recovery clause",
                    Marks =
                    [
                        new InlineMark
                        {
                            Type = InlineMarkType.Revision,
                            RevisionId = "recovery-revision-insertion",
                            Value = "Insertion"
                        }
                    ]
                },
                new TextRun { Id = "recovery-insertion-suffix", Text = "." }
            ]));
        document.Blocks.Add(CreateParagraph(
            "recovery-deletion-revision-paragraph",
            40,
            [
                new TextRun { Id = "recovery-deletion-prefix", Text = "Pending deleted text should remain visible in all markup: " },
                new TextRun
                {
                    Id = "recovery-deletion-run",
                    Text = "deleted recovery clause",
                    Marks =
                    [
                        new InlineMark
                        {
                            Type = InlineMarkType.Revision,
                            RevisionId = "recovery-revision-deletion",
                            Value = "Deletion"
                        }
                    ]
                },
                new TextRun { Id = "recovery-deletion-suffix", Text = "." }
            ]));
        document.Blocks.Add(CreateParagraph(
            "recovery-selection-paragraph",
            50,
            "Select this inline recovery text with the mouse to verify that the floating toolbar appears and stays anchored near the selection."));
        document.Blocks.Add(CreateImage(
            "recovery-url-image",
            60,
            DocumentImageSource.Url,
            RecoveryUrlImageUrl,
            null,
            "URL recovery evidence",
            "URL image for recovery baseline",
            156,
            88,
            DocumentImageAlignment.Center,
            DocumentObjectLayout.Inline()));
        document.Blocks.Add(CreateImage(
            "recovery-provider-image",
            70,
            DocumentImageSource.Asset,
            null,
            RecoveryProviderAssetId,
            "Provider recovery evidence",
            "Provider image for recovery baseline",
            156,
            88,
            DocumentImageAlignment.Center,
            DocumentObjectLayout.Inline()));
        document.Blocks.Add(CreateImage(
            "recovery-inline-image",
            80,
            DocumentImageSource.Url,
            RecoveryUrlImageUrl,
            null,
            "Inline recovery image",
            "Inline image for recovery baseline",
            140,
            79,
            DocumentImageAlignment.Center,
            DocumentObjectLayout.Inline()));
        document.Blocks.Add(CreateImage(
            "recovery-left-wrap-image",
            90,
            DocumentImageSource.Url,
            RecoveryUrlImageUrl,
            null,
            "Left wrapped recovery image",
            "Left wrapped image for recovery baseline",
            148,
            84,
            DocumentImageAlignment.Start,
            CreateRecoveryWrappedImageLayout(148, 84, DocumentImageHorizontalPosition.Left, "recovery-left-wrap-text")));
        document.Blocks.Add(CreateParagraph(
            "recovery-left-wrap-text",
            91,
            "This text belongs below the left wrapped image and is long enough to create several visual lines beside the object. It should be readable, selectable, and free from image overlap."));
        document.Blocks.Add(CreateImage(
            "recovery-right-wrap-image",
            100,
            DocumentImageSource.Url,
            RecoveryUrlImageUrl,
            null,
            "Right wrapped recovery image",
            "Right wrapped image for recovery baseline",
            148,
            84,
            DocumentImageAlignment.End,
            CreateRecoveryWrappedImageLayout(148, 84, DocumentImageHorizontalPosition.Right, "recovery-right-wrap-text")));
        document.Blocks.Add(CreateParagraph(
            "recovery-right-wrap-text",
            101,
            "This text belongs below the right wrapped image and verifies that opposite image positioning still leaves clear selectable text lines for human interaction."));
        document.Blocks.Add(CreateImage(
            "recovery-top-bottom-image",
            110,
            DocumentImageSource.Asset,
            null,
            RecoveryProviderAssetId,
            "Top bottom recovery image",
            "Top-bottom image for recovery baseline",
            220,
            124,
            DocumentImageAlignment.Center,
            CreateRecoveryTopBottomImageLayout(220, 124, "recovery-top-bottom-text")));
        document.Blocks.Add(CreateParagraph(
            "recovery-top-bottom-text",
            111,
            "Top-bottom wrapping reserves a full horizontal band before this text continues."));
        document.Blocks.Add(CreateParagraph(
            "recovery-empty-image-anchor",
            112,
            [new TextRun { Id = "recovery-empty-image-anchor-text", Text = string.Empty }],
            spacingAfter: 9));
        AddDrawingRunToParagraph(
            document,
            "recovery-empty-image-anchor",
            CreateRecoveryDrawingRun(
                "recovery-empty-paragraph-image",
                "Image anchored to an empty paragraph",
                DocumentWrapMode.Square,
                "recovery-empty-image-anchor",
                112,
                63,
                0));
        document.Blocks.Add(CreateImage(
            "recovery-missing-alt-image",
            120,
            DocumentImageSource.Url,
            RecoveryUrlImageUrl,
            null,
            null,
            "Missing alt text image for recovery baseline",
            156,
            88,
            DocumentImageAlignment.Center,
            DocumentObjectLayout.Inline()));
        document.Blocks.Add(CreateRecoveryTable());
        AddRecoveryHeaderFooterDrawingRuns(document);

        document.Comments.Add(new DocumentComment
        {
            Id = "recovery-comment-visible",
            Anchor = new DocumentCommentAnchor
            {
                Type = DocumentCommentAnchorType.TextRange,
                BlockId = "recovery-comment-paragraph",
                StartInlineIndex = 1,
                EndInlineIndex = 1,
                StartOffset = 0,
                EndOffset = "visible comment anchor".Length,
                ExternalAnchorId = "recovery-comment-visible-anchor"
            },
            Visibility = DocumentCommentVisibility.Internal,
            Entries =
            [
                new DocumentCommentEntry
                {
                    Id = "recovery-comment-visible-entry",
                    Author = new DocumentEditorAuthor { Id = "recovery-reviewer", DisplayName = "Recovery Reviewer" },
                    Text = "The recovery baseline must show this comment in the document.",
                    CreatedAt = new DateTimeOffset(2026, 5, 23, 8, 0, 0, TimeSpan.Zero)
                }
            ]
        });
        document.Revisions.Add(CreateRecoveryRevision(
            "recovery-revision-insertion",
            DocumentRevisionType.Insertion,
            "recovery-insertion-revision-paragraph",
            1,
            "inserted recovery clause",
            0));
        document.Revisions.Add(CreateRecoveryRevision(
            "recovery-revision-deletion",
            DocumentRevisionType.Deletion,
            "recovery-deletion-revision-paragraph",
            1,
            "deleted recovery clause",
            5));

        StoreDocument(document, "recovery-2026-05-23-canonical-v1");
        return Clone(document);
    }

    /// <summary>Seeds the deterministic ONLYOFFICE parity baseline used by P0 editor engine E2E tests.</summary>
    public DocumentEditorDocument SeedOnlyOfficeParityDocument(string documentId = OnlyOfficeParity20260524DocumentId)
    {
        var document = SeedRecoveryDocument(documentId);
        document.Metadata.Title = "ONLYOFFICE parity baseline";
        document.Metadata.Description = "Deterministic baseline for selection, formatting, track changes, comments, and undo parity tests.";

        document.Blocks.Add(CreateParagraph(
            "onlyoffice-formatting-paragraph",
            52,
            "Apply formatting to this exact target phrase and keep the surrounding text unchanged.",
            spacingAfter: 9));

        document.Blocks.Add(CreateParagraph(
            "onlyoffice-mixed-formatting-paragraph",
            53,
            [
                new TextRun
                {
                    Id = "onlyoffice-mixed-bold-run",
                    Text = "Bold mixed segment",
                    Marks = [new InlineMark { Type = InlineMarkType.Bold }]
                },
                new TextRun { Id = "onlyoffice-mixed-plain-run", Text = " and plain mixed segment." }
            ],
            spacingAfter: 9));

        document.Blocks.Add(CreateParagraph(
            "onlyoffice-collapsed-caret-paragraph",
            54,
            "Collapsed caret typing style starts here.",
            spacingAfter: 9));

        document.Blocks.Add(CreateParagraph(
            "onlyoffice-track-changes-paragraph",
            55,
            "Track changes typing target.",
            spacingAfter: 9));

        document.Blocks.Add(CreateParagraph(
            "onlyoffice-comment-boundary-paragraph",
            56,
            [
                new TextRun { Id = "onlyoffice-comment-boundary-prefix", Text = "Text before " },
                new TextRun
                {
                    Id = "onlyoffice-comment-boundary-anchor",
                    Text = "commented range",
                    Marks =
                    [
                        new InlineMark
                        {
                            Type = InlineMarkType.CommentAnchor,
                            CommentAnchor = new CommentAnchorMarkData
                            {
                                CommentId = "onlyoffice-comment-boundary",
                                AnchorId = "onlyoffice-comment-boundary-anchor"
                            }
                        }
                    ]
                },
                new TextRun { Id = "onlyoffice-comment-boundary-suffix", Text = " and editable suffix." }
            ],
            spacingAfter: 9));

        document.Blocks.Add(CreateParagraph(
            "onlyoffice-image-normal-paragraph",
            57,
            "Image parity body text must stay editable before, beside, and after anchored drawing objects.",
            spacingAfter: 9));

        document.Blocks.Add(CreateParagraph(
            "onlyoffice-image-empty-paragraph",
            58,
            [new TextRun { Id = "onlyoffice-image-empty-run", Text = string.Empty }],
            spacingAfter: 9));

        document.Blocks.Add(CreateParagraph(
            "onlyoffice-image-insertion-paragraph",
            59,
            "Image insertion before target after target.",
            spacingAfter: 9));

        AddOnlyOfficeParityOverlayDrawingRuns(document);
        AddOnlyOfficeParityAdvancedDrawingRuns(document);
        PrepareOnlyOfficeParityImageAssets(document);

        document.Comments.Add(new DocumentComment
        {
            Id = "onlyoffice-comment-boundary",
            Anchor = new DocumentCommentAnchor
            {
                Type = DocumentCommentAnchorType.TextRange,
                BlockId = "onlyoffice-comment-boundary-paragraph",
                StartInlineIndex = 1,
                EndInlineIndex = 1,
                StartOffset = 0,
                EndOffset = "commented range".Length,
                ExternalAnchorId = "onlyoffice-comment-boundary-anchor"
            },
            Visibility = DocumentCommentVisibility.Internal,
            Entries =
            [
                new DocumentCommentEntry
                {
                    Id = "onlyoffice-comment-boundary-entry",
                    Author = new DocumentEditorAuthor { Id = "onlyoffice-reviewer", DisplayName = "ONLYOFFICE Reviewer" },
                    Text = "Typing after this comment must not extend the comment range.",
                    CreatedAt = new DateTimeOffset(2026, 5, 24, 8, 0, 0, TimeSpan.Zero)
                }
            ]
        });

        StoreDocument(document, "onlyoffice-parity-2026-05-24-canonical-v1");
        return Clone(document);
    }

    private static void ConvertOnlyOfficeParityImageBlocksToDrawingRuns(DocumentEditorDocument document)
    {
        for (var index = 0; index < document.Blocks.Count; index++)
        {
            var block = document.Blocks[index];
            if (block.Content is not ImageBlockContent image)
            {
                continue;
            }

            var drawing = CreateDrawingRunFromImageBlock(block, image);
            block.Type = DocumentBlockType.Paragraph;
            block.ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = DocumentTextAlignment.Left,
                LineSpacing = 1.25,
                SpacingAfter = 9
            };
            block.Content = new ParagraphBlockContent
            {
                Inlines = [drawing]
            };
        }
    }

    private static DocumentDrawingRun CreateDrawingRunFromImageBlock(DocumentBlock block, ImageBlockContent image)
    {
        var layout = Clone(image.Layout);
        layout.Anchor.BlockId = string.IsNullOrWhiteSpace(layout.Anchor.BlockId) ? block.Id : layout.Anchor.BlockId;
        layout.Anchor.InlineIndex ??= 0;
        layout.Anchor.Offset ??= 0;
        if (layout.Transform.Width is null or <= 0)
        {
            layout.Transform.Width = image.Size.Width;
        }

        if (layout.Transform.Height is null or <= 0)
        {
            layout.Transform.Height = image.Size.Height;
        }

        layout.Transform.NaturalWidth ??= image.NaturalSize.Width > 0 ? image.NaturalSize.Width : image.Size.Width;
        layout.Transform.NaturalHeight ??= image.NaturalSize.Height > 0 ? image.NaturalSize.Height : image.Size.Height;

        return new DocumentDrawingRun
        {
            Id = $"{block.Id}-drawing",
            ObjectId = block.Id ?? Guid.NewGuid().ToString("N"),
            Source = image.Source,
            Url = image.Source == DocumentImageSource.Url ? image.Url : null,
            AssetId = image.Source == DocumentImageSource.Asset ? image.AssetId : null,
            AltText = image.AltText,
            IsDecorative = image.IsDecorative,
            Caption = image.Caption,
            Size = Clone(image.Size),
            NaturalSize = Clone(image.NaturalSize),
            Layout = layout,
            LinkUrl = image.LinkUrl
        };
    }

    private static void AddOnlyOfficeParityOverlayDrawingRuns(DocumentEditorDocument document)
    {
        AddDrawingRunToParagraph(
            document,
            "onlyoffice-image-normal-paragraph",
            CreateOnlyOfficeParityDrawingRun(
                "onlyoffice-behind-text-image",
                "Behind-text parity image",
                DocumentWrapMode.BehindText,
                "onlyoffice-image-normal-paragraph",
                96,
                54,
                -1));
        AddDrawingRunToParagraph(
            document,
            "onlyoffice-image-normal-paragraph",
            CreateOnlyOfficeParityDrawingRun(
                "onlyoffice-front-text-image",
                "In-front parity image",
                DocumentWrapMode.InFrontOfText,
                "onlyoffice-image-normal-paragraph",
                96,
                54,
                12));
    }

    private static void AddOnlyOfficeParityAdvancedDrawingRuns(DocumentEditorDocument document)
    {
        document.Blocks.Add(CreateParagraph(
            "onlyoffice-image-crop-paragraph",
            60,
            "Cropped image parity checks source rectangle export and visible object sizing.",
            spacingAfter: 9));
        var crop = CreateOnlyOfficeParityDrawingRun(
            "onlyoffice-cropped-image",
            "Cropped image parity",
            DocumentWrapMode.Square,
            "onlyoffice-image-crop-paragraph",
            128,
            72,
            14);
        crop.Layout.Transform.Crop = new DocumentObjectCrop
        {
            Left = 8,
            Top = 5,
            Right = 12,
            Bottom = 6
        };
        AddDrawingRunToParagraph(document, "onlyoffice-image-crop-paragraph", crop);

        document.Blocks.Add(CreateParagraph(
            "onlyoffice-image-rotation-paragraph",
            61,
            "Rotated image parity checks native DrawingML transform preservation.",
            spacingAfter: 9));
        var rotated = CreateOnlyOfficeParityDrawingRun(
            "onlyoffice-rotated-image",
            "Rotated image parity",
            DocumentWrapMode.Square,
            "onlyoffice-image-rotation-paragraph",
            112,
            63,
            15);
        rotated.Layout.Transform.Rotation = 12;
        rotated.Layout.Position.X = 24;
        rotated.Layout.Position.Y = 6;
        AddDrawingRunToParagraph(document, "onlyoffice-image-rotation-paragraph", rotated);

        document.Blocks.Add(CreateParagraph(
            "onlyoffice-image-tight-paragraph",
            62,
            "Tight wrapping parity keeps custom contour metadata while allowing renderers to fall back gracefully.",
            spacingAfter: 9));
        var tight = CreateOnlyOfficeParityDrawingRun(
            "onlyoffice-tight-image",
            "Tight wrap parity",
            DocumentWrapMode.Tight,
            "onlyoffice-image-tight-paragraph",
            120,
            72,
            16);
        tight.Layout.Wrap.Side = DocumentObjectWrapSide.Largest;
        tight.Layout.Wrap.WrapContourPoints =
        [
            new() { X = 0.5, Y = 0 },
            new() { X = 1, Y = 0.45 },
            new() { X = 0.62, Y = 1 },
            new() { X = 0, Y = 0.55 }
        ];
        AddDrawingRunToParagraph(document, "onlyoffice-image-tight-paragraph", tight);

        document.Blocks.Add(CreateParagraph(
            "onlyoffice-image-through-paragraph",
            63,
            "Through wrapping parity uses the same contour path but must export as wp:wrapThrough.",
            spacingAfter: 9));
        var through = CreateOnlyOfficeParityDrawingRun(
            "onlyoffice-through-image",
            "Through wrap parity",
            DocumentWrapMode.Through,
            "onlyoffice-image-through-paragraph",
            120,
            72,
            17);
        through.Layout.Wrap.Side = DocumentObjectWrapSide.Left;
        through.Layout.Wrap.WrapContourPoints =
        [
            new() { X = 0.15, Y = 0 },
            new() { X = 1, Y = 0.2 },
            new() { X = 0.85, Y = 1 },
            new() { X = 0, Y = 0.8 }
        ];
        AddDrawingRunToParagraph(document, "onlyoffice-image-through-paragraph", through);
    }

    private static void PrepareOnlyOfficeParityImageAssets(DocumentEditorDocument document)
    {
        if (document.Assets.All(asset => !string.Equals(asset.Id, RecoveryProviderAssetId, StringComparison.Ordinal)))
        {
            document.Assets.Add(new DocumentImageAsset
            {
                Id = RecoveryProviderAssetId,
                DocumentId = document.DocumentId,
                Source = DocumentImageSource.Asset,
                ContentType = "image/png",
                FileName = "onlyoffice-parity-evidence.png",
                AltText = "ONLYOFFICE parity evidence",
                Caption = "Provider-backed parity image",
                ImageSize = new DocumentImageSize { Width = 240, Height = 135 }
            });
        }

        foreach (var drawing in DocumentImagePersistence.EnumerateDrawingRuns(document))
        {
            drawing.Source = DocumentImageSource.Asset;
            drawing.AssetId = RecoveryProviderAssetId;
            drawing.Url = null;
        }
    }

    private static DocumentDrawingRun CreateOnlyOfficeParityDrawingRun(
        string objectId,
        string altText,
        DocumentWrapMode wrapMode,
        string anchorBlockId,
        double width,
        double height,
        int zIndex)
        => new()
        {
            Id = $"{objectId}-drawing",
            ObjectId = objectId,
            Source = DocumentImageSource.Asset,
            AssetId = RecoveryProviderAssetId,
            AltText = altText,
            Caption = altText,
            Size = new DocumentImageSize { Width = width, Height = height },
            NaturalSize = new DocumentImageSize { Width = width, Height = height },
            Layout = new DocumentObjectLayout
            {
                Kind = DocumentObjectLayoutKind.Anchored,
                Anchor = new DocumentObjectAnchor
                {
                    BlockId = anchorBlockId,
                    InlineIndex = 1,
                    Offset = 16,
                    MoveWithText = true,
                    FixedOnPage = false
                },
                Position = new DocumentObjectPosition
                {
                    HorizontalRelativeTo = DocumentRelativePosition.Page,
                    VerticalRelativeTo = DocumentRelativePosition.Paragraph,
                    HorizontalAlignment = DocumentImageHorizontalPosition.Center,
                    X = 0,
                    Y = 0
                },
                Wrap = new DocumentObjectWrap
                {
                    Mode = wrapMode
                },
                Transform = new DocumentObjectTransform
                {
                    Width = width,
                    Height = height,
                    NaturalWidth = width,
                    NaturalHeight = height,
                    LockAspectRatio = true
                },
                Stacking = new DocumentObjectStacking
                {
                    ZIndex = zIndex,
                    AllowOverlap = true
                }
            }
        };

    private static void AddDrawingRunToParagraph(DocumentEditorDocument document, string paragraphId, DocumentDrawingRun drawing)
    {
        var paragraph = document.Blocks
            .FirstOrDefault(block => string.Equals(block.Id, paragraphId, StringComparison.Ordinal))
            ?.Content as ParagraphBlockContent;
        if (paragraph is null)
        {
            return;
        }

        drawing.Layout.Anchor.InlineIndex = paragraph.Inlines.Count;
        paragraph.Inlines.Add(drawing);
    }

    /// <summary>Seeds a simple court filing document.</summary>
    public DocumentEditorDocument SeedFilingDocument(string documentId = "filing-demo")
    {
        var document = DocumentEditorDocument.Empty(documentId);
        document.Metadata.Title = "Court filing";
        document.Blocks.Add(new DocumentBlock
        {
            Type = DocumentBlockType.Heading,
            Order = 10,
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Text = "Court filing" }]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Type = DocumentBlockType.Paragraph,
            Order = 20,
            Content = new ParagraphBlockContent
            {
                Inlines = [new TextRun { Text = "The claimant submits the following petition." }]
            }
        });

        StoreDocument(document);
        return Clone(document);
    }

    /// <summary>Seeds a document that exercises canvas search, bookmarks, outline extraction, and generated TOC.</summary>
    public DocumentEditorDocument SeedCanvasSearchOutlineTocDocument(string documentId = CanvasSearchOutlineTocDocumentId)
    {
        // Keep a document the user already saved: the demo host re-runs this seeder on every in-app navigation back to the page, and
        // re-seeding would silently replace the saved edits (generated TOC, replaced text) with the pristine seed.
        if (_documents.TryGetValue(documentId, out var existing))
        {
            return Clone(existing.Document);
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "phase18-section-main";
        document.Metadata.Title = "Canvas search outline and TOC";
        document.Metadata.CreatedAt = new DateTimeOffset(2026, 6, 4, 8, 0, 0, TimeSpan.Zero);
        document.Metadata.ModifiedAt = document.Metadata.CreatedAt;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.22,
            ParagraphSpacingAfter = 8
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 72, Bottom = 72, Left = 72 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;
        document.Blocks.Add(CreatePhase18Heading("phase18-h1", sectionId, 10, 1, "Project Tempo"));
        document.Blocks.Add(CreatePhase18Paragraph(
            "phase18-intro",
            sectionId,
            20,
            [
                new TextRun { Id = "phase18-intro-a", Text = "Tempo-18 search baseline includes Tempo-42 and Tempo-108 for regex replacement. " },
                new TextRun
                {
                    Id = "phase18-bookmark-run",
                    Text = "Bookmark target paragraph remains navigable.",
                    Marks = [new InlineMark { Type = InlineMarkType.Bookmark, Value = "phase18-target" }]
                }
            ]));
        document.Blocks.Add(CreatePhase18Heading("phase18-h2-scope", sectionId, 30, 2, "Delivery Scope"));
        document.Blocks.Add(CreatePhase18Paragraph(
            "phase18-scope-body",
            sectionId,
            40,
            "The outline panel should jump to Delivery Scope and generated TOC entries should resolve page numbers from the canvas layout cache."));
        document.Blocks.Add(CreatePhase18Heading("phase18-h2-quality", sectionId, 50, 2, "Quality Gates"));
        document.Blocks.Add(CreatePhase18Paragraph(
            "phase18-quality-body",
            sectionId,
            60,
            "Quality Gates contain Tempo-204 and stable prose that forces the search overlay to paint multiple highlights without moving the layout."));
        document.Blocks.Add(CreatePhase18Heading("phase18-h3-details", sectionId, 70, 3, "Implementation Details"));
        document.Blocks.Add(CreatePhase18Paragraph(
            "phase18-details-body",
            sectionId,
            80,
            "Implementation Details verify that a level-three heading appears in the generated table of contents and remains undoable."));

        StoreDocument(document, $"{documentId}-canonical-v1");
        return Clone(document);
    }

    /// <inheritdoc />
    public virtual Task<DocumentEditorLoadResult> LoadAsync(
        string documentId,
        DocumentEditorLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DocumentEditorLoadOptions();
        if (!_documents.TryGetValue(documentId, out var stored))
        {
            return Task.FromResult(DocumentEditorLoadResult.NotFound());
        }

        return Task.FromResult(new DocumentEditorLoadResult
        {
            Found = true,
            Document = options.IncludeDocument ? Clone(stored.Document) : null,
            JsonSnapshot = options.IncludeJson ? stored.Json : null,
            ConcurrencyToken = stored.ConcurrencyToken
        });
    }

    /// <inheritdoc />
    public Task<string?> LoadJsonAsync(string documentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_documents.TryGetValue(documentId, out var stored) ? stored.Json : null);
    }

    /// <inheritdoc />
    public virtual Task<DocumentEditorSaveResult> SaveAsync(
        DocumentEditorSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_documents.TryGetValue(request.DocumentId, out var stored)
            && request.ConcurrencyMode == DocumentEditorConcurrencyMode.Required
            && stored.ConcurrencyToken != request.BaseConcurrencyToken)
        {
            return Task.FromResult(DocumentEditorSaveResult.ConcurrencyConflict(stored.ConcurrencyToken));
        }

        if (_documents.TryGetValue(request.DocumentId, out stored)
            && request.ConcurrencyMode == DocumentEditorConcurrencyMode.Optional
            && request.BaseConcurrencyToken is not null
            && stored.ConcurrencyToken != request.BaseConcurrencyToken)
        {
            return Task.FromResult(DocumentEditorSaveResult.ConcurrencyConflict(stored.ConcurrencyToken));
        }

        var document = request.Document ?? DocumentEditorJson.Deserialize(request.JsonSnapshot ?? string.Empty);
        document.DocumentId = request.DocumentId;
        if (request.Document is not null)
        {
            document.Metadata.ModifiedAt = DateTimeOffset.UtcNow;
        }

        if (!request.PreserveImageBlocks)
        {
            DocumentImagePersistence.ConvertImageBlocksToDrawingRuns(document);
        }

        DocumentImagePersistence.Sanitize(document);

        var json = request.Document is not null
            ? DocumentEditorJson.Serialize(document)
            : request.NormalizeJson ? DocumentEditorJson.Serialize(document) : request.JsonSnapshot!;

        var savedDocument = request.Document is not null ? Clone(document) : DocumentEditorJson.Deserialize(json);
        var concurrencyToken = CreateConcurrencyToken();
        _documents[request.DocumentId] = new StoredDocument(savedDocument, json, concurrencyToken);

        return Task.FromResult(DocumentEditorSaveResult.Saved(Clone(savedDocument), json, concurrencyToken));
    }

    /// <inheritdoc />
    public virtual Task<DocumentVersion> CreateVersionAsync(
        DocumentVersionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(request.DocumentId, out var stored))
        {
            throw new KeyNotFoundException($"Document '{request.DocumentId}' was not found.");
        }

        var snapshot = new DocumentVersionSnapshot
        {
            DocumentId = request.DocumentId,
            SchemaVersion = stored.Document.SchemaVersion,
            Json = stored.Json
        };
        snapshot.Hash = DocumentVersionHashHelper.ComputeSnapshotHash(snapshot);

        var version = new DocumentVersion
        {
            DocumentId = request.DocumentId,
            Kind = request.Kind,
            Label = request.Label,
            Description = request.Description,
            Author = request.Author,
            Snapshot = snapshot
        };

        if (!_versions.TryGetValue(request.DocumentId, out var versions))
        {
            versions = [];
            _versions[request.DocumentId] = versions;
        }

        versions.Add(Clone(version));
        return Task.FromResult(Clone(version));
    }

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<DocumentVersion>> GetVersionsAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var versions = _versions.TryGetValue(documentId, out var stored)
            ? stored.Select(Clone).ToList()
            : [];

        return Task.FromResult<IReadOnlyList<DocumentVersion>>(versions);
    }

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<DocumentComment>> GetCommentsAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var comments = _documents.TryGetValue(documentId, out var stored)
            ? stored.Document.Comments.Select(Clone).ToList()
            : [];

        return Task.FromResult<IReadOnlyList<DocumentComment>>(comments);
    }

    /// <inheritdoc />
    public virtual Task<DocumentComment> CreateCommentAsync(
        string documentId,
        DocumentComment comment,
        CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(documentId, out var stored))
        {
            throw new KeyNotFoundException($"Document '{documentId}' was not found.");
        }

        var storedComment = NormalizeComment(Clone(comment));
        stored.Document.Comments.Add(storedComment);
        StoreDocument(stored.Document, stored.ConcurrencyToken);
        return Task.FromResult(Clone(storedComment));
    }

    /// <inheritdoc />
    public virtual Task<DocumentComment> AddCommentReplyAsync(
        string documentId,
        string commentId,
        DocumentCommentEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(documentId, out var stored))
        {
            throw new KeyNotFoundException($"Document '{documentId}' was not found.");
        }

        var comment = stored.Document.Comments.First(item => item.Id == commentId);
        var storedEntry = NormalizeCommentEntry(Clone(entry));
        comment.Entries.Add(storedEntry);
        StoreDocument(stored.Document, stored.ConcurrencyToken);
        return Task.FromResult(Clone(comment));
    }

    /// <inheritdoc />
    public virtual Task<DocumentComment> UpdateCommentEntryAsync(
        string documentId,
        string commentId,
        string entryId,
        string text,
        DocumentEditorAuthor updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(documentId, out var stored))
        {
            throw new KeyNotFoundException($"Document '{documentId}' was not found.");
        }

        var comment = stored.Document.Comments.First(item => item.Id == commentId);
        var entry = comment.Entries.First(item => item.Id == entryId);
        entry.Text = text.Trim();
        entry.ModifiedAt = DateTimeOffset.UtcNow;
        StoreDocument(stored.Document, stored.ConcurrencyToken);
        return Task.FromResult(Clone(comment));
    }

    /// <inheritdoc />
    public virtual Task<DocumentComment> ResolveCommentAsync(
        string documentId,
        string commentId,
        DocumentEditorAuthor resolvedBy,
        CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(documentId, out var stored))
        {
            throw new KeyNotFoundException($"Document '{documentId}' was not found.");
        }

        var comment = stored.Document.Comments.First(item => item.Id == commentId);
        comment.Status = DocumentCommentStatus.Resolved;
        comment.ResolvedAt = DateTimeOffset.UtcNow;
        comment.ResolvedBy = resolvedBy;
        StoreDocument(stored.Document, stored.ConcurrencyToken);
        return Task.FromResult(Clone(comment));
    }

    /// <inheritdoc />
    public virtual Task<DocumentComment> ReopenCommentAsync(
        string documentId,
        string commentId,
        DocumentEditorAuthor reopenedBy,
        CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(documentId, out var stored))
        {
            throw new KeyNotFoundException($"Document '{documentId}' was not found.");
        }

        var comment = stored.Document.Comments.First(item => item.Id == commentId);
        comment.Status = DocumentCommentStatus.Open;
        comment.ResolvedAt = null;
        comment.ResolvedBy = null;
        StoreDocument(stored.Document, stored.ConcurrencyToken);
        return Task.FromResult(Clone(comment));
    }

    /// <inheritdoc />
    public virtual Task DeleteCommentAsync(
        string documentId,
        string commentId,
        DocumentEditorAuthor deletedBy,
        CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(documentId, out var stored))
        {
            throw new KeyNotFoundException($"Document '{documentId}' was not found.");
        }

        stored.Document.Comments.RemoveAll(item => item.Id == commentId);
        StoreDocument(stored.Document, stored.ConcurrencyToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TmCommentThread>> GetForEntityAsync(
        TmEntityRef entityRef,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(entityRef.EntityType, DocumentCommentBridge.EntityType, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(entityRef.EntityId))
        {
            return [];
        }

        var comments = await GetCommentsAsync(entityRef.EntityId, cancellationToken);
        return comments
            .Select(comment => DocumentCommentBridge.ToTmCommentThread(comment, entityRef.EntityId))
            .ToList();
    }

    /// <inheritdoc />
    public virtual async Task<TmCommentThread> CreateThreadAsync(
        TmCommentThread thread,
        CancellationToken cancellationToken = default)
    {
        var documentId = thread.EntityRef.EntityId;
        var created = await CreateCommentAsync(
            documentId,
            DocumentCommentBridge.ToDocumentComment(thread),
            cancellationToken);

        return DocumentCommentBridge.ToTmCommentThread(created, documentId);
    }

    /// <inheritdoc />
    public virtual async Task<TmCommentEntry> ReplyAsync(
        string threadId,
        TmCommentEntry entry,
        CancellationToken cancellationToken = default)
    {
        FindStoredDocumentByCommentId(threadId, out var documentId);
        entry.ThreadId = threadId;
        var documentEntry = DocumentCommentBridge.ToDocumentCommentEntry(entry);
        var updated = await AddCommentReplyAsync(documentId, threadId, documentEntry, cancellationToken);
        var storedEntry = updated.Entries.First(item => item.Id == documentEntry.Id);
        return DocumentCommentBridge.ToTmCommentEntry(storedEntry, threadId);
    }

    /// <inheritdoc />
    public virtual async Task<TmCommentEntry> UpdateEntryAsync(
        string threadId,
        string entryId,
        TmCommentEntry entry,
        CancellationToken cancellationToken = default)
    {
        FindStoredDocumentByCommentId(threadId, out var documentId);
        var updated = await UpdateCommentEntryAsync(
            documentId,
            threadId,
            entryId,
            entry.Body,
            DocumentCommentBridge.ToDocumentEditorAuthor(entry.Author),
            cancellationToken);

        var storedEntry = updated.Entries.First(item => item.Id == entryId);
        return DocumentCommentBridge.ToTmCommentEntry(storedEntry, threadId);
    }

    /// <inheritdoc />
    public virtual async Task DeleteThreadAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        FindStoredDocumentByCommentId(threadId, out var documentId);
        await DeleteCommentAsync(documentId, threadId, new DocumentEditorAuthor(), cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task DeleteEntryAsync(
        string threadId,
        string entryId,
        CancellationToken cancellationToken = default)
    {
        var stored = FindStoredDocumentByCommentId(threadId, out _);
        var comment = stored.Document.Comments.First(item => item.Id == threadId);
        comment.Entries.RemoveAll(item => item.Id == entryId);
        StoreDocument(stored.Document, stored.ConcurrencyToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual async Task<TmCommentThread> ResolveAsync(
        string threadId,
        TmUserRef? resolvedBy = null,
        CancellationToken cancellationToken = default)
    {
        FindStoredDocumentByCommentId(threadId, out var documentId);
        var updated = await ResolveCommentAsync(
            documentId,
            threadId,
            resolvedBy is null ? new DocumentEditorAuthor() : DocumentCommentBridge.ToDocumentEditorAuthor(resolvedBy),
            cancellationToken);

        return DocumentCommentBridge.ToTmCommentThread(updated, documentId);
    }

    /// <inheritdoc />
    public virtual async Task<TmCommentThread> ReopenAsync(
        string threadId,
        TmUserRef? reopenedBy = null,
        CancellationToken cancellationToken = default)
    {
        FindStoredDocumentByCommentId(threadId, out var documentId);
        var updated = await ReopenCommentAsync(
            documentId,
            threadId,
            reopenedBy is null ? new DocumentEditorAuthor() : DocumentCommentBridge.ToDocumentEditorAuthor(reopenedBy),
            cancellationToken);

        return DocumentCommentBridge.ToTmCommentThread(updated, documentId);
    }

    /// <inheritdoc />
    async Task<IReadOnlyList<TmActivityEntry>> ITmActivityProvider.GetForEntityAsync(
        TmEntityRef entityRef,
        CancellationToken cancellationToken)
    {
        var result = await ((ITmActivityProvider)this).QueryAsync(new TmActivityQuery
        {
            EntityRef = entityRef,
            Take = 500
        }, cancellationToken);

        return result.Items;
    }

    /// <inheritdoc />
    Task<PagedResult<TmActivityEntry>> ITmActivityProvider.QueryAsync(
        TmActivityQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);

        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 500);
        var matches = _activityEntries
            .Where(entry => MatchesActivity(entry, query))
            .OrderByDescending(entry => entry.Timestamp)
            .ThenByDescending(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult(new PagedResult<TmActivityEntry>
        {
            Items = matches.Skip(skip).Take(take).Select(Clone).ToArray(),
            TotalCount = matches.Length,
            Page = skip / take + 1,
            PageSize = take
        });
    }

    /// <inheritdoc />
    Task<TmActivityEntry> ITmActivityProvider.AppendAsync(
        TmActivityEntry entry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);

        var normalized = NormalizeActivity(entry);
        _activityEntries.Add(Clone(normalized));
        return Task.FromResult(Clone(normalized));
    }

    /// <summary>Stores a document snapshot in memory.</summary>
    protected void StoreDocument(DocumentEditorDocument document, string? concurrencyToken = null)
    {
        var clone = Clone(document);
        DocumentImagePersistence.Sanitize(clone);
        _documents[clone.DocumentId] = new StoredDocument(
            clone,
            DocumentEditorJson.Serialize(clone),
            concurrencyToken ?? CreateConcurrencyToken());
    }

    /// <summary>Stores a prepared document version in memory.</summary>
    protected void StoreVersion(DocumentVersion version)
    {
        if (!_versions.TryGetValue(version.DocumentId, out var versions))
        {
            versions = [];
            _versions[version.DocumentId] = versions;
        }

        versions.Add(Clone(version));
    }

    private static string CreateConcurrencyToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    private static DocumentComment NormalizeComment(DocumentComment comment)
    {
        if (string.IsNullOrWhiteSpace(comment.Id))
        {
            comment.Id = Guid.NewGuid().ToString("N");
        }

        foreach (var entry in comment.Entries)
        {
            NormalizeCommentEntry(entry);
        }

        return comment;
    }

    private static DocumentCommentEntry NormalizeCommentEntry(DocumentCommentEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            entry.Id = Guid.NewGuid().ToString("N");
        }

        if (entry.CreatedAt == default)
        {
            entry.CreatedAt = DateTimeOffset.UtcNow;
        }

        return entry;
    }

    private static TmActivityEntry NormalizeActivity(TmActivityEntry entry)
    {
        var clone = Clone(entry);
        clone.Id = string.IsNullOrWhiteSpace(clone.Id) ? Guid.NewGuid().ToString("N") : clone.Id.Trim();
        clone.Action = string.IsNullOrWhiteSpace(clone.Action) ? "unknown" : clone.Action.Trim();
        clone.Timestamp = clone.Timestamp == default ? DateTimeOffset.UtcNow : clone.Timestamp.ToUniversalTime();
        clone.EntityRef = clone.EntityRef.IsValid
            ? clone.EntityRef.Normalize()
            : TmEntityRef.Create(DocumentEditorActivityBridge.EntityType, "unknown");

        return clone;
    }

    private static bool MatchesActivity(TmActivityEntry entry, TmActivityQuery query)
    {
        if (query.EntityRef?.IsValid == true && !entry.EntityRef.Equals(query.EntityRef))
            return false;

        if (!string.IsNullOrWhiteSpace(query.EntityType) && !string.Equals(entry.EntityRef.EntityType, query.EntityType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(query.EntityId) && !string.Equals(entry.EntityRef.EntityId, query.EntityId, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(query.ActorId) && !string.Equals(entry.Actor?.Id, query.ActorId, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(query.SearchText)
            && !Contains(entry.Actor?.Id ?? string.Empty, query.SearchText)
            && !Contains(entry.Actor?.DisplayName ?? string.Empty, query.SearchText)
            && !Contains(entry.Summary ?? string.Empty, query.SearchText))
            return false;

        if (!string.IsNullOrWhiteSpace(query.Action) && !string.Equals(entry.Action, query.Action, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(query.CorrelationId) && !string.Equals(entry.CorrelationId, query.CorrelationId, StringComparison.Ordinal))
            return false;

        return (query.From is null || entry.Timestamp >= query.From.Value)
            && (query.To is null || entry.Timestamp <= query.To.Value);
    }

    private static bool Contains(string value, string filter)
        => value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private StoredDocument FindStoredDocumentByCommentId(string commentId, out string documentId)
    {
        foreach (var pair in _documents)
        {
            if (pair.Value.Document.Comments.Any(comment => comment.Id == commentId))
            {
                documentId = pair.Key;
                return pair.Value;
            }
        }

        throw new KeyNotFoundException($"Comment '{commentId}' was not found.");
    }

    // B10: the demo footer must use real page-number / page-count fields (it printed the literal "Page 1" on
    // every page). The field engine resolves PageNumber/PageCount per rendered page.
    private static DocumentHeaderFooter CreateSeedFooterWithPageFields(string id)
    {
        InlineMark FontSize() => new() { Type = InlineMarkType.FontSize, Value = "9pt" };
        return new DocumentHeaderFooter
        {
            Id = id,
            Type = DocumentHeaderFooterType.Footer,
            Scope = DocumentHeaderFooterScope.Primary,
            SectionId = "contract-section-main",
            Blocks =
            [
                new DocumentBlock
                {
                    Id = $"{id}-block",
                    SectionId = "contract-section-main",
                    Type = DocumentBlockType.Paragraph,
                    Content = new ParagraphBlockContent
                    {
                        Inlines =
                        [
                            new TextRun { Id = $"{id}-prefix", Text = "Confidential · Page ", Marks = [FontSize()] },
                            new DocumentFieldRun { Id = $"{id}-page", FieldType = DocumentFieldType.PageNumber, FallbackText = "1", Marks = [FontSize()] },
                            new TextRun { Id = $"{id}-sep", Text = " of ", Marks = [FontSize()] },
                            new DocumentFieldRun { Id = $"{id}-count", FieldType = DocumentFieldType.PageCount, FallbackText = "1", Marks = [FontSize()] }
                        ]
                    }
                }
            ]
        };
    }

    private static DocumentHeaderFooter CreateSeedHeaderFooter(string id, DocumentHeaderFooterType type, string text)
    {
        return new DocumentHeaderFooter
        {
            Id = id,
            Type = type,
            Scope = DocumentHeaderFooterScope.Primary,
            SectionId = "contract-section-main",
            Blocks =
            [
                new DocumentBlock
                {
                    Id = $"{id}-block",
                    SectionId = "contract-section-main",
                    Type = DocumentBlockType.Paragraph,
                    Content = new ParagraphBlockContent
                    {
                        Inlines =
                        [
                            new TextRun
                            {
                                Id = $"{id}-text",
                                Text = text,
                                Marks = [new InlineMark { Type = InlineMarkType.FontSize, Value = "9pt" }]
                            }
                        ]
                    }
                }
            ]
        };
    }

    private static DocumentHeaderFooter CreateRecoveryHeader()
        => new()
        {
            Id = "recovery-header-primary",
            Type = DocumentHeaderFooterType.Header,
            Scope = DocumentHeaderFooterScope.Primary,
            SectionId = RecoverySectionId,
            Blocks =
            [
                new DocumentBlock
                {
                    Id = "recovery-header-primary-block",
                    SectionId = RecoverySectionId,
                    Type = DocumentBlockType.Paragraph,
                    Content = new ParagraphBlockContent
                    {
                        Inlines =
                        [
                            new TextRun
                            {
                                Id = "recovery-header-primary-text",
                                Text = "Recovery Primary Header",
                                Marks = [new InlineMark { Type = InlineMarkType.FontSize, Value = "9pt" }]
                            }
                        ]
                    }
                }
            ]
        };

    private static DocumentHeaderFooter CreateRecoveryFooter()
        => new()
        {
            Id = "recovery-footer-primary",
            Type = DocumentHeaderFooterType.Footer,
            Scope = DocumentHeaderFooterScope.Primary,
            SectionId = RecoverySectionId,
            Blocks =
            [
                new DocumentBlock
                {
                    Id = "recovery-footer-primary-block",
                    SectionId = RecoverySectionId,
                    Type = DocumentBlockType.Paragraph,
                    Content = new ParagraphBlockContent
                    {
                        Inlines =
                        [
                            new TextRun
                            {
                                Id = "recovery-footer-primary-prefix",
                                Text = "Recovery Primary Footer - Page ",
                                Marks = [new InlineMark { Type = InlineMarkType.FontSize, Value = "9pt" }]
                            },
                            new DocumentFieldRun
                            {
                                Id = "recovery-footer-primary-page-number",
                                FieldType = DocumentFieldType.PageNumber,
                                FallbackText = "1",
                                DisplayText = "1",
                                Marks = [new InlineMark { Type = InlineMarkType.FontSize, Value = "9pt" }]
                            }
                        ]
                    }
                }
            ]
        };

    private static DocumentBlock CreateParagraph(string id, double order, string text, double spacingAfter = 10)
        => CreateParagraph(
            id,
            order,
            [new TextRun { Id = $"{id}-text", Text = text }],
            spacingAfter);

    private static DocumentBlock CreateParagraph(string id, double order, List<InlineContent> inlines, double spacingAfter = 10)
        => new()
        {
            Id = id,
            SectionId = RecoverySectionId,
            Type = DocumentBlockType.Paragraph,
            Order = order,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = DocumentTextAlignment.Left,
                LineSpacing = 1.25,
                SpacingAfter = spacingAfter
            },
            Content = new ParagraphBlockContent { Inlines = inlines }
        };

    private static DocumentBlock CreatePhase18Heading(string id, string sectionId, double order, int level, string text)
        => new()
        {
            Id = id,
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = order,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 10 },
            Content = new HeadingBlockContent
            {
                Level = level,
                Inlines = [new TextRun { Id = $"{id}-text", Text = text }]
            }
        };

    private static DocumentBlock CreatePhase18Paragraph(string id, string sectionId, double order, string text)
        => CreatePhase18Paragraph(id, sectionId, order, [new TextRun { Id = $"{id}-text", Text = text }]);

    private static DocumentBlock CreatePhase18Paragraph(string id, string sectionId, double order, List<InlineContent> inlines)
        => new()
        {
            Id = id,
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = order,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = DocumentTextAlignment.Left,
                LineSpacing = 1.22,
                SpacingAfter = 8
            },
            Content = new ParagraphBlockContent { Inlines = inlines }
        };

    private static DocumentBlock CreateImage(
        string id,
        double order,
        DocumentImageSource source,
        string? url,
        string? assetId,
        string? altText,
        string caption,
        double width,
        double height,
        DocumentImageAlignment alignment,
        DocumentObjectLayout layout)
    {
        var drawing = CreateImageDrawingRun(id, source, url, assetId, altText, caption, width, height, layout);
        return new DocumentBlock
        {
            Id = id,
            SectionId = RecoverySectionId,
            Type = DocumentBlockType.Paragraph,
            Order = order,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = ToTextAlignment(alignment),
                LineSpacing = 1.25,
                SpacingAfter = 9
            },
            Content = new ParagraphBlockContent
            {
                Inlines = [drawing]
            }
        };
    }

    private static DocumentTextAlignment ToTextAlignment(DocumentImageAlignment alignment)
        => alignment switch
        {
            DocumentImageAlignment.Center => DocumentTextAlignment.Center,
            DocumentImageAlignment.End => DocumentTextAlignment.Right,
            _ => DocumentTextAlignment.Left
        };

    private static DocumentDrawingRun CreateImageDrawingRun(
        string objectId,
        DocumentImageSource source,
        string? url,
        string? assetId,
        string? altText,
        string caption,
        double width,
        double height,
        DocumentObjectLayout layout)
    {
        NormalizeDrawingLayout(layout, objectId, 0);
        if (layout.Transform.Width is null or <= 0)
        {
            layout.Transform.Width = width;
        }

        if (layout.Transform.Height is null or <= 0)
        {
            layout.Transform.Height = height;
        }

        layout.Transform.NaturalWidth ??= width;
        layout.Transform.NaturalHeight ??= height;

        var drawing = new DocumentDrawingRun
        {
            Id = $"{objectId}-drawing",
            ObjectId = objectId,
            Source = source,
            Url = source == DocumentImageSource.Url ? url : null,
            AssetId = source == DocumentImageSource.Asset ? assetId : null,
            AltText = altText,
            Caption = caption,
            Size = new DocumentImageSize { Width = width, Height = height },
            NaturalSize = new DocumentImageSize { Width = width, Height = height },
            Layout = layout
        };
        DocumentImagePersistence.Sanitize(drawing);
        return drawing;
    }

    private static void NormalizeDrawingLayout(DocumentObjectLayout layout, string blockId, int inlineIndex)
    {
        layout.Anchor ??= new DocumentObjectAnchor();
        layout.Position ??= new DocumentObjectPosition();
        layout.Wrap ??= new DocumentObjectWrap();
        layout.Transform ??= new DocumentObjectTransform();
        layout.Stacking ??= new DocumentObjectStacking();
        layout.Anchor.BlockId = string.IsNullOrWhiteSpace(layout.Anchor.BlockId) ? blockId : layout.Anchor.BlockId;
        layout.Anchor.InlineIndex ??= inlineIndex;
        layout.Anchor.Offset ??= 0;
    }

    private static void AddRecoveryHeaderFooterDrawingRuns(DocumentEditorDocument document)
    {
        AddDrawingRunToHeaderFooter(
            document,
            "recovery-header-primary",
            "recovery-header-primary-block",
            CreateRegionDrawingRun(
                "recovery-header-logo-image",
                "Header logo evidence",
                DocumentRenditionAnchorScope.Header,
                "recovery-header-primary",
                "recovery-header-primary-block",
                52,
                29));
        AddDrawingRunToHeaderFooter(
            document,
            "recovery-footer-primary",
            "recovery-footer-primary-block",
            CreateRegionDrawingRun(
                "recovery-footer-logo-image",
                "Footer logo evidence",
                DocumentRenditionAnchorScope.Footer,
                "recovery-footer-primary",
                "recovery-footer-primary-block",
                44,
                25));
    }

    private static DocumentDrawingRun CreateRegionDrawingRun(
        string objectId,
        string altText,
        DocumentRenditionAnchorScope region,
        string headerFooterId,
        string blockId,
        double width,
        double height)
    {
        var drawing = CreateImageDrawingRun(
            objectId,
            DocumentImageSource.Url,
            RecoveryUrlImageUrl,
            null,
            altText,
            altText,
            width,
            height,
            DocumentObjectLayout.Inline());
        drawing.Layout.Anchor.Region = region;
        drawing.Layout.Anchor.HeaderFooterId = headerFooterId;
        drawing.Layout.Anchor.BlockId = blockId;
        return drawing;
    }

    private static DocumentDrawingRun CreateRecoveryDrawingRun(
        string objectId,
        string altText,
        DocumentWrapMode wrapMode,
        string anchorBlockId,
        double width,
        double height,
        int zIndex)
        => CreateOnlyOfficeParityDrawingRun(objectId, altText, wrapMode, anchorBlockId, width, height, zIndex);

    private static void AddDrawingRunToHeaderFooter(
        DocumentEditorDocument document,
        string headerFooterId,
        string blockId,
        DocumentDrawingRun drawing)
    {
        var paragraph = document.HeadersFooters
            .FirstOrDefault(headerFooter => string.Equals(headerFooter.Id, headerFooterId, StringComparison.Ordinal))
            ?.Blocks.FirstOrDefault(block => string.Equals(block.Id, blockId, StringComparison.Ordinal))
            ?.Content as ParagraphBlockContent;
        if (paragraph is null)
        {
            return;
        }

        drawing.Layout.Anchor.InlineIndex = paragraph.Inlines.Count;
        paragraph.Inlines.Add(drawing);
    }

    private static DocumentObjectLayout CreateRecoveryWrappedImageLayout(
        double width,
        double height,
        DocumentImageHorizontalPosition horizontalPosition,
        string anchorBlockId)
        => new()
        {
            Kind = DocumentObjectLayoutKind.Anchored,
            Anchor = new DocumentObjectAnchor
            {
                BlockId = anchorBlockId,
                MoveWithText = true,
                FixedOnPage = false
            },
            Position = new DocumentObjectPosition
            {
                HorizontalRelativeTo = DocumentRelativePosition.Page,
                VerticalRelativeTo = DocumentRelativePosition.Paragraph,
                HorizontalAlignment = horizontalPosition
            },
            Wrap = new DocumentObjectWrap
            {
                Mode = DocumentWrapMode.Square,
                DistanceLeft = horizontalPosition == DocumentImageHorizontalPosition.Right ? 16 : 0,
                DistanceRight = horizontalPosition == DocumentImageHorizontalPosition.Left ? 16 : 0,
                DistanceBottom = 12
            },
            Transform = new DocumentObjectTransform
            {
                Width = width,
                Height = height,
                NaturalWidth = width,
                NaturalHeight = height,
                LockAspectRatio = true
            }
        };

    private static DocumentObjectLayout CreateRecoveryTopBottomImageLayout(double width, double height, string anchorBlockId)
        => new()
        {
            Kind = DocumentObjectLayoutKind.Anchored,
            Anchor = new DocumentObjectAnchor
            {
                BlockId = anchorBlockId,
                MoveWithText = true,
                FixedOnPage = false
            },
            Position = new DocumentObjectPosition
            {
                HorizontalRelativeTo = DocumentRelativePosition.Page,
                VerticalRelativeTo = DocumentRelativePosition.Paragraph,
                HorizontalAlignment = DocumentImageHorizontalPosition.Center
            },
            Wrap = new DocumentObjectWrap
            {
                Mode = DocumentWrapMode.TopBottom,
                DistanceTop = 10,
                DistanceBottom = 12
            },
            Transform = new DocumentObjectTransform
            {
                Width = width,
                Height = height,
                NaturalWidth = width,
                NaturalHeight = height,
                LockAspectRatio = true
            }
        };

    private static DocumentBlock CreateRecoveryTable()
        => new()
        {
            Id = "recovery-table-under-images",
            SectionId = RecoverySectionId,
            Type = DocumentBlockType.Table,
            Order = 130,
            Content = new TableBlockContent
            {
                Layout = new TableLayoutContent
                {
                    Width = 420,
                    Alignment = TableHorizontalAlignment.Center,
                    CellPadding = 7,
                    Borders = new TableCellBorders
                    {
                        Top = "1px solid #cbd5e1",
                        Right = "1px solid #cbd5e1",
                        Bottom = "1px solid #cbd5e1",
                        Left = "1px solid #cbd5e1"
                    }
                },
                Rows =
                [
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateRecoveryTableCell("Scenario", true),
                            CreateRecoveryTableCell("Expected visible state", true)
                        ]
                    },
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateRecoveryTableCell("Images"),
                            CreateRecoveryTableImageCell()
                        ]
                    },
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateRecoveryTableCell("Review"),
                            CreateRecoveryTableCell("Comments and revisions are visible in text")
                        ]
                    }
                ]
            }
        };

    private static TableCellContent CreateRecoveryTableCell(string text, bool isHeader = false)
        => new()
        {
            IsHeader = isHeader,
            Blocks =
            [
                new DocumentBlock
                {
                    Type = DocumentBlockType.Paragraph,
                    Content = new ParagraphBlockContent
                    {
                        Inlines = [new TextRun { Text = text }]
                    }
                }
            ]
        };

    private static TableCellContent CreateRecoveryTableImageCell()
    {
        var blockId = "recovery-table-image-cell-block";
        var cellId = "recovery-table-image-cell";
        var drawing = CreateImageDrawingRun(
            "recovery-table-cell-image",
            DocumentImageSource.Url,
            RecoveryUrlImageUrl,
            null,
            "Table cell evidence image",
            "Table cell evidence image",
            72,
            41,
            DocumentObjectLayout.Inline());
        drawing.Layout.Anchor.BlockId = blockId;
        drawing.Layout.Anchor.TableId = "recovery-table-under-images";
        drawing.Layout.Anchor.CellId = cellId;

        return new TableCellContent
        {
            Id = cellId,
            Blocks =
            [
                new DocumentBlock
                {
                    Id = blockId,
                    Type = DocumentBlockType.Paragraph,
                    Content = new ParagraphBlockContent
                    {
                        Inlines =
                        [
                            new TextRun { Id = "recovery-table-image-cell-prefix", Text = "All image layouts render before this table " },
                            drawing
                        ]
                    }
                }
            ]
        };
    }

    private static DocumentRevision CreateRecoveryRevision(
        string id,
        DocumentRevisionType type,
        string blockId,
        int inlineIndex,
        string text,
        int createdAtOffsetMinutes)
        => new()
        {
            Id = id,
            Type = type,
            Range = new DocumentRevisionRange
            {
                BlockId = blockId,
                StartInlineIndex = inlineIndex,
                EndInlineIndex = inlineIndex,
                StartOffset = 0,
                EndOffset = text.Length
            },
            Author = new DocumentRevisionAuthor
            {
                Id = "recovery-reviewer",
                DisplayName = "Recovery Reviewer",
                Email = "recovery@example.local"
            },
            CreatedAt = new DateTimeOffset(2026, 5, 23, 8, createdAtOffsetMinutes, 0, TimeSpan.Zero),
            Action = DocumentRevisionAction.Pending,
            PayloadJson = text
        };

    private static T Clone<T>(T value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value, DocumentEditorJson.Options);
        return System.Text.Json.JsonSerializer.Deserialize<T>(json, DocumentEditorJson.Options)
            ?? throw new System.Text.Json.JsonException("Could not clone document editor value.");
    }

    private sealed record StoredDocument(DocumentEditorDocument Document, string Json, string ConcurrencyToken);
}
