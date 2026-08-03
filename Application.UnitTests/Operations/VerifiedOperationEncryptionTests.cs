using Application.Common.Behaviors;
using Application.Common.Interfaces;
using Application.Common.Logging;
using Application.Common.Models;
using Application.Common.Operations;
using Application.Common.Otp;
using Application.Common.Serialization;
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
using System.Security.Cryptography;
using System.Text.Json;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.

namespace Application.UnitTests.Operations
{
    /// <summary>
    /// Field-level encryption of stored payloads, driven through the real service against a real
    /// database context with the real cipher.
    /// <para>
    /// The assertions that matter here read <c>PendingOperation.Payload</c> <strong>directly off the
    /// row</strong>. Everything else about this feature could work while it silently stored
    /// plaintext, and no amount of round-trip testing would notice — a round trip passes just as
    /// happily with no encryption at all.
    /// </para>
    /// <para>
    /// Separate fixture from <c>VerifiedOperationFlowTests</c> because the registry here is built
    /// from a local fixture command rather than the real assembly: proving this needs an operation
    /// carrying <c>[SensitiveData]</c>, and adding an enum member for one would put an unreachable
    /// value into the published contract.
    /// </para>
    /// </summary>
    [TestFixture]
    public class VerifiedOperationEncryptionTests
    {
        private const VerifiableOperationType Operation = VerifiableOperationType.DeleteLoanApplication;
        private const string StaticCode = "123456";
        private const string UserId = "user-id";
        private const string Password = "hunter2-plaintext";
        private const string Token = "issued-token-plaintext";

        private ServiceProvider _provider;
        private ApplicationDbContext _context;
        private StubOptions<PayloadProtectionOptions> _payloadOptions;

        /// <summary>
        /// <c>Password</c> and <c>token</c> are both on <c>LogRedactor.DefaultSensitiveProperties</c>,
        /// so this type satisfies the registry's redaction check. <c>LoanId</c> is deliberately not
        /// marked — it is what proves the encryption is per-property rather than whole-blob.
        /// </summary>
        [VerifiableOperation(Operation)]
        public record SecretBearingCommand : IRequest<SecretResult>
        {
            public int LoanId { get; init; }

            [SensitiveData]
            public string? Password { get; init; }
        }

        public record SecretResult
        {
            public int LoanId { get; init; }

            [SensitiveData]
            public string? Token { get; init; }
        }

        /// <summary>Records what the handler was actually handed, to prove it received plaintext.</summary>
        public static string? LastSeenPassword;

        public class SecretBearingCommandHandler : IRequestHandler<SecretBearingCommand, SecretResult>
        {
            public Task<SecretResult> Handle(SecretBearingCommand request, CancellationToken cancellationToken)
            {
                LastSeenPassword = request.Password;
                return Task.FromResult(new SecretResult { LoanId = request.LoanId, Token = Token });
            }
        }

        [SetUp]
        public void SetUp()
        {
            LastSeenPassword = null;

            var services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
            services.AddScoped<AuditableEntityInterceptor>();

            services.AddDbContext<ApplicationDbContext>((sp, options) => options
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));

            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IOtpVerificationRepository, OtpVerificationRepository>();
            services.AddScoped<IPendingOperationRepository, PendingOperationRepository>();

            services.AddSingleton<IOptionsMonitor<OtpOptions>>(new StubOptions<OtpOptions>(
                new OtpOptions { Secret = "test-secret", StaticCode = StaticCode, ResendCooldown = TimeSpan.Zero }));
            _payloadOptions = new StubOptions<PayloadProtectionOptions>(
                new PayloadProtectionOptions { Secret = "test-payload-secret" });
            services.AddSingleton<IOptionsMonitor<PayloadProtectionOptions>>(_payloadOptions);

            services.AddSingleton<IOtpCodeHasher, HmacOtpCodeHasher>();
            services.AddSingleton<IPayloadProtector, AesGcmPayloadProtector>();
            services.AddSingleton<IProtectedPayloadSerializer, ProtectedPayloadSerializer>();
            services.AddSingleton<IDateTime, Clock>();
            services.AddSingleton<IVerificationCodeSender, SilentSender>();

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

