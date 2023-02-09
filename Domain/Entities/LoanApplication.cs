using Domain.Common;
using Domain.Common.Models;
using Domain.Enums;
using Domain.Events;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    public class LoanApplication : BaseAuditableEntity
    {
        public int LoanTypeId { get; private set; }
        public decimal Amount { get; private set; }
        public int CurrencyId { get; private set; }
        public int PeriodPerMonth { get; private set; }
        public LoanStatus Status { get; private set; }

        [ForeignKey(nameof(LoanTypeId))]
        public LoanType? LoanType { get; private set; }

        [ForeignKey(nameof(CurrencyId))]
        public Currency? Currency { get; private set; }

        [NotMapped]
        public User? CreatedByUser { get; set; }

        public static LoanApplication Create(
            int loanTypeId,
            decimal amount,
            int currencyId,
            int periodPerMonth,
            string createdById,
            DateTime created
            )
        {
            var entity = new LoanApplication()
            {
                LoanTypeId = loanTypeId,
                Amount = amount,
                CurrencyId = currencyId,
                PeriodPerMonth = periodPerMonth,
                CreatedBy = createdById,
                Status = LoanStatus.Sent,
                Created = created
            };

            entity.AddDomainEvent(new ApplicationCreatedEvent(entity));

            return entity;
        }

        public void Update(
            int loanTypeId,
            decimal amount,
            int currencyId,
            int periodPerMonth,
            string lastModifiedBy,
            DateTime lastModified
            )
        {
            LoanTypeId = loanTypeId;
            Amount = amount;
            CurrencyId = currencyId;
            PeriodPerMonth = periodPerMonth;
            LastModifiedBy = lastModifiedBy;
            LastModified = lastModified;

            this.AddDomainEvent(new ApplicationUpdatedEvent(this));
        }

        public void UpdateStatus(
            LoanStatus newStatus,
            string lastModifiedBy,
            DateTime lastModified
            )
        {
            this.Status = newStatus;
            this.LastModifiedBy = lastModifiedBy;
            this.LastModified = lastModified;
            this.AddDomainEvent(new ApplicationStatusChangedEvent(this));
        }
    }
}
