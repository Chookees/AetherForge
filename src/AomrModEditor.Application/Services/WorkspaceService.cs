using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;

namespace AomrModEditor.Application.Services;

public sealed class WorkspaceService
{
    private readonly ISourceSnapshotStore _snapshots;
    private readonly IProjectStore _projects;
    private readonly IChangeApplier _applier;
    private readonly IDiffService _diff;
    private readonly IModGenerator _generator;
    private readonly IValidator _validator;
    private readonly ILocalModInstaller _installer;
    private readonly IEffectiveStatEngine _effectiveStats;
    private readonly IMigrationService _migration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IEntityCatalog? _catalog;
    private IEntityCatalog? _previousCatalog;
    private EditorSchemaContext? _schemaContext;
    private IEntityCatalog? _schemaContextCatalog;

    public WorkspaceService(
        ISourceSnapshotStore snapshots,
        IProjectStore projects,
        IChangeApplier applier,
        IDiffService diff,
        IModGenerator generator,
        IValidator validator,
        ILocalModInstaller installer,
        IEffectiveStatEngine effectiveStats,
        IMigrationService migration)
    {
        _snapshots = snapshots;
        _projects = projects;
        _applier = applier;
        _diff = diff;
        _generator = generator;
        _validator = validator;
        _installer = installer;
        _effectiveStats = effectiveStats;
        _migration = migration;
    }

    public IEntityCatalog Catalog => _catalog ?? throw new InvalidOperationException("No source imported.");
    public SourceSnapshot? Snapshot => _snapshots.Current;
    public ProjectInfo? Project => _projects.Current;
    public bool IsSourceLoaded => _snapshots.Current is not null && _catalog is not null;

    public void AttachCatalog(IEntityCatalog catalog)
    {
        _catalog = catalog;
        InvalidateSchemaContext();
    }

    public async Task<SourceSnapshot> ImportAsync(string directory, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _previousCatalog = _catalog;
            var snap = await _snapshots.ImportDirectoryAsync(directory, ct).ConfigureAwait(false);
            _catalog = _snapshots.Catalog ?? throw new InvalidOperationException("Import produced no catalog.");
            InvalidateSchemaContext();
            return snap;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SourceSnapshot> ImportEmbeddedPackAsync(Stream gzipPackStream, string rootLabel, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _previousCatalog = _catalog;
            var snap = await _snapshots.ImportPackAsync(gzipPackStream, rootLabel, ct).ConfigureAwait(false);
            _catalog = _snapshots.Catalog ?? throw new InvalidOperationException("Embedded import produced no catalog.");
            InvalidateSchemaContext();
            return snap;
        }
        finally
        {
            _gate.Release();
        }
    }

    public EntityEditorSchema GetEditorSchema(string entityKey)
    {
        _gate.Wait();
        try
        {
            var node = GetModifiedNodeUnlocked(entityKey);
            GodPowerUnifiedView? gp = null;
            if (node.Category == EntityCategory.GodPower)
            {
                // Build unified without re-entering lock
                gp = BuildGodPowerUnifiedUnlocked(entityKey);
            }

            var context = GetOrBuildSchemaContext();
            return EditorSchemaBuilder.Build(node, gp, context);
        }
        finally
        {
            _gate.Release();
        }
    }

    private EditorSchemaContext GetOrBuildSchemaContext()
    {
        if (_schemaContext is null || !ReferenceEquals(_schemaContextCatalog, Catalog))
        {
            _schemaContext = EditorSchemaBuilder.BuildContext(Catalog);
            _schemaContextCatalog = Catalog;
        }

        return _schemaContext;
    }

    private void InvalidateSchemaContext()
    {
        _schemaContext = null;
        _schemaContextCatalog = null;
    }

