using Application.Common.Exceptions;
using Domain.Exceptions;
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
    // which left every void command with no validation at all, surfacing domain exceptions as
    // raw 500s instead of the localized 400s the exception filter is built to produce.
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

            try
            {
                return await next();
            }
            catch (DomainValidationException ex)
            {
                throw new DomainValidationExceptionWrapper(ex.Message, ex);
            }
        }
    }
}