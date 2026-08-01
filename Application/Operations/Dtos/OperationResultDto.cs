using Domain.Enums;
using System.Text.Json;

namespace Application.Operations.Dtos
{
    /// <summary>
    /// What <c>confirm</c> returns once the operation has run.
    /// <para>
    /// <see cref="Result"/> is heterogeneous by construction — one endpoint returns every
    /// operation's result — so NSwag types it as <c>any</c> in
    /// <c>WebApi/ApiClient/web-api-client.ts</c>. That is the unavoidable price of this shape.
    /// A client that wants a typed result should ignore it and re-fetch the affected resource.
    /// </para>
    /// </summary>
    public class OperationResultDto
    {
        public Guid OperationId { get; set; }

        public string? OperationType { get; set; }

        public PendingOperationStatus Status { get; set; }

        /// <summary>
        /// The handler's return value, or null for a void command. Held as a
        /// <see cref="JsonElement"/> rather than <c>object</c> so a replayed result -- which comes
        /// back off the row as text -- and a freshly executed one serialize identically.
        /// </summary>
        public JsonElement? Result { get; set; }
    }
}
