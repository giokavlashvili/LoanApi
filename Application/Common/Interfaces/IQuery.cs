using MediatR;

namespace Application.Common.Interfaces;

/// <summary>
/// Marker interface for query requests. Queries are read-only operations
/// and should not create database transactions.
/// </summary>
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}



