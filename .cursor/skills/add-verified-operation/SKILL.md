---
name: add-verified-operation
description: >-
  Exposes an existing MediatR command through the generic initiate/confirm
  endpoints via [VerifiableOperation], so the client sends its payload once and
  confirms with a code. Use when adding OTP/2FA to an operation whose caller
  should not have to re-send the request body.
---

# Add a verified operation

Opting in is **an enum member and an attribute** — no new endpoint, no handler change, no members
on the command.

## Choose the mechanism first

Two mechanisms exist. **Never put both on one command** — startup throws if you do.

| Situation | Use |
|---|---|
| Payload is sensitive (personal data, credentials) | `IRequireOtpVerification` → skill `add-otp-gate` |
| Client can re-send the payload with the code | `IRequireOtpVerification` |
| Client should not have to re-send it (2 calls, not 2 identical ones) | **this skill** |
| Operation is dispatched outside HTTP (a job) | `IRequireOtpVerification` — the endpoints cannot reach it |

The deciding difference: this mechanism **stores the request body in the database, unencrypted**,
between the two calls. That is a recorded trade (see `docs/plans/06-generic-verified-operations.md`),
and routing sensitive operations to the other mechanism is its mitigation.

## Steps

1. Add a member to `Application.Common.Operations.VerifiableOperationType`, then put
   `[VerifiableOperation(VerifiableOperationType.YourOperation)]` on the command. The member name is
   the client's `operationType` **and** the OTP purpose, so a code issued for one operation can
   never be spent on another.
   - **Renaming a member later is a breaking change, not a refactor.** Names are persisted in
     `PendingOperations.OperationType` and hashed as the OTP purpose, so in-flight operations and
     issued codes stop resolving. Add a new member and retire the old one instead.
   - Numeric values are never persisted or transmitted; reorder them freely. They start at 1 so an
     omitted `operationType` is a 400 rather than whichever operation sits at 0.
   - Adding a member exposes nothing on its own — only the attribute does.
2. Optional attribute properties:
   - `RequiredPolicies = ["SomePolicy"]` — checked at initiate **and again** at confirm.
   - `RequiresAuthentication = false` — only for flows with no account yet.
   - `AllowsCallerSuppliedRecipient = true` — only when there is no account to read a number
     from. Left false, a caller who sends `recipient` is **rejected**, not ignored.
3. **Remove the command's direct endpoint.** Registering an operation does *not* gate an existing
   route — leaving one makes the confirmation optional and therefore decorative. See
   `LoanApplicationController`, where `DeleteApplication` was removed for exactly this reason.
4. Keep the command's FluentValidation validator. It runs at initiate, before a code is spent,
   and again at confirm through the pipeline.
5. **Add the payload to the TypeScript map** in `WebApi/ApiClient/verified-operations.ts` — one line.
   That file is hand-written (the generated client cannot express it, see below) and its map is
   keyed by `VerifiableOperationType`, so skipping this step is a compile error on the client rather
   than a runtime surprise. Then rebuild `Debug` **without** `SkipNSwag` so the payload schema
   reaches `web-api-client.ts`.
6. Manual/API test:
   - `POST api/v1/Verification/Initiate` `{ "operationType": "...", "channel": "Sms", "payload": { ... } }`
     → `operationId`, `challengeId`, `expiresAt`
   - `POST api/v1/Verification/Confirm` `{ "operationId": "...", "code": "123456" }` → runs it
   - `POST api/v1/Verification/Resend` `{ "operationId": "..." }` if the message was lost
7. Dev: read the code from the logs (`LoggingSmsCodeSender`), or set `Otp:StaticCode` in
   `appsettings.Development.json` and always answer with that.
8. Check the boot log: `Registered N verifiable operations: ...` — that is the allowlist, and
   yours should be in it.

## Reference implementation

`DeleteApplicationCommand` — `[VerifiableOperation("DeleteLoanApplication")]`, a **void**
(`IRequest`) command with its direct route removed. `VerifiedOperationFlowTests` drives it end to
end against a real container.

## Do not

- Add `IRequireOtpVerification` as well — startup throws, because dispatching such a command at
  confirm re-enters the gate and issues a second challenge that can never be answered.
- Call `IVerifiableOperationService` from a handler; the endpoints own the flow.
- Leave the old direct endpoint in place (step 3).
- Register an operation carrying sensitive data — use `add-otp-gate` instead.
- Assume the operation is safe to run twice. A crash between verification and commit rolls the
  work back **only** for handlers that stay inside the database; one calling an external system
  needs its own idempotency.

## Gotchas

- **Void commands are supported.** `IRequest` and `IRequest<T>` are unrelated types in
  MediatR.Contracts 2.x, and the registry accepts both.
- `OperationResultDto.Result` is `any` in the generated TS client — inherent to one endpoint
  returning every operation's result. Clients wanting a typed result should re-fetch the resource.
- **`payload` can never be discriminated by OpenAPI**, so don't try. Its shape depends on a
  *sibling* property, and `oneOf` + `discriminator` requires the discriminator inside the schema
  being discriminated. `VerifiableOperationPayloadsDocumentProcessor` goes as far as the format
  allows — it emits every payload type as a named schema (nothing else references them, since
  registering an operation *removes* its route) and narrows the published enum to registered
  operations. The link itself lives in `verified-operations.ts`, because TypeScript can express what
  OpenAPI cannot.
- **Don't "fix" this with per-operation typed endpoints.** They would type themselves and delete the
  reason the generic endpoint exists. An operation that genuinely needs its own typed route is an
  operation that wants `IRequireOtpVerification`.
- Nothing deletes `PendingOperations` rows; there is a DELETE script in the phase 6 plan to
  schedule outside the app.

Also see: `.cursor/rules/verified-operations.mdc`, skill `add-otp-gate`.
