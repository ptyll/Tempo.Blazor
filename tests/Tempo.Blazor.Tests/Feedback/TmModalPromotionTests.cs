using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// F3 follow-up (c51ddf81 #1): the trap of a promoted overlay root may only activate AFTER the
/// promote interop completes. While the popover root is still unpromoted it can be
/// <c>display:none</c> (the UA rule <c>[popover]:not(:popover-open)</c> wins whenever a consumer
/// class or a future CSS edit removes the root's own author <c>display</c>), and the trap's
/// initial-focus move then lands on <c>&lt;body&gt;</c>. This test file is separate from
/// <c>TmModalTests</c> because that class replaces <c>IJSRuntime</c> with an NSubstitute mock —
/// the module invocations never reach bUnit's JSInterop, so a pending <c>promote</c> could not be
/// staged there.
/// </summary>
public class TmModalPromotionTests : LocalizationTestBase
{
    private const string TopLayerModule = "./_content/Tempo.Blazor/js/tm-top-layer.js";
    private const string FocusTrapModule = "./_content/Tempo.Blazor/js/tm-focus-trap.js";

    [Fact]
    public void TmModal_FocusTrap_ActivatesOnlyAfterPromotion()
    {
        var top = JSInterop.SetupModule(TopLayerModule);
        var promote = top.SetupVoid("promote", _ => true); // no result: the call stays pending
        var trap = JSInterop.SetupModule(FocusTrapModule);
        var activate = trap.SetupVoid("activate", _ => true).SetVoidResult();

        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Promotion gate")
            .AddChildContent("<p>content</p>"));

        activate.Invocations["activate"].Should().BeEmpty(
            "the trap must not activate while the promote interop is still pending — the popover " +
            "root can still be display:none and the initial-focus move would land on <body>");

        promote.SetVoidResult();
        cut.WaitForAssertion(() => activate.Invocations["activate"].Should().HaveCount(1, "completing the promote is what releases the trap's activation"));
    }
}
