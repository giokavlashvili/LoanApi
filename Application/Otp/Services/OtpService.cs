using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Otp.Dtos;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Application.Otp.Services
{
    /// <summary>
    /// Issues and redeems one time passwords. Knows nothing about what any given code protects —
    /// the purpose string is opaque here, which is what lets one implementation serve every
    /// operation that opts in.
    /// <para>
    /// Lives in <c>Application</c> because it holds no infrastructure: every collaborator below
    /// is an abstraction, and the two that are mechanism — <c>IOtpCodeHasher</c> (crypto) and
    /// <c>ISmsSender</c> (a vendor adapter) — stay in <c>Infrastructure</c>. That split is the
    /// point: the policy of how many codes, for how long, and how many attempts is a use case
    /// decision; hashing and delivery are not.
    /// </para>
    /// </summary>
    public class OtpService : IOtpService
    {
        private readonly IOtpVerificationRepository _challenges;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IOtpCodeHasher _codeHasher;
        private readonly IEnumerable<IVerificationCodeSender> _senders;
        private readonly IDateTime _dateTime;
        private readonly IOptionsMonitor<OtpOptions> _optionsMonitor;
        private readonly ILogger<OtpService> _logger;

        public OtpService(
            IOtpVerificationRepository challenges,
            IUnitOfWork unitOfWork,
            IOtpCodeHasher codeHasher,
            IEnumerable<IVerificationCodeSender> senders,
            IDateTime dateTime,
            IOptionsMonitor<OtpOptions> optionsMonitor,
            ILogger<OtpService> logger)
        {
            _challenges = challenges;
            _unitOfWork = unitOfWork;
            _codeHasher = codeHasher;
            _senders = senders;
            _dateTime = dateTime;
            _optionsMonitor = optionsMonitor;
            _logger = logger;
        }

        public async Task<OtpChallengeDto> IssueAsync(
            string purpose,
            string recipient,
            VerificationChannel channel,
            string? userId,
            string requestHash,
            CancellationToken cancellationToken = default)
        {
            var options = _optionsMonitor.CurrentValue;
            var now = _dateTime.UtcNow;

            // Resolved before anything is written. An unregistered channel must fail before a
            // challenge exists, not after — a stored challenge whose code was never delivered is
            // indistinguishable to the user from one that was.
            var sender = ResolveSender(channel);

            var previous = await _challenges.GetLatestAsync(recipient, purpose, cancellationToken);

            EnsureCooldownElapsed(options, previous, now);

            await EnsureHourlyCapAsync(options, recipient, purpose, now, cancellationToken);

            // Only one code may be live per recipient and purpose, otherwise an older message
            // arriving late still opens the operation the newer one was meant to gate.
            // Loaded tracked, so mutating it is what persists — no Update call needed.
            previous?.Invalidate(now);

            // The id is part of the code hash, so it has to exist before the code is hashed.
            var challengeId = Guid.NewGuid();
            var code = ResolveCode(options);

            var entity = OtpVerification.Create(
                challengeId,
                purpose,
                recipient,
                channel,
                userId,
                _codeHasher.Hash(challengeId, code),
                requestHash,
                now.Add(options.Lifetime),
                options.MaxAttempts,
                now);

            await _challenges.AddAsync(entity, cancellationToken);

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) // lost the race to another concurrent issue
            {
                throw new DomainValidationException("OtpThrottled");
            }

            // Sent only after the challenge is committed. Domain events dispatch before the save,
            // so texting from an event handler would hand out codes for challenges a failed save
            // never stored, and the user would have no way to tell.
            //
            // This relies on the save above having actually committed, which is why every command
            // reaching here from its own handler carries ISkipTransaction — see ResendOtpCommand.
            await sender.SendAsync(recipient, string.Format(options.MessageTemplate, code), cancellationToken);

            return ToDto(entity);
        }

        public async Task<OtpChallengeDto> ResendAsync(Guid challengeId, CancellationToken cancellationToken = default)
        {
            var existing = await _challenges.GetByChallengeIdAsync(challengeId, cancellationToken)
                ?? throw new DomainValidationException("OtpChallengeNotFound");

            // Reissued against the original purpose, recipient, channel and payload, so a resend
            // cannot move a confirmation onto a different number or a different request.
            return await IssueAsync(
                existing.Purpose!,
                existing.Recipient!,
                existing.Channel,
                existing.UserId,
                existing.RequestHash!,
                cancellationToken);
        }

        public async Task VerifyAsync(
            Guid challengeId,
            string purpose,
            string code,
            string requestHash,
            CancellationToken cancellationToken = default)
        {
            var entity = await _challenges.GetByChallengeIdAsync(challengeId, cancellationToken)
                ?? throw new DomainValidationException("OtpChallengeNotFound");

            // Checked before the code: a challenge issued for registration must not be spendable
            // on a loan approval even by someone holding a valid code for it.
            if (!string.Equals(entity.Purpose, purpose, StringComparison.Ordinal))
                throw new DomainValidationException("OtpPurposeMismatch");

            try
            {
                entity.Verify(_codeHasher.Hash(challengeId, code), requestHash, _dateTime.UtcNow);
            }
            finally
            {
                // In a finally, not after the call: Verify increments AttemptCount and then throws
                // on a wrong code. Saving only on the happy path would roll that increment back
                // every time, MaxAttempts would never be reached, and six digits would be
                // brute-forceable at leisure. Same guarantee LoggingMiddleware uses to always
                // write its row. The challenge is tracked, so the increment persists without an
                // Update call.
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Picks the sender registered for a channel. Resolved from the set rather than by keyed
        /// injection because the channel is only known per call — keying the constructor would mean
        /// injecting <c>IServiceProvider</c> into an Application service to look it up anyway.
        /// </summary>
        private IVerificationCodeSender ResolveSender(VerificationChannel channel)
        {
            var sender = _senders.FirstOrDefault(s => s.Channel == channel);

            if (sender is null)
            {
                // Not a DomainValidationException: an unregistered channel is a misconfiguration,
                // not something the caller did wrong, and a localized 400 would hide it.
                throw new InvalidOperationException(
                    $"No {nameof(IVerificationCodeSender)} is registered for channel '{channel}'. " +
                    "Only Sms ships with this template; see phase 6 task 1 for what Email needs.");
            }

            return sender;
        }

        /// <summary>Stops a caller hammering the send button into a stream of messages.</summary>
        private static void EnsureCooldownElapsed(OtpOptions options, OtpVerification? previous, DateTime now)
        {
            if (previous is not null
                && options.ResendCooldown > TimeSpan.Zero
                && previous.Created.Add(options.ResendCooldown) > now)
            {
                throw new DomainValidationException("OtpThrottled");
            }
        }

        private async Task EnsureHourlyCapAsync(
            OtpOptions options,
            string recipient,
            string purpose,
            DateTime now,
            CancellationToken cancellationToken)
        {
            if (options.MaxPerRecipientPerHour <= 0)
                return;

            var issuedInWindow = await _challenges.CountRecentAsync(
                recipient, purpose, now.AddHours(-1), cancellationToken);

            // Without this the endpoint is an open SMS relay: anyone can point it at any number
            // and bill the messages to whoever owns the provider account.
            if (issuedInWindow >= options.MaxPerRecipientPerHour)
                throw new DomainValidationException("OtpThrottled");
        }

        /// <summary>
        /// Returns <see cref="OtpOptions.StaticCode"/> when configured, otherwise a fresh random
        /// code. Every call is a chance for a stray Development setting to survive into a real
        /// deployment, so this warns loudly rather than staying quiet the way the real path does.
        /// </summary>
        private string ResolveCode(OtpOptions options)
        {
            if (string.IsNullOrEmpty(options.StaticCode))
                return GenerateCode(options.CodeLength);

            _logger.LogWarning(
                "Otp:StaticCode is configured — issuing the fixed test code instead of a random one. " +
                "This must never be set outside local development.");

            return options.StaticCode;
        }

        /// <summary>
        /// Uniform over the whole range. <see cref="Random"/> is seeded predictably enough that
        /// codes could be guessed without ever seeing one.
        /// </summary>
        private static string GenerateCode(int length)
        {
            var digits = new char[length];

            for (var i = 0; i < length; i++)
                digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));

            return new string(digits);
        }

        private static OtpChallengeDto ToDto(OtpVerification entity) => new()
        {
            ChallengeId = entity.ChallengeId,
            ExpiresAt = entity.ExpiresAt,
            Recipient = Mask(entity.Recipient),
            MaxAttempts = entity.MaxAttempts
        };

        /// <summary>
        /// Keeps the last four digits so the user can tell which of their numbers was texted,
        /// and hides the rest so the response does not disclose it to anyone else.
        /// </summary>
        private static string? Mask(string? recipient)
        {
            if (string.IsNullOrWhiteSpace(recipient))
                return recipient;

            const int visible = 4;

            if (recipient.Length <= visible)
                return new string('*', recipient.Length);

            return new string('*', recipient.Length - visible) + recipient[^visible..];
        }
    }
}
