using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;

namespace AomrModEditor.ModGeneration;

public sealed class ProjectValidator : IValidator
{
    public IReadOnlyList<ValidationIssue> ValidateProject(
        IReadOnlyList<ChangeOp> changes,
        IReadOnlyDictionary<string, SourceNode> entities)
    {
        var issues = new List<ValidationIssue>();
        if (changes.Count == 0)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Info, "NO_CHANGES", "No project changes to validate."));
            return issues;
        }

        foreach (var op in changes)
        {
            if (!entities.ContainsKey(op.EntityKey))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Blocking,
                    "MISSING_ENTITY",
                    $"Change references unknown entity '{op.EntityKey}'.",
                    op.EntityKey));
            }

            if (op is SetScalar set && string.IsNullOrWhiteSpace(set.NewValue))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "EMPTY_SCALAR",
                    $"Empty value for {set.ElementName} on {set.EntityKey}.",
                    set.EntityKey));
            }

            if (op is ReplaceActionValue ra && string.IsNullOrWhiteSpace(ra.ActionName))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Blocking,
                    "ACTION_NAME_REQUIRED",
                    "ReplaceActionValue requires protoaction name selector.",
                    ra.EntityKey));
            }
        }

        return issues;
    }
}