            services.AddSingleton<IVerifiableOperationRegistry>(_ => VerifiableOperationRegistry.Build(
                [typeof(SecretBearingCommand)],
                payloadProtectionConfigured: true,
                LogRedactor.DefaultSensitiveProperties));

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(VerifiedOperationEncryptionTests).Assembly);
                cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            });

            _provider = services.BuildServiceProvider();
            _context = _provider.GetRequiredService<ApplicationDbContext>();
        }

        [TearDown]
        public void TearDown() => _provider.Dispose();

        private IVerifiableOperationService Operations => _provider.GetRequiredService<IVerifiableOperationService>();

        private static JsonElement Payload() =>
            JsonSerializer.SerializeToElement(new { loanId = 42, password = Password });

        private PendingOperation Row(Guid operationId) =>
            _context.PendingOperations.AsNoTracking().Single(o => o.OperationId == operationId);

        /// <summary>
        /// The load-bearing assertion of the whole feature. If the plaintext is in the row, nothing
        /// else here matters.
        /// </summary>
        [Test]
        public async Task Initiate_EncryptsMarkedPropertiesButNotTheRest()
        {
            var pending = await Operations.InitiateAsync(Operation, VerificationChannel.Sms, Payload());

            var stored = Row(pending.OperationId).Payload;

            Assert.That(stored, Does.Not.Contain(Password), "the marked property must not be readable in the row");

            // Per-property, not whole-blob: an unmarked field stays plain so a pending row can still
            // be understood at a glance. A refactor to blob encryption would fail here, deliberately.
            Assert.That(stored, Does.Contain("42"), "an unmarked property must stay readable");
            Assert.That(stored, Does.Contain("v1:"), "the marked property should carry the cipher's version prefix");
        }

        [Test]
        public async Task Confirm_HandsTheHandlerThePlaintext()
        {
            var pending = await Operations.InitiateAsync(Operation, VerificationChannel.Sms, Payload());

            var result = await Operations.ConfirmAsync(pending.OperationId, StaticCode);

            Assert.That(result.Status, Is.EqualTo(PendingOperationStatus.Succeeded));
            Assert.That(LastSeenPassword, Is.EqualTo(Password), "the round trip must restore the original value");
        }

        /// <summary>
        /// Results are <em>not</em> encrypted, and this pins that down deliberately rather than by
        /// omission. Protecting them broke the endpoint — <c>ToResult</c> returns the stored text
        /// verbatim, so callers got ciphertext — and bought little, since a result is returned to
        /// the caller and captured in the response log either way. The rule instead is: do not
        /// return secrets.
        /// </summary>
        [Test]
        public async Task Confirm_ReturnsThePlaintextResultToTheCaller()
        {
            var pending = await Operations.InitiateAsync(Operation, VerificationChannel.Sms, Payload());

            var result = await Operations.ConfirmAsync(pending.OperationId, StaticCode);

            Assert.That(result.Result!.Value.GetProperty("token").GetString(), Is.EqualTo(Token),
                "the caller must receive the handler's actual result, not its stored form");
        }

        /// <summary>
        /// The replay path returns the same stored result, so it has to be readable too — this is
        /// the case the original bug hit hardest, since a retry after a lost response is exactly
        /// when a client most needs the real value.
        /// </summary>
        [Test]
        public async Task ConfirmTwice_ReplaysAReadableResult()
        {
            var pending = await Operations.InitiateAsync(Operation, VerificationChannel.Sms, Payload());

            await Operations.ConfirmAsync(pending.OperationId, StaticCode);
            var replayed = await Operations.ConfirmAsync(pending.OperationId, StaticCode);

            Assert.That(replayed.Result!.Value.GetProperty("token").GetString(), Is.EqualTo(Token));
            Assert.That(Row(pending.OperationId).Payload, Is.Null,
                "the request payload is dropped once the operation completes");
        }

        /// <summary>
        /// A payload that will not decrypt must fail <em>closed</em>, and terminally. Rotating the
        /// secret is the realistic way to produce one — this cipher keeps no key history, which is
        /// the documented cost of choosing it over <c>IDataProtector</c>.
        /// <para>
        /// Marked <c>Failed</c> rather than left <c>Pending</c> because by this point the code has
        /// been spent: <c>VerifyAsync</c> runs before binding, so a retry would cost the caller
        /// another attempt against a row that can never bind.
        /// </para>
        /// </summary>
        [Test]
        public async Task Confirm_AfterTheSecretIsRotated_FailsClosedAndDoesNotRun()
        {
            var pending = await Operations.InitiateAsync(Operation, VerificationChannel.Sms, Payload());

            _payloadOptions.CurrentValue = new PayloadProtectionOptions { Secret = "a-completely-different-secret" };

            Assert.That(async () => await Operations.ConfirmAsync(pending.OperationId, StaticCode),
                Throws.InstanceOf<DomainValidationException>());

            Assert.That(LastSeenPassword, Is.Null, "the handler must never run on a payload that would not decrypt");
            Assert.That(Row(pending.OperationId).Status, Is.EqualTo(PendingOperationStatus.Failed));
        }

        /// <summary>
        /// Editing a stored row does not reach the cipher at all — it is caught earlier, by the
        /// request hash <c>VerifyAsync</c> checks against the value recorded when the challenge was
        /// issued. Worth pinning down, because it is easy to assume the authenticated cipher is what
        /// stops a tampered row, and to then weaken the hash on that assumption.
        /// <para>
        /// The operation stays <c>Pending</c>: nothing has been decided about it, and the edit is
        /// rejected the same way a swapped request body is on the gated path.
        /// </para>
        /// </summary>
        [Test]
        public async Task Confirm_WhenTheStoredRowWasEdited_IsRejectedByTheRequestHashBeforeDecrypting()
        {
            var pending = await Operations.InitiateAsync(Operation, VerificationChannel.Sms, Payload());

            // Written through EF because Payload has a private setter -- which is the point of it.
            var tracked = _context.PendingOperations.Single(o => o.OperationId == pending.OperationId);
            var edited = JsonSerializer.Serialize(new
            {
                loanId = 99,
                password = "v1:" + Convert.ToBase64String(new byte[40])
            });
            _context.Entry(tracked).Property(nameof(PendingOperation.Payload)).CurrentValue = edited;
            await _context.SaveChangesAsync();

            Assert.That(async () => await Operations.ConfirmAsync(pending.OperationId, StaticCode),
                Throws.InstanceOf<DomainValidationException>());

            Assert.That(LastSeenPassword, Is.Null);
            Assert.That(Row(pending.OperationId).Status, Is.EqualTo(PendingOperationStatus.Pending),
                "the hash check rejects the code, so the operation itself is never resolved either way");
        }

        /// <summary>
        /// The deployment case: a payload written <em>before</em> a property was marked
        /// <c>[SensitiveData]</c> holds a bare value where the converter now expects a protected
        /// string. Left unguarded, a non-string one throws <c>InvalidOperationException</c> out of
        /// <c>Utf8JsonReader.GetString()</c>, which <c>BindStoredAsync</c> does not catch — a 500
        /// and a permanently stuck operation instead of a clean failure.
        /// <para>
        /// Driven through the serializer directly rather than the full flow, because reaching it
        /// end to end would need a row whose request hash was computed over the old plaintext, and
        /// that state cannot be produced by this build.
        /// </para>
        /// </summary>
        [Test]
        public void Deserialize_WhenAMarkedPropertyHoldsAPreEncryptionValue_ThrowsCryptographic()
        {
            var serializer = _provider.GetRequiredService<IProtectedPayloadSerializer>();

            Assert.That(
                () => serializer.Deserialize("""{"loanId":42,"password":12345}""", typeof(SecretBearingCommand)),
                Throws.InstanceOf<CryptographicException>(),
                "a non-string where a protected value belongs must fail as an unreadable payload, not as a crash");

            // The string case already degraded correctly -- kept alongside so the two stay
            // consistent if the converter is ever touched again.
            Assert.That(
                () => serializer.Deserialize("""{"loanId":42,"password":"plain-text-from-before"}""", typeof(SecretBearingCommand)),
                Throws.InstanceOf<CryptographicException>());
        }

        private sealed class SilentSender : IVerificationCodeSender
        {
            public VerificationChannel Channel => VerificationChannel.Sms;
            public Task SendAsync(string recipient, string message, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }

        private sealed class Clock : IDateTime
        {
            public DateTime UtcNow => DateTime.UtcNow;
            public DateTime LocalNow => DateTime.Now;
        }

        private sealed class StubOptions<T> : IOptionsMonitor<T>
        {
            public StubOptions(T value) => CurrentValue = value;

            /// <summary>Settable so a test can rotate the secret mid-flow.</summary>
            public T CurrentValue { get; set; }

            public T Get(string? name) => CurrentValue;
            public IDisposable? OnChange(Action<T, string?> listener) => null;
        }
    }
}
