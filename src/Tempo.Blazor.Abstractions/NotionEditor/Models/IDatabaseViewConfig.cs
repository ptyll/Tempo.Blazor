namespace Tempo.Blazor.NotionEditor.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Marker base for database view configurations. Polymorphic JSON (like <see cref="IFieldConfig"/>):
/// both the demo API and the WASM client serialize/deserialize view configs through this interface.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TableViewConfig), "table")]
[JsonDerivedType(typeof(BoardViewConfig), "board")]
[JsonDerivedType(typeof(GalleryViewConfig), "gallery")]
[JsonDerivedType(typeof(CalendarViewConfig), "calendar")]
[JsonDerivedType(typeof(TimelineViewConfig), "timeline")]
[JsonDerivedType(typeof(ListViewConfig), "list")]
public interface IDatabaseViewConfig
{
}
