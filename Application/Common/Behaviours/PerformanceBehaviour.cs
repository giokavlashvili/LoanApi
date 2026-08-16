using Application.Common.Interfaces;
using Application.Common.Logging;
using Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Application.Common.Behaviors
{
    // "notnull" rather than "TRequest : IRequest<TResponse>" — see ValidationBehavior for why
    // the tighter constraint silently excluded every void command.
    public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        private readonly ILogger<TRequest> _logger;
        private readonly ICurrentUserService _currentUserService;
        private readonly IOptionsMonitor<PerformanceLoggingOptions> _optionsMonitor;

        public PerformanceBehavior(
            ILogger<TRequest> logger,
            ICurrentUserService currentUserService,
            IOptionsMonitor<PerformanceLoggingOptions> optionsMonitor)
        {
            _logger = logger;
            _currentUserService = currentUserService;
            _optionsMonitor = optionsMonitor;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();

            var response = await next();

            timer.Stop();

            var elapsedMilliseconds = timer.ElapsedMilliseconds;
            var threshold = _optionsMonitor.CurrentValue.LongRunningThresholdMs;

            // Log long running actions
            if (elapsedMilliseconds > threshold)
            {
                // Redacted: LoginCommand/RegisterUserCommand carry passwords, and authentication
                // handlers are exactly the ones that cross the threshold (password hashing is
                // deliberately slow), so the raw request must never be handed to a sink.
                var payload = LogRedactor.RedactObject(request);

                _logger.LogWarning(
                    "Long running request {RequestName} took {DurationMs} ms for user {UserId} — {RequestBody}",
                    typeof(TRequest).Name,
                    elapsedMilliseconds,
                    LogColumnLimits.Truncate(_currentUserService.UserId, LogColumnLimits.UserId) ?? string.Empty,
                    payload);
            }

            return response;
        }
    }
}
