using AomrModEditor.Domain;

namespace AomrModEditor.Application.Abstractions;

public sealed record OperationInfo(
    Guid Id,
    string Kind,
    string State,
    int ProgressPercent,
    string? Phase,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultLink,
    object? Result);

public interface IOperationRunner
{
    Guid Enqueue(string kind, Func<IOperationProgress, CancellationToken, Task<object?>> work);
    OperationInfo? Get(Guid id);
    IReadOnlyList<OperationInfo> List();
}

public interface IOperationProgress
{
    void Report(int percent, string? phase = null, string? warning = null);
}
