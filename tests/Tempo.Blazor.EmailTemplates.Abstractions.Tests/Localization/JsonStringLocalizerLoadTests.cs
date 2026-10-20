using Tempo.Blazor.EmailTemplates.Abstractions.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions.Tests.Resources;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Localization;

/// <summary>
/// The parse contract of <see cref="JsonStringLocalizer{TResourceSource}"/>: a table is a flat
/// <c>{ "Key": "Value" }</c> map, and anything else must fail loudly at the first lookup instead of
/// rendering a number as a translation (or letting an opaque <see cref="InvalidOperationException"/> out
/// of <c>JsonElement.GetString</c>). A missing table is a different thing entirely — it is empty.
/// </summary>
public class JsonStringLocalizerLoadTests
{
    [Fact]
    public void NonStringValue_FailsLoudly_InsteadOfSilentlyRenderingIt()
    {
        var localizer = new JsonStringLocalizer<NonStringResources>();

        var act = () => localizer["Numeric_Key"];

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*NonStringResources.json: 'Numeric_Key' must be a string*");
    }

    [Fact]
    public void MissingTable_IsAnEmptyTable_NotAFailure()
    {
        // A marker whose tables were never packaged must fall through to "key not found", never throw.
        var localizer = new JsonStringLocalizer<MarkerWithoutTables>();

        var missing = localizer["Nope"];

        missing.ResourceNotFound.Should().BeTrue();
        missing.Value.Should().Be("Nope");
    }
}
