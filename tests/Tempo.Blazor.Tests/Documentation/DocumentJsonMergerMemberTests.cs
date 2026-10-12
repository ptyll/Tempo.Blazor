using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using JsonDocumentationGenerator;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Documentation;

/// <summary>
/// Overlay <c>members</c> merging (phase FZE-3 review fix).
/// <para>
/// <see cref="DocumentJsonMerger.Merge"/> used to fall through to its default branch for
/// <c>members</c>, so once the generator started emitting members for records (positional
/// parameters, expression-bodied properties) the generated array replaced the overlay's curated
/// one wholesale — every hand-written member description at the repo root went dark. These tests
/// pin the contract: the overlay is the editable source, so its prose wins, while the generated
/// entry stays the source of truth for the API shape (kind, type, order).
/// </para>
/// </summary>
public sealed class DocumentJsonMergerMemberTests
{
    [Fact]
    public void Merge_OverlayMemberDescription_Wins_AndGeneratedKindTypeAreKept()
    {
        var merged = DocumentJsonMerger.Merge(
            new JsonObject
            {
                ["itemName"] = "Widget",
                ["members"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "ElementId",
                        ["kind"] = "Property",
                        ["type"] = "string",
                        ["description"] = "Generated prose that must not win."
                    },
                }
            },
            new JsonObject
            {
                ["members"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "ElementId",
                        ["kind"] = "Field",
                        ["type"] = "System.String",
                        ["description"] = "Id of the element the warning concerns."
                    },
                }
            },
            generatedDescriptionIsFallback: false);

        var member = merged["members"]!.AsArray()[0]!.AsObject();
        member.GetString("description").Should().Be(
            "Id of the element the warning concerns.",
            "the overlay is the editable source — its description must beat the generated one");
        member.GetString("kind").Should().Be("Property", "the generated kind is the API shape");
        member.GetString("type").Should().Be("string", "the generated type is the API shape");
    }

    [Fact]
    public void Merge_OverlayOnlyMembers_AreAppendedAtTheEnd()
    {
        var merged = DocumentJsonMerger.Merge(
            new JsonObject
            {
                ["itemName"] = "Widget",
                ["members"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Known", ["kind"] = "Property", ["type"] = "int" },
                }
            },
            new JsonObject
            {
                ["members"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Extra", ["kind"] = "Field", ["type"] = "bool", ["description"] = "Only the overlay knows this." },
                }
            },
            generatedDescriptionIsFallback: false);

        var names = merged["members"]!.AsArray().Select(m => m!.AsObject().GetString("name")).ToList();
        names.Should().Equal(["Known", "Extra"], "overlay-only members are appended after every generated one");
    }

    [Fact]
    public void Merge_EmptyOverlayDescription_DoesNotBlankGeneratedDescription()
    {
        var merged = DocumentJsonMerger.Merge(
            new JsonObject
            {
                ["itemName"] = "Widget",
                ["members"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "Raw",
                        ["kind"] = "Property",
                        ["type"] = "object?",
                        ["description"] = "Generated prose kept."
                    },
                }
            },
            new JsonObject
            {
                ["members"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Raw", ["kind"] = "Property", ["type"] = "object?" },
                }
            },
            generatedDescriptionIsFallback: false);

        merged["members"]!.AsArray()[0]!.AsObject().GetString("description").Should().Be(
            "Generated prose kept.",
            "an overlay entry without a description must not erase the generated one");
    }

    [Fact]
    public void Merge_OverloadedName_OverlayIsAppliedOnlyToTheMatchingType()
    {
        var merged = DocumentJsonMerger.Merge(
            new JsonObject
            {
                ["itemName"] = "Widget",
                ["members"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Read", ["kind"] = "Method", ["type"] = "string", ["description"] = "String overload." },
                    new JsonObject { ["name"] = "Read", ["kind"] = "Method", ["type"] = "int", ["description"] = "Int overload." },
                }
            },
            new JsonObject
            {
                ["members"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Read", ["kind"] = "Method", ["type"] = "int", ["description"] = "Overlay prose for the int overload." },
                }
            },
            generatedDescriptionIsFallback: false);

        var members = merged["members"]!.AsArray();
        members[0]!.AsObject().GetString("description").Should().Be(
            "String overload.",
            "the overlay entry names an overload of a different type — it must not leak there");
        members[1]!.AsObject().GetString("description").Should().Be(
            "Overlay prose for the int overload.",
            "the overlay entry whose type matches the generated overload is applied");
    }

    [Fact]
    public void Merge_OverlayMemberOtherKeys_FillOnlyMissingGeneratedKeys()
    {
        var merged = DocumentJsonMerger.Merge(
            new JsonObject
            {
                ["itemName"] = "Widget",
                ["members"] = new JsonArray
                {
                    new JsonObject { ["name"] = "State", ["kind"] = "Property", ["type"] = "TmPickerFetchState" },
                }
            },
            new JsonObject
            {
                ["members"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "State",
                        ["kind"] = "Property",
                        ["type"] = "TmPickerFetchState",
                        ["description"] = "Whether the value resolved.",
                        ["examples"] = new JsonArray("state == TmPickerFetchState.Ok")
                    },
                }
            },
            generatedDescriptionIsFallback: false);

        var member = merged["members"]!.AsArray()[0]!.AsObject();
        member.GetString("description").Should().Be("Whether the value resolved.");
        member["examples"].Should().NotBeNull("overlay extras a generated entry lacks are copied");
    }

    [Fact]
    public void TheCommittedMcpBundle_WireframeLintWarningElementId_IsADescribedProperty()
    {
        // Regression: when the generator gained record-member extraction, the generated members
        // array replaced the overlay's wholesale and this description went dark in the committed
        // bundle the MCP server serves.
        var path = Path.Combine(ThemeCss.RepositoryRoot().FullName, "tempo-blazor-mcp.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var warning = FindItem(document.RootElement, "WireframeLintWarning");
        warning.ValueKind.Should().Be(JsonValueKind.Object, "WireframeLintWarning must be documented in tempo-blazor-mcp.json");

        var elementId = warning.GetProperty("members")
            .EnumerateArray()
            .Single(m => m.GetProperty("name").GetString() == "ElementId");

        elementId.GetProperty("kind").GetString().Should().Be("Property");
        elementId.GetProperty("description").GetString().Should().Be("Id of the element the warning concerns.");
    }

    private static JsonElement FindItem(JsonElement root, string itemName)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        // A per-package bundle keeps its items in a top-level array; the aggregate nests them
        // under packages[].items. Walk every array at both levels.
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    var nested = FindItem(property.Value, itemName);
                    if (nested.ValueKind == JsonValueKind.Object)
                    {
                        return nested;
                    }
                }

                continue;
            }

            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("itemName", out var name)
                    && name.GetString() == itemName)
                {
                    return item;
                }
            }
        }

        return default;
    }
}
