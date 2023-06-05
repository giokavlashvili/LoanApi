using Application.Common.Interfaces;

namespace LoanApi.Middlwares
{
    public class LoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LoggingMiddleware> _logger;
        private readonly IDateTime _dateTimeService;
        private readonly ICurrentUserService _currentUserService;

        public LoggingMiddleware(ILogger<LoggingMiddleware> logger, RequestDelegate next, IDateTime dateTimeService, ICurrentUserService currentUserService)
        {
            _logger = logger;
            _next = next;
            _dateTimeService = dateTimeService;
            _currentUserService = currentUserService;
        }

        public async Task Invoke(HttpContext context)
        {
            // Log request
            _logger.LogInformation("Request {method} {url} {time} User:{UserName}", context.Request?.Method, context.Request?.Path.Value, _dateTimeService.Now, _currentUserService.Name);
            await _next(context);
            // Log response
            _logger.LogInformation("Response {statusCode} {time} User:{UserName}", context.Response?.StatusCode, _dateTimeService.Now, _currentUserService.Name);
        }
    }
}
