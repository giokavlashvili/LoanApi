using Application.Common.Interfaces;
using Application.Common.Logging;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.Common.Behaviors
{
    public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        private const int LongRunningThresholdMs = 500;

        private readonly ILogger<TRequest> _logger;
        private readonly ICurrentUserService _currentUserService;

        public PerformanceBehavior(
            ILogger<TRequest> logger,
            ICurrentUserService currentUserService)
        {
            _logger = logger;
            _currentUserService = currentUserService;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();

            var response = await next();

            timer.Stop();

            var elapsedMilliseconds = timer.ElapsedMilliseconds;

            // Log long running actions
            if (elapsedMilliseconds > LongRunningThresholdMs)
            {
                // Redacted: LoginCommand/RegisterUserCommand carry passwords, and authentication
                // handlers are exactly the ones that cross the threshold (password hashing is
                // deliberately slow), so the raw request must never be handed to a sink.
                var payload = LogRedactor.RedactObject(request);

                _logger.LogWarning(
                    "Long running request {RequestName} took {DurationMs} ms for user {UserId} — {RequestBody}",
                    typeof(TRequest).Name,
                    elapsedMilliseconds,
                    _currentUserService.UserId ?? string.Empty,
                    payload);
            }

            return response;
        }
    }
}
