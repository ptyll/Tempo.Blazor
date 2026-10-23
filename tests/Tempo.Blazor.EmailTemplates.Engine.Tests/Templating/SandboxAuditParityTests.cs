using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Scriban.Runtime;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Templating;

/// <summary>
/// Audit-trail and configuration-hardening parity with PostrionCore
/// (<c>AuditLoggingTests</c>, <c>SecurityConfigurationTests</c>): every <see cref="TemplateSecurityEventKind"/>
/// reaches a registered sink, sink failures never break a render, options cannot be configured fail-open,
/// and the sandbox context wires the member filter, renamer and limits.
/// </summary>
public class SandboxAuditParityTests
{
    private sealed class SpySink : ITemplateSecurityAuditSink
    {
        public List<TemplateSecurityEvent> Events { get; } = new();

        public void OnEvent(TemplateSecurityEvent securityEvent) => Events.Add(securityEvent);
    }

    private sealed class ThrowingSink : ITemplateSecurityAuditSink
    {
        public void OnEvent(TemplateSecurityEvent securityEvent)
            => throw new InvalidOperationException("sink failure");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static ScribanTemplateEngine Engine(SpySink sink, TemplateSecurityOptions? options = null)
        => new(options, new[] { sink });

    // ---------------------------------------------------------------------------------------------
    // PostrionCore AuditLoggingTests — every Kind reaches the sink
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Audit_BlockedFunction_ReportsBlockedFunctionEvent()
    {
        // PostrionCore: AuditLoggingTests.Render_SuspiciousTemplate_LogsWarning
        var sink = new SpySink();
        var template = "{{ file.read 'C:/secret.txt' }}";

        var result = Engine(sink).Render(template, new { });

        result.IsSuccess.Should().BeFalse();
        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.BlockedFunction);
        sink.Events[0].Detail.Should().Contain("file");
        sink.Events[0].TemplateExcerpt.Should().Be(template);
        sink.Events[0].TemplateHash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(template))));
    }

    [Fact]
    public void Audit_EnvironmentAccess_ReportsBlockedNamespaceEvent()
    {
        // PostrionCore: AuditLoggingTests.Render_EnvironmentAccess_LogsWarning
        var sink = new SpySink();

        var result = Engine(sink).Render("{{ env.PATH }}", new { });

        result.IsSuccess.Should().BeFalse();
        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.BlockedNamespace);
        sink.Events[0].Detail.Should().Contain("env");
    }

    [Fact]
    public void Audit_ReflectionAccess_ReportsBlockedNamespaceEvent()
    {
        // PostrionCore: AuditLoggingTests.Render_ReflectionAccess_LogsWarning
        var sink = new SpySink();

        var result = Engine(sink).Render("{{ @System.Diagnostics.Process.Start('cmd') }}", new { });

        result.IsSuccess.Should().BeFalse();
        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.BlockedNamespace);
        sink.Events[0].Detail.Should().Contain("System.Diagnostics");
    }

    [Fact]
    public void Audit_BlockedMemberName_ReportsBlockedMemberEvent()
    {
        var sink = new SpySink();
        var model = new Dictionary<string, object?> { ["t"] = typeof(string) };

        var result = Engine(sink).Render("{{ t.get_type }}", model);

        result.IsSuccess.Should().BeFalse();
        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.BlockedMember);
        sink.Events[0].Detail.Should().Contain("get_type");
    }

    [Fact]
    public void Audit_ForbiddenNamespaceMember_ReportsBlockedNamespaceEvent()
    {
        var sink = new SpySink();
        var model = new Dictionary<string, object?> { ["t"] = typeof(string) };

        var result = Engine(sink).Render("{{ t.full_name }}", model);

        result.IsSuccess.Should().BeFalse();
        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.BlockedNamespace);
    }

    [Fact]
    public void Audit_LoopLimit_ReportsLimitExceededEvent()
    {
        var sink = new SpySink();
        var engine = Engine(sink, new TemplateSecurityOptions { LoopLimit = 10 });

        var result = engine.Render("{{ for i in 1..100 }}x{{ end }}", new { });

        result.IsSuccess.Should().BeFalse();
        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.LimitExceeded);
    }

    [Fact]
    public void Audit_OutputLimit_ReportsLimitExceededEvent()
    {
        var sink = new SpySink();
        var engine = Engine(sink, new TemplateSecurityOptions { MaxOutputLength = 20 });

        var result = engine.Render("{{ for i in 1..100 }}xxxxxxxxxx{{ end }}", new { });

        result.IsSuccess.Should().BeFalse();
        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.LimitExceeded);
    }

    [Fact]
    public void Audit_RenderTimeout_ReportsTimeoutEvent()
    {
        var sink = new SpySink();
        var engine = Engine(sink, new TemplateSecurityOptions
        {
            LoopLimit = 1_000_000,
            MaxOutputLength = 10_000_000,
            Timeout = TimeSpan.FromTicks(1),
        });

        var result = engine.Render("{{ for i in 1..200000 }}x{{ end }}", new { });

        result.IsSuccess.Should().BeFalse();
        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.Timeout);
    }

    [Fact]
    public void Audit_RegexTimeOut_ReportsTimeoutEvent()
    {
        // Catastrophic backtracking: without the regex timeout this hangs for minutes.
        var sink = new SpySink();
        var engine = Engine(sink, new TemplateSecurityOptions
        {
            RegexTimeOut = TimeSpan.FromMilliseconds(50),
            Timeout = TimeSpan.FromSeconds(30),
        });

        var result = engine.Render("{{ \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!\" | regex.match \"^(a+)+$\" }}", new { });

        result.IsSuccess.Should().BeFalse();
        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.Timeout);
    }

    [Fact]
    public void Audit_SafeTemplate_DoesNotReport()
    {
        // PostrionCore: AuditLoggingTests.Render_SafeTemplate_DoesNotLog
        var sink = new SpySink();

        var result = Engine(sink).Render("Hello {{ name }}!", new { Name = "World" });

        result.IsSuccess.Should().BeTrue();
        sink.Events.Should().BeEmpty();
    }

    [Fact]
    public void Audit_BuiltinRemoval_HoldsEvenWhenTheTextScanCannotSeeTheName()
    {
        // Defence in depth: the name is assembled at render time, so only the removed builtin (not the
        // pre-render scan) can stop `object.eval` from evaluating another template.
        var result = new ScribanTemplateEngine().Render("{{ object['ev' + 'al'] '1 + 2' }}", new { });

        result.IsSuccess.Should().BeFalse("object.eval nesmí být v sandboxu dostupné");
        result.Value.Should().NotBe("3");
    }

    [Fact]
    public void Sandbox_DelegateValuedMember_IsNotCallable()
    {
        var sink = new SpySink();
        var calls = 0;
        var model = new Dictionary<string, object?>
        {
            ["payload"] = new DelegateHolder(() => { calls++; return "ran"; }),
        };

        var result = Engine(sink).Render("{{ payload.factory }}", model);

        calls.Should().Be(0, "šablona nesmí umět zavolat delegáta z modelu");
        result.IsSuccess.Should().BeFalse("delegát-nesoucí člen je odmítnutý člen");
        sink.Events.Should().ContainSingle(e => e.Kind == TemplateSecurityEventKind.BlockedMember);
    }

    [Fact]
    public void Sandbox_DataIdentifierThatLooksLikeAReflectionName_StillRenders()
    {
        // A legitimate variable named `module` must not be refused by the text scan (the probe only
        // reacts to an actual member access).
        var result = new ScribanTemplateEngine().Render("{{ module.name }}", new { module = new { name = "Core" } });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Core");
    }

    [Fact]
    public void Sandbox_DelegateHiddenBehindAnObjectTypedMember_IsNotCallable()
    {
        var calls = 0;
        var model = new Dictionary<string, object?>
        {
            ["holder"] = new UntypedDelegateHolder((Func<string>)(() => { calls++; return "ran"; })),
            ["dict"] = new Dictionary<string, object?> { ["factory"] = (Func<string>)(() => { calls++; return "ran"; }) },
        };

        var engine = new ScribanTemplateEngine();
        var member = engine.Render("{{ holder.callback }}", model);
        var dictionary = engine.Render("{{ dict.factory }}", model);

        calls.Should().Be(0, "delegát se nesmí vykonat ani skrytý za object-typovaným členem");
        member.IsSuccess.Should().BeFalse("delegát vrácený z CLR členu je odmítnutý člen");
        dictionary.IsSuccess.Should().BeTrue("delegát ve slovníku se zahodí (null) a nic se nevykoná");
        dictionary.Value.Should().BeEmpty();
    }

    private sealed class UntypedDelegateHolder(object callback)
    {
        public object Callback { get; } = callback;
    }

    private sealed class DelegateHolder(Func<string> factory)
    {
        public Func<string> Factory { get; } = factory;
    }

    [Fact]
    public void Audit_NoSinkRegistered_DoesNotThrow()
    {
        // PostrionCore: AuditLoggingTests.Render_WithoutLogger_DoesNotThrow
        var act = () => new ScribanTemplateEngine().Render("{{ file.read 'C:/secret.txt' }}", new { });

        act.Should().NotThrow();
        act().IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Audit_ThrowingSink_IsSwallowed_AndRenderOutcomeIsUnchanged()
    {
        var engine = new ScribanTemplateEngine(null, new ITemplateSecurityAuditSink[] { new ThrowingSink() });

        var blocked = engine.Render("{{ file.read 'C:/secret.txt' }}", new { });
        blocked.IsSuccess.Should().BeFalse("výjimka sinku nesmí změnit výsledek renderu");
        blocked.Error.Should().NotContain("sink failure");

        var ok = engine.Render("Hello {{ name }}!", new { Name = "World" });
        ok.IsSuccess.Should().BeTrue("výjimka sinku nesmí shodit úspěšný render");
        ok.Value.Should().Be("Hello World!");
    }

    [Fact]
    public void Audit_MultipleSinks_AllReceiveTheEvent()
    {
        var first = new SpySink();
        var second = new SpySink();
        var engine = new ScribanTemplateEngine(null, new ITemplateSecurityAuditSink[] { first, second, new ThrowingSink() });

        engine.Render("{{ file.read 'x' }}", new { }).IsSuccess.Should().BeFalse();

        first.Events.Should().ContainSingle();
        second.Events.Should().ContainSingle();
    }

    [Fact]
    public void Audit_TimestampComesFromTimeProvider()
    {
        var sink = new SpySink();
        var now = new DateTimeOffset(2026, 10, 20, 12, 34, 56, TimeSpan.Zero);
        var engine = new ScribanTemplateEngine(null, new ITemplateSecurityAuditSink[] { sink }, new FixedTimeProvider(now));

        engine.Render("{{ file.read 'x' }}", new { }).IsSuccess.Should().BeFalse();

        sink.Events[0].Timestamp.Should().Be(now);
    }

    [Fact]
    public void Audit_ExcerptIsCappedAt200Chars_AndHashIsLowercaseHex()
    {
        var sink = new SpySink();
        var template = "{{ file.read '" + new string('a', 400) + "' }}";

        Engine(sink).Render(template, new { }).IsSuccess.Should().BeFalse();

        sink.Events[0].TemplateExcerpt.Length.Should().Be(200);
        sink.Events[0].TemplateHash.Should().MatchRegex("^[0-9a-f]{64}$");
        sink.Events[0].TemplateHash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(template))));
    }

    [Fact]
    public void Audit_DetailNeverContainsModelValues()
    {
        var sink = new SpySink();
        var model = new Dictionary<string, object?>
        {
            ["t"] = typeof(string),
            ["secret"] = "TOPSECRET-VALUE",
        };

        Engine(sink).Render("{{ t.full_name }}{{ secret }}", model).IsSuccess.Should().BeFalse();

        sink.Events.Should().NotBeEmpty();
        sink.Events.Should().OnlyContain(e => !e.Detail.Contains("TOPSECRET-VALUE"));
    }

    // ---------------------------------------------------------------------------------------------
    // Options hardening (PostrionCore SecurityConfigurationTests / TemplateSecurityOptions)
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(TemplateSecurityOptions.LoopLimit))]
    [InlineData(nameof(TemplateSecurityOptions.RecursiveLimit))]
    [InlineData(nameof(TemplateSecurityOptions.MaxOutputLength))]
    [InlineData(nameof(TemplateSecurityOptions.LimitToString))]
    [InlineData(nameof(TemplateSecurityOptions.ObjectRecursionLimit))]
    public void Options_NonPositiveIntLimit_ThrowsAtEngineConstruction(string property)
    {
        var options = new TemplateSecurityOptions();
        typeof(TemplateSecurityOptions).GetProperty(property)!.SetValue(options, 0);

        var act = () => new ScribanTemplateEngine(options);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(property)
            .WithMessage("*greater than zero*");
    }

    [Fact]
    public void Options_NonPositiveTimeout_ThrowsAtEngineConstruction()
    {
        ((Action)(() => new ScribanTemplateEngine(new TemplateSecurityOptions { Timeout = TimeSpan.Zero })))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("Timeout");
        ((Action)(() => new ScribanTemplateEngine(new TemplateSecurityOptions { RegexTimeOut = TimeSpan.FromMilliseconds(-1) })))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("RegexTimeOut");
    }

    [Fact]
    public void Options_NonPositiveLimit_ThrowsAtDiRegistration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTempoEmailTemplateEngine(o => o.LoopLimit = -1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("LoopLimit");
    }

    [Fact]
    public void Audit_SinkRegisteredInDi_IsUsedByTheResolvedEngine()
    {
        // PostrionCore: SecurityConfigurationTests.DI_Container_ResolvesSecureEngine — the DI-resolved
        // engine must apply the same sandbox and forward events to every registered sink.
        var sink = new SpySink();
        var services = new ServiceCollection();
        services.AddSingleton<ITemplateSecurityAuditSink>(sink);
        services.AddTempoEmailTemplateEngine();

        using var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredService<ITemplateEngine>();

        engine.Render("{{ file.read 'x' }}", new { }).IsSuccess.Should().BeFalse();
        sink.Events.Should().ContainSingle(e => e.Kind == TemplateSecurityEventKind.BlockedFunction);
    }

    [Theory]
    [InlineData("{{ t.get_type }}")]
    [InlineData("{{ @System.IO.File.ReadAllText 'x' }}")]
    [InlineData("{{ \"1 + 2\" | object.eval }}")]
    [InlineData("{{ include 'x' }}")]
    [InlineData("{{ file.read 'x' }}")]
    public void Validate_RefusedConstruct_IsReportedAsInvalid(string template)
    {
        var engine = new ScribanTemplateEngine();

        var result = engine.Validate(template);

        result.IsValid.Should().BeFalse("editor musí uživatele upozornit dřív, než render selže");
        result.Errors.Should().ContainSingle();
        result.Errors[0].Line.Should().BeGreaterThan(0);
        result.Errors[0].Column.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Validate_SafeTemplate_StaysValid()
    {
        new ScribanTemplateEngine().Validate("Hi {{ user.name }} {{ for i in items }}{{ i }}{{ end }}")
            .IsValid.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------
    // White-box pins: the sandbox context itself
    // ---------------------------------------------------------------------------------------------

    private sealed class SampleModel
    {
        public string Name { get; set; } = "Jan";
    }

    [Fact]
    public void SandboxedContext_SetsMemberFilter_BlockedReflectionMembers()
    {
        // PostrionCore: SandboxedContext_SetsMemberFilter_BlockedReflectionMembers
        var context = new SandboxedTemplateContext(new TemplateSecurityOptions());

        context.MemberFilter.Should().NotBeNull("Scriban 7 sandbox musí filtrovat .NET členy");

        context.MemberFilter!(typeof(SampleModel).GetProperty(nameof(SampleModel.Name))!).Should().BeTrue();
        context.MemberFilter!(typeof(object).GetMethod(nameof(object.GetHashCode))!).Should().BeTrue();
        context.MemberFilter!(typeof(Type).GetProperty(nameof(Type.FullName))!).Should().BeFalse();
        context.MemberFilter!(typeof(Type).GetProperty(nameof(Type.Assembly))!).Should().BeFalse();
        context.MemberFilter!(typeof(object).GetMethod(nameof(object.GetType))!).Should().BeFalse();
        context.MemberFilter!(typeof(System.IO.FileInfo).GetProperty(nameof(System.IO.FileInfo.Length))!).Should().BeFalse();
        context.MemberFilter!(typeof(System.Diagnostics.Process).GetProperty(nameof(System.Diagnostics.Process.Id))!).Should().BeFalse();
    }

    [Fact]
    public void SandboxedContext_UsesStandardMemberRenamer()
    {
        // PostrionCore: SandboxedContext_UsesStandardMemberRenamer_SnakeCaseForClrObjects
        var context = new SandboxedTemplateContext(new TemplateSecurityOptions());

        context.MemberRenamer.Should().NotBeNull();
        context.MemberRenamer!(typeof(SampleModel).GetProperty(nameof(SampleModel.Name))!).Should().Be("name");
    }

    [Fact]
    public void SandboxedContext_AppliesLimitsFromOptions()
    {
        var options = new TemplateSecurityOptions
        {
            LoopLimit = 7,
            RecursiveLimit = 8,
            LimitToString = 4321,
            ObjectRecursionLimit = 9,
            RegexTimeOut = TimeSpan.FromSeconds(2),
        };

        var context = new SandboxedTemplateContext(options);

        context.LoopLimit.Should().Be(7);
        context.RecursiveLimit.Should().Be(8);
        context.LimitToString.Should().Be(4321);
        context.ObjectRecursionLimit.Should().Be(9);
        context.RegexTimeOut.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SandboxedContext_RemovesStringEvaluatingAndLoaderBuiltins()
    {
        var context = new SandboxedTemplateContext(new TemplateSecurityOptions());
        var objectNamespace = (IDictionary<string, object?>)context.BuiltinObject["object"]!;

        objectNamespace.Should().NotContainKey("eval");
        objectNamespace.Should().NotContainKey("eval_template");
        objectNamespace.Should().ContainKey("default", "běžné builtiny musí zůstat dostupné");

        ((IDictionary<string, object?>)context.BuiltinObject).Should().NotContainKey("include");
        ((IDictionary<string, object?>)context.BuiltinObject).Should().NotContainKey("include_join");
    }

    [Fact]
    public void SandboxedContext_IsInternalOnly_AndRejectsNullOptions()
    {
        var act = () => new SandboxedTemplateContext(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---------------------------------------------------------------------------------------------
    // Review fix #1: the render time budget is enforced even when the template writes no output
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Audit_RenderTimeout_IsEnforcedEvenWhenTheTemplateWritesNoOutput()
    {
        // Root cause of the former flake (Fáze 1): the old template raced the wall-clock budget against the
        // machine's SPEED — 50 backtracking regex matches (~100 ms each on the author's machine) were meant to
        // outlast a 2 s budget, but on a faster host / Release build / .NET with better regex optimisations the
        // whole loop finished BEFORE the budget (render succeeded, "musí být zastaven" went red), and on a loaded
        // host a single match could cross RegexTimeOut instead (the audit event became "regex", not "render time
        // budget"). The test asserted a property of the render's duration, not of the cancellation path.
        //
        // The workload must be unbounded relative to ANY budget so that the context's own cancellation token is
        // the only thing that can stop it: a pure-assignment loop (no Write, so the output guard never fires; no
        // regex, so the regex guard never fires) over a range no CPU can finish (2 billion steps) with the loop
        // limit raised out of the way. The security limit under test (Timeout) stays at a production-like
        // value — nothing is relaxed; the test only stops depending on how fast the machine is.
        var sink = new SpySink();
        var budget = TimeSpan.FromMilliseconds(500);
        var engine = Engine(sink, new TemplateSecurityOptions
        {
            Timeout = budget,
            LoopLimit = int.MaxValue,
        });

        var stopwatch = Stopwatch.StartNew();
        var result = engine.Render(
            "{{ for i in 1..2000000000 }}{{ x = i }}{{ end }}done",
            new { });
        stopwatch.Stop();

        result.IsSuccess.Should().BeFalse("render musí být zastaven časovým rozpočtem");

        // Lower bound: the render can only end through the token, so it cannot end much before the budget
        // (half of it absorbs timer granularity). Upper bound: deliberately generous — a render that never
        // stopped would run for hours, so 30 s separates "stopped" from "not stopped" on any loaded host.
        stopwatch.Elapsed.Should().BeGreaterThan(budget / 2);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.Timeout);
        sink.Events[0].Detail.Should().Be("render time budget");
    }

    // ---------------------------------------------------------------------------------------------
    // Review fix #4: options are snapshotted at construction, a later mutation cannot weaken them
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Options_MutatedAfterConstruction_DoNotWeakenTheEngine()
    {
        var options = new TemplateSecurityOptions();
        var engine = new ScribanTemplateEngine(options);

        options.LoopLimit = 0;
        options.Timeout = TimeSpan.FromDays(1);

        var result = engine.Render("{{ for i in 1..10000 }}{{ i }}{{ end }}", new { });

        result.IsSuccess.Should().BeFalse("engine musí používat kopii options z konstruktoru");
    }

    // ---------------------------------------------------------------------------------------------
    // Review fix #7: the engine takes TimeProvider from DI
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Audit_RegisteredTimeProvider_IsUsedByTheDiResolvedEngine()
    {
        var sink = new SpySink();
        var now = new DateTimeOffset(2026, 10, 20, 12, 34, 56, TimeSpan.Zero);
        var services = new ServiceCollection();
        services.AddSingleton<ITemplateSecurityAuditSink>(sink);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        services.AddTempoEmailTemplateEngine();

        using var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredService<ITemplateEngine>();

        engine.Render("{{ file.read 'x' }}", new { }).IsSuccess.Should().BeFalse();

        sink.Events.Should().ContainSingle();
        sink.Events[0].Timestamp.Should().Be(now, "engine má Timestamp brát z registrovaného TimeProvideru");
    }

    // ---------------------------------------------------------------------------------------------
    // Review fix #9: refusal detail names a type as Namespace.Name, not as its reflection FullName
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Audit_ForbiddenGenericType_IsDescribedAsNamespaceAndName()
    {
        var sink = new SpySink();
        var model = new Dictionary<string, object?> { ["task"] = Task.FromResult("x") };

        var result = Engine(sink).Render("{{ task.status }}", model);

        result.IsSuccess.Should().BeFalse();
        sink.Events.Should().ContainSingle();
        sink.Events[0].Kind.Should().Be(TemplateSecurityEventKind.BlockedNamespace);
        sink.Events[0].Detail.Should().Be("System.Threading.Tasks.Task`1");
    }

    // ---------------------------------------------------------------------------------------------
    // Review fix #11: the Kind values keep the contract order (§3.6) as explicit numbers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EventKind_ValuesMatchTheContractOrder()
    {
        ((int)TemplateSecurityEventKind.BlockedMember).Should().Be(0);
        ((int)TemplateSecurityEventKind.BlockedNamespace).Should().Be(1);
        ((int)TemplateSecurityEventKind.BlockedFunction).Should().Be(2);
        ((int)TemplateSecurityEventKind.BlockedUrlScheme).Should().Be(3);
        ((int)TemplateSecurityEventKind.LimitExceeded).Should().Be(4);
        ((int)TemplateSecurityEventKind.Timeout).Should().Be(5);
    }

    // ---------------------------------------------------------------------------------------------
    // Review fix #13: SecurityConfigurationTests parity (options + engine resolved from DI)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SecurityConfiguration_ConfiguredOptionsAreResolvableFromDi()
    {
        var services = new ServiceCollection();
        services.AddTempoEmailTemplateEngine(o =>
        {
            o.LoopLimit = 10;
            o.Timeout = TimeSpan.FromSeconds(2);
        });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<TemplateSecurityOptions>();
        options.LoopLimit.Should().Be(10);
        options.Timeout.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SecurityConfiguration_DiEngine_EnforcesConfiguredLoopLimit()
    {
        var services = new ServiceCollection();
        services.AddTempoEmailTemplateEngine(o => o.LoopLimit = 10);

        using var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredService<ITemplateEngine>();

        engine.Render("{{ for i in 1..100 }}{{ i }}{{ end }}", new { }).IsSuccess.Should().BeFalse();
    }
}
