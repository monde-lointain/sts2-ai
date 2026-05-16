namespace Sts2Headless.Adapters.Replay;

/// <summary>
/// Sealed exception type thrown by every fail-path in the replay recorder
/// and reader — tamper detection, schema-version mismatch, malformed bytes,
/// missing trailer magic, unexpected EOF, illegal recorder lifecycle (e.g.,
/// AppendStep after Close). Callers catch this (and only this) to distinguish
/// "replay file invalid" from other failures.
/// </summary>
public sealed class ReplayException : Exception
{
    public ReplayException() { }

    public ReplayException(string message)
        : base(message) { }

    public ReplayException(string message, Exception inner)
        : base(message, inner) { }
}
