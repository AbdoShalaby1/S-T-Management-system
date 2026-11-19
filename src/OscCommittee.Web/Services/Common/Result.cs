namespace OscCommittee.Web.Services.Common;

/// <summary>
/// Outcome of an expected failure path. Exceptions stay reserved for bugs;
/// controllers turn a failed result into a toast plus a redirect.
/// </summary>
public class Result
{
    protected Result(bool succeeded, string? errorCode, string message)
    {
        Succeeded = succeeded;
        ErrorCode = errorCode;
        Message = message;
    }

    public bool Succeeded { get; }

    public bool Failed => !Succeeded;

    /// <summary>Stable machine-readable code, e.g. <c>invalid_credentials</c>.</summary>
    public string? ErrorCode { get; }

    /// <summary>Copy that is safe to show to the person.</summary>
    public string Message { get; }

    public static Result Ok(string message = "") => new(true, null, message);

    public static Result<T> Ok<T>(T value, string message = "") => new Result<T>(true, value, null, message);

    public static Result Fail(string errorCode, string message) => new(false, errorCode, message);

    public static Result<T> Fail<T>(string errorCode, string message) => new(false, default, errorCode, message);
}

public sealed class Result<T> : Result
{
    internal Result(bool succeeded, T? value, string? errorCode, string message)
        : base(succeeded, errorCode, message)
    {
        Value = value;
    }

    /// <summary>Non-null only when <see cref="Result.Succeeded"/> is true.</summary>
    public T? Value { get; }
}
