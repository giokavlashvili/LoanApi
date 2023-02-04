using Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class LoanType : BaseEntity
    {
        public string? Name { get; private set; }

        public static LoanType Create(string name)
        {
            return new LoanType()
            {
                Name = name
            };
        }
    }
}
