using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Serializes <see cref="CreatureId"/> as a bare <see cref="uint"/> JSON
/// number — preserving the pre-typification wire shape on the control-plane
/// RPC (<see cref="PlayerAction"/>'s <c>TargetEnemyId</c>) and JSON-line log
/// lines (<see cref="Sts2Headless.Host.MainLoop"/> emits <c>id = e.Id</c>).
///
/// <para>
/// Without this converter, <see cref="System.Text.Json.JsonSerializer"/>
/// would emit <c>{"value": N}</c> (the record-struct's default property
/// layout) and break every JSON-RPC consumer.
/// </para>
///
/// <para>
/// <see cref="Nullable{T}"/> is handled automatically by <see cref="JsonSerializer"/>:
/// when serializing a <c>CreatureId?</c>, <c>null</c> round-trips as JSON
/// <c>null</c>; a non-null value delegates to this converter.
/// </para>
/// </summary>
public sealed class CreatureIdJsonConverter : JsonConverter<CreatureId>
{
    public override CreatureId Read(
        ref Utf8JsonReader reader,
        System.Type typeToConvert,
        JsonSerializerOptions options
    ) => new(reader.GetUInt32());

    public override void Write(
        Utf8JsonWriter writer,
        CreatureId value,
        JsonSerializerOptions options
    ) => writer.WriteNumberValue(value.Value);
}
