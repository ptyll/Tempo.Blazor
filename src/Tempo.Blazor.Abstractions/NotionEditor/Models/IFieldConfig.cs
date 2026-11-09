namespace Tempo.Blazor.NotionEditor.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Marker base for database field configurations. Polymorphic JSON (like <see cref="IBlockContent"/>):
/// both the demo API and the WASM client serialize/deserialize configs through this interface —
/// the discriminator keeps the write path (PUT field) working, not just the read path.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(StatusFieldConfig), "status")]
[JsonDerivedType(typeof(SelectFieldConfig), "select")]
[JsonDerivedType(typeof(DateFieldConfig), "date")]
[JsonDerivedType(typeof(NumberFieldConfig), "number")]
[JsonDerivedType(typeof(RelationFieldConfig), "relation")]
[JsonDerivedType(typeof(RollupFieldConfig), "rollup")]
[JsonDerivedType(typeof(FormulaFieldConfig), "formula")]
public interface IFieldConfig
{
}
