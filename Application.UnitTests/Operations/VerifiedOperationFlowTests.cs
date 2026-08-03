using Application.Common.Behaviors;
using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Operations;
using Application.Common.Otp;
using Application.Common.Serialization;
using Application.LoanApplications.Commands;
using Application.Operations.Services;
using Application.Otp.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Repositories;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Interceptors;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using System.Reflection;
using System.Text.Json;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Application.UnitTests.Operations
{
    /// <summary>
    /// The whole flow against a real container and a real database context: initiate stores the
    /// request and sends a code, confirm verifies it and the handler actually runs.
    /// <para>
    /// Everything here is the production wiring except the database provider, the SMS sender and
    /// identity. In particular the real MediatR pipeline is registered in production order, so
    /// this is what proves the nested <c>Send</c> at confirm joins the service's transaction
    /// rather than opening its own.
    /// </para>
    /// </summary>
    [TestFixture]
    public class VerifiedOperationFlowTests
    {
        private const VerifiableOperationType Operation = VerifiableOperationType.DeleteLoanApplication;
        private const string StaticCode = "123456";
        private const string UserId = "user-id";

        private ServiceProvider _provider;
        private ApplicationDbContext _context;
        private RecordingCodeSender _sender;

        [SetUp]
        public void SetUp()
        {
            var services = new ServiceCollection();
            var databaseName = Guid.NewGuid().ToString();

            // MediatR 14 requires an ILoggerFactory and refuses to register without one.
            services.AddLogging();
            services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));

            // Registered because production registers it, and because CreatedBy is required on
            // every auditable entity -- without the interceptor even seeding fails.
            services.AddScoped<AuditableEntityInterceptor>();

            services.AddDbContext<ApplicationDbContext>((sp, options) => options
                .UseInMemoryDatabase(databaseName)
                // The in-memory provider has no transactions and throws on BeginTransactionAsync
                // unless this is ignored, at which point it hands back a no-op. Worth knowing
                // before writing any test that touches the confirm path.
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));

            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<ILoanApplicationRepository, LoanApplicationRepository>();
            services.AddScoped<IOtpVerificationRepository, OtpVerificationRepository>();
            services.AddScoped<IPendingOperationRepository, PendingOperationRepository>();

            // StaticCode is the Development escape hatch: the rest of the flow -- hashing, expiry,
            // attempt limit, purpose binding -- runs exactly as with a random code.
            services.AddSingleton<IOptionsMonitor<OtpOptions>>(
                new StubOptionsMonitor<OtpOptions>(new OtpOptions { Secret = "test-secret", StaticCode = StaticCode, ResendCooldown = TimeSpan.Zero }));

            services.AddSingleton<IOtpCodeHasher, HmacOtpCodeHasher>();

            // The real cipher, not a stub. The assertion that matters here is that plaintext is
            // absent from the stored row, and a pass-through protector would let that pass while
            // the feature did nothing.
            services.AddSingleton<IOptionsMonitor<PayloadProtectionOptions>>(
                new StubOptionsMonitor<PayloadProtectionOptions>(new PayloadProtectionOptions { Secret = "test-payload-secret" }));
            services.AddSingleton<IPayloadProtector, AesGcmPayloadProtector>();
            services.AddSingleton<IProtectedPayloadSerializer, ProtectedPayloadSerializer>();
            services.AddSingleton<IDateTime, StubClock>();
            _sender = new RecordingCodeSender();
            services.AddSingleton<IVerificationCodeSender>(_sender);

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.UserId).Returns(UserId);
            services.AddSingleton(currentUser.Object);

            var userService = new Mock<IUserService>();
            userService.Setup(u => u.GetUserByIdAsync(UserId))
                .ReturnsAsync(new User { Id = UserId, PhoneNumber = "+995555123456" });
            services.AddSingleton(userService.Object);

            var identity = new Mock<IIdentityService>();
            identity.Setup(i => i.AuthorizeAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            services.AddSingleton(identity.Object);

            services.AddScoped<IOtpRecipientResolver, OtpRecipientResolver>();
            services.AddScoped<IOtpService, OtpService>();
            services.AddScoped<IVerifiableOperationService, VerifiableOperationService>();
            services.AddSingleton<IVerifiableOperationRegistry>(
                _ => VerifiableOperationRegistry.Build(typeof(DeleteApplicationCommand).Assembly));

            // Production order: validation, performance, OTP gate, then the transaction innermost.
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(DeleteApplicationCommand).Assembly);
                cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
                cfg.AddOpenBehavior(typeof(OtpVerificationBehavior<,>));
                cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            });

            _provider = services.BuildServiceProvider();
            _context = _provider.GetRequiredService<ApplicationDbContext>();
        }

        [TearDown]
        public void TearDown() => _provider.Dispose();

        private IVerifiableOperationService Operations => _provider.GetRequiredService<IVerifiableOperationService>();

        private LoanApplication SeedApplication()
        {
            var entity = LoanApplication.Create(loanTypeId: 1, amount: 5000m, currencyId: 1, periodPerMonth: 12);
            _context.LoanApplications.Add(entity);
            _context.SaveChanges();
            return entity;
        }

        /// <summary>
        /// The end-to-end proof: nothing happens at initiate, and the handler runs at confirm.
        /// </summary>
        [Test]
        public async Task InitiateThenConfirm_SendsACodeAndThenRunsTheOperation()
        {
            var application = SeedApplication();
            var payload = JsonSerializer.SerializeToElement(new { id = application.Id });

            // Act -- initiate
            var pending = await Operations.InitiateAsync(Operation, VerificationChannel.Sms, payload);

            // Assert -- a code went out and the application is untouched.
            Assert.That(_sender.Messages, Has.Count.EqualTo(1));
            Assert.That(_sender.Messages[0].Message, Does.Contain(StaticCode));
            Assert.That(_sender.Messages[0].Recipient, Is.EqualTo("+995555123456"));
            Assert.That(await _context.LoanApplications.CountAsync(a => a.Id == application.Id), Is.EqualTo(1),
                "initiate must not touch the operation it is gating");

            // Act -- confirm
            var result = await Operations.ConfirmAsync(pending.OperationId, StaticCode);

            // Assert -- the handler actually ran.
            Assert.That(result.Status, Is.EqualTo(PendingOperationStatus.Succeeded));
            Assert.That(await _context.LoanApplications.CountAsync(a => a.Id == application.Id), Is.Zero,
                "confirm must execute the registered handler");
        }

        [Test]
        public async Task Confirm_WithTheWrongCode_LeavesTheOperationUndone()
        {
            var application = SeedApplication();
            var payload = JsonSerializer.SerializeToElement(new { id = application.Id });

            var pending = await Operations.InitiateAsync(Operation, VerificationChannel.Sms, payload);

            Assert.That(async () => await Operations.ConfirmAsync(pending.OperationId, "000000"),
                Throws.InstanceOf<DomainValidationException>());

            Assert.That(await _context.LoanApplications.CountAsync(a => a.Id == application.Id), Is.EqualTo(1));

            // The attempt was counted and persisted -- the finally-save in VerifyAsync. Without it
            // MaxAttempts would never be reached and six digits would be brute-forceable.
            var challenge = await _context.OtpVerifications.SingleAsync(o => o.ChallengeId == pending.ChallengeId);
            Assert.That(challenge.AttemptCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A retry after a lost response must replay, not re-run — and here the operation is a
        /// delete, so re-running would hit a row that no longer exists.
        /// </summary>
        [Test]
        public async Task Confirm_Twice_ReplaysInsteadOfRunningAgain()
        {
            var application = SeedApplication();
            var payload = JsonSerializer.SerializeToElement(new { id = application.Id });

            var pending = await Operations.InitiateAsync(Operation, VerificationChannel.Sms, payload);

            var first = await Operations.ConfirmAsync(pending.OperationId, StaticCode);
            var second = await Operations.ConfirmAsync(pending.OperationId, StaticCode);

            Assert.That(first.Status, Is.EqualTo(PendingOperationStatus.Succeeded));
            Assert.That(second.Status, Is.EqualTo(PendingOperationStatus.Succeeded));
            Assert.That(second.OperationId, Is.EqualTo(first.OperationId));
        }

        /// <summary>
        /// Resend retires the old challenge and issues a new one, so the operation has to move
        /// with it — otherwise every later confirm verifies against a dead challenge.
        /// </summary>
        [Test]
        public async Task Resend_ThenConfirm_StillWorks()
        {
            var application = SeedApplication();
            var payload = JsonSerializer.SerializeToElement(new { id = application.Id });

            var pending = await Operations.InitiateAsync(Operation, VerificationChannel.Sms, payload);
            var resent = await Operations.ResendAsync(pending.OperationId);

            Assert.That(resent.ChallengeId, Is.Not.EqualTo(pending.ChallengeId));
            Assert.That(resent.OperationId, Is.EqualTo(pending.OperationId));
            Assert.That(_sender.Messages, Has.Count.EqualTo(2));

            var result = await Operations.ConfirmAsync(pending.OperationId, StaticCode);

            Assert.That(result.Status, Is.EqualTo(PendingOperationStatus.Succeeded));
            Assert.That(await _context.LoanApplications.CountAsync(a => a.Id == application.Id), Is.Zero);
        }

        [Test]
        public void Initiate_WithAnUnregisteredOperation_SendsNothing()
        {
            Assert.That(
                async () => await Operations.InitiateAsync((VerifiableOperationType)999, VerificationChannel.Sms, JsonSerializer.SerializeToElement(new { id = 1 })),
                Throws.InstanceOf<DomainValidationException>());

            Assert.That(_sender.Messages, Is.Empty);
        }

        /// <summary>
        /// The registry is built from the real assembly here, so this also asserts that the
        /// sample operation is actually registered and that nothing in the assembly trips the
        /// startup validation.
        /// </summary>
        /// <summary>
        /// The encryption sample driven through the real registry and the real assembly scan, so
        /// this also asserts that <c>SubmitPayoutDetailsCommand</c> passes every startup check —
        /// including the redaction-coverage one, which is only satisfied because <c>cardNumber</c>
        /// and <c>personalNumber</c> are both masked by the configured list.
        /// </summary>
        [Test]
        public async Task SubmitPayoutDetails_EncryptsMarkedPropertiesAndReturnsAMaskedResult()
        {
            var application = SeedApplication();
            var payload = JsonSerializer.SerializeToElement(new
            {
                applicationId = application.Id,
                cardNumber = "4111111111111111",
                personalNumber = "01001012345",
                bankName = "Bank of Georgia"
            });

            var pending = await Operations.InitiateAsync(
                VerifiableOperationType.SubmitPayoutDetails, VerificationChannel.Sms, payload);

            var stored = _context.PendingOperations.AsNoTracking()
                .Single(o => o.OperationId == pending.OperationId).Payload;

            Assert.Multiple(() =>
            {
                Assert.That(stored, Does.Not.Contain("4111111111111111"), "the card number must not be readable in the row");
                Assert.That(stored, Does.Not.Contain("01001012345"), "the personal number must not be readable in the row");
                Assert.That(stored, Does.Contain("Bank of Georgia"), "an unmarked property stays readable");
            });

            var result = await Operations.ConfirmAsync(pending.OperationId, StaticCode);

            // Masked, not encrypted: the handler received the real card number -- which is the proof
            // the round trip decrypted -- and deliberately returns only its last four digits,
            // because results are not protected and go straight into the response log.
            Assert.That(result.Result!.Value.GetProperty("maskedCardNumber").GetString(),
                Is.EqualTo("**** **** **** 1111"));
        }

        [Test]
        public void Registry_ContainsTheSampleOperation()
        {
            var registry = _provider.GetRequiredService<IVerifiableOperationRegistry>();

            Assert.That(registry.Get(Operation).PayloadType, Is.EqualTo(typeof(DeleteApplicationCommand)));
        }

        private sealed class RecordingCodeSender : IVerificationCodeSender
        {
            public List<(string Recipient, string Message)> Messages { get; } = [];

            public VerificationChannel Channel => VerificationChannel.Sms;

            public Task SendAsync(string recipient, string message, CancellationToken cancellationToken = default)
            {
                Messages.Add((recipient, message));
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// A real clock rather than a frozen one: the flow spans an expiry window and a resend
        /// cooldown, so freezing time would only prove the assertions against a standstill.
        /// (`RS0030`, which bans direct clock reads elsewhere, is suppressed in this project.)
        /// </summary>
        private sealed class StubClock : IDateTime
        {
            public DateTime UtcNow => DateTime.UtcNow;
            public DateTime LocalNow => DateTime.Now;
        }

        private sealed class StubOptionsMonitor<T> : IOptionsMonitor<T>
        {
            public StubOptionsMonitor(T value) => CurrentValue = value;

            public T CurrentValue { get; }
            public T Get(string? name) => CurrentValue;
            public IDisposable? OnChange(Action<T, string?> listener) => null;
        }
    }
}
