using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Net;

namespace WebApi.Middlwares
{
    public class UnhandledExceptionHandlerMiddlware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<UnhandledExceptionHandlerMiddlware> _logger;

        public UnhandledExceptionHandlerMiddlware(RequestDelegate next, ILogger<UnhandledExceptionHandlerMiddlware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            // Log unhandled exceptions and modify response to avoid sensitive data exposure
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception for request {Method} {Path}",
                    context.Request?.Method, context.Request?.Path.Value);

                // Once the response has started the status and headers are already on the
                // wire; writing again throws and would bury the original exception.
                if (context.Response.HasStarted)
                    throw;

                context.Response.Clear();
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";

                var result = new ProblemDetails
                {
                    Status = (int)HttpStatusCode.InternalServerError,
                    Title = "Internal Server Error",
                    Type = "https://www.rfc-editor.org/rfc/rfc7231#section-6.6.1",
                    Detail = "Something went wrong! Contact support.",
                    // Lets a caller quote one id that finds every row for this request.
                    Instance = context.TraceIdentifier
                };

                var dtoJson = JsonConvert.SerializeObject(result);
                await context.Response.WriteAsync(dtoJson);
            }
        }
    }
}
