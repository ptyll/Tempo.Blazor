using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Templating;

/// <summary>
/// Host function registry (§3.7, D4): an <see cref="ITemplateFunctionProvider"/> exposes application
/// functions under one namespace, the registration never overrides a Scriban builtin or another
/// provider, and the functions run inside the same sandbox and escaping as everything else.
/// </summary>
public class TemplateFunctionProviderTests
{
    private sealed class FakeProvider : ITemplateFunctionProvider
    {
        private readonly Dictionary<string, Delegate> _functions;
        private readonly IReadOnlyList<TemplateFunctionInfo>? _described;

        public FakeProvider(string @namespace, params (string Name, Delegate Function)[] functions)
            : this(@namespace, functions, described: null)
        {
        }

        public FakeProvider(
            string @namespace,
            (string Name, Delegate Function)[] functions,
            IReadOnlyList<TemplateFunctionInfo>? described)
        {
            Namespace = @namespace;
            _functions = functions.ToDictionary(f => f.Name, f => f.Function, StringComparer.Ordinal);
            _described = described;
        }

        public string Namespace { get; }

        public IReadOnlyDictionary<string, Delegate> GetFunctions() => _functions;

        public IReadOnlyList<TemplateFunctionInfo> Describe()
            => _described ?? _functions.Select(f => new TemplateFunctionInfo(f.Key, "Documented " + f.Key)).ToList();
    }

    private static ScribanTemplateEngine EngineWith(params ITemplateFunctionProvider[] providers)
        => new(functionProviders: providers);

    private static ScribanTemplateEngine EngineWith(TemplateSecurityOptions options, params ITemplateFunctionProvider[] providers)
        => new(options, functionProviders: providers);

    private static FakeProvider UpperProvider(params (string, Delegate)[] extra)
        => new("app", [("upper", new Func<string, string>(value => value.ToUpperInvariant())), .. extra]);

    // ── Registration and use ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Provider_FunctionIsCallableDirectly()
    {
        var engine = EngineWith(UpperProvider());

        var result = engine.Render("{{ app.upper \"abc\" }}", new { });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ABC");
    }

    [Fact]
    public void Provider_FunctionIsUsableAsAFilter()
    {
        var engine = EngineWith(UpperProvider());

        engine.Render("{{ \"abc\" | app.upper }}", new { }).Value.Should().Be("ABC");
    }

    [Fact]
    public void Provider_MultipleProviders_AreAllReachable()
    {
        var engine = EngineWith(
            UpperProvider(),
            new FakeProvider("money", ("czk", new Func<decimal, string>(
                value => value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture) + " Kč"))));

        var result = engine.Render("{{ app.upper name }} {{ money.czk total }}", new { name = "ada", total = 12.5m });

