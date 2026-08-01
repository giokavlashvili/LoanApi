using Application.Common.Operations;
using Application.Common.Otp;
using Domain.Exceptions;
using MediatR;
using NUnit.Framework;

namespace Application.UnitTests.Common.Operations
{
    [TestFixture]
    public class VerifiableOperationRegistryTests
    {
        [VerifiableOperation("ApproveLoan", RequiredPolicies = ["CanApproveLoans"])]
        public record ApproveLoanOperation : IRequest<bool>;

        /// <summary>A void command — <c>IRequest</c>, not <c>IRequest&lt;T&gt;</c>.</summary>
        [VerifiableOperation("CloseLoan")]
        public record CloseLoanOperation : IRequest;

        [VerifiableOperation("Duplicate")]
        public record FirstDuplicate : IRequest<bool>;

        [VerifiableOperation("Duplicate")]
        public record SecondDuplicate : IRequest<bool>;

        /// <summary>Gated twice — the trap the startup check exists for.</summary>
        [VerifiableOperation("DoubleGated")]
        public record DoubleGatedOperation : IRequest<bool>, IRequireOtpVerification
        {
            public Guid? ChallengeId { get; init; }
            public string? OtpCode { get; init; }
        }

        [VerifiableOperation("NotARequest")]
        public class NotARequestOperation
        {
        }

        [VerifiableOperation("")]
        public record UnnamedOperation : IRequest<bool>;

        [Test]
        public void Build_RegistersAnOperationWithItsAttributeValues()
        {
            var registry = VerifiableOperationRegistry.Build([typeof(ApproveLoanOperation)]);

            var descriptor = registry.Get("ApproveLoan");

            Assert.That(descriptor.PayloadType, Is.EqualTo(typeof(ApproveLoanOperation)));
            Assert.That(descriptor.RequiresAuthentication, Is.True);
            Assert.That(descriptor.AllowsCallerSuppliedRecipient, Is.False);
            Assert.That(descriptor.RequiredPolicies, Is.EqualTo(new[] { "CanApproveLoans" }));
        }

        /// <summary>
        /// MediatR.Contracts 2.x made <c>IRequest</c> and <c>IRequest&lt;T&gt;</c> unrelated, so a
        /// check written against only the generic one would reject every void command.
        /// </summary>
        [Test]
        public void Build_AcceptsAVoidCommand()
        {
            var registry = VerifiableOperationRegistry.Build([typeof(CloseLoanOperation)]);

            Assert.That(registry.Get("CloseLoan").PayloadType, Is.EqualTo(typeof(CloseLoanOperation)));
        }

        [Test]
        public void Build_IgnoresTypesWithoutTheAttribute()
        {
            var registry = VerifiableOperationRegistry.Build([typeof(string), typeof(VerifiableOperationRegistryTests)]);

            Assert.That(registry.All, Is.Empty);
        }

        /// <summary>
        /// The recursion trap. Dispatching such a command at confirm re-enters
        /// <c>OtpVerificationBehavior</c>, issues a second challenge and throws 428 from inside the
        /// confirm call, so the caller can never get through and every attempt bills two messages.
        /// </summary>
        [Test]
        public void Build_WithAnOperationThatIsAlsoOtpGated_Throws()
        {
            Assert.That(
                () => VerifiableOperationRegistry.Build([typeof(DoubleGatedOperation)]),
                Throws.InstanceOf<InvalidOperationException>()
                      .With.Message.Contains(nameof(IRequireOtpVerification)));
        }

        [Test]
        public void Build_WithTwoOperationsSharingAName_Throws()
        {
            Assert.That(
                () => VerifiableOperationRegistry.Build([typeof(FirstDuplicate), typeof(SecondDuplicate)]),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("Duplicate"));
        }

        /// <summary>
        /// Dispatch is dynamic, so without this the failure would only appear on a live request.
        /// </summary>
        [Test]
        public void Build_WithAnOperationThatIsNotAMediatrRequest_Throws()
        {
            Assert.That(
                () => VerifiableOperationRegistry.Build([typeof(NotARequestOperation)]),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("IRequest"));
        }

        [Test]
        public void Build_WithAnEmptyName_Throws()
        {
            Assert.That(
                () => VerifiableOperationRegistry.Build([typeof(UnnamedOperation)]),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("empty name"));
        }

        /// <summary>
        /// A DomainValidationException, not an InvalidOperationException: an unregistered name is
        /// something the caller sent, so it maps to a localized 400 — and it must be refused before
        /// a code is issued, never after.
        /// </summary>
        [Test]
        public void Get_WithAnUnregisteredName_ThrowsDomainValidation()
        {
            var registry = VerifiableOperationRegistry.Build([typeof(ApproveLoanOperation)]);

            Assert.That(() => registry.Get("NoSuchOperation"), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => registry.Get(""), Throws.InstanceOf<DomainValidationException>());
        }

        /// <summary>
        /// Ordinal, not case-insensitive: names address operations remotely, and quietly accepting
        /// a different casing widens what the allowlist matches.
        /// </summary>
        [Test]
        public void Get_IsCaseSensitive()
        {
            var registry = VerifiableOperationRegistry.Build([typeof(ApproveLoanOperation)]);

            Assert.That(() => registry.Get("approveloan"), Throws.InstanceOf<DomainValidationException>());
        }
    }
}
