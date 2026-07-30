using Application.Common.Interfaces;
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
        private readonly IOptionsMonitor<LogRetentionOptions> _optionsMonitor;
        private readonly IDateTime _dateTime;

        public LogRetentionService(
            IServiceScopeFactory scopeFactory,
            ILogger<LogRetentionService> logger,
            IOptionsMonitor<LogRetentionOptions> optionsMonitor,
            IDateTime dateTime)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _optionsMonitor = optionsMonitor;
            _dateTime = dateTime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Enabled/RetentionDays are re-read every iteration rather than checked once up
            // front, so flipping LogRetention:Enabled back on later actually resumes purging
            // instead of leaving this task completed forever from an early return.
            while (!stoppingToken.IsCancellationRequested)
            {
                var options = _optionsMonitor.CurrentValue;

                if (options.Enabled && options.RetentionDays > 0)
                {
                    try
                    {
                        await PurgeAsync(options, stoppingToken);
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
                }

                try
                {
                    await Task.Delay(options.Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task PurgeAsync(LogRetentionOptions options, CancellationToken cancellationToken)
        {
            var cutoff = _dateTime.UtcNow.AddDays(-options.RetentionDays);

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var isPostgres = context.Database.IsNpgsql();

            var total = 0;
            int deleted;

            do
            {
                // Batched so each statement is short lived and uses IX_Logs_When.
                //
                // The two dialects have no shared spelling for a bounded delete: PostgreSQL has no
                // DELETE ... LIMIT, so the bound has to be applied by a subquery selecting the keys
                // first. Both forms parameterise the batch size and the cutoff — string
                // interpolation into ExecuteSqlInterpolatedAsync produces parameters, not literals.
                deleted = isPostgres
                    ? await context.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        DELETE FROM "Logs" WHERE "Id" IN (
                            SELECT "Id" FROM "Logs" WHERE "When" < {cutoff} LIMIT {options.BatchSize}
                        )
                        """,
                        cancellationToken)
                    : await context.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE TOP ({options.BatchSize}) FROM Logs WHERE [When] < {cutoff}",
                        cancellationToken);

                total += deleted;
            }
            while (deleted == options.BatchSize && !cancellationToken.IsCancellationRequested);

            if (total > 0)
            {
                _logger.LogInformation(
                    "Log retention removed {DeletedRows} rows older than {RetentionDays} days",
                    total, options.RetentionDays);
            }
        }
    }
}