    private GodPowerUnifiedView BuildGodPowerUnifiedUnlocked(string powerKey)
    {
        var power = GetModifiedNodeUnlocked(powerKey);
        if (power.Category != EntityCategory.GodPower)
            throw new InvalidOperationException($"{powerKey} is not a god power.");

        string? behavior = power.Xml.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("behavior", StringComparison.OrdinalIgnoreCase))?.Value;
        string? damage = power.Xml.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("damage", StringComparison.OrdinalIgnoreCase))?.Value;

        string? techKey = null;
        string? cooldown = null;
        string? costFavor = null;
        string? cooldownFile = null;
        var techNodes = Catalog.All
            .Where(e => e.Category == EntityCategory.Technology)
            .Select(e => GetModifiedNodeUnlocked(e.InternalKey))
            .Select(tech =>
            {
                var effect = tech.Xml.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(e.Attribute("power")?.Value, powerKey, StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(e.Attribute("subtype")?.Value, "GodPower", StringComparison.OrdinalIgnoreCase));
                var isDedicated = tech.Xml.Elements().Any(e =>
                    e.Name.LocalName.Equals("flag", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Value, "GodPower", StringComparison.OrdinalIgnoreCase));
                return (tech, effect, isDedicated);
            })
            .Where(t => t.effect is not null)
            .OrderByDescending(t => t.isDedicated)
            .ToList();

        if (techNodes.Count > 0)
        {
            var (tech, effect, _) = techNodes[0];
            techKey = tech.InternalKey;
            cooldown = effect!.Attribute("cooldown")?.Value;
            cooldownFile = tech.SourceFile;
            costFavor = tech.Xml.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(e.Attribute("resourcetype")?.Value, "Favor", StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }

        return new GodPowerUnifiedView(
            powerKey, behavior, damage, techKey, cooldown, costFavor, power.SourceFile, cooldownFile);
    }

    public async Task<ProjectInfo> CreateProjectAsync(string name, string directory, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = _snapshots.Current ?? throw new InvalidOperationException("Import a source first.");
            return await _projects.CreateAsync(name, directory, snap, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChangeOp> EditMaxHitpointsAsync(string entityKey, string newValue, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var detail = Catalog.GetByInternalKey(entityKey) ?? throw new KeyNotFoundException(entityKey);
            var old = detail.NormalizedFields.GetValueOrDefault("maxhitpoints");
            var op = new SetScalar(Guid.NewGuid(), entityKey, DateTimeOffset.UtcNow, "maxhitpoints", old, newValue);
            await _projects.AppendChangeAsync(op, ct).ConfigureAwait(false);
            return op;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChangeOp> EditActionValueAsync(
        string entityKey,
        string actionName,
        string childElement,
        string newValue,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? childAttributes = null)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var node = Catalog.GetSourceNode(entityKey) ?? throw new KeyNotFoundException(entityKey);
            var action = node.Xml.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Element("name")?.Value, actionName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Action '{actionName}' not found on {entityKey}.");
            var child = action.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(childElement, StringComparison.OrdinalIgnoreCase)
                    && AttributesMatch(e, childAttributes));
            var old = child?.Value;
            var op = new ReplaceActionValue(
                Guid.NewGuid(), entityKey, DateTimeOffset.UtcNow, actionName, childElement, old, newValue, childAttributes);
            await _projects.AppendChangeAsync(op, ct).ConfigureAwait(false);
            return op;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChangeOp> EditScalarAsync(
        string entityKey,
        string elementName,
        string newValue,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? attrs = null)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var node = Catalog.GetSourceNode(entityKey) ?? throw new KeyNotFoundException(entityKey);
            var el = FindElement(node, elementName, attrs);
            var op = new SetScalar(Guid.NewGuid(), entityKey, DateTimeOffset.UtcNow, elementName, el?.Value, newValue, attrs);
            await _projects.AppendChangeAsync(op, ct).ConfigureAwait(false);
            return op;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChangeOp> EditAttributeAsync(
        string entityKey,
        string elementName,
        string attributeName,
        string newValue,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? matchAttributes = null)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var node = Catalog.GetSourceNode(entityKey) ?? throw new KeyNotFoundException(entityKey);
            // Create-on-missing for addable attributes (e.g. armor @value) — ChangeApplier.FindOrCreate
            var el = FindElement(node, elementName, matchAttributes);
            var old = el?.Attribute(attributeName)?.Value;
            var op = new SetAttribute(
                Guid.NewGuid(), entityKey, DateTimeOffset.UtcNow, elementName, attributeName, old, newValue, matchAttributes);
            await _projects.AppendChangeAsync(op, ct).ConfigureAwait(false);
            return op;
        }
        finally
        {
            _gate.Release();
        }
    }

    public GodPowerUnifiedView GetGodPowerUnified(string powerKey)
    {
        _gate.Wait();
        try
        {
            var power = GetModifiedNodeUnlocked(powerKey);
            if (power.Category != EntityCategory.GodPower)
                throw new InvalidOperationException($"{powerKey} is not a god power.");

            string? behavior = power.Xml.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals("behavior", StringComparison.OrdinalIgnoreCase))?.Value;
            string? damage = power.Xml.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals("damage", StringComparison.OrdinalIgnoreCase))?.Value;

            string? techKey = null;
            string? cooldown = null;
            string? costFavor = null;
            string? cooldownFile = null;
            var techNodes = Catalog.All
                .Where(e => e.Category == EntityCategory.Technology)
                .Select(e => GetModifiedNodeUnlocked(e.InternalKey))
                .Select(tech =>
                {
                    var effect = tech.Xml.Elements()
                        .FirstOrDefault(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)
                                             && string.Equals(e.Attribute("power")?.Value, powerKey, StringComparison.OrdinalIgnoreCase)
                                             && string.Equals(e.Attribute("subtype")?.Value, "GodPower", StringComparison.OrdinalIgnoreCase));
                    var isDedicated = tech.Xml.Elements().Any(e =>
                        e.Name.LocalName.Equals("flag", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(e.Value, "GodPower", StringComparison.OrdinalIgnoreCase));
                    return (tech, effect, isDedicated);
                })
                .Where(t => t.effect is not null)
                .OrderByDescending(t => t.isDedicated)
                .ToList();

            if (techNodes.Count > 0)
            {
                var (tech, effect, _) = techNodes[0];
                techKey = tech.InternalKey;
                cooldown = effect!.Attribute("cooldown")?.Value;
                cooldownFile = tech.SourceFile;
                costFavor = tech.Xml.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(e.Attribute("resourcetype")?.Value, "Favor", StringComparison.OrdinalIgnoreCase))
                    ?.Value;
            }

            return new GodPowerUnifiedView(
                powerKey, behavior, damage, techKey, cooldown, costFavor, power.SourceFile, cooldownFile);
        }
        finally
        {
            _gate.Release();
        }
    }

    public DiffResult DiffEntity(string entityKey)
    {
        _gate.Wait();
        try
        {
            EnsureFingerprint();
            var original = Catalog.GetSourceNode(entityKey) ?? throw new KeyNotFoundException(entityKey);
            var modified = GetModifiedNodeUnlocked(entityKey);
            return _diff.Diff(original, modified);
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<ValidationIssue> Validate()
    {
        _gate.Wait();
        try
        {
            EnsureFingerprint();
            var changes = _projects.GetChanges();
            var map = BuildModifiedMapUnlocked();
            return _validator.ValidateProject(changes, map);
        }
        finally
        {
            _gate.Release();
        }
    }

    public GenerationResult Generate(string? stagingDirectory = null)
    {
        _gate.Wait();
        try
        {
            EnsureFingerprint();
            var issues = _validator.ValidateProject(_projects.GetChanges(), BuildModifiedMapUnlocked());
            if (issues.Any(i => i.Severity == ValidationSeverity.Blocking))
                throw new InvalidOperationException("Generation blocked by validation errors.");

            var originals = Catalog.All.ToDictionary(
                e => e.InternalKey,
                e => Catalog.GetSourceNode(e.InternalKey)!,
                StringComparer.OrdinalIgnoreCase);
            var modified = BuildModifiedMapUnlocked();
            var staging = stagingDirectory ?? Path.Combine(_projects.Current?.Directory ?? Path.GetTempPath(), "generated");
            Directory.CreateDirectory(staging);
            return _generator.Generate(originals, modified, staging);
        }
        finally
        {
            _gate.Release();
        }
    }

    public InstallResult Install(string modsRoot, string modFolderName)
    {
        _gate.Wait();
        try
        {
            var project = _projects.Current ?? throw new InvalidOperationException("No project.");
            var staging = Path.Combine(project.Directory, "generated");
            return _installer.InstallGeneratedOnly(staging, modsRoot, modFolderName);
        }
        finally
        {
            _gate.Release();
        }
    }

    public string PackageZip(string? outputZip = null)
    {
        _gate.Wait();
        try
        {
            var project = _projects.Current ?? throw new InvalidOperationException("No project.");
            var staging = Path.Combine(project.Directory, "generated");
            if (!Directory.Exists(staging))
                throw new InvalidOperationException("Generate before packaging.");
            var zip = outputZip ?? Path.Combine(project.Directory, "package", $"{project.Name}.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
            if (File.Exists(zip)) File.Delete(zip);
            System.IO.Compression.ZipFile.CreateFromDirectory(staging, zip);
            return zip;
        }
        finally
        {
            _gate.Release();
        }
    }

    public EffectiveStatsResult GetEffectiveStats(string entityKey)
    {
        _gate.Wait();
        try
        {
            var node = GetModifiedNodeUnlocked(entityKey);
            var techs = Catalog.All
                .Where(e => e.Category == EntityCategory.Technology)
                .Select(e => Catalog.GetSourceNode(e.InternalKey)!)
                .ToList();
            return _effectiveStats.Compute(node, techs);
        }
        finally
        {
            _gate.Release();
        }
    }

    public MigrationInspectReport InspectMigration()
    {
        _gate.Wait();
        try
        {
            var project = _projects.Current ?? throw new InvalidOperationException("No project.");
            var snap = _snapshots.Current ?? throw new InvalidOperationException("No source.");
            var old = _previousCatalog ?? Catalog;
            return _migration.Inspect(old, Catalog, project.SourceFingerprintHash, snap.Fingerprint.AggregateHash);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RebaseResult> RebaseMigrationAsync(bool applyFuzzyBinds, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var project = _projects.Current ?? throw new InvalidOperationException("No project.");
            var snap = _snapshots.Current ?? throw new InvalidOperationException("No source.");
            var old = _previousCatalog ?? Catalog;
            var result = _migration.Rebase(
                _projects.GetChanges(),
                old,
                Catalog,
                snap.Fingerprint.AggregateHash,
                applyFuzzyBinds);
            await _projects.ApplyRebaseAsync(result.RebasedChanges, result.NewFingerprintHash, ct).ConfigureAwait(false);
            var reportPath = Path.Combine(project.Directory, "reports", "migration-rebase.md");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, result.ReportMarkdown, ct).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public string ExportDocsMarkdown()
    {
        _gate.Wait();
        try
        {
            var project = _projects.Current ?? throw new InvalidOperationException("No project.");
            var snap = _snapshots.Current;
            var md = $"""
                # AoMR Mod Editor export
                - Project: {project.Name}
                - Entities: {Catalog.All.Count}
                - Fingerprint: {snap?.Fingerprint.AggregateHash}
                - Changes: {_projects.GetChanges().Count}
                """;
            var path = Path.Combine(project.Directory, "reports", "export-docs.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, md);
            return path;
        }
        finally
        {
            _gate.Release();
        }
    }

    public SourceNode GetModifiedNode(string entityKey)
    {
        _gate.Wait();
        try
        {
            return GetModifiedNodeUnlocked(entityKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    public EntityDetail GetEffectiveDetail(string entityKey)
    {
        _gate.Wait();
        try
        {
            var baseDetail = Catalog.GetByInternalKey(entityKey) ?? throw new KeyNotFoundException(entityKey);
            var modified = GetModifiedNodeUnlocked(entityKey);
            var original = Catalog.GetSourceNode(entityKey)!;
            var fields = ExtractFields(modified);
            var baseFields = ExtractFields(original);
            var provenance = new Dictionary<string, FieldProvenance>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in fields)
            {
                var baseVal = baseFields.GetValueOrDefault(key);
                var isOverride = !string.Equals(baseVal, value, StringComparison.Ordinal);
                provenance[key] = new FieldProvenance(
                    isOverride ? ProvenanceKind.Override : ProvenanceKind.Base,
                    modified.SourceFile,
                    key,
                    baseVal,
                    isOverride ? value : null,
                    value);
            }

            return new EntityDetail(baseDetail.Summary, modified.Xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting), provenance, fields);
        }
        finally
        {
            _gate.Release();
        }
    }

    private SourceNode GetModifiedNodeUnlocked(string entityKey)
    {
        var original = Catalog.GetSourceNode(entityKey) ?? throw new KeyNotFoundException(entityKey);
        var ops = _projects.GetChanges().Where(c => c.EntityKey.Equals(entityKey, StringComparison.OrdinalIgnoreCase));
        return _applier.Apply(original, ops);
    }

    private Dictionary<string, SourceNode> BuildModifiedMapUnlocked()
    {
        var map = new Dictionary<string, SourceNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Catalog.All)
            map[e.InternalKey] = GetModifiedNodeUnlocked(e.InternalKey);
        return map;
    }

    private void EnsureFingerprint()
    {
        var snap = _snapshots.Current ?? throw new InvalidOperationException("No source.");
        _projects.EnsureSourceFingerprintMatches(snap.Fingerprint.AggregateHash);
    }

    private static bool AttributesMatch(System.Xml.Linq.XElement e, IReadOnlyDictionary<string, string>? attrs)
    {
        if (attrs is null || attrs.Count == 0) return true;
        foreach (var (k, v) in attrs)
        {
            var a = e.Attribute(k);
            if (a is null || !string.Equals(a.Value, v, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static System.Xml.Linq.XElement? FindElement(SourceNode node, string name, IReadOnlyDictionary<string, string>? attrs)
        => node.Xml.Elements().FirstOrDefault(e =>
            e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase) && AttributesMatch(e, attrs));

    private static Dictionary<string, string?> ExtractFields(SourceNode node)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in node.Xml.Elements())
        {
            if (child.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!child.HasAttributes)
                dict[child.Name.LocalName] = child.Value;
            else
            {
                var attrKey = string.Join(',', child.Attributes().Select(a => $"{a.Name.LocalName}={a.Value}"));
                dict[$"{child.Name.LocalName}[{attrKey}]"] = child.Value;
            }
        }

        return dict;
    }
}
