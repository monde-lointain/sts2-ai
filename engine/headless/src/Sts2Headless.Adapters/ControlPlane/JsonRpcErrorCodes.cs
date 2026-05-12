namespace Sts2Headless.Adapters.ControlPlane;

/// <summary>
/// Standard JSON-RPC 2.0 error codes used by the Q1 control plane. Codes
/// less than or equal to -32000 are reserved for implementation-defined
/// server errors; the protocol-level codes here all live in the
/// <c>-32700..-32600</c> band per the JSON-RPC spec.
/// </summary>
public static class JsonRpcErrorCodes
{
    /// <summary>Malformed JSON received.</summary>
    public const int ParseError = -32700;

    /// <summary>JSON valid but not a well-formed request object.</summary>
    public const int InvalidRequest = -32600;

    /// <summary>Method name not registered.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>Handler rejected the params object.</summary>
    public const int InvalidParams = -32602;

    /// <summary>Handler threw an unhandled exception.</summary>
    public const int InternalError = -32603;
}
