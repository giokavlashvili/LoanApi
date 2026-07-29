using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using NUnit.Framework;

namespace Domain.UnitTests.Entities
{
    [TestFixture]
    public class OtpVerificationTests
    {
        // Valid Base64 so the constant-time comparison in OtpVerification.Verify actually
        // decodes them, instead of every comparison short-circuiting on a FormatException.
        private const string CodeHash = "Y29kZS1oYXNo";
        private const string RequestHash = "cmVxdWVzdC1oYXNo";

        private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0);

        private static OtpVerification CreateChallenge(int maxAttempts = 3) =>
            OtpVerification.Create(
                Guid.NewGuid(),
                "RegisterUserCommand",
                "+995555123456",
                userId: null,
                CodeHash,
                RequestHash,
                Now.AddMinutes(5),
                maxAttempts,
                Now);

        [Test]
        public void CreateOtpVerification_WithInvalidParameters_ThrowDomainException()
        {
            Assert.That(() => OtpVerification.Create(Guid.Empty, "Purpose", "+995555123456", null, CodeHash, RequestHash, Now.AddMinutes(5), 3, Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => OtpVerification.Create(Guid.NewGuid(), string.Empty, "+995555123456", null, CodeHash, RequestHash, Now.AddMinutes(5), 3, Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => OtpVerification.Create(Guid.NewGuid(), "Purpose", string.Empty, null, CodeHash, RequestHash, Now.AddMinutes(5), 3, Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => OtpVerification.Create(Guid.NewGuid(), "Purpose", "+995555123456", null, string.Empty, RequestHash, Now.AddMinutes(5), 3, Now), Throws.InstanceOf<DomainValidationException>());
            // Already expired when issued.
            Assert.That(() => OtpVerification.Create(Guid.NewGuid(), "Purpose", "+995555123456", null, CodeHash, RequestHash, Now, 3, Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => OtpVerification.Create(Guid.NewGuid(), "Purpose", "+995555123456", null, CodeHash, RequestHash, Now.AddMinutes(5), 0, Now), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void VerifyOtp_WithCorrectCode_MarkVerified()
        {
            var entity = CreateChallenge();

            entity.Verify(CodeHash, RequestHash, Now.AddMinutes(1));

            Assert.That(entity.Status, Is.EqualTo(OtpVerificationStatus.Verified));
            Assert.That(entity.ConsumedAt, Is.Not.Null);
        }

        [Test]
        public void VerifyOtp_AfterExpiry_ThrowDomainExceptionAndMarkExpired()
        {
            var entity = CreateChallenge();

            Assert.That(() => entity.Verify(CodeHash, RequestHash, Now.AddMinutes(6)), Throws.InstanceOf<DomainValidationException>());
            Assert.That(entity.Status, Is.EqualTo(OtpVerificationStatus.Expired));
        }

        [Test]
        public void VerifyOtp_WhenAlreadyUsed_ThrowDomainException()
        {
            var entity = CreateChallenge();

            entity.Verify(CodeHash, RequestHash, Now.AddMinutes(1));

            // The whole point of a one time password: a correct code is spent, not reusable.
            Assert.That(() => entity.Verify(CodeHash, RequestHash, Now.AddMinutes(2)), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void VerifyOtp_WithWrongCode_CountAttemptAndLockAtLimit()
        {
            var entity = CreateChallenge(maxAttempts: 3);

            for (var attempt = 0; attempt < 3; attempt++)
                Assert.That(() => entity.Verify("d3JvbmctaGFzaA==", RequestHash, Now.AddMinutes(1)), Throws.InstanceOf<DomainValidationException>());

            Assert.That(entity.AttemptCount, Is.EqualTo(3));
            Assert.That(entity.Status, Is.EqualTo(OtpVerificationStatus.Locked));

            // Locked means locked — the correct code no longer helps.
            Assert.That(() => entity.Verify(CodeHash, RequestHash, Now.AddMinutes(1)), Throws.InstanceOf<DomainValidationException>());
            Assert.That(entity.Status, Is.EqualTo(OtpVerificationStatus.Locked));
        }

        [Test]
        public void VerifyOtp_WhenRequestChanged_ThrowDomainException()
        {
            var entity = CreateChallenge();

            // Confirming a different payload than the one the code was sent for.
            Assert.That(() => entity.Verify(CodeHash, "ZGlmZmVyZW50LXJlcXVlc3QtaGFzaA==", Now.AddMinutes(1)), Throws.InstanceOf<DomainValidationException>());
            Assert.That(entity.Status, Is.EqualTo(OtpVerificationStatus.Pending));
        }

        [Test]
        public void VerifyOtp_WithMalformedBase64Candidate_RejectAsInvalidCodeAndCountAttempt()
        {
            var entity = CreateChallenge();

            // Not valid Base64 (the '!' is outside the alphabet) — must fail as a wrong code,
            // not escape as a FormatException.
            Assert.That(() => entity.Verify("not-valid-base64!", RequestHash, Now.AddMinutes(1)), Throws.InstanceOf<DomainValidationException>());
            Assert.That(entity.AttemptCount, Is.EqualTo(1));
        }

        [Test]
        public void InvalidateOtp_WhenPending_MarkExpired()
        {
            var entity = CreateChallenge();

            entity.Invalidate(Now.AddMinutes(1));

            Assert.That(entity.Status, Is.EqualTo(OtpVerificationStatus.Expired));
        }

        [Test]
        public void InvalidateOtp_WhenVerified_LeaveStatusUnchanged()
        {
            var entity = CreateChallenge();

            entity.Verify(CodeHash, RequestHash, Now.AddMinutes(1));
            entity.Invalidate(Now.AddMinutes(2));

            Assert.That(entity.Status, Is.EqualTo(OtpVerificationStatus.Verified));
        }
    }
}
