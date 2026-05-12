using System.Text.Json;

namespace Sts2Headless.Adapters.ControlPlane;

/// <summary>
/// Discriminated-union return value for a JSON-RPC handler: either a
/// successful result payload or an error code + message. The dispatcher
/// serializes whichever variant is returned into the wire response.
///
/// <para>
/// Handlers should return <see cref="Ok"/> on success and <see cref="Error"/>
/// on validation failures (with <c>-32602</c> for invalid params, or any
/// domain-defined code &lt;= -32000 for application errors). Throwing from
/// the handler is interpreted as <c>-32603</c> Internal Error.
/// </para>
/// </summary>
public readonly struct JsonRpcResult
{
    /// <summary>True iff this is a successful result.</summary>
    public bool IsOk { get; }

    /// <summary>The result payload (only valid when <see cref="IsOk"/>).</summary>
    public JsonElement ResultValue { get; }

    /// <summary>Error code (only valid when not <see cref="IsOk"/>).</summary>
    public int ErrorCode { get; }

    /// <summary>Error message (only valid when not <see cref="IsOk"/>).</summary>
    public string ErrorMessage { get; }

    private JsonRpcResult(bool isOk, JsonElement resultValue, int errorCode, string errorMessage)
    {
        IsOk = isOk;
        ResultValue = resultValue;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Successful result with the given payload.</summary>
    public static JsonRpcResult Ok(JsonElement result) =>
        new(isOk: true, resultValue: result, errorCode: 0, errorMessage: string.Empty);

    /// <summary>Error with the given code and message.</summary>
    public static JsonRpcResult Error(int code, string message) =>
        new(isOk: false, resultValue: default, errorCode: code, errorMessage: message ?? string.Empty);
}
