namespace Sts2Headless.Adapters.StateCodec;

/// <summary>
/// Sealed exception type thrown by every fail-path in the state codec —
/// tamper detection, schema-version mismatch, malformed bytes, missing
/// section, unexpected EOF. Callers catch this (and only this) to
/// distinguish "load failed" from "load succeeded but state has bug."
/// </summary>
public sealed class StateCodecException : Exception
{
    public StateCodecException(string message) : base(message) { }
    public StateCodecException(string message, Exception inner) : base(message, inner) { }
}
