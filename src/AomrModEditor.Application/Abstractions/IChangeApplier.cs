using AomrModEditor.Domain;

namespace AomrModEditor.Application.Abstractions;

public interface IChangeApplier
{
    SourceNode Apply(SourceNode source, IEnumerable<ChangeOp> ops);
}