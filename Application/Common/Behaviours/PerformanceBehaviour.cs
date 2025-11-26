using Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Application.Common.Behaviors
{
    public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        private readonly Stopwatch _timer;
        private readonly ILogger<TRequest> _logger;
        private readonly ICurrentUserService _currentUserService;
        private readonly IIdentityService _identityService;
        private static readonly string[] SensitivePropertyNames = { "Password", "ConfirmPassword", "PersonalNumber", "Token", "Secret", "ApiKey" };

        public PerformanceBehavior(
            ILogger<TRequest> logger,
            ICurrentUserService currentUserService,
            IIdentityService identityService)
        {
            _timer = new Stopwatch();

            _logger = logger;
            _currentUserService = currentUserService;
            _identityService = identityService;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            _timer.Start();

            var response = await next();

            _timer.Stop();

            var elapsedMilliseconds = _timer.ElapsedMilliseconds;

            // Log long running actions
            if (elapsedMilliseconds > 10000)
            {
                var requestName = typeof(TRequest).Name;
                var userId = _currentUserService.UserId ?? string.Empty;
                var userName = string.Empty;

                if (!string.IsNullOrEmpty(userId))
                {
                    userName = await _identityService.GetUserNameAsync(userId);
                }

                // Sanitize request to remove sensitive data
                var sanitizedRequest = SanitizeRequest(request);

                // CorrelationId will be automatically included via LogContext from CorrelationIdMiddleware
                _logger.LogWarning("Api Long Running Request: {Name} ({ElapsedMilliseconds} milliseconds) UserId:{UserId} UserName:{UserName} Request:{Request}",
                    requestName, elapsedMilliseconds, userId, userName, sanitizedRequest);
            }

            return response;
        }

        private static object? SanitizeRequest(TRequest request)
        {
            if (request == null)
                return null;

            try
            {
                // Use reflection to create a sanitized copy
                var requestType = request.GetType();
                var sanitized = Activator.CreateInstance(requestType);

                if (sanitized == null)
                    return null;

                var properties = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var property in properties)
                {
                    if (property.CanRead && property.CanWrite)
                    {
                        var value = property.GetValue(request);
                        
                        // Check if property name contains sensitive data
                        if (SensitivePropertyNames.Any(sensitive => 
                            property.Name.Contains(sensitive, StringComparison.OrdinalIgnoreCase)))
                        {
                            property.SetValue(sanitized, "***REDACTED***");
                        }
                        else
                        {
                            property.SetValue(sanitized, value);
                        }
                    }
                }

                return sanitized;
            }
            catch
            {
                // If sanitization fails, return request type name only
                return new { Type = typeof(TRequest).Name, Message = "Request data sanitization failed" };
            }
        }
    }
}