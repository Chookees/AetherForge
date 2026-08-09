using System.Text.Json;
using System.Text.Json.Serialization;
using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;
using AomrModEditor.GameData;

namespace AomrModEditor.Persistence;

public sealed class SourceSnapshotStore : ISourceSnapshotStore
{
    private readonly GameplayImporter _importer;
    private SourceSnapshot? _current;
    private IEntityCatalog? _catalog;

    public SourceSnapshotStore(GameplayImporter importer) => _importer = importer;

    public SourceSnapshot? Current => _current;
    public IEntityCatalog? Catalog => _catalog;

    public Task<SourceSnapshot> ImportDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (memory, fingerprint) = _importer.ImportDirectory(directory);
        var catalog = SqliteFtsEntityCatalog.From(memory, ":memory:");
        var snap = new SourceSnapshot(Guid.NewGuid(), Path.GetFullPath(directory), fingerprint, DateTimeOffset.UtcNow, catalog.All.Count);
        _catalog = catalog;
        _current = snap;
        return Task.FromResult(snap);
    }

    public Task<SourceSnapshot> ImportPackAsync(Stream gzipPackStream, string rootLabel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var doc = GameplayPackBuilder.ReadGzip(gzipPackStream);
        var (memory, fingerprint) = GameplayPackBuilder.ToCatalog(doc);
        var catalog = SqliteFtsEntityCatalog.From(memory, ":memory:");
        var snap = new SourceSnapshot(Guid.NewGuid(), rootLabel, fingerprint, DateTimeOffset.UtcNow, catalog.All.Count);
        _catalog = catalog;
        _current = snap;
        return Task.FromResult(snap);
    }

    public void ReplaceCatalog(IEntityCatalog catalog, SourceSnapshot snapshot)
    {
        _catalog = catalog;
        _current = snapshot;
    }
}

public sealed class FileProjectStore : IProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(), new ChangeOpJsonConverter() }
    };

    private ProjectInfo? _current;
    private readonly List<ChangeOp> _changes = new();
    private readonly Stack<ChangeOp> _redo = new();
    private string? _projectFile;

    public ProjectInfo? Current => _current;

    public async Task<ProjectInfo> CreateAsync(string name, string directory, SourceSnapshot snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "changes"));
        Directory.CreateDirectory(Path.Combine(directory, "generated"));
        Directory.CreateDirectory(Path.Combine(directory, "reports"));
        Directory.CreateDirectory(Path.Combine(directory, "backups"));

        _current = new ProjectInfo(Guid.NewGuid(), name, Path.GetFullPath(directory), snapshot.Id, snapshot.Fingerprint.AggregateHash, 1);
        _projectFile = Path.Combine(directory, "aomrmod.project.json");
        _changes.Clear();
        _redo.Clear();
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return _current;
    }

    public async Task AppendChangeAsync(ChangeOp op, CancellationToken cancellationToken)
    {
        EnsureProject();
        _changes.Add(op);
        _redo.Clear();
        _current = _current! with { Version = _current.Version + 1 };
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<ChangeOp> GetChanges() => _changes.ToList();

    public async Task UndoAsync(CancellationToken cancellationToken)
    {
        EnsureProject();
        if (_changes.Count == 0) return;
        var last = _changes[^1];
        _changes.RemoveAt(_changes.Count - 1);
        _redo.Push(last);
        _current = _current! with { Version = _current.Version + 1 };
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RedoAsync(CancellationToken cancellationToken)
    {
        EnsureProject();
        if (_redo.Count == 0) return;
        var op = _redo.Pop();
        _changes.Add(op);
        _current = _current! with { Version = _current.Version + 1 };
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public void EnsureSourceFingerprintMatches(string currentAggregateHash)
    {
        EnsureProject();
        if (!string.Equals(_current!.SourceFingerprintHash, currentAggregateHash, StringComparison.OrdinalIgnoreCase))
            throw new SourceFingerprintMismatchException(_current.SourceFingerprintHash, currentAggregateHash);
    }

    public async Task ApplyRebaseAsync(IReadOnlyList<ChangeOp> rebasedChanges, string newFingerprintHash, CancellationToken cancellationToken)
    {
        EnsureProject();
        _changes.Clear();
        _changes.AddRange(rebasedChanges);
        _redo.Clear();
        _current = _current! with
        {
            SourceFingerprintHash = newFingerprintHash,
            Version = _current.Version + 1
        };
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureProject()
    {
        if (_current is null) throw new InvalidOperationException("No project loaded.");
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var dto = new
        {
            id = _current!.Id,
            name = _current.Name,
            sourceSnapshotId = _current.SourceSnapshotId,
            sourceFingerprintHash = _current.SourceFingerprintHash,
            version = _current.Version,
            changes = _changes
        };
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var tmp = _projectFile + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
        File.Move(tmp, _projectFile!, overwrite: true);
        var changesPath = Path.Combine(_current.Directory, "changes", "changelog.json");
        await File.WriteAllTextAsync(changesPath, JsonSerializer.Serialize(_changes, JsonOptions), ct).ConfigureAwait(false);
    }
}

public sealed class SourceFingerprintMismatchException : Exception
{
    public string Expected { get; }
    public string Actual { get; }

    public SourceFingerprintMismatchException(string expected, string actual)
        : base("Source fingerprint changed; migration required before generate/diff.")
    {
        Expected = expected;
        Actual = actual;
    }
}
