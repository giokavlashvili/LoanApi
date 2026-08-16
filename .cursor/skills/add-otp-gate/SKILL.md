---
name: add-otp-gate
description: >-
  Adds two-step OTP verification to an existing MediatR command via
  IRequireOtpVerification. Use when requiring phone confirmation, OTP, 2FA,
  or 428 challenge flow on a command.
---

# Add OTP gate to a command

Opting in is **one interface** — no per-feature OTP services or controller changes.

## Choose the mechanism first

There are two, sharing one core. **Never put both on one command** — startup throws.

- **This one** when the payload is sensitive (it is never persisted), when the client can re-send
  the body with the code, or when the command is ever dispatched outside HTTP.
- **`[VerifiableOperation]`** (skill `add-verified-operation`) when the client should not have to
  re-send the body. It stores the payload, unencrypted, between the two calls.

## Steps

1. On the command record, implement `Application.Common.Otp.IRequireOtpVerification`.
2. Add:
   - `Guid? ChallengeId { get; init; }` (or `set`)
   - `[SensitiveData] string? OtpCode { get; init; }`
3. **Recipient**
   - Default: leave `OtpRecipient` unset (`null`) → authenticated user's phone.
   - Registration / no user yet: override `OtpRecipient => PhoneNumber` (see `RegisterUserCommand`).
4. Do **not** override `OtpPurpose` unless you need a shared purpose across types (default = command type name).
5. Ensure OTP property names are in the redactor's merged set (`LogRedactor.DefaultSensitiveProperties`
   plus `RequestLogging:SensitiveProperties` — config is additive). Names already on the defaults
   (`otpCode`, `otp`, `code`) need no extra config.
6. Validator: allow null `OtpCode`/`ChallengeId` on first call; validate format when present.
7. Manual/API test: first call → **428** + `challengeId`/`expiresAt`; second call with code → handler runs.
8. Dev: read code from logs (`LoggingSmsCodeSender`), not SMS.

## Reference implementations

- `RegisterUserCommand` — explicit recipient
- `UpdateApplicationStatusCommand` — account phone
- `DeleteApplicationCommand` — the *other* mechanism, for contrast

## Do not

- Call `IOtpService` from the handler (behavior owns the gate)
- Save verify attempts only on success (must be `finally` in `OtpService`)
- Put live codes in logs / PerformanceBehavior output without redaction
