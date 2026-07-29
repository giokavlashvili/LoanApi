using Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace WebUI.Filters
{
    public class ApiExceptionFilterAttribute : ExceptionFilterAttribute
    {
        private readonly IDictionary<Type, Action<ExceptionContext>> _exceptionHandlers;
        private readonly IStringLocalizer _stringLocalizer;
        private readonly ILogger<ApiExceptionFilterAttribute> _logger;

        public ApiExceptionFilterAttribute(IStringLocalizer stringLocalizer, ILogger<ApiExceptionFilterAttribute> logger)
        {
            _stringLocalizer = stringLocalizer;
            _logger = logger;

            // Register known exception types and handlers.
            _exceptionHandlers = new Dictionary<Type, Action<ExceptionContext>>
            {
                { typeof(ValidationException), HandleValidationException },
                { typeof(NotFoundException), HandleNotFoundException },
                { typeof(UnauthorizedAccessException), HandleUnauthorizedAccessException },
                { typeof(ForbiddenAccessException), HandleForbiddenAccessException },
                { typeof(DomainValidationExceptionWrapper), HandleDomainValidationException },
                { typeof(OtpRequiredException), HandleOtpRequiredException },
                { typeof(DbUpdateConcurrencyException), HandleDbUpdateConcurrencyException },
            };
        }

        public override void OnException(ExceptionContext context)
        {
            HandleException(context);

            base.OnException(context);
        }

        private void HandleException(ExceptionContext context)
        {
            Type type = context.Exception.GetType();
            if (_exceptionHandlers.ContainsKey(type))
            {
                // Without this a mapped 400/403/404 left no server side trace at all, so a
                // caller reporting "I keep getting 400" could not be investigated.
                LogHandledException(context, type);

                _exceptionHandlers[type].Invoke(context);
                return;
            }

            if (!context.ModelState.IsValid)
            {
                LogHandledException(context, typeof(ValidationException));

                HandleInvalidModelStateException(context);
                return;
            }
        }

        private void LogHandledException(ExceptionContext context, Type exceptionType)
        {
            // Authentication and authorization failures are security relevant, so they are
            // raised to Warning; routine input validation stays at Information to keep the
            // Logs table from filling with expected client mistakes.
            var level = exceptionType == typeof(UnauthorizedAccessException)
                        || exceptionType == typeof(ForbiddenAccessException)
                ? LogLevel.Warning
                : LogLevel.Information;

            _logger.Log(
                level,
                context.Exception,
                "Handled {ExceptionType} for {Method} {Path}",
                exceptionType.Name,
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path.Value);
        }

        private void HandleValidationException(ExceptionContext context)
        {
            var exception = (ValidationException)context.Exception;

            var details = new ValidationProblemDetails(exception.Errors)
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
            }; 

            context.Result = new BadRequestObjectResult(details);

            context.ExceptionHandled = true;
        }

        private void HandleInvalidModelStateException(ExceptionContext context)
        {
            var details = new ValidationProblemDetails(context.ModelState)
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
            };

            context.Result = new BadRequestObjectResult(details);

            context.ExceptionHandled = true;
        }

        private void HandleNotFoundException(ExceptionContext context)
        {
            var exception = (NotFoundException)context.Exception;

            var details = new ProblemDetails()
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                Title = "The specified resource was not found.",
                Detail = exception.Message,
            };

            context.Result = new NotFoundObjectResult(details);

            context.ExceptionHandled = true;
        }

        private void HandleUnauthorizedAccessException(ExceptionContext context)
        {
            var details = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Type = "https://tools.ietf.org/html/rfc7235#section-3.1"
            };

            context.Result = new ObjectResult(details)
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };

            context.ExceptionHandled = true;
        }

        private void HandleForbiddenAccessException(ExceptionContext context)
        {
            var details = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3"
            };

            context.Result = new ObjectResult(details)
            {
                StatusCode = StatusCodes.Status403Forbidden
            };

            context.ExceptionHandled = true;
        }

        /// <summary>
        /// 428, not 401: the caller is who they say they are, the request is simply missing a
        /// precondition — a confirmation code — and re-sending it with one will succeed. A 401
        /// would tell clients to re-authenticate, which is the wrong recovery.
        /// </summary>
        private void HandleOtpRequiredException(ExceptionContext context)
        {
            var exception = (OtpRequiredException)context.Exception;

            var details = new ProblemDetails()
            {
                Status = StatusCodes.Status428PreconditionRequired,
                Type = "https://tools.ietf.org/html/rfc6585#section-3",
                Title = "Verification code required",
                Detail = _stringLocalizer.GetString(exception.Message)
            };

            // On Extensions rather than in the body so the shape stays a plain ProblemDetails.
            details.Extensions["challengeId"] = exception.Challenge.ChallengeId;
            details.Extensions["expiresAt"] = exception.Challenge.ExpiresAt;
            details.Extensions["recipient"] = exception.Challenge.Recipient;
            details.Extensions["maxAttempts"] = exception.Challenge.MaxAttempts;

            context.Result = new ObjectResult(details)
            {
                StatusCode = StatusCodes.Status428PreconditionRequired
            };

            context.ExceptionHandled = true;
        }

        private void HandleDbUpdateConcurrencyException(ExceptionContext context)
        {
            var details = new ProblemDetails()
            {
                Status = StatusCodes.Status409Conflict,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                Title = "Concurrency conflict",
                Detail = _stringLocalizer.GetString("ConcurrencyConflict")
            };

            context.Result = new ObjectResult(details)
            {
                StatusCode = StatusCodes.Status409Conflict
            };

            context.ExceptionHandled = true;
        }

        private void HandleDomainValidationException(ExceptionContext context)
        {
            var exception = (DomainValidationExceptionWrapper)context.Exception;

            var details = new ValidationProblemDetails()
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "Domain validation error ocured",
                Detail = _stringLocalizer.GetString(exception.Message)
            };

            context.Result = new BadRequestObjectResult(details);

            context.ExceptionHandled = true;
        }
    }
}