        result.Value.Should().Be("ADA 12.50 Kč");
    }

    [Fact]
    public void Provider_ProducerOfACollection_IsIterable()
    {
        var engine = EngineWith(
            new FakeProvider("app", ("numbers", new Func<IEnumerable<int>>(() => [1, 2, 3]))));

        engine.Render("{{ for n in app.numbers }}{{ n }}{{ end }}", new { }).Value.Should().Be("123");
    }

    [Fact]
    public void Provider_RegistryIsSnapshottedAtConstruction()
    {
        var functions = new Dictionary<string, Delegate>(StringComparer.Ordinal)
        {
            ["upper"] = new Func<string, string>(value => value.ToUpperInvariant()),
        };
        var provider = new MutableProvider("app", functions);
        var engine = EngineWith(provider);

        functions["upper"] = new Func<string, string>(_ => "hijacked");

        engine.Render("{{ app.upper \"abc\" }}", new { }).Value.Should().Be("ABC");
    }

    [Fact]
    public void Provider_FunctionThatThrows_FailsTheRender_InsteadOfCrashing()
    {
        var engine = EngineWith(
            new FakeProvider("app", ("boom", new Func<string>(() => throw new InvalidOperationException("kaput")))));

        var result = engine.Render("{{ app.boom }}", new { });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Provider_NamespacesAreScopedToTheirEngine()
    {
        EngineWith(UpperProvider()).Render("{{ app.upper \"abc\" }}", new { }).IsSuccess.Should().BeTrue();

        // A provider registered for one engine must not become callable in another one (no shared
        // builtin scope — a template from another tenant could otherwise reach host code).
        var result = new ScribanTemplateEngine().Render("{{ app.upper \"abc\" }}", new { });

        result.IsSuccess.Should().BeFalse();
    }

    // ── Enumerables are materialized, so a host type cannot leak its members ───────────────────

    [Fact]
    public void Provider_EnumerableResult_IsMaterialized_AndItsHostMembersStayUnreachable()
    {
        var engine = EngineWith(new FakeProvider("app", ("leaky", new Func<Leaky>(() => new Leaky()))));

        var result = engine.Render("{{ (app.leaky).password }}", new { });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain("s3cret");
        result.Value.Should().BeEmpty("a materialized ScriptArray has no host member 'password'");
    }

    [Fact]
    public void Provider_EnumerableResult_IsEnumeratedOncePerCall()
    {
        var counting = new CountingEnumerable([1, 2, 3]);
        var engine = EngineWith(new FakeProvider("app", ("numbers", new Func<IEnumerable<int>>(() => counting))));

        var result = engine.Render("{{ (app.numbers).size }}", new { });

        result.Value.Should().Be("3");
        counting.Enumerations.Should().Be(1, "the value is materialized once, not re-enumerated by every member");
    }

    [Fact]
    public void Provider_InfiniteEnumerable_FailsClosedOnTheIterationLimit()
    {
        var engine = EngineWith(new TemplateSecurityOptions { LoopLimit = 10 },
            new FakeProvider("app", ("infinite", new Func<IEnumerable<int>>(Infinite))));

        var result = engine.Render("{{ (app.infinite).size }}", new { });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("LoopLimit");
    }

    [Fact]
    public void Provider_EnumerableOfForbiddenElementType_ThrowsAtRegistration()
    {
        var act = () => EngineWith(
            new FakeProvider("app", ("types", new Func<IEnumerable<Type>>(() => [typeof(string)]))));

        act.Should().Throw<ArgumentException>().WithMessage("*types*");
    }

    // ── A failing delegate never leaks its message into the render error ────────────────────────

    [Fact]
    public void Provider_ThrowingDelegate_ReportsOnlyTheFunctionName()
    {
        var engine = EngineWith(new FakeProvider(
            "app",
            ("boom", new Func<string>(() => throw new InvalidOperationException("connection string Server=secret;Pwd=hunter2")))));

        var result = engine.Render("{{ app.boom }}", new { });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("app.boom");
        result.Error.Should().NotContain("hunter2");
        result.Error.Should().NotContain("Server=secret");
    }

    // ── Escaping: provider output is not trusted ────────────────────────────────────────────────

    [Fact]
    public void Provider_MarkupResult_IsHtmlEncodedInTheBodyPass()
    {
        var engine = EngineWith(new FakeProvider("app", ("tag", new Func<string>(() => "<b>hi</b>"))));

        var result = engine.Render("{{ app.tag }}", new { });

        result.Value.Should().Be("&lt;b&gt;hi&lt;/b&gt;");
    }

    [Fact]
    public void Provider_MarkupResult_StaysRawInTheTextPass()
    {
        var engine = EngineWith(new FakeProvider("app", ("tag", new Func<string>(() => "<b>hi</b>"))));

        var result = ((IEscapingTemplateEngine)engine)
            .Render("{{ app.tag }}", new { }, null, TemplateEscapeMode.None);

        result.Value.Should().Be("<b>hi</b>");
    }

    [Fact]
    public void Provider_CannotReturnTrustedHtml_SoTheOptOutCannotBeForged()
    {
        // TrustedHtml/EscapedHtml are the engine's internal escaping markers; a provider must never
        // hand one out, otherwise it could bypass the automatic HTML encoding.
        var act = () => EngineWith(new FakeProvider("app", ("forged", new Func<TrustedHtml>(() => new TrustedHtml("<b>x</b>")))));

        act.Should().Throw<ArgumentException>().WithMessage("*forged*");
    }

    [Fact]
    public void Provider_CannotReturnEscapedHtmlMarkers()
    {
        var act = () => EngineWith(new FakeProvider("app", ("forged", new Func<EscapedHtml>(() => new EscapedHtml("&lt;")))));

        act.Should().Throw<ArgumentException>().WithMessage("*forged*");
    }

    // ── Sandbox: provider functions never weaken the policy or the limits ───────────────────────

    [Fact]
    public void Provider_DoesNotWeakenTheSandboxScan()
    {
        var engine = EngineWith(UpperProvider());

        var result = engine.Render("{{ t.get_type }}", new { t = "x" });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("get_type");
    }

    [Fact]
    public void Provider_DoesNotExposeReflectionThroughItsArguments()
    {
        var engine = EngineWith(new FakeProvider("app", ("upper", new Func<string, string>(value => value.ToUpperInvariant()))));

        // The member filter still refuses the reflected member before the provider is ever called.
        var result = engine.Render("{{ app.upper t.assembly }}", new { t = typeof(string) });

        result.IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("{{ app.upper t }}", "Type")]
    [InlineData("{{ t | string.upcase }}", "Type")]
    [InlineData("{{ app.upper inner }}", "System.IO.FileInfo")]
    public void Provider_ArgumentCannotLaunderARefusedValueIntoText(string template, string expectedDetail)
    {
        var engine = EngineWith(UpperProvider());
        var model = new { t = typeof(string), inner = new System.IO.FileInfo("/tmp/secret") };

        var result = engine.Render(template, model);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain(expectedDetail);
    }

    [Fact]
    public async Task Provider_FunctionIsCallableFromTheAsyncRenderPath()
    {
        var options = new TemplateSecurityOptions();
        using var context = new SandboxedTemplateContext(
            options,
            TemplateEscapeMode.Html,
            TemplateFunctionRegistry.Create([UpperProvider()]),
            CultureInfo.InvariantCulture);
        context.PushGlobal(ObjectToScriptObjectConverter.ToScriptObject(new { }));
        var output = new SandboxedScriptOutput(options.MaxOutputLength, options.Timeout);
        context.PushOutput(output);

        await Scriban.Template.Parse("{{ app.upper \"abc\" }}").RenderAsync(context);

        output.ToString().Should().Be("ABC");
    }

    [Fact]
    public void Provider_ResultsAreBoundedByTheLoopLimit()
    {
        var engine = EngineWith(new TemplateSecurityOptions { LoopLimit = 3 },
            new FakeProvider("app", ("numbers", new Func<IEnumerable<int>>(() => Enumerable.Range(0, 10).ToList()))));

        var result = engine.Render("{{ for n in app.numbers }}{{ n }}{{ end }}", new { });

        // The enumerable is materialized at call time, so the same loop limit bounds the result before a
        // template loop could ever run away with it.
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("LoopLimit");
    }

    [Fact]
    public void Provider_ResultsAreBoundedByTheOutputLimit()
    {
        var engine = EngineWith(new TemplateSecurityOptions { MaxOutputLength = 32 },
            new FakeProvider("app", ("blob", new Func<string>(() => new string('x', 512)))));

        var result = engine.Render("{{ app.blob }}", new { });

        result.IsSuccess.Should().BeFalse();
    }

    // ── Registration validation ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("for")]
    [InlineData("this")]
    [InlineData("if")]
    [InlineData("end")]
    [InlineData("func")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("in")]
    [InlineData("while")]
    [InlineData("case")]
    [InlineData("when")]
    [InlineData("else")]
    [InlineData("ret")]
    [InlineData("break")]
    [InlineData("continue")]
    [InlineData("capture")]
    [InlineData("readonly")]
    [InlineData("import")]
    [InlineData("with")]
    [InlineData("wrap")]
    [InlineData("tablerow")]
    [InlineData("empty")]
    public void Provider_KeywordNamespace_ThrowsAtRegistration(string @namespace)
    {
        // A namespace the Scriban parser treats as a keyword could never be called; refuse it loudly.
        var act = () => EngineWith(new FakeProvider(@namespace, ("x", new Func<string>(() => "x"))));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("string")]
    [InlineData("array")]
    [InlineData("object")]
    [InlineData("math")]
    [InlineData("date")]
    [InlineData("html")]
    [InlineData("regex")]
    [InlineData("timespan")]
    public void Provider_NamespaceCollidingWithABuiltin_ThrowsAtRegistration(string @namespace)
    {
        var act = () => EngineWith(new FakeProvider(@namespace, ("x", new Func<string>(() => "x"))));

        var exception = act.Should().Throw<ArgumentException>().Which;
        exception.Message.Should().Contain(@namespace);
    }

    [Fact]
    public void Provider_NamespaceCollidingWithAnotherProvider_ThrowsAtRegistration()
    {
        var act = () => EngineWith(UpperProvider(), UpperProvider());

        act.Should().Throw<ArgumentException>().WithMessage("*app*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("App")]
    [InlineData("1app")]
    [InlineData("app-x")]
    [InlineData("app.x")]
    [InlineData("app x")]
    public void Provider_InvalidNamespace_ThrowsAtRegistration(string @namespace)
    {
        var act = () => EngineWith(new FakeProvider(@namespace, ("x", new Func<string>(() => "x"))));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Upper")]
    [InlineData("1upper")]
    [InlineData("up-per")]
    [InlineData("")]
    public void Provider_InvalidFunctionName_ThrowsAtRegistration(string name)
    {
        var act = () => EngineWith(new FakeProvider("app", (name, new Func<string>(() => "x"))));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Provider_AsyncDelegate_ThrowsAtRegistration()
    {
        var act = () => EngineWith(new FakeProvider("app", ("later", new Func<Task<string>>(() => Task.FromResult("x")))));

        act.Should().Throw<ArgumentException>().WithMessage("*synchronous*");
    }

    [Theory]
    [InlineData(typeof(Type))]
    [InlineData(typeof(Delegate))]
    [InlineData(typeof(object))]
    [InlineData(typeof(System.IO.FileInfo))]
    [InlineData(typeof(Guid))]
    public void Provider_DisallowedReturnType_ThrowsAtRegistration(Type returnType)
    {
        var function = Delegate.CreateDelegate(
            typeof(Func<>).MakeGenericType(returnType), typeof(ReturnTypeFactory).GetMethod(nameof(ReturnTypeFactory.Create))!
                .MakeGenericMethod(returnType));

        var act = () => EngineWith(new FakeProvider("app", ("bad", function)));

        act.Should().Throw<ArgumentException>().WithMessage("*bad*");
    }

    [Fact]
    public void Provider_ObjectParameter_ThrowsAtRegistration()
    {
        var act = () => EngineWith(new FakeProvider("app", ("echo", new Func<object?, string>(value => value?.ToString() ?? ""))));

        act.Should().Throw<ArgumentException>().WithMessage("*echo*");
    }

    [Fact]
    public void Provider_DescribeAdvertisingAFunctionThatIsNotExposed_ThrowsAtRegistration()
    {
        var provider = new FakeProvider(
            "app",
            [("upper", new Func<string, string>(value => value.ToUpperInvariant()))],
            [new TemplateFunctionInfo("upper", "upper"), new TemplateFunctionInfo("lower", "ghost")]);

        var act = () => EngineWith(provider);

        act.Should().Throw<ArgumentException>().WithMessage("*lower*");
    }

    [Fact]
    public void Provider_DescribeIsOptional()
    {
        var provider = new FakeProvider("app", [("upper", new Func<string, string>(value => value.ToUpperInvariant()))], []);

        var act = () => EngineWith(provider);

        act.Should().NotThrow();
    }

    // ── TemplateSecurityOptions.FunctionNamespaces (D6) ───────────────────────────────────────

    [Fact]
    public void SecurityOptions_FunctionNamespaces_StartEmpty()
        => new TemplateSecurityOptions().FunctionNamespaces.Should().BeEmpty();

    [Fact]
    public void SecurityOptions_CloneCopiesFunctionNamespacesDeeply()
    {
        var options = new TemplateSecurityOptions();
        options.FunctionNamespaces.Add("app");

        var clone = options.Clone();
        clone.FunctionNamespaces.Add("money");

        options.FunctionNamespaces.Should().BeEquivalentTo(["app"]);
        clone.FunctionNamespaces.Should().BeEquivalentTo(["app", "money"]);
    }

    // ── Runtime result guard (defence in depth) ────────────────────────────────────────────────

    [Fact]
    public void ProviderResultGuard_RefusesReflectionAndHostCode()
    {
        var type = () => TemplateFunctionResultPolicy.EnsureAllowed(typeof(string), "f");
        var method = () => TemplateFunctionResultPolicy.EnsureAllowed(typeof(string).GetMethod(nameof(ToString), Type.EmptyTypes)!, "f");
        var assembly = () => TemplateFunctionResultPolicy.EnsureAllowed(typeof(string).Assembly, "f");
        var del = () => TemplateFunctionResultPolicy.EnsureAllowed(new Func<string>(() => "x"), "f");
        var file = () => TemplateFunctionResultPolicy.EnsureAllowed(new System.IO.FileInfo("/tmp/x"), "f");
        var trusted = () => TemplateFunctionResultPolicy.EnsureAllowed(new TrustedHtml("<b>x</b>"), "f");

        type.Should().Throw<TemplateSecurityViolationException>();
        method.Should().Throw<TemplateSecurityViolationException>();
        assembly.Should().Throw<TemplateSecurityViolationException>();
        del.Should().Throw<TemplateSecurityViolationException>();
        file.Should().Throw<TemplateSecurityViolationException>();
        trusted.Should().Throw<TemplateSecurityViolationException>();
    }

    [Theory]
    [InlineData("text")]
    [InlineData(42)]
    [InlineData(12.5)]
    [InlineData(true)]
    public void ProviderResultGuard_AllowsTheContractTypes(object value)
    {
        var act = () => TemplateFunctionResultPolicy.EnsureAllowed(value, "f");

        act.Should().NotThrow();
    }

    [Fact]
    public void ProviderResultGuard_AllowsNullAndEnumerablesOfAllowedTypes()
    {
        var act = () =>
        {
            TemplateFunctionResultPolicy.EnsureAllowed(null, "f");
            TemplateFunctionResultPolicy.EnsureAllowed(new List<string> { "a" }, "f");
            TemplateFunctionResultPolicy.EnsureAllowed(new decimal[] { 1m, 2m }, "f");
            TemplateFunctionResultPolicy.EnsureAllowed(DateTimeOffset.UnixEpoch, "f");
        };

        act.Should().NotThrow();
    }

    // ── DI registration ────────────────────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider(params ITemplateFunctionProvider[] providers)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        foreach (var provider in providers)
        {
            services.AddSingleton(provider);
        }

        services.AddTempoEmailTemplateEngine();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void DiRegistration_ForwardsEveryRegisteredProvider()
    {
        using var provider = BuildProvider(UpperProvider());
        var engine = provider.GetRequiredService<ITemplateEngine>();

        engine.Render("{{ app.upper \"abc\" }}", new { }).Value.Should().Be("ABC");
    }

    [Fact]
    public void DiRegistration_FillsFunctionNamespacesOnTheSecurityOptions_WithoutResolvingTheEngine()
    {
        using var provider = BuildProvider(UpperProvider(), new FakeProvider("money", ("czk", new Func<decimal, string>(v => $"{v}"))));

        // The host must be able to read the namespaces for its own template validation; the options are
        // built from the registry, not lazily mutated while the engine is being resolved.
        var options = provider.GetRequiredService<TemplateSecurityOptions>();

        options.FunctionNamespaces.Should().BeEquivalentTo(["app", "money"]);
    }

    [Fact]
    public void DiRegistration_KeepsNamespacesTheHostSeeded()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<ITemplateFunctionProvider>(UpperProvider());
        services.AddTempoEmailTemplateEngine(options => options.FunctionNamespaces.Add("seeded"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TemplateSecurityOptions>().FunctionNamespaces
            .Should().BeEquivalentTo(["seeded", "app"]);
    }

    [Fact]
    public void DiRegistration_ScopedFunctionProvider_IsRefused()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddScoped<ITemplateFunctionProvider>(_ => UpperProvider());
        services.AddTempoEmailTemplateEngine();
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ITemplateEngine>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*singleton*");
    }

    [Fact]
    public void DiRegistration_TransientFunctionProvider_IsRefused()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddTransient<ITemplateFunctionProvider>(_ => UpperProvider());
        services.AddTempoEmailTemplateEngine();
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ITemplateEngine>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*singleton*");
    }

    [Fact]
    public void DiRegistration_NamespaceCollision_FailsWhenTheEngineIsResolved()
    {
        using var provider = BuildProvider(UpperProvider(), new FakeProvider("app", ("x", new Func<string>(() => "x"))));

        var act = () => provider.GetRequiredService<ITemplateEngine>();

        act.Should().Throw<ArgumentException>();
    }

    private static IEnumerable<int> Infinite()
    {
        var i = 0;
        while (true)
        {
            yield return i++;
        }
    }

    /// <summary>A host type that is enumerable *and* carries a secret member.</summary>
    private sealed class Leaky : IEnumerable<string>
    {
        public string Password => "s3cret";

        public IEnumerator<string> GetEnumerator()
        {
            yield return "visible";
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CountingEnumerable(IEnumerable<int> inner) : IEnumerable<int>
    {
        public int Enumerations { get; private set; }

        public IEnumerator<int> GetEnumerator()
        {
            Enumerations++;
            return inner.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class MutableProvider : ITemplateFunctionProvider
    {
        private readonly IDictionary<string, Delegate> _functions;

        public MutableProvider(string @namespace, IDictionary<string, Delegate> functions)
        {
            Namespace = @namespace;
            _functions = functions;
        }

        public string Namespace { get; }

        public IReadOnlyDictionary<string, Delegate> GetFunctions() => (IReadOnlyDictionary<string, Delegate>)_functions;

        public IReadOnlyList<TemplateFunctionInfo> Describe()
            => _functions.Keys.Select(name => new TemplateFunctionInfo(name, null)).ToList();
    }

    private static class ReturnTypeFactory
    {
        public static T Create<T>() => default!;
    }
}
