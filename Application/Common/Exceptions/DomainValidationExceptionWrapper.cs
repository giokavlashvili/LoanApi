using Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Exceptions
{
    public class DomainValidationExceptionWrapper : DomainValidationException
    {
        public DomainValidationExceptionWrapper()
            : base()
        {
        }

        public DomainValidationExceptionWrapper(string message)
            : base(message)
        {
        }

        public DomainValidationExceptionWrapper(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public DomainValidationExceptionWrapper(string name, object key)
            : base($"Entity \"{name}\" ({key}) was not found.")
        {
        }
    }
}
