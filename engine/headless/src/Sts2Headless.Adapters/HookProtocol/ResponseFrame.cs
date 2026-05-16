// ResponseFrame — owned-buffer view of one inbound response message.
//
// Layout: (Header, Payload). Caller owns the Payload array; it was allocated
// by the inbound reader thread, sized exactly to the message's PayloadLength,
// and handed off through the response slot. Caller may stash or process at
// leisure.
//
// Why a class (not a struct): callers store ResponseFrames in queues or hand
// them to async callers; struct copying through TaskCompletionSource is
// awkward and gains nothing here.

using System;

namespace Sts2Headless.Adapters.HookProtocol;

/// <summary>
/// A decoded inbound response. <see cref="Payload"/> bytes are valid for the
/// lifetime of the response (caller-owned heap array).
/// </summary>
public sealed class ResponseFrame
{
    public MessageHeader Header { get; }
    public ReadOnlyMemory<byte> Payload { get; }

    public ResponseFrame(MessageHeader header, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != header.PayloadLength)
        {
            throw new ArgumentException(
                $"payload length {payload.Length} != header.PayloadLength {header.PayloadLength}",
                nameof(payload)
            );
        }
        Header = header;
        Payload = payload;
    }
}
