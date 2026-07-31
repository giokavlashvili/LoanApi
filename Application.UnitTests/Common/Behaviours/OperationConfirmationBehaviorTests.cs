using Application.Common.Behaviors;
using Application.Common.Confirmation;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Otp.Dtos;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.

namespace Application.UnitTests.Common.Behaviours
{
    [TestFixture]
    public class OperationConfirmationBehaviorTests
    {
        public record ConfirmableCommand : IRequest<bool>, IRequireOperationConfirmation
        {
            public int Id { get; set; }
        }

        /// <summary>
        /// A void command, i.e. <c>IRequest</c> rather than <c>IRequest&lt;T&gt;</c>. Worth its own
        /// case for the same reason as in <see cref="OtpVerificationBehaviorTests"/>: a behavior
        /// constrained to <c>TRequest : IRequest&lt;TResponse&gt;</c> is silently dropped by the
        /// container. It matters more here — the one confirmable command in the box,
        /// <c>UpdateApplicationStatusCommand</c>, returns nothing, so that mistake would leave
        /// loan approvals completely ungated.
        /// </summary>
        public record ConfirmableVoidCommand : IRequest, IRequireOperationConfirmation;

        public record PlainCommand : IRequest<bool>;

        private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

        private Mock<IOperationTypeRegistry> _registry;
        private Mock<IOperationConfirmationService> _confirmationService;
        private Mock<IPendingOperationRepository> _operations;
        private Mock<IUnitOfWork> _unitOfWork;
        private Mock<ICurrentUserService> _currentUserService;
        private Mock<IDateTime> _dateTime;
        private OperationExecutionContext _executionContext;

