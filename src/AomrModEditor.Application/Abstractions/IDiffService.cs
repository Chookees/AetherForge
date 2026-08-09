using AomrModEditor.Domain;

namespace AomrModEditor.Application.Abstractions;

public interface IDiffService
{
    DiffResult Diff(SourceNode original, SourceNode modified);
}