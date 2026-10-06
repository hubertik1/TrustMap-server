using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TrustMap.Api.Services;
using TrustMap.Domain.Entities;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.IntegrationTests;

public sealed class AccountDeletionOutboxTests
{
    [Fact]
    public async Task PendingMediaJobs_SurviveDatabaseReopen_AndFinishAfterStorageRecovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "trustmap-outbox", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var storage = new RecoverableStorage { Fail = true };
        try
        {
            await using (var provider = Services(root, storage))
            {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
                await db.Database.MigrateAsync();
                db.AccountDeletionJobs.AddRange(new[] { "photo.jpg", "photo-medium.jpg", "photo-thumb.jpg" }
                    .Select(key => new AccountDeletionJob
                    {
                        Kind = AccountDeletionJobKind.Media, Payload = key,
                        CreatedAtUtc = DateTime.UtcNow, NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1)
                    }));
                await db.SaveChangesAsync();
                await scope.ServiceProvider.GetRequiredService<AccountDeletionJobProcessor>().ProcessPendingAsync(default);
                Assert.Equal(3, await db.AccountDeletionJobs.CountAsync());
            }
            // Reopen the persisted SQLite file with a completely new service provider/process state.
            storage.Fail = false;
            await using var restarted = Services(root, storage);
            await using var retryScope = restarted.CreateAsyncScope();
            var retryDb = retryScope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            Assert.Equal(3, await retryDb.AccountDeletionJobs.CountAsync());
            await retryDb.AccountDeletionJobs.ExecuteUpdateAsync(x => x.SetProperty(j => j.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));
            await retryScope.ServiceProvider.GetRequiredService<AccountDeletionJobProcessor>().ProcessPendingAsync(default);
            Assert.Empty(await retryDb.AccountDeletionJobs.ToListAsync());
            Assert.Equal(3, storage.Deleted.Count);
            // Reprocessing completed jobs is harmless.
            await retryScope.ServiceProvider.GetRequiredService<AccountDeletionJobProcessor>().ProcessPendingAsync(default);
            Assert.Equal(3, storage.Deleted.Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ShutdownDuringDeletion_LeavesLeaseAndJobForRetry()
    {
        var root = Path.Combine(Path.GetTempPath(), "trustmap-outbox", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var shutdown = new CancellationTokenSource();
        var storage = new RecoverableStorage { CancelOnDelete = shutdown };
        try
        {
            await using var provider = Services(root, storage);
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
                await db.Database.MigrateAsync();
                db.AccountDeletionJobs.Add(new AccountDeletionJob
                {
                    Kind = AccountDeletionJobKind.Media, Payload = "photo.jpg",
                    CreatedAtUtc = DateTime.UtcNow, NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1)
                });
                await db.SaveChangesAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    scope.ServiceProvider.GetRequiredService<AccountDeletionJobProcessor>().ProcessPendingAsync(shutdown.Token));
            }
            await using var retryScope = provider.CreateAsyncScope();
            var retryDb = retryScope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            var job = await retryDb.AccountDeletionJobs.SingleAsync();
            Assert.Equal(1, job.Attempts);
            Assert.True(job.NextAttemptAtUtc > DateTime.UtcNow);
            storage.CancelOnDelete = null;
            await retryScope.ServiceProvider.GetRequiredService<AccountDeletionJobProcessor>().ProcessPendingAsync(default);
            Assert.Empty(storage.Deleted); // A second worker cannot steal an active lease.
            await retryDb.AccountDeletionJobs.ExecuteUpdateAsync(x => x.SetProperty(j => j.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));
            await retryScope.ServiceProvider.GetRequiredService<AccountDeletionJobProcessor>().ProcessPendingAsync(default);
            Assert.Single(storage.Deleted);
            Assert.Empty(await retryDb.AccountDeletionJobs.ToListAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ServiceProvider Services(string root, RecoverableStorage storage)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<AppleRefreshTokenProtection>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPhotoStorage>(storage);
        services.AddSingleton<IAppleTokenClient, UnusedAppleClient>();
        services.AddDbContext<TrustMapDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(root, "outbox.db")};Pooling=False"));
        services.AddScoped<AccountDeletionJobProcessor>();
        return services.BuildServiceProvider();
    }

    private sealed class UnusedAppleClient : IAppleTokenClient
    {
        public Task<string> ExchangeAsync(string code, string clientId, string subject, string? rawNonce, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeAsync(string refreshToken, string clientId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecoverableStorage : IPhotoStorage
    {
        public bool Fail { get; set; }
        public CancellationTokenSource? CancelOnDelete { get; set; }
        public HashSet<string> Deleted { get; } = new();
        public Task<StoredPhoto> SaveAsync(Stream stream, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredFileResult?> OpenReadAsync(string storageKey, string? contentTypeHint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            CancelOnDelete?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new IOException("Storage is unavailable.");
            Deleted.Add(storageKey);
            return Task.CompletedTask;
        }
    }
}
