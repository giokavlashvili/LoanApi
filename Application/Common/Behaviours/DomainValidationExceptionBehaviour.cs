using Application.Common.Exceptions;
using Domain.Exceptions;
using MediatR;

namespace Application.Common.Behaviours
{
    public class DomainValidationExceptionBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
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