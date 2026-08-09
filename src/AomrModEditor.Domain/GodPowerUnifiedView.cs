namespace AomrModEditor.Domain;

public sealed record GodPowerUnifiedView(
    string PowerKey,
    string? Behavior,
    string? Damage,
    string? CooldownTechKey,
    string? Cooldown,
    string? CostFavor,
    string BehaviorSourceFile,
    string? CooldownSourceFile);