        [SetUp]
        public void SetUp()
        {
            _registry = new Mock<IOperationTypeRegistry>();
            _confirmationService = new Mock<IOperationConfirmationService>();
            _operations = new Mock<IPendingOperationRepository>();
            _unitOfWork = new Mock<IUnitOfWork>();
            _currentUserService = new Mock<ICurrentUserService>();
            _dateTime = new Mock<IDateTime>();
            _executionContext = new OperationExecutionContext();

            _currentUserService.Setup(c => c.UserId).Returns("initiator-id");
            _dateTime.Setup(d => d.UtcNow).Returns(Now);

            _registry
                .Setup(r => r.Find(It.IsAny<Type>()))
                .Returns(new OperationTypeDescriptor("test.op.v1", typeof(ConfirmableCommand), ConfirmationPolicy.SameUser, null));

            _confirmationService
                .Setup(s => s.IssueChallengeAsync(It.IsAny<PendingOperation>(), It.IsAny<OperationTypeDescriptor>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OtpChallengeDto { ChallengeId = Guid.NewGuid(), ExpiresAt = Now.AddMinutes(5) });
        }

        private OperationConfirmationBehavior<TRequest, TResponse> Behavior<TRequest, TResponse>() where TRequest : notnull =>
            new(
                _registry.Object,
                _executionContext,
                _confirmationService.Object,
                _operations.Object,
                _unitOfWork.Object,
                _currentUserService.Object,
                _dateTime.Object,
                Mock.Of<IOptionsMonitor<PendingOperationOptions>>(o => o.CurrentValue == new PendingOperationOptions()),
                Mock.Of<ILogger<OperationConfirmationBehavior<TRequest, TResponse>>>());

        [Test]
        public async Task Handle_WithAPlainCommand_PassStraightThrough()
        {
            var handlerRan = false;

            var result = await Behavior<PlainCommand, bool>()
                .Handle(new PlainCommand(), _ => { handlerRan = true; return Task.FromResult(true); }, CancellationToken.None);

            Assert.That(handlerRan, Is.True);
            Assert.That(result, Is.True);
            _operations.Verify(o => o.AddAsync(It.IsAny<PendingOperation>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// The regression guard for the bypass this design exists to prevent. However the request
        /// is shaped, a confirmable command reaching the pipeline from outside must be parked, not
        /// executed — the only thing that lets one through is the ambient flag, which nothing on
        /// the wire can set.
        /// </summary>
        [Test]
        public void Handle_WithAConfirmableCommand_ParkItAndNeverRunTheHandler()
        {
            var handlerRan = false;

            Assert.That(async () => await Behavior<ConfirmableCommand, bool>()
                    .Handle(new ConfirmableCommand { Id = 42 }, _ => { handlerRan = true; return Task.FromResult(true); }, CancellationToken.None),
                Throws.InstanceOf<OperationConfirmationRequiredException>());

            Assert.That(handlerRan, Is.False);
            _operations.Verify(o => o.AddAsync(It.IsAny<PendingOperation>(), It.IsAny<CancellationToken>()), Times.Once);
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <inheritdoc cref="ConfirmableVoidCommand"/>
        [Test]
        public void Handle_WithAConfirmableVoidCommand_StillParkIt()
        {
            var handlerRan = false;

            Assert.That(async () => await Behavior<ConfirmableVoidCommand, Unit>()
                    .Handle(new ConfirmableVoidCommand(), _ => { handlerRan = true; return Task.FromResult(Unit.Value); }, CancellationToken.None),
                Throws.InstanceOf<OperationConfirmationRequiredException>());

            Assert.That(handlerRan, Is.False);
        }

        [Test]
        public async Task Handle_InsideAConfirmedExecution_PassThroughToTheHandler()
        {
            var handlerRan = false;

            using (_executionContext.BeginConfirmedExecution(Guid.NewGuid()))
            {
                await Behavior<ConfirmableCommand, bool>()
                    .Handle(new ConfirmableCommand { Id = 42 }, _ => { handlerRan = true; return Task.FromResult(true); }, CancellationToken.None);
            }

            Assert.That(handlerRan, Is.True);
            _operations.Verify(o => o.AddAsync(It.IsAny<PendingOperation>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public void Handle_WhenTheExecutionScopeHasEnded_ParkAgain()
        {
            using (_executionContext.BeginConfirmedExecution(Guid.NewGuid()))
            {
            }

            // Disposing must restore the flag, or one confirmed command would leave every later
            // command in the same request scope ungated.
            Assert.That(async () => await Behavior<ConfirmableCommand, bool>()
                    .Handle(new ConfirmableCommand(), _ => Task.FromResult(true), CancellationToken.None),
                Throws.InstanceOf<OperationConfirmationRequiredException>());
        }

        [Test]
        public void Handle_PastThePerUserCap_RefuseBeforeIssuingAnything()
        {
            _operations
                .Setup(o => o.CountPendingForUserAsync("initiator-id", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PendingOperationOptions().MaxPendingPerUser);

            Assert.That(async () => await Behavior<ConfirmableCommand, bool>()
                    .Handle(new ConfirmableCommand(), _ => Task.FromResult(true), CancellationToken.None),
                Throws.InstanceOf<DomainValidationException>());

            // The cap is worthless if it is only checked after the row is written and the SMS sent.
            _operations.Verify(o => o.AddAsync(It.IsAny<PendingOperation>(), It.IsAny<CancellationToken>()), Times.Never);
            _confirmationService.Verify(
                s => s.IssueChallengeAsync(It.IsAny<PendingOperation>(), It.IsAny<OperationTypeDescriptor>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Under four eyes the approver is somebody the server has not met at initiate, so texting
        /// the initiator would send the code to the one person who must not be able to complete
        /// the operation alone.
        /// </summary>
        [Test]
        public void Handle_WhenAnotherUserMustConfirm_ParkWithoutIssuingACode()
        {
            _registry
                .Setup(r => r.Find(It.IsAny<Type>()))
                .Returns(new OperationTypeDescriptor("test.op.v1", typeof(ConfirmableCommand), ConfirmationPolicy.DifferentUser, null));

            OperationConfirmationRequiredException? thrown = null;

            try
            {
                Behavior<ConfirmableCommand, bool>()
                    .Handle(new ConfirmableCommand(), _ => Task.FromResult(true), CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (OperationConfirmationRequiredException ex)
            {
                thrown = ex;
            }

            Assert.That(thrown, Is.Not.Null);
            Assert.That(thrown!.Challenge, Is.Null);
            _confirmationService.Verify(
                s => s.IssueChallengeAsync(It.IsAny<PendingOperation>(), It.IsAny<OperationTypeDescriptor>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
