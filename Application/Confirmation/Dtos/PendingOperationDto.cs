using Application.Common.Confirmation;
using Domain.Enums;

namespace Application.Confirmation.Dtos
{
    /// <summary>
    /// A parked operation as its initiator or a prospective confirmer sees it. Deliberately
    /// without <c>Payload</c> — the captured command is server state, and echoing it back would
    /// put a command body into every response and every log.
    /// </summary>
    public class PendingOperationDto
    {
        public Guid OperationId { get; set; }

        public string? OperationType { get; set; }

        public ConfirmationPolicy Policy { get; set; }

        public PendingOperationStatus Status { get; set; }

        public string? InitiatedBy { get; set; }

        public DateTime Created { get; set; }

        public DateTime ExpiresAt { get; set; }
    }
}
