using Application.Common.Exceptions;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WebApi.Models;

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
                { typeof(InvalidCredentialsException), HandleInvalidCredentialsException },
                { typeof(ForbiddenAccessException), HandleForbiddenAccessException },
                // The base type, deliberately. HandleException below looks up by *exact* runtime
                // type, so registering a derived marker instead would only map the paths that
                // produce that marker — which used to be the MediatR pipeline and nothing else.
                // A DomainValidationException raised anywhere outside it (an MVC filter, a
                // hosted service reaching Application code) then fell through to a raw 500.
                { typeof(DomainValidationException), HandleDomainValidationException },
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
                        // A failed login is the one input error worth Warning: it is the signal a
                        // credential-stuffing run shows up as, and at Information it would be
                        // indistinguishable from ordinary validation noise.
                        || exceptionType == typeof(InvalidCredentialsException)
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
                // Localized like the OTP and domain-validation handlers: NotFoundException carries
                // a key, not a sentence, so the 404 body is not hardcoded English.
                Detail = _stringLocalizer.GetString(exception.Message),
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

        /// <summary>
        /// 401, where this used to be a 404. A wrong password is not a missing resource, and the
        /// 404 also disclosed which user names exist. The body carries the same localized message
        /// whether the user name was unknown or the password was wrong.
        /// </summary>
        private void HandleInvalidCredentialsException(ExceptionContext context)
        {
            var exception = (InvalidCredentialsException)context.Exception;

            var details = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                Title = "Unauthorized",
                Detail = _stringLocalizer.GetString(exception.Message)
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

            // Declared properties on a ProblemDetails subclass, not Extensions entries. Both
            // serialize to the same JSON — Extensions is [JsonExtensionData], so its keys land flat
            // next to the standard members — but only declared properties reach the OpenAPI
            // document, and these four are precisely what the client needs to answer the challenge.
            var details = new OtpChallengeProblemDetails()
            {
                Status = StatusCodes.Status428PreconditionRequired,
                Type = "https://tools.ietf.org/html/rfc6585#section-3",
                Title = "Verification code required",
                Detail = _stringLocalizer.GetString(exception.Message),
                ChallengeId = exception.Challenge.ChallengeId,
                ExpiresAt = exception.Challenge.ExpiresAt,
                Recipient = exception.Challenge.Recipient,
                MaxAttempts = exception.Challenge.MaxAttempts
            };

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

        /// <summary>
        /// The message is a localization key rather than user text, which is why it goes through
        /// the localizer rather than into <c>Detail</c> directly.
        /// </summary>
        private void HandleDomainValidationException(ExceptionContext context)
        {
            var details = new ValidationProblemDetails()
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "Domain validation error ocured",
                Detail = _stringLocalizer.GetString(context.Exception.Message)
            };

            context.Result = new BadRequestObjectResult(details);

            context.ExceptionHandled = true;
        }
    }
}