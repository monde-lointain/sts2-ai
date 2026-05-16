using System.Text.Json;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Host;

/// <summary>
/// JSON-Lines <see cref="IStructuredLogger"/>. Each <c>Log</c> call writes a
/// single line of the form
/// <code>
///   {"ts":&lt;long&gt;,"event":"&lt;type&gt;",...payload}
/// </code>
/// terminated by <c>\n</c> to the configured <see cref="TextWriter"/>
/// (stderr by default, per the M9 spec).
///
/// <para>
/// <b>ts is logical, not wall-clock.</b> The <c>ts</c> field is the
/// <see cref="IClock.NowTicks"/> value at the moment of the call — replays
/// reproduce identical log streams. Reading wall-clock here would defeat
/// determinism and is banned in the Domain assembly anyway (the host project
/// could technically use wall-clock, but per the M9 spec we don't).
/// </para>
///
/// <para>
/// <b>Thread safety:</b> writes are guarded by an internal lock so concurrent
/// callers don't interleave bytes. The background metrics-thread (S8-T5) can
/// log safely alongside the main thread.
/// </para>
/// </summary>
public sealed class JsonLineLogger : IStructuredLogger
{
    private readonly TextWriter _writer;
    private readonly IClock _clock;
    private readonly object _writeLock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // Compact output: no indenting (we want one line per event). The
        // PropertyNamingPolicy isn't set because the keys are user-supplied
        // payload fields plus the literal "ts"/"event" constants.
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Construct against <paramref name="writer"/> (defaults to stderr).</summary>
    public JsonLineLogger(IClock clock, TextWriter? writer = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _writer = writer ?? Console.Error;
    }

    /// <inheritdoc/>
    public void Log(string eventType, IReadOnlyDictionary<string, object?> payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventType);
        ArgumentNullException.ThrowIfNull(payload);

        // Build the JSON envelope. We hand-merge so ts/event are first (better
        // tail-reading ergonomics) and payload fields with the same names are
        // shadowed by the envelope (they'd be ambiguous anyway).
        var envelope = new Dictionary<string, object?>(payload.Count + 2)
        {
            ["ts"] = _clock.NowTicks,
            ["event"] = eventType,
        };
        foreach (KeyValuePair<string, object?> kv in payload)
        {
            // Skip reserved keys to keep the envelope unambiguous.
            if (kv.Key == "ts" || kv.Key == "event")
                continue;
            envelope[kv.Key] = kv.Value;
        }

        string line = JsonSerializer.Serialize(envelope, JsonOpts);
        lock (_writeLock)
        {
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }
}
