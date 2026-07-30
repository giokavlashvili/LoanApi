using Domain.Common;
using Domain.Enums;
using Domain.Events;
using Domain.Exceptions;

namespace Domain.Entities
{
    public class LoanApplication : BaseAuditableEntity, IAggregateRoot
    {
        public int LoanTypeId { get; private set; }
        public decimal Amount { get; private set; }
        public int CurrencyId { get; private set; }
        public int PeriodPerMonth { get; private set; }
        public LoanStatus Status { get; private set; }

        /// <remarks>
        /// The relationship to <see cref="LoanTypeId"/> is declared by Fluent API in
        /// <c>LoanApplicationConfiguration</c>, not by a <c>[ForeignKey]</c> annotation — persistence
        /// mapping is Infrastructure's concern and every other entity is configured that way.
        /// </remarks>
        public LoanType? LoanType { get; private set; }

        /// <inheritdoc cref="LoanType"/>
        public Currency? Currency { get; private set; }

        /// <summary>
        /// Optimistic concurrency token. Two concurrent writers to the same row make the second
        /// SaveChanges throw DbUpdateConcurrencyException instead of silently overwriting.
        /// </summary>
        public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

        /// <remarks>
        /// Takes no <c>createdById</c> or <c>created</c>: <c>AuditableEntityInterceptor</c> stamps
        /// both at the save boundary. The <c>InvalidUser</c> invariant that used to be checked here
        /// moved to the command handlers via <c>ICurrentUserService.RequireUserId()</c> — the entity
        /// no longer receives a user id, so it can no longer be the one to insist on it.
        /// </remarks>
        public static LoanApplication Create(
            int loanTypeId,
            decimal amount,
            int currencyId,
            int periodPerMonth)
        {
            if (amount <= 0)
                throw new DomainValidationException("InvalidAmount");

            if (periodPerMonth <= 0)
                throw new DomainValidationException("InvalidPeriod");

            var entity = new LoanApplication()
            {
                LoanTypeId = loanTypeId,
                Amount = amount,
                CurrencyId = currencyId,
                PeriodPerMonth = periodPerMonth,
                Status = LoanStatus.Sent
            };

            entity.AddDomainEvent(new ApplicationCreatedEvent(entity));

            return entity;
        }

        public void Update(
            int loanTypeId,
            decimal amount,
            int currencyId,
            int periodPerMonth)
        {
            if (amount <= 0)
                throw new DomainValidationException("InvalidAmount");

            if (periodPerMonth <= 0)
                throw new DomainValidationException("InvalidPeriod");

            LoanTypeId = loanTypeId;
            Amount = amount;
            CurrencyId = currencyId;
            PeriodPerMonth = periodPerMonth;

            this.AddDomainEvent(new ApplicationUpdatedEvent(this));
        }

        public void UpdateStatus(LoanStatus newStatus)
        {
            if (Status == LoanStatus.Accepted || Status == LoanStatus.Rejected)
                throw new DomainValidationException("ApplicationAlreadyProcessed");

            this.Status = newStatus;
            this.AddDomainEvent(new ApplicationStatusChangedEvent(this));
        }

        /// <summary>
        /// Raises <see cref="ApplicationDeletedEvent"/>. Call this before handing the aggregate to
        /// the repository's <c>Remove</c>.
        /// <para>
        /// The handler used to raise the event itself. That worked only because dispatch happens
        /// before <c>SaveChanges</c> and the entity is still in the change tracker in the
        /// <c>Deleted</c> state — a detail no handler should need to know, and the one event in the
        /// aggregate not raised by the aggregate.
        /// </para>
        /// </summary>
        public void Delete()
        {
            this.AddDomainEvent(new ApplicationDeletedEvent(this));
        }
    }
}
