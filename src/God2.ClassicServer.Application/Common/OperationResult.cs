namespace God2.ClassicServer.Application.Common;

public sealed record OperationError(string Code, string Message, string Source = "")
{
    public static OperationError None { get; } = new("none", string.Empty);
}

public sealed record OperationResult(bool Succeeded, OperationError Error)
{
    public static OperationResult Success { get; } = new(true, OperationError.None);

    public static OperationResult Failure(string code, string message, string source = "") =>
        new(false, new OperationError(code, message, source));
}

public sealed record OperationResult<T>(bool Succeeded, T? Value, OperationError Error)
{
    public static OperationResult<T> Success(T value) => new(true, value, OperationError.None);

    public static OperationResult<T> Failure(string code, string message, string source = "") =>
        new(false, default, new OperationError(code, message, source));
}
