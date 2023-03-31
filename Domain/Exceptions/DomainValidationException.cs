using System.ComponentModel.DataAnnotations;

namespace Domain.Exceptions
{
    public class DomainValidationException : Exception
    {
        public DomainValidationException()
            : base()
        {
        }

        public DomainValidationException(string message)
            : base(message)
        {
        }

        public DomainValidationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public DomainValidationException(string message, string errorCode)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public DomainValidationException(string message, string errorCode, Exception innerException)
           : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        public DomainValidationException(string message, string errorCode, string attemptedValue)
            : base(message)
        {
            ErrorCode = errorCode;
            AttemptedValue = attemptedValue;
        }
        public DomainValidationException(string message, string errorCode, string attemptedValue, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            AttemptedValue = attemptedValue;
        }


        /// <summary>
        /// Gets or sets the error code.
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// The property value that caused the failure.
        /// </summary>
        public object? AttemptedValue { get; set; }
    }
}
