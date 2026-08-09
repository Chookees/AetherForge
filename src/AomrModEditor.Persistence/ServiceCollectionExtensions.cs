using AomrModEditor.Application.Abstractions;
using AomrModEditor.Application.Services;
using AomrModEditor.GameData;
using AomrModEditor.ModGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AomrModEditor.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAomrModEditorCore(this IServiceCollection services)
    {
        services.AddSingleton<ISecureXmlLoader, SecureXmlLoader>();
        services.AddSingleton<IGameArchiveReader, NullGameArchiveReader>();
        services.AddSingleton<IPathSafety, PathSafety>();
        services.AddSingleton<IInstallationDiscovery, InstallationDiscovery>();
        services.AddSingleton<GameplayImporter>();
        services.AddSingleton<SourceSnapshotStore>();
        services.AddSingleton<ISourceSnapshotStore>(sp => sp.GetRequiredService<SourceSnapshotStore>());
        services.AddSingleton<IProjectStore, FileProjectStore>();
        services.AddSingleton<IChangeApplier, ChangeApplier>();
        services.AddSingleton<IDiffService, DiffService>();
        services.AddSingleton<IModGenerator, AdditiveModGenerator>();
        services.AddSingleton<IValidator, ProjectValidator>();
        services.AddSingleton<ILocalModInstaller, LocalModInstaller>();
        services.AddSingleton<IOperationRunner, OperationRunner>();
        services.AddSingleton<IEffectiveStatEngine, EffectiveStatEngine>();
        services.AddSingleton<IMigrationService, MigrationService>();
        services.AddSingleton<WorkspaceService>();
        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "aomr-app-meta.db")}"),
            contextLifetime: ServiceLifetime.Singleton,
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddSingleton<ProjectMetadataStore>();
        return services;
    }
}
