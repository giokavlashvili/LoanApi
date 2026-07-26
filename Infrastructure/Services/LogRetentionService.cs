using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services
{
    /// <summary>
    /// Deletes Logs rows past the retention window. Logging every request/response makes this
    /// table the fastest growing one in the database, so the purge is part of the feature
    /// rather than an afterthought left to a DBA.
    /// </summary>
    public class LogRetentionService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LogRetentionService> _logger;
        private readonly LogRetentionOptions _options;

        public LogRetentionService(
            IServiceScopeFactory scopeFactory,
            ILogger<LogRetentionService> logger,
            IOptions<LogRetentionOptions> options)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled || _options.RetentionDays <= 0)
                return;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PurgeAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // A failed purge must never take the host down; it retries next interval.
                    _logger.LogError(ex, "Log retention purge failed");
                }

                try
                {
                    await Task.Delay(_options.Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task PurgeAsync(CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var total = 0;
            int deleted;

            do
            {
                // Batched so each statement is short lived and uses IX_Logs_When.
                deleted = await context.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE TOP ({_options.BatchSize}) FROM Logs WHERE [When] < {cutoff}",
                    cancellationToken);

                total += deleted;
            }
            while (deleted == _options.BatchSize && !cancellationToken.IsCancellationRequested);

            if (total > 0)
            {
                _logger.LogInformation(
                    "Log retention removed {DeletedRows} rows older than {RetentionDays} days",
                    total, _options.RetentionDays);
            }
        }
    }
}
