namespace Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

/// <summary>
/// Thrown when an email template document cannot be (de)serialized — for example malformed JSON, an
/// unknown block type discriminator or a schema version newer than the engine supports. Carries a
/// clear, surfaced message instead of crashing, plus an optional localized key for the host UI.
/// </summary>
public sealed class EmailTemplateSerializationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EmailTemplateSerializationException"/> class.</summary>
    public EmailTemplateSerializationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailTemplateSerializationException"/> class with a
    /// localization key (see <see cref="SerializationKeys"/>) the host can turn into a message.
    /// </summary>
    public EmailTemplateSerializationException(string message, string key, Exception? innerException = null)
        : base(message, innerException)
        => Key = key;

    /// <summary>Gets the localization key describing the failure, when it has one.</summary>
    public string? Key { get; }
}
