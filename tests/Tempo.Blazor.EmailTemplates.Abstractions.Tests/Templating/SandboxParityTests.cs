using System.Diagnostics;
using System.Text.Json;
using Scriban.Runtime;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Templating;

/// <summary>
/// Security parity suite: every sandbox scenario proven by PostrionCore
/// (<c>tests/PostrionCore.EmailTemplates.Tests/Templating</c>) ported to the Tempo API. PostrionCore
/// deletes its own engine and takes over this one, so this suite is the migration gate.
/// </summary>
public class SandboxParityTests
{
    private sealed class SampleModel
    {
        public string Name { get; set; } = "Jan";

        public string Secret { get; set; } = "tajnost";
    }

    private sealed class PersonModel
    {
        public string Name { get; set; } = "John";

        public int Age { get; set; } = 30;
    }

    private static ScribanTemplateEngine Engine(TemplateSecurityOptions? options = null) => new(options);

    // ---------------------------------------------------------------------------------------------
    // PostrionCore Scriban7SandboxRegressionTests
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Sandbox_RenderGetTypeReflection_IsBlocked()
    {
        // PostrionCore: Scriban7SandboxRegressionTests.Render_GetTypeReflection_IsBlocked
        var result = Engine().Render("{{ model.GetType().FullName }}", new { Model = new SampleModel() });

        result.IsSuccess.Should().BeFalse("reflexe ze šablony musí být zablokována");
    }

    [Fact]
    public void Sandbox_GetTypeOnConvertedModelMember_IsBlocked()
    {
        // PostrionCore: SandboxedContext_SetsMemberFilter_BlockedReflectionMembers (behavioral half)
        var model = new Dictionary<string, object?> { ["user"] = new SampleModel() };

        var result = Engine().Render("{{ user.get_type }}", model);

        result.IsSuccess.Should().BeFalse("GetType otevírá přístup k reflexi");
    }

    [Fact]
    public void Sandbox_TypeInstanceFullName_IsBlocked()
    {
        // PostrionCore: SandboxedContext_IsTypeAllowed_BlocksDangerousTypes / TypeFilteringTests
        var model = new Dictionary<string, object?> { ["t"] = typeof(string) };

        var result = Engine().Render("{{ t.full_name }}", model);

        result.IsSuccess.Should().BeFalse("System.Type nesmí být dosažitelný ze šablony");
    }

