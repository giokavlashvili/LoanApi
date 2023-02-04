using Domain.Common;
using Domain.Common.Models;
using Domain.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            return new LoanApplication()
            {
                LoanTypeId = loanTypeId,
                Amount = amount,
                CurrencyId = currencyId,
                PeriodPerMonth = periodPerMonth,
                CreatedBy = createdById,
                Status = LoanStatus.Sent,
                Created = created
            };
        }
    }
}
