using System.Globalization;
using System.Reflection;
using Xunit.Sdk;
using Tempo.Blazor.EmailTemplates.Tests.Localization;

[assembly: Tempo.Blazor.EmailTemplates.Tests.EnglishUiCulture]

namespace Tempo.Blazor.EmailTemplates.Tests;

/// <summary>
/// Assembly-level xUnit hook: every test runs on its own test thread under <see cref="CultureInfo.InvariantCulture"/> (formatting)
/// and the English UI culture (the shipped wording), and the previous cultures are restored afterwards. It complements
/// <see cref="TestCulture"/> (which only sets the default for threads that have no culture yet): this attribute also protects against a
/// previous test leaking a language onto the thread. A test that opened its own <see cref="CultureScope"/> in the constructor keeps that
/// choice (xUnit constructs the class before it runs <see cref="Before"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
internal sealed class EnglishUiCultureAttribute : BeforeAfterTestAttribute
{
    private static readonly AsyncLocal<CultureScope?> Active = new();

    /// <inheritdoc />
    public override void Before(MethodInfo methodUnderTest)
    {
        if (CultureScope.HasExplicitScope)
        {
            Active.Value = null;
            return;
        }

        Active.Value = CultureScope.UseAssemblyDefault(CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("en"));
    }

    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest)
    {
        Active.Value?.Dispose();
        Active.Value = null;
    }
}