    [Fact]
    public void Sandbox_TypeAssemblyAndTypeHandle_AreBlocked()
    {
        // PostrionCore: SandboxedContext_SetsMemberFilter_BlockedReflectionMembers (Assembly, TypeHandle)
        var model = new Dictionary<string, object?> { ["t"] = typeof(string) };

        Engine().Render("{{ t.assembly }}", model).IsSuccess.Should().BeFalse();
        Engine().Render("{{ t.type_handle }}", model).IsSuccess.Should().BeFalse();
        Engine().Render("{{ t.underlying_system_type }}", model).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Sandbox_MemberRenamer_UsesStandardSnakeCase()
    {
        // PostrionCore: SandboxedContext_UsesStandardMemberRenamer_SnakeCaseForClrObjects
        var model = new Dictionary<string, object?> { ["user"] = new SampleModel() };

        var bySnakeCase = Engine().Render("{{ user.name }}", model);

        bySnakeCase.IsSuccess.Should().BeTrue();
        bySnakeCase.Value.Should().Be("Jan", "StandardMemberRenamer převádí Name → name");
    }

    [Fact]
    public void Sandbox_ClrMemberAccess_IsCaseSensitive()
    {
        // PostrionCore: Render_ConvertedModel_MembersKeepDotNetNames_CaseSensitive (Tempo uses the
        // standard snake_case renamer, so the pinned invariant is case sensitivity, not dot-names).
        var model = new Dictionary<string, object?> { ["user"] = new SampleModel() };

        Engine().Render("{{ user.name }}", model).Value.Should().Be("Jan");
        Engine().Render("{{ user.Name }}", model).Value.Should().NotContain("Jan");
    }

    [Fact]
    public void Sandbox_FileAndEnvironmentAccess_AreBlocked()
    {
        // PostrionCore: Scriban7SandboxRegressionTests.Render_FileAndEnvironmentAccess_AreBlocked
        var engine = Engine();

        engine.Render("{{ file.read '/etc/passwd' }}", new { }).IsSuccess.Should().BeFalse();
        engine.Render("Hodnota: {{ env.PATH }}", new { }).IsSuccess.Should().BeFalse();
        engine.Render("{{ include 'x' }}", new { }).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Sandbox_LoopLimit_IsEnforced()
    {
        // PostrionCore: Scriban7SandboxRegressionTests.Render_LoopLimit_StillEnforced_UnderScriban7
        var engine = Engine(new TemplateSecurityOptions { LoopLimit = 10 });

        var result = engine.Render("{{ for i in 1..100 }}x{{ end }}", new { });

        result.IsSuccess.Should().BeFalse("LoopLimit sandboxu musí být vynucený");
    }

    // ---------------------------------------------------------------------------------------------
    // PostrionCore SecurityTemplateEngineTests
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Sandbox_AttemptFileAccess_Fails()
    {
        // PostrionCore: SecurityTemplateEngineTests.Render_AttemptFileAccess_ThrowsSecurityException
        Engine().Render("{{ file.read 'C:/secret.txt' }}", new { }).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Sandbox_AttemptEnvironmentAccess_Fails()
    {
        // PostrionCore: SecurityTemplateEngineTests.Render_AttemptEnvironmentAccess_BlocksAccess
        Engine().Render("{{ env.USERNAME }}", new { }).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Sandbox_AttemptSystemNamespaceReflection_Fails()
    {
        // PostrionCore: SecurityTemplateEngineTests.Render_AttemptReflection_BlocksReflection
        Engine().Render("{{ @System.Diagnostics.Process.GetCurrentProcess().Id }}", new { })
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Sandbox_CatastrophicLoop_Fails()
    {
        // PostrionCore: SecurityTemplateEngineTests.Render_InfiniteLoop_ThrowsTimeout
        var engine = Engine(new TemplateSecurityOptions { LoopLimit = 1000 });

        engine.Render("{{ for i in (1..10000) }}{{ i }}{{ end }}", new { }).IsSuccess.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------------
    // PostrionCore TypeFilteringTests
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Sandbox_SystemDiagnosticsNamespace_IsBlocked()
    {
        // PostrionCore: TypeFilteringTests.Render_SystemTypeAccess_Blocked
        Engine().Render("{{ @System.Diagnostics.Process.GetCurrentProcess().Id }}", new { })
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Sandbox_AnonymousObjectModel_IsAllowed()
    {
        // PostrionCore: TypeFilteringTests.Render_AnonymousObject_Allowed
        var result = Engine().Render("{{ user.name }}", new { User = new { Name = "John" } });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("John");
    }

    [Fact]
    public void Sandbox_PlainObjectModel_IsAllowed()
    {
        // PostrionCore: TypeFilteringTests.Render_PlainObject_Allowed
        var result = Engine().Render("{{ name }} - {{ age }}", new PersonModel());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("John - 30");
    }

    [Fact]
    public void Sandbox_SafeTypesInModel_StayReachable()
    {
        // PostrionCore: TypeFilteringTests.SandboxedContext_IsTypeAllowed_AllowsSafeTypes
        var result = Engine().Render(
            "{{ text }}|{{ number }}|{{ created | date.to_string \"%Y-%m-%d\" }}",
            new { Text = "x", Number = 5, Created = new DateTime(2026, 6, 11) });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("x|5|2026-06-11");
    }

    [Fact]
    public void Sandbox_DangerousTypesInModel_AreNotReachable()
    {
        // PostrionCore: TypeFilteringTests.SandboxedContext_IsTypeAllowed_BlocksDangerousTypes
        var model = new Dictionary<string, object?>
        {
            ["info"] = new FileInfo("/etc/passwd"),
            ["proc"] = Process.GetCurrentProcess(),
        };

        var engine = Engine();
        engine.Render("{{ info.length }}", model).IsSuccess.Should().BeFalse("System.IO je zakázaný namespace");
        engine.Render("{{ proc.id }}", model).IsSuccess.Should().BeFalse("System.Diagnostics je zakázaný namespace");
    }

    [Fact]
    public void Sandbox_ThreadingAndRuntimeNamespaces_AreBlocked()
    {
        var model = new Dictionary<string, object?>
        {
            ["token"] = new CancellationTokenSource(),
        };

        Engine().Render("{{ token.token }}", model).IsSuccess.Should().BeFalse(
            "System.Threading je zakázaný namespace");
    }

    // ---------------------------------------------------------------------------------------------
    // PostrionCore ResourceLimitsTests
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Sandbox_DeeplyNestedTemplate_IsRenderedOrRejected_ButNeverThrows()
    {
        // PostrionCore: ResourceLimitsTests.Render_DeepNesting_ThrowsDepthExceeded
        var template = "{{ x }}";
        for (var i = 0; i < 50; i++) template = $"{{{{ if true }}}}{template}{{{{ end }}}}";

        var act = () => Engine(new TemplateSecurityOptions { RecursiveLimit = 40 }).Render(template, new { X = "test" });

        act.Should().NotThrow();
    }

    [Fact]
    public void Sandbox_OutputOverLimit_Fails()
    {
        // PostrionCore: ResourceLimitsTests.Render_LongString_TruncatesOrThrows
        var engine = Engine(new TemplateSecurityOptions { MaxOutputLength = 100 });

        var result = engine.Render("{{ content }}", new { Content = new string('x', 200) });

        result.IsSuccess.Should().BeFalse();
        // The exact wording comes from the layer that aborts first (Scriban's OutputLimit guard or the
        // sandbox output); the audit classification is pinned by SandboxAuditParityTests.
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Sandbox_ManyIterations_RespectLoopLimit()
    {
        // PostrionCore: ResourceLimitsTests.Render_ManyIterations_RespectsMaxIterations
        var engine = Engine(new TemplateSecurityOptions { LoopLimit = 10 });

        engine.Render("{{ for i in (1..100) }}{{ i }}{{ end }}", new { }).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Sandbox_RenderWithinLimits_Succeeds()
    {
        // PostrionCore: ResourceLimitsTests.Render_WithinLimits_Succeeds
        var engine = Engine(new TemplateSecurityOptions { LoopLimit = 100 });

        var result = engine.Render("{{ for i in (1..10) }}{{ i }}{{ end }}", new { });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("12345678910");
    }

    // ---------------------------------------------------------------------------------------------
    // PostrionCore LegacyCompatibilityTests
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Sandbox_LegacySimpleSyntax_Renders()
    {
        // PostrionCore: LegacyCompatibilityTests.Render_OldSimpleSyntax_WorksWithNewEngine
        Engine().Render("Welcome {{ user.name }}", new { User = new { Name = "John" } })
            .Value.Should().Be("Welcome John");
    }

    [Fact]
    public void Sandbox_LegacySubjectPreheaderAndMultipleVariables_Render()
    {
        // PostrionCore: Render_OldSubjectTemplate / Render_OldPreheaderTemplate / Render_MultipleVariables
        var engine = Engine();
        engine.Render("Welcome {{ user.name }}!", new { User = new { Name = "John" } })
            .Value.Should().Be("Welcome John!");
        engine.Render("Hello {{ user.name }}", new { User = new { Name = "Jane" } })
            .Value.Should().Be("Hello Jane");
        engine.Render("{{ user.name }} ({{ user.email }})", new { User = new { Name = "John", Email = "j@e.com" } })
            .Value.Should().Be("John (j@e.com)");
    }

    [Fact]
    public void Sandbox_LegacyNoSpaceSyntax_Renders()
    {
        // PostrionCore: LegacyCompatibilityTests.Render_NoSpaceSyntax_WorksWithNewEngine
        Engine().Render("Welcome {{user.name}}", new { User = new { Name = "John" } })
            .Value.Should().Be("Welcome John");
    }

    [Fact]
    public void Sandbox_LegacyNoModel_RendersEmptyVariables()
    {
        // PostrionCore: LegacyCompatibilityTests.Render_NoModel_ReturnsTemplateWithEmptyVariables
        // A chain through an undefined target stays empty (PostrionCore parity); a refused member never
        // becomes empty — the lookup probe throws instead.
        var result = Engine().Render("Welcome {{ user.name }}", new { });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Welcome ");
    }

    // ---------------------------------------------------------------------------------------------
    // PostrionCore ObjectToScriptObjectConverterTests
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Sandbox_Converter_PlainAndNestedObjects()
    {
        // PostrionCore: ObjectToScriptObjectConverterTests.Convert_PlainObject / Convert_NestedObject
        // Tempo keeps nested POCOs as live CLR values and reaches them through the MemberFilter, so the
        // pinned invariant is the rendered value, not the internal ScriptObject shape.
        var result = Engine().Render("{{ user.name }} {{ user.email }}", new { user = new { name = "John", email = "j@e.com" } });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("John j@e.com");
    }

    [Fact]
    public void Sandbox_Converter_CollectionAndNull()
    {
        // PostrionCore: Convert_Collection_ConvertsToScriptArray / Convert_NullObject_ReturnsEmptyScriptObject
        var script = ObjectToScriptObjectConverter.ToScriptObject(new { items = new[] { "a", "b", "c" } });
        script["items"].Should().BeAssignableTo<IEnumerable<object?>>()
            .Which.Should().HaveCount(3);

        ObjectToScriptObjectConverter.ToScriptObject(null).Should().BeOfType<ScriptObject>();
    }

    [Fact]
    public void Sandbox_Converter_DeeplyNestedObject()
    {
        // PostrionCore: Convert_DeeplyNestedObject_ConvertsAllLevels
        var result = Engine().Render("{{ company.name }}/{{ company.address.city }}/{{ company.address.country }}", new
        {
            company = new { name = "Acme", address = new { city = "NYC", country = "USA" } },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Acme/NYC/USA");
    }

    // ---------------------------------------------------------------------------------------------
    // PostrionCore Safe*FunctionsTests (the safe function surface Tempo inherits from Scriban builtins)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Sandbox_SafeDateFunctions_AreAvailable()
    {
        // PostrionCore: SafeDateFunctionsTests.Integration_DateNow_WorksInTemplate
        var result = Engine().Render("{{ date.now | date.to_string \"%Y\" }}", new { });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(DateTime.UtcNow.Year.ToString());
    }

    [Fact]
    public void Sandbox_SafeMathFunctions_AreAvailable()
    {
        // PostrionCore: SafeMathFunctionsTests.Integration_Round_WorksInTemplate
        var result = Engine().Render("{{ 1234.567 | math.round 2 }}", new { });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("1234.5");
    }

    [Fact]
    public void Sandbox_SafeStringFunctions_AreAvailable()
    {
        // PostrionCore: SafeStringFunctionsTests.Integration_Truncate_WorksInTemplate / Escape_HtmlEncodesString
        var engine = Engine();

        engine.Render("{{ \"abc\" | string.upcase }}", new { }).Value.Should().Be("ABC");
        engine.Render("{{ \"a<b\" | html.escape }}", new { }).Value.Should().Be("a&lt;b");
    }

    // ---------------------------------------------------------------------------------------------
    // Review fix #2: the text scan must not read data (string literals, model identifiers) as code
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("{{ \"Prices include VAT\" }}")]
    [InlineData("{{ \"see GetType\" }}")]
    [InlineData("{{ \"System.IO\" }}")]
    [InlineData("{{ x = \"include\" }}")]
    [InlineData("{{ if status == \"eval\" }}ok{{ end }}")]
    [InlineData("{{ \"include\" | string.upcase }}")]
    public void Sandbox_ForbiddenNamesInsideStringLiterals_AreNotRefused(string template)
    {
        var result = Engine().Render(template, new { Status = "eval" });

        result.IsSuccess.Should().BeTrue("text uvnitř řetězcového literálu není kód šablony");
    }

    [Fact]
    public void Sandbox_ModelMemberNamedEnvironment_IsDataNotTheEnvironmentRoot()
    {
        var result = Engine().Render(
            "{{ environment.region }}",
            new { Environment = new { Region = "eu" } });

        result.IsSuccess.Should().BeTrue("model může legitimně obsahovat klíč environment");
        result.Value.Should().Be("eu");
    }

    [Fact]
    public void Sandbox_IncludeStatement_IsStillRefused()
    {
        Engine().Render("{{ include 'x' }}", new { }).IsSuccess.Should().BeFalse(
            "skener nesmí kvůli řetězcovým literálům přestat vidět skutečný include");
    }

    // ---------------------------------------------------------------------------------------------
    // Review fix #3: printing a value must not leak CLR type metadata through ToString
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Sandbox_TypeToString_DoesNotLeakTheClrTypeName()
    {
        var result = Engine().Render("{{ t }}", new Dictionary<string, object?> { ["t"] = typeof(string) });

        result.IsSuccess.Should().BeFalse("System.Type se nesmí vypsat");
        result.Error.Should().NotContain("System.String");
        (result.Value ?? string.Empty).Should().NotContain("System.String");
    }

    [Fact]
    public void Sandbox_MetadataToString_IsBlocked()
    {
        var engine = Engine();

        engine.Render("{{ info }}", new Dictionary<string, object?> { ["info"] = new FileInfo("/etc/passwd") })
            .IsSuccess.Should().BeFalse("System.IO.FileInfo se nesmí vypsat");
        engine.Render("{{ task }}", new Dictionary<string, object?> { ["task"] = Task.FromResult("x") })
            .IsSuccess.Should().BeFalse("System.Threading.Tasks.Task se nesmí vypsat");
    }

    [Fact]
    public void Sandbox_DelegateToString_IsNeverInvoked()
    {
        // The delegate hides in a collection property (an `object`-typed member is already refused by
        // the accessor and a delegate value in a dictionary is dropped by the model converter), so the
        // only layer that can still see it is the text conversion.
        var calls = 0;
        var model = new Dictionary<string, object?>
        {
            ["holder"] = new DelegateListHolder((Func<string>)(() => { calls++; return "ran"; })),
        };

        var result = Engine().Render("{{ holder.factories }}", model);

        calls.Should().Be(0, "delegát se nesmí vykonat");
        result.IsSuccess.Should().BeFalse("delegát se nesmí vypsat jako ToString");
        (result.Value ?? string.Empty).Should().NotContain("Func");
    }

    private sealed class DelegateListHolder(Func<string> factory)
    {
        public List<Func<string>> Factories { get; } = [factory];
    }

    [Fact]
    public void Sandbox_DelegateInObjectTypedMember_IsStillRefused()
    {
        var calls = 0;
        var model = new Dictionary<string, object?>
        {
            ["holder"] = new ObjectDelegateHolder((Func<string>)(() => { calls++; return "ran"; })),
        };

        var result = Engine().Render("{{ holder.callback }}", model);

        calls.Should().Be(0, "delegát se nesmí vykonat ani vypsat");
        result.IsSuccess.Should().BeFalse();
    }

    private sealed class ObjectDelegateHolder(object callback)
    {
        public object Callback { get; } = callback;
    }

    // ---------------------------------------------------------------------------------------------
    // Review fix #8: a scanner refusal reports the same message in Render and Validate
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Render_ScannerRejection_UsesTheSameMessageAsValidate()
    {
        var engine = new ScribanTemplateEngine();
        const string template = "{{ include 'x' }}";

        var validation = engine.Validate(template);
        var render = engine.Render(template, new { });

        validation.IsValid.Should().BeFalse();
        render.IsSuccess.Should().BeFalse();
        render.Error.Should().Be(validation.Errors[0].Message,
            "Render nesmí k hlášce přidávat pozici ve šabloně");
        render.Error.Should().Be("Template is not allowed to use 'include'.");
    }
}
