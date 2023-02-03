using MediatR;

namespace Application.Common.Behaviours
{
    public interface IValidationBehaviour<TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
    }
}