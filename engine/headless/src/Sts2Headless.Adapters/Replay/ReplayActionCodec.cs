using System.Buffers.Binary;
using Sts2Headless.Domain.Combat;

namespace Sts2Headless.Adapters.Replay;

/// <summary>
/// Encodes / decodes a <see cref="PlayerAction"/> to/from the
/// <c>action_type</c> byte + <c>action_data</c> payload of a replay entry.
///
/// <para>
/// Wire shapes:
/// </para>
/// <list type="bullet">
///   <item><see cref="ReplayActionType.PlayCard"/>:
///   <c>u32 CardInstanceId, u8 HasTarget, (if 1) u32 TargetEnemyId</c>.</item>
///   <item><see cref="ReplayActionType.EndTurn"/>: empty payload.</item>
///   <item><see cref="ReplayActionType.EnemyMove"/>: empty payload (Phase 1).</item>
/// </list>
/// </summary>
internal static class ReplayActionCodec
{
    /// <summary>Encode a PlayerAction to (type, data) bytes.</summary>
    public static (ReplayActionType Type, byte[] Data) Encode(PlayerAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        switch (action)
        {
            case PlayerAction.PlayCard pc:
            {
                byte[] data;
                if (pc.TargetEnemyId is null)
                {
                    data = new byte[4 + 1];
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), pc.CardInstanceId);
                    data[4] = 0;
                }
                else
                {
                    data = new byte[4 + 1 + 4];
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), pc.CardInstanceId);
                    data[4] = 1;
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        data.AsSpan(5, 4),
                        pc.TargetEnemyId.Value
                    );
                }
                return (ReplayActionType.PlayCard, data);
            }
            case PlayerAction.EndTurn:
                return (ReplayActionType.EndTurn, Array.Empty<byte>());
            default:
                throw new ArgumentException(
                    $"ReplayActionCodec.Encode: unsupported PlayerAction type {action.GetType().Name}.",
                    nameof(action)
                );
        }
    }

    /// <summary>Decode (type, data) bytes back into a PlayerAction. Throws on malformed input.</summary>
    public static PlayerAction Decode(ReplayActionType type, ReadOnlySpan<byte> data)
    {
        switch (type)
        {
            case ReplayActionType.PlayCard:
            {
                if (data.Length < 5)
                {
                    throw new ReplayException(
                        $"ReplayActionCodec.Decode: PlayCard payload too short ({data.Length} bytes, need >=5)."
                    );
                }
                uint cardId = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
                byte hasTarget = data[4];
                if (hasTarget == 0)
                {
                    if (data.Length != 5)
                    {
                        throw new ReplayException(
                            $"ReplayActionCodec.Decode: PlayCard with HasTarget=0 must be 5 bytes (got {data.Length})."
                        );
                    }
                    return new PlayerAction.PlayCard(cardId, null);
                }
                if (hasTarget == 1)
                {
                    if (data.Length != 9)
                    {
                        throw new ReplayException(
                            $"ReplayActionCodec.Decode: PlayCard with HasTarget=1 must be 9 bytes (got {data.Length})."
                        );
                    }
                    uint targetId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(5, 4));
                    return new PlayerAction.PlayCard(cardId, targetId);
                }
                throw new ReplayException(
                    $"ReplayActionCodec.Decode: PlayCard HasTarget byte 0x{hasTarget:X2} is not 0 or 1."
                );
            }
            case ReplayActionType.EndTurn:
                if (data.Length != 0)
                {
                    throw new ReplayException(
                        $"ReplayActionCodec.Decode: EndTurn payload must be empty (got {data.Length} bytes)."
                    );
                }
                return PlayerAction.EndTurn.Instance;
            case ReplayActionType.EnemyMove:
                // EnemyMove has no PlayerAction equivalent; callers iterating
                // raw entries (e.g., probe/dumper) handle this type directly
                // and don't go through Decode.
                throw new ReplayException(
                    "ReplayActionCodec.Decode: EnemyMove is not a PlayerAction; callers must dispatch on ActionType."
                );
            default:
                throw new ReplayException(
                    $"ReplayActionCodec.Decode: unknown action_type byte 0x{(byte)type:X2}."
                );
        }
    }
}
