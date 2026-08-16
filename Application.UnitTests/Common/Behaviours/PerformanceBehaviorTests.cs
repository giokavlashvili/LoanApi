using Application.Common.Behaviors;
using Application.Common.Interfaces;
using Application.Common.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Application.UnitTests.Common.Behaviours
{
    [TestFixture]
    public class PerformanceBehaviorTests
    {
        public record SampleCommand(string Name);

        [Test]
        public async Task Handle_WhenFasterThanThreshold_DoesNotLog()
        {
            var logger = new CapturingLogger<SampleCommand>();
            var behavior = CreateBehavior(logger, thresholdMs: 500);

            await behavior.Handle(new SampleCommand("n"), _ => Task.FromResult(true), default);

            Assert.That(logger.Entries, Is.Empty);
        }

        [Test]
        public async Task Handle_WhenSlowerThanThreshold_LogsAWarningWithTheRedactedRequest()
        {
            var logger = new CapturingLogger<SampleCommand>();
            var behavior = CreateBehavior(logger, thresholdMs: 1);

            await behavior.Handle(new SampleCommand("n"), async _ =>
            {
                await Task.Delay(20);
                return true;
            }, default);

            Assert.That(logger.Entries, Has.Count.EqualTo(1));
            Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(logger.Entries[0].Message, Does.Contain(nameof(SampleCommand)));
        }

        private static PerformanceBehavior<SampleCommand, bool> CreateBehavior(
            ILogger<SampleCommand> logger,
            int thresholdMs)
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.UserId).Returns("user-1");

            return new PerformanceBehavior<SampleCommand, bool>(
                logger,
                currentUser.Object,
                new StubOptionsMonitor<PerformanceLoggingOptions>(
                    new PerformanceLoggingOptions { LongRunningThresholdMs = thresholdMs }));
        }

        private sealed class StubOptionsMonitor<T> : IOptionsMonitor<T>
        {
            public StubOptionsMonitor(T value) => CurrentValue = value;

            public T CurrentValue { get; }

            public T Get(string? name) => CurrentValue;

            public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

            private sealed class NullDisposable : IDisposable
            {
                public static readonly NullDisposable Instance = new();
                public void Dispose() { }
            }
        }

        private sealed class CapturingLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = [];

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
