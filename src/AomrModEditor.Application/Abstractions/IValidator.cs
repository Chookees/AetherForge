using AomrModEditor.Domain;

namespace AomrModEditor.Application.Abstractions;

public interface IValidator
{
    IReadOnlyList<ValidationIssue> ValidateProject(
        IReadOnlyList<ChangeOp> changes,
        IReadOnlyDictionary<string, SourceNode> entities);
}