using System.Text.RegularExpressions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

public class StencilPackFeedbackFormsTests
{
    private static readonly string[] FeedbackFormTypes =
    [
        "TmAlert",
        "TmModal",
        "TmDialog",
        "TmTooltip",
        "TmPopover",
        "TmProgressBar",
        "TmSpinner",
        "TmSkeleton",
        "TmToastContainer",
        "TmAutoSaveIndicator",
        "TmNotificationBell",
        "TmFormSection",
        "TmFormRow",
        "TmFormField",
        "TmInlineEdit",
        "TmValidatedField",
        "TmFormValidationMessage",
        "TmValidationSummary",
        "TmDynamicFormRenderer",
        "TmConditionBuilder",
        "TmFormulaBuilder",
        "TmFileDropZone",
        "TmAttachmentManager",
        "TmAvatar",
        "TmAvatarGroup",
        "TmIcon",
        "TmColorPicker",
        "TmFlatColorPicker",
        "TmColorPalette",
        "TmColorGradient",
        "TmApprovalCard",
        "TmReviewQueue"
    ];

    public static TheoryData<string> FeedbackFormTypeData
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var type in FeedbackFormTypes)
                data.Add(type);
            return data;
        }
    }

    [Fact]
    public void FeedbackFormTypeList_CoversPlanScope()
    {
        FeedbackFormTypes.Should().HaveCount(32);
        FeedbackFormTypes.Should().Contain(["TmAlert", "TmProgressBar", "TmNotificationBell", "TmFormField", "TmColorGradient"]);
    }

    [Theory]
    [MemberData(nameof(FeedbackFormTypeData))]
    public void PackDefinition_Exists_WithSeededSize(string type)
    {
        var def = Registry().GetDef(type);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == type);

        using var _ = new AssertionScope(type);
        def.Should().NotBeNull();
        def!.PackId.Should().Be("tempo");
        def.NativeType.Should().BeNull();
        def.IsBuiltIn.Should().BeTrue();
        def.Category.Should().Be(schema.Category);
        def.DisplayName.Should().Be(schema.DisplayName);
        def.DefaultWidth.Should().Be(schema.DefaultWidth);
        def.DefaultHeight.Should().Be(schema.DefaultHeight);
        def.Props.Select(p => p.Name).Should().Equal(schema.Props.Select(p => p.Name));
        def.SizePresets.Should().BeEquivalentTo(schema.SizePresets);
    }

    [Fact]
    public async Task Alert_Danger_RendersRedVariant()
    {
        var svg = await RenderAsync("TmAlert", ("variant", "danger"), ("message", "Payment failed"));

        svg.Should().Contain("#fee2e2");
        svg.Should().Contain(">Payment failed<");
        svg.Should().NotContain(">TmAlert<");
    }

    [Fact]
    public async Task ProgressBar_Success_RendersGreenFill()
    {
        var svg = await RenderAsync("TmProgressBar", ("variant", "success"), ("value", 40), ("max", 100));

        svg.Should().Contain("#22c55e");
        svg.Should().MatchRegex("<rect[^>]+width='96'[^>]+fill='#22c55e'");
        svg.Should().NotContain(">TmProgressBar<");
    }

    [Fact]
    public async Task NotificationBell_Unread_RendersBadge()
    {
        var svg = await RenderAsync("TmNotificationBell", ("unreadCount", 3));

        svg.Should().Contain("#ef4444");
        svg.Should().Contain(">3<");
        svg.Should().NotContain(">TmNotificationBell<");
    }

    [Fact]
    public async Task ColorPalette_RendersSwatches()
    {
        var svg = await RenderAsync("TmColorPalette", ("swatches", 8));
        var swatches = Regex.Matches(svg, "<rect[^>]+fill='#[0-9a-fA-F]{6}'");

        swatches.Count.Should().BeGreaterThanOrEqualTo(8);
        svg.Should().NotContain(">TmColorPalette<");
    }

    [Fact]
    public async Task ApprovalCard_RendersTheRiskAccentBothActionsAndTheDeadline()
    {
        var high = await RenderAsync("TmApprovalCard", ("title", "Release the invoice"), ("risk", "high"));
        high.Should().Contain(">Release the invoice<");
        high.Should().Contain(">Approve<");
        high.Should().Contain(">Reject<");
        high.Should().Contain(">Expires in 04:00<");
        high.Should().Contain("#ef4444", "a high-risk request gets the danger accent");

        var low = await RenderAsync("TmApprovalCard", ("risk", "low"));
        low.Should().Contain("#22c55e", "a low-risk request gets the success accent");

        var quiet = await RenderAsync("TmApprovalCard", ("showCountdown", false));
        quiet.Should().NotContain("Expires in");
    }

    [Fact]
    public async Task ReviewQueue_RendersRowsWithRiskAndBothDecisions()
    {
        var list = await RenderAsync("TmReviewQueue", ("title", "My reviews"), ("itemCount", 3));
        list.Should().Contain(">My reviews<");
        list.Should().Contain(">Invoice #2041<");
        list.Should().Contain(">High risk<");
        list.Should().Contain(">Approve<");
        list.Should().Contain(">Reject<");
        list.Should().Contain("Items to review");
        list.Should().NotContain(">TmReviewQueue<");

        var focus = await RenderAsync("TmReviewQueue", ("mode", "focus"));
        focus.Should().Contain(">Item 1 of 4<");
        focus.Should().Contain(">Detail of the current item<");
        focus.Should().NotContain("items to review", "focus mode is one item at a time");
    }

    [Fact]
    public async Task Avatar_RendersInitialsOrCircle()
    {
        var svg = await RenderAsync("TmAvatar", ("name", "JD"), ("color", "blue"));

        svg.Should().Contain("<rect");
        svg.Should().Contain(">JD<");
        svg.Should().NotContain(">TmAvatar<");
    }

    [Theory]
    [MemberData(nameof(FeedbackFormTypeData))]
    public async Task PackComponents_ProduceSafeSvg(string type)
    {
        var svg = await RenderAsync(type, DangerousPropsFor(type));

        using var _ = new AssertionScope(type);
        svg.Should().StartWith("<svg");
        svg.Should().Contain("<rect");
        svg.Should().NotContainEquivalentOf("<script");
        svg.Should().NotContainEquivalentOf("javascript:");
        svg.Should().NotContainEquivalentOf("onerror=");
        svg.Should().NotContainEquivalentOf("<foreignObject");
    }

    [Fact]
    public void Registry_WithPack_CoversAllBuiltInSchemas()
    {
        var registry = Registry();
        var missing = new BuiltInComponentSchemas()
            .GetSchemas()
            .Select(schema => schema.Type)
            .Where(type => registry.GetDef(type) is null)
            .ToArray();

        missing.Should().BeEmpty();
    }

    private static (string Key, object? Value)[] DangerousPropsFor(string type)
        => type switch
        {
            "TmColorPicker" => [("value", "<script>alert(1)</script>")],
            "TmColorGradient" => [("startColor", "<script>alert(1)</script>"), ("endColor", "#8b5cf6")],
            _ => []
        };

    private static async Task<string> RenderAsync(
        string type,
        params (string Key, object? Value)[] props)
    {
        var def = Registry().GetDef(type) ?? throw new InvalidOperationException($"Missing definition for {type}.");
        var page = new WireframePage { Id = "page", Name = "Golden", Width = Math.Max(640, def.DefaultWidth + 80), Height = Math.Max(360, def.DefaultHeight + 80) };
        var element = new WireframeElement
        {
            Id = "sut",
            Type = type,
            X = 40,
            Y = 40,
            W = def.DefaultWidth,
            H = def.DefaultHeight
        };
        foreach (var (key, value) in props)
            element.SetProp(key, value);

        page.Elements.Add(element);
        return await Renderer().RenderPageAsync(page);
    }

    private static WireframeSvgRenderer Renderer()
        => new(Registry(), Services());

    private static WireframeComponentRegistry Registry()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());
        return registry;
    }

    private static IServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}
