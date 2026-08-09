using System.Collections.Concurrent;
using AomrModEditor.Application.Abstractions;

namespace AomrModEditor.Application.Services;

public sealed class OperationRunner : IOperationRunner
{
    private readonly ConcurrentDictionary<Guid, OperationInfo> _ops = new();

    public Guid Enqueue(string kind, Func<IOperationProgress, CancellationToken, Task<object?>> work)
    {
        var id = Guid.NewGuid();
        var info = new OperationInfo(
            id, kind, "Queued", 0, null, Array.Empty<string>(), Array.Empty<string>(),
            DateTimeOffset.UtcNow, null, $"/api/v1/operations/{id}", null);
        _ops[id] = info;
        _ = Task.Run(async () =>
        {
            var warnings = new List<string>();
            var progress = new Progress(p =>
            {
                _ops[id] = _ops[id] with
                {
                    State = "Running",
                    ProgressPercent = p.Percent,
                    Phase = p.Phase,
                    Warnings = warnings.ToList()
                };
                if (!string.IsNullOrWhiteSpace(p.Warning))
                    warnings.Add(p.Warning!);
            });
            try
            {
                _ops[id] = _ops[id] with { State = "Running", ProgressPercent = 1, Phase = "starting" };
                var result = await work(progress, CancellationToken.None).ConfigureAwait(false);
                _ops[id] = _ops[id] with
                {
                    State = "Succeeded",
                    ProgressPercent = 100,
                    Phase = "done",
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Result = result,
                    Warnings = warnings
                };
            }
            catch (Exception ex)
            {
                _ops[id] = _ops[id] with
                {
                    State = "Failed",
                    ProgressPercent = 100,
                    Phase = "failed",
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Errors = new[] { ex.Message },
                    Warnings = warnings
                };
            }
        });
        return id;
    }

    public OperationInfo? Get(Guid id) => _ops.TryGetValue(id, out var o) ? o : null;

    public IReadOnlyList<OperationInfo> List() => _ops.Values.OrderByDescending(o => o.CreatedAtUtc).ToList();

    private sealed class Progress : IOperationProgress
    {
        private readonly Action<(int Percent, string? Phase, string? Warning)> _on;

        public Progress(Action<(int Percent, string? Phase, string? Warning)> on) => _on = on;

        public void Report(int percent, string? phase = null, string? warning = null)
            => _on((Math.Clamp(percent, 0, 100), phase, warning));
    }
}
