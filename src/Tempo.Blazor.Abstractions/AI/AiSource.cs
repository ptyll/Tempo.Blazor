namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One source an assistant answer cites. The model refers to it from the markdown as
/// <c>[1](cite:&lt;Id&gt;)</c>; the renderer turns that link into a citation chip carrying the id, and
/// the source cards read the same record.
/// </summary>
public sealed class AiSource
{
    private string _id = string.Empty;
    private string _title = string.Empty;
    private string? _url;
    private string? _snippet;
    private string? _iconUrl;
    private string? _domain;

    /// <summary>Stable identifier the <c>cite:</c> link points at. Required.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Human readable source title.</summary>
    public string Title
    {
        get => _title;
        set => _title = value ?? string.Empty;
    }

    /// <summary>Absolute source URL, when the source has one.</summary>
    public string? Url
    {
        get => _url;
        set => _url = value;
    }

    /// <summary>Short excerpt shown in the citation preview.</summary>
    public string? Snippet
    {
        get => _snippet;
        set => _snippet = value;
    }

    /// <summary>Optional favicon/icon URL.</summary>
    public string? IconUrl
    {
        get => _iconUrl;
        set => _iconUrl = value;
    }

    /// <summary>Display domain; derived from <see cref="Url"/> by the host when it is convenient.</summary>
    public string? Domain
    {
        get => _domain;
        set => _domain = value;
    }
}
