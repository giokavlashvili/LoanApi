using Application.Common.Exceptions;
using FluentValidation.Results;
using NUnit.Framework;

namespace LoanOrigination.Application.UnitTests.Common.Exceptions
{
    public class ValidationExceptionTests
    {
        [Test]
        public void Validation_WithDefaultConstructor_CreatesAnEmptyErrorDictionary()
        {
            var actual = new ValidationException().Errors;

            Assert.That(actual, Is.EquivalentTo(Array.Empty<string>()));
        }

        [Test]
        public void Validation_WithOneFailure_CreatesASingleElementErrorDictionary()
        {
            var failures = new List<ValidationFailure>
            {
                new ValidationFailure("Age", "must be over 18"),
            };

            var actual = new ValidationException(failures).Errors;

            Assert.That(actual.Keys, Is.EquivalentTo(new string[] { "Age" }));
            Assert.That(actual["Age"], Is.EquivalentTo(new string[] { "must be over 18" }));
        }

        [Test]
        public void Validation_WithMulitpleValidationFailureForMultipleProperties_CreatesAMultipleElementErrorDictionaryEachWithMultipleValues()
        {
            var failures = new List<ValidationFailure>
            {
                new ValidationFailure("Age", "must be 18 or older"),
                new ValidationFailure("Age", "must be 25 or younger"),
                new ValidationFailure("Password", "must contain at least 8 characters"),
                new ValidationFailure("Password", "must contain a digit"),
                new ValidationFailure("Password", "must contain upper case letter"),
                new ValidationFailure("Password", "must contain lower case letter"),
            };

            var actual = new ValidationException(failures).Errors;

            Assert.That(actual.Keys, Is.EquivalentTo(new string[] { "Password", "Age" }));

            Assert.That(actual["Age"], Is.EquivalentTo(new string[]
            {
                "must be 25 or younger",
                "must be 18 or older",
            }));

            Assert.That(actual["Password"], Is.EquivalentTo(new string[]
            {
                "must contain lower case letter",
                "must contain upper case letter",
                "must contain at least 8 characters",
                "must contain a digit"
            }));
        }
    }
}