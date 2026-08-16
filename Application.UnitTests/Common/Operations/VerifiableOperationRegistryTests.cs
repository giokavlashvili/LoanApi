using Application.Common.Logging;
using Application.Common.Operations;
using Application.Common.Otp;
using Domain.Exceptions;
using MediatR;
using NUnit.Framework;

namespace Application.UnitTests.Common.Operations
{
    /// <summary>
    /// Every fixture reuses <see cref="VerifiableOperationType.DeleteLoanApplication"/>. That is not
    /// laziness: an enum member exists to make an operation reachable, so adding members here purely
    /// to serve tests would put unreachable values into the published contract. Each test builds its
    /// own registry from an explicit type list, so reuse across tests cannot collide — and the one
    /// place two fixtures share a member is the duplicate test, where that is the point.
    /// </summary>
    [TestFixture]
    public class VerifiableOperationRegistryTests
    {
        private const VerifiableOperationType Registered = VerifiableOperationType.DeleteLoanApplication;

        [VerifiableOperation(Registered, RequiredPolicies = ["CanApproveLoans"])]
        public record ApproveLoanOperation : IRequest<bool>;

        /// <summary>A void command — <c>IRequest</c>, not <c>IRequest&lt;T&gt;</c>.</summary>
        [VerifiableOperation(Registered)]
        public record CloseLoanOperation : IRequest;

        [VerifiableOperation(Registered)]
        public record FirstDuplicate : IRequest<bool>;

        [VerifiableOperation(Registered)]
        public record SecondDuplicate : IRequest<bool>;

        /// <summary>Gated twice — the trap the startup check exists for.</summary>
        [VerifiableOperation(Registered)]
        public record DoubleGatedOperation : IRequest<bool>, IRequireOtpVerification
        {
            public Guid? ChallengeId { get; init; }
            public string? OtpCode { get; init; }
        }

        [VerifiableOperation(Registered)]
        public class NotARequestOperation
        {
        }

        /// <summary>
        /// Enums are not closed at runtime — this cast compiles, and so does the attribute.
        /// </summary>
        [VerifiableOperation((VerifiableOperationType)999)]
        public record UndefinedOperation : IRequest<bool>;

        [Test]
        public void Build_RegistersAnOperationWithItsAttributeValues()
        {
            var registry = VerifiableOperationRegistry.Build([typeof(ApproveLoanOperation)]);

            var descriptor = registry.Get(Registered);

            Assert.That(descriptor.PayloadType, Is.EqualTo(typeof(ApproveLoanOperation)));
            Assert.That(descriptor.RequiresAuthentication, Is.True);
            Assert.That(descriptor.AllowsCallerSuppliedRecipient, Is.False);
            Assert.That(descriptor.RequiredPolicies, Is.EqualTo(new[] { "CanApproveLoans" }));
        }

        /// <summary>
        /// The name is what gets persisted and hashed as an OTP purpose, so it has to be the member
        /// name and not the numeric value — a number would make stored rows unreadable and turn
        /// reordering the enum into silent data corruption.
        /// </summary>
        [Test]
        public void Descriptor_NameIsTheEnumMemberName()
        {
            var registry = VerifiableOperationRegistry.Build([typeof(ApproveLoanOperation)]);

            Assert.That(registry.Get(Registered).Name, Is.EqualTo(nameof(VerifiableOperationType.DeleteLoanApplication)));
        }

        /// <summary>
        /// MediatR.Contracts 2.x made <c>IRequest</c> and <c>IRequest&lt;T&gt;</c> unrelated, so a
        /// check written against only the generic one would reject every void command.
        /// </summary>
        [Test]
        public void Build_AcceptsAVoidCommand()
        {
            var registry = VerifiableOperationRegistry.Build([typeof(CloseLoanOperation)]);

            Assert.That(registry.Get(Registered).PayloadType, Is.EqualTo(typeof(CloseLoanOperation)));
        }

        /// <summary>Its sensitive property is named on <c>LogRedactor.DefaultSensitiveProperties</c>.</summary>
        [VerifiableOperation(Registered)]
        public record RedactedSecretOperation : IRequest<bool>
        {
            [SensitiveData]
            public string? Password { get; init; }
        }

        /// <summary>
        /// <c>birthDate</c> is deliberately <em>not</em> on the redaction list — marking it
        /// encrypts it in the database while leaving it in the clear in the Logs table.
        /// </summary>
        [VerifiableOperation(Registered)]
        public record UnredactedSecretOperation : IRequest<bool>
        {
            [SensitiveData]
            public DateTime? BirthDate { get; init; }
        }

        public record NestedSecretHolder
        {
            [SensitiveData]
            public DateTime? BirthDate { get; init; }
        }

        /// <summary>The sensitive property sits inside a collection element type.</summary>
        [VerifiableOperation(Registered)]
        public record CollectionOfSecretsOperation : IRequest<bool>
        {
            public List<NestedSecretHolder>? Holders { get; init; }
        }

        /// <summary>The same, one level down but not in a collection.</summary>
        [VerifiableOperation(Registered)]
        public record NestedSecretOperation : IRequest<bool>
        {
            public NestedSecretHolder? Holder { get; init; }
        }

        [Test]
        public void Build_FindsASensitivePropertyOnANestedObject()
        {
            Assert.That(
                () => VerifiableOperationRegistry.Build(
                    [typeof(NestedSecretOperation)], true, LogRedactor.DefaultSensitiveProperties),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("BirthDate"));
        }

