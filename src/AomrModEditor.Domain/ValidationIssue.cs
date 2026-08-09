namespace AomrModEditor.Domain;

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Code,
    string Message,
    string? EntityKey = null,
    string? SafeFixHint = null);