using Application.Common.Behaviors;
using Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Application.UnitTests.Common.Behaviours
{
    [TestFixture]
    public class TransactionBehaviorTests
    {
        public class PlainCommand : IRequest<bool>
        {
        }

        /// <summary>
        /// A void command, i.e. <c>IRequest</c> rather than <c>IRequest&lt;T&gt;</c>. Its own case
        /// because MediatR.Contracts 2.x made the two interfaces unrelated, so a behavior
        /// constrained to <c>TRequest : IRequest&lt;TResponse&gt;</c> cannot close over
        /// <c>&lt;this, Unit&gt;</c> and the container drops it without a word.
        /// </summary>
        public class VoidCommand : IRequest
        {
        }

        public record TestQuery : IQuery<bool>;

        /// <summary>Opts out the way <c>ResendOtpCommand</c> does.</summary>
        public class ExemptCommand : IRequest<bool>, ISkipTransaction
        {
        }

        private Mock<IApplicationDbContext> _dbContext;
        private Mock<IDbContextTransaction> _transaction;

        [SetUp]
        public void SetUp()
        {
            _transaction = new Mock<IDbContextTransaction>();
            _transaction.SetupGet(t => t.TransactionId).Returns(Guid.NewGuid());

            _dbContext = new Mock<IApplicationDbContext>();
            _dbContext.SetupGet(c => c.HasActiveTransaction).Returns(false);
            _dbContext
                .Setup(c => c.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_transaction.Object);
        }

        private TransactionBehavior<TRequest, TResponse> CreateBehavior<TRequest, TResponse>() where TRequest : notnull =>
            new(_dbContext.Object, NullLogger<TransactionBehavior<TRequest, TResponse>>.Instance);

        private void VerifyNoTransactionOpened() =>
            _dbContext.Verify(c => c.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never());

        [Test]
        public async Task Handle_WithQuery_CallsHandlerWithoutOpeningTransaction()
        {
            var behavior = CreateBehavior<TestQuery, bool>();
            var handlerCalled = false;

            var result = await behavior.Handle(new TestQuery(), _ =>
            {
                handlerCalled = true;
                return Task.FromResult(true);
            }, default);

            Assert.That(handlerCalled, Is.True);
            Assert.That(result, Is.True);
            VerifyNoTransactionOpened();
        }

        [Test]
        public async Task Handle_WithSkipTransactionCommand_CallsHandlerWithoutOpeningTransaction()
        {
            var behavior = CreateBehavior<ExemptCommand, bool>();

            await behavior.Handle(new ExemptCommand(), _ => Task.FromResult(true), default);

            VerifyNoTransactionOpened();
        }

        /// <summary>
        /// A nested Send must join the outer transaction, not start a second one — this is what
        /// lets a command dispatch another and still commit as a unit.
        /// </summary>
        [Test]
        public async Task Handle_WhenTransactionAlreadyActive_DoesNotOpenAnother()
        {
            _dbContext.SetupGet(c => c.HasActiveTransaction).Returns(true);
            var behavior = CreateBehavior<PlainCommand, bool>();

            await behavior.Handle(new PlainCommand(), _ => Task.FromResult(true), default);

            VerifyNoTransactionOpened();
        }

        [Test]
        public async Task Handle_WithPlainCommand_OpensAndCommits()
        {
            var behavior = CreateBehavior<PlainCommand, bool>();

            var result = await behavior.Handle(new PlainCommand(), _ => Task.FromResult(true), default);

            Assert.That(result, Is.True);
            _dbContext.Verify(c => c.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once());
            _dbContext.Verify(c => c.CommitTransactionAsync(_transaction.Object, It.IsAny<CancellationToken>()), Times.Once());
            _dbContext.Verify(c => c.RollbackTransaction(), Times.Never());
        }

        /// <summary>
        /// Rolling back explicitly is what clears <c>_currentTransaction</c> on the context.
        /// Disposal alone reverses the database but leaves <c>HasActiveTransaction</c> reporting
        /// true against a dead object, so the next Send in the scope would run untransacted.
        /// </summary>
        [Test]
        public void Handle_WhenHandlerThrows_RollsBackAndRethrows()
        {
            var behavior = CreateBehavior<PlainCommand, bool>();
            var boom = new InvalidOperationException("handler failed");

            var thrown = Assert.ThrowsAsync<InvalidOperationException>(() =>
                behavior.Handle(new PlainCommand(), _ => Task.FromException<bool>(boom), default));

            Assert.That(thrown, Is.SameAs(boom));
            _dbContext.Verify(c => c.RollbackTransaction(), Times.Once());
            _dbContext.Verify(c => c.CommitTransactionAsync(It.IsAny<IDbContextTransaction>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        /// <summary>
        /// The regression test for the generic constraint, and the only place the container gets a
        /// say — in production the failure is pure silence, since MediatR simply never resolves the
        /// behavior for a void command.
        /// <para>
        /// Verified: tightening the constraint back to <c>TRequest : IRequest&lt;TResponse&gt;</c>
        /// fails the <em>build</em> here (CS0314) rather than this assertion, because
        /// <c>TransactionBehavior&lt;VoidCommand, Unit&gt;</c> stops being a nameable type. A
        /// compile error is the stronger guard, so keep <see cref="VoidCommand"/> named explicitly
        /// below rather than reaching it through a helper.
        /// </para>
        /// </summary>
        [Test]
        public void Registration_ForAVoidCommand_ResolvesTheBehavior()
        {
            var services = new ServiceCollection();

            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton(new Mock<IApplicationDbContext>().Object);
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(TransactionBehaviorTests).Assembly);
                cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            });

            using var provider = services.BuildServiceProvider();

            var behaviors = provider.GetServices<IPipelineBehavior<VoidCommand, Unit>>().ToList();

            Assert.That(
                behaviors.Any(b => b is TransactionBehavior<VoidCommand, Unit>),
                Is.True,
                "A void command resolved no TransactionBehavior. MediatR.Contracts 2.x made IRequest and "
                + "IRequest<T> unrelated types, so constraining TRequest to IRequest<TResponse> leaves the "
                + "container unable to satisfy it — and it drops the registration silently rather than "
                + "failing, so every void command would mutate data with no transaction at all.");
        }
    }
}
