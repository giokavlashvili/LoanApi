using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Otp;
using Domain.Exceptions;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Application.UnitTests.Common.Otp
{
    /// <summary>
    /// One rule, one implementation, one test. This is what stops a caller redirecting their own
    /// confirmation code to a phone they control, so it is worth guarding directly rather than
    /// through whichever mechanism happens to call it.
    /// </summary>
    [TestFixture]
    public class OtpRecipientResolverTests
    {
        private Mock<ICurrentUserService> _currentUserService;
        private Mock<IUserService> _userService;
        private OtpRecipientResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            _currentUserService = new Mock<ICurrentUserService>();
            _userService = new Mock<IUserService>();
            _resolver = new OtpRecipientResolver(_currentUserService.Object, _userService.Object);
        }

        [Test]
        public async Task ResolveAsync_WithASuppliedRecipient_UseIt()
        {
            var resolved = await _resolver.ResolveAsync("+995555123456");

            Assert.That(resolved, Is.EqualTo("+995555123456"));
            // Registration has no account to read from, so nothing should be looked up.
            _userService.Verify(u => u.GetUserByIdAsync(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public async Task ResolveAsync_WithoutOne_FallBackToTheAuthenticatedAccount()
        {
            _currentUserService.Setup(u => u.UserId).Returns("userId");
            _userService.Setup(u => u.GetUserByIdAsync("userId"))
                .ReturnsAsync(new User { Id = "userId", PhoneNumber = "+995555999888" });

            var resolved = await _resolver.ResolveAsync(null);

            Assert.That(resolved, Is.EqualTo("+995555999888"));
        }

        [Test]
        public void ResolveAsync_WithoutOneAndWithoutAUser_ThrowDomainException()
        {
            _currentUserService.Setup(u => u.UserId).Returns((string?)null);

            Assert.That(async () => await _resolver.ResolveAsync(null), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void ResolveAsync_WhenTheAccountHasNoNumber_ThrowDomainException()
        {
            _currentUserService.Setup(u => u.UserId).Returns("userId");
            _userService.Setup(u => u.GetUserByIdAsync("userId"))
                .ReturnsAsync(new User { Id = "userId", PhoneNumber = null });

            Assert.That(async () => await _resolver.ResolveAsync(null), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void ResolveAsync_WhenTheUserIsUnknown_ThrowDomainException()
        {
            _currentUserService.Setup(u => u.UserId).Returns("userId");
            _userService.Setup(u => u.GetUserByIdAsync("userId")).ReturnsAsync((User?)null);

            Assert.That(async () => await _resolver.ResolveAsync(null), Throws.InstanceOf<DomainValidationException>());
        }

        /// <summary>Whitespace is not a recipient — it must fall through, not be sent to.</summary>
        [Test]
        public void ResolveAsync_WithABlankSuppliedRecipient_FallsThrough()
        {
            _currentUserService.Setup(u => u.UserId).Returns((string?)null);

            Assert.That(async () => await _resolver.ResolveAsync("   "), Throws.InstanceOf<DomainValidationException>());
        }
    }
}
