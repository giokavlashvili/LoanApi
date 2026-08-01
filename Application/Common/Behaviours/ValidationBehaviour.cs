using FluentValidation;
using MediatR;
using ValidationException = Application.Common.Exceptions.ValidationException;

namespace Application.Common.Behaviors
{
    // The constraint is "notnull", matching IPipelineBehavior itself, and not
    // "TRequest : IRequest<TResponse>". MediatR.Contracts 2.x made IRequest and IRequest<T>
    // unrelated interfaces, so a void command (: IRequest) is not an IRequest<Unit>. MediatR
    // still resolves IPipelineBehavior<TCommand, Unit> for it, the tighter constraint could not
    // be satisfied, and the DI container silently skipped the registration rather than failing —
    // which left every void command with no validation at all, so invalid input reached the
    // handler and was only caught by whatever the domain happened to assert.
    public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;

        public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            _validators = validators;
        }

        //Validate request data
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (_validators.Any())
            {
                var context = new ValidationContext<TRequest>(request);

                var validationResults = await Task.WhenAll(
                    _validators.Select(v =>
                        v.ValidateAsync(context, cancellationToken)));

                var failures = validationResults
                    .Where(r => r.Errors.Any())
                    .SelectMany(r => r.Errors)
                    .ToList();

                if (failures.Any())
                    throw new ValidationException(failures);
            }

            // No try/catch around this. A DomainValidationException used to be rethrown here as a
            // DomainValidationExceptionWrapper purely so the exception filter's exact-type lookup
            // could find it; the filter now maps the base type, so the marker had nothing left to
            // do and any path that skipped this behaviour no longer loses its 400.
            return await next();
        }
    }
}