        [Test]
        public void Build_FindsASensitivePropertyInsideACollection()
        {
            Assert.That(
                () => VerifiableOperationRegistry.Build(
                    [typeof(CollectionOfSecretsOperation)], true, LogRedactor.DefaultSensitiveProperties),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("BirthDate"));
        }

        [Test]
        public void Build_WithASensitivePayloadAndNoConfiguredSecret_Throws()
        {
            Assert.That(
                () => VerifiableOperationRegistry.Build([typeof(RedactedSecretOperation)], payloadProtectionConfigured: false),
                Throws.InstanceOf<InvalidOperationException>()
                      .With.Message.Contains("PayloadProtection:Secret"));
        }

        /// <summary>
        /// The subtle failure this guards. Encryption at rest plus a plaintext log row is worse
        /// than no encryption, because it looks handled.
        /// </summary>
        [Test]
        public void Build_WithASensitivePropertyTheRedactorDoesNotMask_Throws()
        {
            Assert.That(
                () => VerifiableOperationRegistry.Build(
                    [typeof(UnredactedSecretOperation)],
                    payloadProtectionConfigured: true,
                    LogRedactor.DefaultSensitiveProperties),
                Throws.InstanceOf<InvalidOperationException>()
                      .With.Message.Contains(nameof(UnredactedSecretOperation.BirthDate)));
        }

        [Test]
        public void Build_WithASensitivePropertyTheRedactorMasks_Succeeds()
        {
            var registry = VerifiableOperationRegistry.Build(
                [typeof(RedactedSecretOperation)],
                payloadProtectionConfigured: true,
                LogRedactor.DefaultSensitiveProperties);

            Assert.That(registry.Get(Registered).PayloadType, Is.EqualTo(typeof(RedactedSecretOperation)));
        }

        /// <summary>
        /// The check mirrors <c>LogRedactor.MergeSensitiveProperties</c>: a configured list
        /// <em>extends</em> the defaults. A name already on the defaults stays masked even if
        /// config omits it.
        /// </summary>
        [Test]
        public void Build_HonoursAMergedRedactionList()
        {
            var merged = LogRedactor.MergeSensitiveProperties(new[] { "somethingElse" });

            var registry = VerifiableOperationRegistry.Build(
                [typeof(RedactedSecretOperation)],
                payloadProtectionConfigured: true,
                merged);

            Assert.That(registry.Get(Registered).PayloadType, Is.EqualTo(typeof(RedactedSecretOperation)));
        }

        [Test]
        public void Build_WithAListThatOmitsTheSensitiveName_Throws()
        {
            Assert.That(
                () => VerifiableOperationRegistry.Build(
                    [typeof(RedactedSecretOperation)],
                    payloadProtectionConfigured: true,
                    new[] { "somethingElse" }),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("Password"),
                "the registry checks the set it is given; merging happens before this call");
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
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains(nameof(SecondDuplicate)));
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

        /// <summary>
        /// The enum's replacement for the old empty-name check. Unchecked, the cast would register
        /// under the name "999", which is then persisted and hashed as an OTP purpose.
        /// </summary>
        [Test]
        public void Build_WithAnUndefinedEnumValue_Throws()
        {
            Assert.That(
                () => VerifiableOperationRegistry.Build([typeof(UndefinedOperation)]),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("999"));
        }

        /// <summary>
        /// A DomainValidationException, not an InvalidOperationException: an unregistered operation
        /// is something the caller sent, so it maps to a localized 400 — and it must be refused
        /// before a code is issued, never after.
        /// <para>
        /// Being a defined enum member is not the same as being registered. The enum is the
        /// authoring surface; the attribute is what grants reachability.
        /// </para>
        /// </summary>
        [Test]
        public void Get_WithADefinedButUnregisteredOperation_ThrowsDomainValidation()
        {
            // Array.Empty rather than [], which is ambiguous between the two Build overloads.
            var registry = VerifiableOperationRegistry.Build(Array.Empty<Type>());

            Assert.That(() => registry.Get(Registered), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void Get_ByName_ResolvesTheStoredForm()
        {
            var registry = VerifiableOperationRegistry.Build([typeof(ApproveLoanOperation)]);

            Assert.That(registry.Get("DeleteLoanApplication").PayloadType, Is.EqualTo(typeof(ApproveLoanOperation)));
        }

        /// <summary>
        /// The read-back path fails closed: a row written before a deploy may name a member that
        /// deploy removed, and guessing at it would run the wrong operation against a stored payload.
        /// </summary>
        [Test]
        public void Get_ByName_WithAnUnparseableName_ThrowsDomainValidation()
        {
            var registry = VerifiableOperationRegistry.Build([typeof(ApproveLoanOperation)]);

            Assert.That(() => registry.Get("RetiredOperation"), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => registry.Get(""), Throws.InstanceOf<DomainValidationException>());
        }

        /// <summary>
        /// Case-sensitive, so a stored name only ever resolves to the member that wrote it. Enum
        /// parsing is case-insensitive by default, which would quietly widen what the allowlist
        /// matches — and would also let "1" resolve, since Enum.TryParse accepts numeric text.
        /// </summary>
        [Test]
        public void Get_ByName_IsCaseSensitiveAndRejectsNumericText()
        {
            var registry = VerifiableOperationRegistry.Build([typeof(ApproveLoanOperation)]);

            Assert.That(() => registry.Get("deleteloanapplication"), Throws.InstanceOf<DomainValidationException>());

            // Enum.TryParse accepts numeric text, so without the round-trip check in TryGet this
            // would resolve — an address into the allowlist that nothing ever writes.
            Assert.That(() => registry.Get("1"), Throws.InstanceOf<DomainValidationException>());
        }
    }
}
