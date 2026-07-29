using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Otp.Dtos;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Infrastructure.Services
{
    /// <summary>
    /// Issues and redeems one time passwords. Knows nothing about what any given code protects —
    /// the purpose string is opaque here, which is what lets one implementation serve every
    /// operation that opts in.
    /// </summary>
    public class OtpService : IOtpService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IOtpCodeHasher _codeHasher;
        private readonly ISmsSender _smsSender;
        private readonly IDateTime _dateTime;
        private readonly IOptionsMonitor<OtpOptions> _optionsMonitor;
        private readonly ILogger<OtpService> _logger;

        public OtpService(
            IUnitOfWork unitOfWork,
            IOtpCodeHasher codeHasher,
            ISmsSender smsSender,
            IDateTime dateTime,
            IOptionsMonitor<OtpOptions> optionsMonitor,
            ILogger<OtpService> logger)
        {
            _unitOfWork = unitOfWork;
            _codeHasher = codeHasher;
            _smsSender = smsSender;
            _dateTime = dateTime;
            _optionsMonitor = optionsMonitor;
            _logger = logger;
        }

        public async Task<OtpChallengeDto> IssueAsync(
            string purpose,
            string recipient,
            string? userId,
            string requestHash,
            CancellationToken cancellationToken = default)
        {
            var options = _optionsMonitor.CurrentValue;
            var now = _dateTime.Now;

            var previous = await _unitOfWork.OtpVerificationRepository.GetLatestAsync(recipient, purpose, cancellationToken);

            EnsureCooldownElapsed(options, previous, now);

            await EnsureHourlyCapAsync(options, recipient, purpose, now, cancellationToken);

            // Only one code may be live per recipient and purpose, otherwise an older message
            // arriving late still opens the operation the newer one was meant to gate.
            if (previous is not null)
            {
                previous.Invalidate(now);
                _unitOfWork.OtpVerificationRepository.Update(previous);
            }

            // The id is part of the code hash, so it has to exist before the code is hashed.
            var challengeId = Guid.NewGuid();
            var code = ResolveCode(options);

            var entity = OtpVerification.Create(
                challengeId,
                purpose,
                recipient,
                userId,
                _codeHasher.Hash(challengeId, code),
                requestHash,
                now.Add(options.Lifetime),
                options.MaxAttempts,
                now);

            await _unitOfWork.OtpVerificationRepository.AddAsync(entity, cancellationToken);

            await _unitOfWork.SaveAsync(cancellationToken);

            // Sent only after the challenge is committed. Domain events dispatch before the save,
            // so texting from an event handler would hand out codes for challenges a failed save
            // never stored, and the user would have no way to tell.
            await _smsSender.SendAsync(recipient, string.Format(options.MessageTemplate, code), cancellationToken);

            return ToDto(entity);
        }

        public async Task<OtpChallengeDto> ResendAsync(Guid challengeId, CancellationToken cancellationToken = default)
        {
            var existing = await _unitOfWork.OtpVerificationRepository.GetByChallengeIdAsync(challengeId, cancellationToken)
                ?? throw new DomainValidationException("OtpChallengeNotFound");

            // Reissued against the original purpose, recipient and payload, so a resend cannot
            // move a confirmation onto a different number or a different request.
            return await IssueAsync(
                existing.Purpose!,
                existing.Recipient!,
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
            var entity = await _unitOfWork.OtpVerificationRepository.GetByChallengeIdAsync(challengeId, cancellationToken)
                ?? throw new DomainValidationException("OtpChallengeNotFound");

            // Checked before the code: a challenge issued for registration must not be spendable
            // on a loan approval even by someone holding a valid code for it.
            if (!string.Equals(entity.Purpose, purpose, StringComparison.Ordinal))
                throw new DomainValidationException("OtpPurposeMismatch");

            try
            {
                entity.Verify(_codeHasher.Hash(challengeId, code), requestHash, _dateTime.Now);
            }
            finally
            {
                // In a finally, not after the call: Verify increments AttemptCount and then throws
                // on a wrong code. Saving only on the happy path would roll that increment back
                // every time, MaxAttempts would never be reached, and six digits would be
                // brute-forceable at leisure. Same guarantee LoggingMiddleware uses to always
                // write its row.
                _unitOfWork.OtpVerificationRepository.Update(entity);

                await _unitOfWork.SaveAsync(cancellationToken);
            }
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

            var issuedInWindow = await _unitOfWork.OtpVerificationRepository.CountRecentAsync(
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
