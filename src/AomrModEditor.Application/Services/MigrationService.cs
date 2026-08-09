using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;

namespace AomrModEditor.Application.Services;

public sealed class MigrationService : IMigrationService
{
    public MigrationInspectReport Inspect(
        IEntityCatalog oldCatalog,
        IEntityCatalog newCatalog,
        string expectedHash,
        string actualHash)
    {
        var oldKeys = oldCatalog.All.Select(e => e.InternalKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newKeys = newCatalog.All.Select(e => e.InternalKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = oldKeys.Intersect(newKeys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
        var missing = oldKeys.Except(newKeys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
        var added = newKeys.Except(oldKeys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
        var fuzzy = new List<FuzzySuggestion>();
        foreach (var miss in missing)
        {
            var suggestion = newKeys
                .Select(k => (Key: k, Score: Similarity(miss, k)))
                .Where(x => x.Score >= 0.6)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();
            if (suggestion.Key is not null)
                fuzzy.Add(new FuzzySuggestion(miss, suggestion.Key, suggestion.Score));
        }

        return new MigrationInspectReport(
            expectedHash,
            actualHash,
            !string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase),
            matched,
            missing,
            added,
            fuzzy);
    }

    public RebaseResult Rebase(
        IReadOnlyList<ChangeOp> changes,
        IEntityCatalog oldCatalog,
        IEntityCatalog newCatalog,
        string newFingerprintHash,
        bool applyFuzzyBinds = false)
    {
        var report = Inspect(oldCatalog, newCatalog, "project", newFingerprintHash);
        var rebased = new List<ChangeOp>();
        var dropped = new List<ChangeOp>();
        var unresolved = new List<FuzzySuggestion>();

        foreach (var change in changes)
        {
            if (newCatalog.GetSourceNode(change.EntityKey) is not null)
            {
                rebased.Add(change);
                continue;
            }

            var fuzzy = report.FuzzySuggestions.FirstOrDefault(f =>
                f.ChangeEntityKey.Equals(change.EntityKey, StringComparison.OrdinalIgnoreCase));
            if (fuzzy is not null && applyFuzzyBinds)
            {
                rebased.Add(Remap(change, fuzzy.SuggestedKey));
            }
            else
            {
                dropped.Add(change);
                if (fuzzy is not null)
                    unresolved.Add(fuzzy);
            }
        }

        var md = $"""
            # Migration rebase report
            - New fingerprint: `{newFingerprintHash}`
            - Rebased: {rebased.Count}
            - Dropped: {dropped.Count}
            - Fuzzy unresolved (not silently bound): {unresolved.Count}
            """;

        return new RebaseResult(rebased, dropped, unresolved, newFingerprintHash, md);
    }

    private static ChangeOp Remap(ChangeOp op, string newKey) => op switch
    {
        SetScalar s => s with { EntityKey = newKey },
        ReplaceActionValue r => r with { EntityKey = newKey },
        SetAttribute a => a with { EntityKey = newKey },
        RawXmlPatch p => p with { EntityKey = newKey },
        _ => op with { EntityKey = newKey }
    };

    private static double Similarity(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 1;
        var aa = a.ToLowerInvariant();
        var bb = b.ToLowerInvariant();
        if (aa.Contains(bb) || bb.Contains(aa)) return 0.75;
        var shared = aa.Intersect(bb).Count();
        return (double)shared / Math.Max(aa.Length, bb.Length);
    }
}
