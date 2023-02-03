using Application.Common.Interfaces;
using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Net;

namespace LoanApi.Middlwares
{
    public class UnhandledExceptionHandlerMiddlware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LoggingMiddleware> _logger;
        private readonly IDateTime _dateTimeService;
        private readonly ICurrentUserService _currentUserService;

        public UnhandledExceptionHandlerMiddlware(RequestDelegate next, ICurrentUserService currentUserService, IDateTime dateTimeService, ILogger<LoggingMiddleware> logger)
        {
            _next = next;
            _currentUserService = currentUserService;
            _dateTimeService = dateTimeService;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LoanOrigination Request: Unhandled Exception for Request {Name}", context.Request?.Path.Value);
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";
                var result = new ProblemDetails { 
                    Status = (int)HttpStatusCode.InternalServerError, 
                    Title = "Internal Server Error",
                    Type = "https://www.rfc-editor.org/rfc/rfc7231#section-6.6.1",
                    Detail = "Something went wrong!"
                };
                var dtoJson = JsonConvert.SerializeObject(result);
                await context.Response.WriteAsync(dtoJson);
            }
        }
    }
}
