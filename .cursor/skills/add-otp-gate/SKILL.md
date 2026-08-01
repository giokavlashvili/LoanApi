---
name: add-otp-gate
description: >-
  Adds two-step OTP verification to an existing MediatR command via
  IRequireOtpVerification. Use when requiring phone confirmation, OTP, 2FA,
  or 428 challenge flow on a command.
---

# Add OTP gate to a command

Opting in is **one interface** — no per-feature OTP services or controller changes.

## Steps

1. On the command record, implement `Application.Common.Otp.IRequireOtpVerification`.
2. Add:
   - `Guid? ChallengeId { get; init; }` (or `set`)
   - `[SensitiveData] string? OtpCode { get; init; }`
3. **Recipient**
   - Default: leave `OtpRecipient` unset (`null`) → authenticated user's phone.
   - Registration / no user yet: override `OtpRecipient => PhoneNumber` (see `RegisterUserCommand`).
4. Do **not** override `OtpPurpose` unless you need a shared purpose across types (default = command type name).
5. Ensure OTP property names appear in **both**:
   - `RequestLogging:SensitiveProperties` (`appsettings.json`)
   - `LogRedactor.DefaultSensitiveProperties`
6. Validator: allow null `OtpCode`/`ChallengeId` on first call; validate format when present.
7. Manual/API test: first call → **428** + `challengeId`/`expiresAt`; second call with code → handler runs.
8. Dev: read code from logs (`LoggingSmsCodeSender`), not SMS.

## Reference implementations

- `RegisterUserCommand` — explicit recipient
- `UpdateApplicationStatusCommand` — account phone

## Do not

- Call `IOtpService` from the handler (behavior owns the gate)
- Save verify attempts only on success (must be `finally` in `OtpService`)
- Put live codes in logs / PerformanceBehavior output without redaction
