# Phase 6 — Generic initiate/confirm verified operations

**Depends on:** nothing in phases 1–5. **EF migration:** yes (two). **Size:** medium.
**Additive** — no existing behaviour changes.

> Design document. Read `00-shared-context.md` first. Nothing here is implemented yet.
>
> A second way to two-factor an operation, alongside the existing `IRequireOtpVerification`
> gate. Both share one core: `OtpService`, `OtpVerification`, throttling, attempt budgets,
> expiry and hashing are untouched. This phase adds a generic `initiate`/`confirm` endpoint pair
> that stores the request and executes it once a code is confirmed.
>
> A third option — a `[RequiresOtp]` MVC filter — is designed in phase 7 and **is not being
> implemented**. Phase 7 remains on disk as a record of that design.

## Why

`OtpVerificationBehavior` gates MediatR commands and requires the client to re-send the whole
payload with the code. That is the right shape when the client holds the payload, but it forces
`ChallengeId`/`OtpCode` onto every gated command's DTO and only works for MediatR requests.

This phase adds a two-call flow that works for any registered operation:

```
POST /api/v1/Verification/Initiate   { operationType, payload }  → operationId, challengeId
POST /api/v1/Verification/Confirm    { operationId, code }       → executes, returns the result
```

## Stated assumptions — read before changing anything here

These are **deliberate decisions**, not oversights. A future reader who tightens one of them
should know what it was traded for.

1. **Stored payloads are not encrypted.** Between `initiate` and `confirm` the request body sits
   in the database as plain text. Accepted because database access is controlled at the
   infrastructure level. The mitigation for anything genuinely sensitive is **route it through
   `IRequireOtpVerification` instead** — that mechanism never persists a payload. Say so in the
   endpoint's XML docs so the choice is visible at the point of use.
2. **Nothing deletes old rows.** No hosted service, no sweep. `PendingOperation` grows without
   bound; see *Retention* below for the DELETE script to schedule outside the app.
3. **SMS only.** The channel abstraction and the `Channel` column ship now so email can be added
   without a second migration, but no email sender is implemented and no email recipient can be
   resolved — see task 1.
4. **A crash mid-execution rolls the work back — for database-only handlers.** `TransactionBehavior`
   landed after this plan was first written (commits `e52f5a1`, `589ce84`), so the execution and
   the `Succeeded` write commit together. A crash therefore leaves the operation `Pending` with
   the work definitively *not* done. A handler that calls an external system — payment provider,
   another API — cannot have that call rolled back, so for those the outcome is still unknown and
   the handler needs its own idempotency. See task 5.

---

## Task 1 — Channel abstraction (SMS only, room for email)

`ISmsSender` becomes channel-resolved. The abstraction and the column land now; the email
implementation is a later phase.

```csharp
public enum VerificationChannel { Sms = 0, Email = 1 }

public interface IVerificationCodeSender
{
    VerificationChannel Channel { get; }
    Task SendAsync(string recipient, string code, CancellationToken cancellationToken = default);
}
```

```csharp
services.AddKeyedTransient<IVerificationCodeSender, LoggingSmsCodeSender>(VerificationChannel.Sms);
// Email is deliberately not registered. Resolving it must fail loudly, not send nothing.
```

Schema, in the same migration so email costs no second one later:

- `OtpVerification.Channel` — required, defaults to `Sms` for existing rows.
- `OtpVerification.Recipient` — `HasMaxLength(32)` → **256**. An email address does not fit in 32,
  and widening later would mean rebuilding two indexes. Both indexes stay within SQL Server's
  1700-byte nonclustered key limit at the new width (~776 bytes worst case).
- The filtered pending-uniqueness index becomes `(Recipient, Purpose, Channel)`.

`OtpOptions.MessageTemplate` stays a single string. It becomes a per-channel section when email
lands, because an email needs a subject and both an HTML and a text body — noted here so that
change is expected rather than discovered.

**Why email is more than a sender class.** Three concrete blockers, all verified:

- `Application.Common.Models.User` has **no `Email` property** — only `PhoneNumber`. Recipient
  resolution goes through `IUserService.GetUserByIdAsync`, so there is nothing to resolve.
- `JwtTokenGenerator` emits no email claim, so `ICurrentUserService.Email` **always returns
  null**. It looks usable and is not. Do not reach for it.
- `AddIdentityCore` sets `RequireUniqueEmail = false` and
  `Infrastructure/Common/Extensions/ConfigureServices.cs:73-80` explains why email is not part of
  the account at all.

Email therefore needs an `Email` input on registration, a format + uniqueness validator running
**before** the gate, `User.Email` plus the `IdentityService` projection, and a migration. Its own
phase.

---

## Task 2 — The operation registry

A generic endpoint accepting any `operationType` would be an open relay. Operations are
allowlisted by attribute, discovered by assembly scan — consistent with the repo's
convention-based DI while keeping the decision visible at the declaration site.

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class VerifiableOperationAttribute : Attribute
{
    public VerifiableOperationAttribute(string name) => Name = name;

    public string Name { get; }
    public bool RequiresAuthentication { get; init; } = true;
    public bool AllowsCallerSuppliedRecipient { get; init; } = false;
    public string[] RequiredPolicies { get; init; } = [];
}
```

```csharp
[VerifiableOperation("ApproveLoan", RequiredPolicies = ["CanApproveLoans"])]
public record ApproveLoanCommand : IRequest<ApproveLoanResult> { ... }
```

The descriptor built at startup holds the name, the payload `Type`, the policy flags, and a
prebuilt executor delegate so `confirm` does no reflection per call:

```csharp
// MediatR command — reuses ValidationBehavior and PerformanceBehavior for free.
// ISender.Send(object, CancellationToken) — "send an object request to a single handler via
// dynamic dispatch". Confirmed present in MediatR 14.2.0 when this phase was written; the
// solution has since been pinned back to 12.5.0 (last Apache-2.0 release) and the overload is
// present there too, so the built descriptor is unaffected.
Execute = (payload, sp, ct) => sp.GetRequiredService<ISender>().Send(payload, ct);
```

An unknown `operationType` is a **400 before anything is sent**.

`AllowsCallerSuppliedRecipient` defaults to `false`, so the recipient comes from the authenticated
user — the same rule `OtpVerificationBehavior.ResolveRecipientAsync` enforces, and the reason a
caller cannot redirect their own code to a phone they control.

### Startup validation — the recursion trap

Throw at boot if any registered operation type **implements `IRequireOtpVerification`**.
Dispatching such a command through `ISender` at confirm re-enters `OtpVerificationBehavior`,
issues a *second* challenge and throws 428 from inside the confirm call — the user verifies a code
and is asked for another, and every attempt costs two messages until `MaxPerRecipientPerHour`
locks them out.

Also validate at boot: name uniqueness, and that each payload type really is an `IRequest` /
`IRequest<T>` (`Send(object)` is dynamically dispatched, so a wrong type fails at runtime
otherwise).

---

## Task 3 — `PendingOperation` aggregate

`Domain/Entities/PendingOperation.cs`. Private setters, static `Create` factory throwing
`DomainValidationException` with localization keys, per the standing entity rules. It links to the
challenge by `ChallengeId`; `OtpVerification` is untouched beyond task 1.

| Member | Notes |
|---|---|
| `OperationId` (Guid) | Public handle. `BaseEntity.Id` is a sequential int and never leaves the server — same reasoning as `ChallengeId`. |
| `ChallengeId` (Guid) | The challenge that gates it. **Mutable** — see the resend fix in task 7. |
| `OperationType` (string, 128) | Registry key. |
| `Payload` (string) | The request body as JSON. Plain text — see assumption 1. |
| `Status` | `Pending`, `Succeeded`, `Failed`. That is the whole state machine. |
| `ResultPayload` (string?) | Stored outcome, for replay (task 5). |
| `ErrorKey` (string?) | Localization key when `Failed`. |
| `UserId` (string?) | Principal that initiated. |
| `ExecutedAt` (DateTime?) | |
| `RowVersion` (byte[]) | Conventional here (`OtpVerification` and `RefreshToken` both carry one). Not load-bearing — concurrency is settled on the challenge, see task 5. |

### Why there is no `Executing` status

Its only job would be blocking two concurrent confirms. That is **already handled**:
`OtpVerification` carries a `RowVersion` and `VerifyAsync` saves in a `finally`, so of two
concurrent confirms the second `SaveChanges` throws `DbUpdateConcurrencyException`, which
`ApiExceptionFilterAttribute` already maps to 409.

Adding `Executing` would buy nothing and cost a great deal: the lease has to be committed to be
visible, so it cannot be inside the transaction it guards, which means a crash strands the row in
`Executing` permanently — and recovering that needs a timeout, a release path, a reaper, and a
timeout value that must exceed the slowest possible execution or the operation runs twice. None of
that exists here because the status does not.

---

## Task 4 — `initiate`

`POST /api/v1/Verification/Initiate`

```jsonc
{ "operationType": "ApproveLoan", "payload": { /* operation-specific */ } }
```

In this order:

1. Resolve the descriptor. Unknown → **400**, before anything is sent.
2. Enforce `RequiresAuthentication` and `RequiredPolicies` (`IIdentityService.AuthorizeAsync`).
3. Bind the payload to **`descriptor.PayloadType`**. Never to a type named in the request —
   deserializing a caller-supplied type name is a remote code execution vector, and there is no
   version of this endpoint where that is acceptable.
4. **Run the payload's FluentValidation validators now.** Resolve `IValidator<T>` and execute it.
   This is `otp.mdc`'s standing rule applied to the new door: a rejection left until confirm costs
   an SMS *and* a code, and the payload is frozen at initiate, so fixing the input needs a whole
   new challenge.
5. Resolve the recipient — from the authenticated user unless `AllowsCallerSuppliedRecipient`.
6. Issue the challenge and store the operation.

`RequestHash` holds a **keyed hash of the stored payload** (`HmacOtpCodeHasher`). Its original job
— stopping the client swapping the payload between calls — does not apply here, since confirm
carries no payload. Instead it detects a payload row edited directly in the database, which is a
real check precisely because the payload is not encrypted.

Response: `{ operationId, challengeId, expiresAt, recipient (masked), maxAttempts }`.

### `InitiateOperationCommand` must carry `ISkipTransaction`

`IOtpService.IssueAsync` persists the challenge and then sends the SMS, relying on that save having
committed. Inside a transaction the save only flushes, so a commit failure afterwards rolls the
challenge back with the message already delivered — the code would be unverifiable. This is the
same reason `ResendOtpCommand` carries the marker.

The cost is that initiate performs two saves (challenge, then operation) with no transaction
between them. If the second fails, an orphan challenge is left with the SMS sent and no
`operationId` returned. It self-heals: the pending-uniqueness index means the caller's next
initiate invalidates that challenge and issues a fresh one. Accepted, because the alternative —
wrapping both in a transaction — reintroduces the far worse failure above.

---

## Task 5 — `confirm`

`POST /api/v1/Verification/Confirm` — `{ operationId, code }`

```csharp
var pending = await _operations.GetByOperationIdAsync(operationId, ct)
    ?? throw new DomainValidationException("PendingOperationNotFound");

// Same error for a different principal — a distinct "not yours" would confirm it exists.
if (pending.UserId != _currentUser.UserId)
    throw new DomainValidationException("PendingOperationNotFound");

// Replay guard, BEFORE the code is touched.
switch (pending.Status)
{
    case Succeeded: return StoredResult(pending);     // idempotent retry
    case Failed:    throw new DomainValidationException("PendingOperationAlreadyCompleted");
}

var descriptor = _registry.Get(pending.OperationType);

// OUTSIDE the transaction — see the rule below. VerifyAsync saves in its own finally.
await _otpService.VerifyAsync(pending.ChallengeId, pending.OperationType, code,
                              _codeHasher.Hash(pending.Payload), ct);

var command = JsonSerializer.Deserialize(pending.Payload, descriptor.PayloadType)
    ?? throw new DomainValidationException("PendingOperationUnavailable");

// Execution and the Succeeded write land together or not at all. The nested Send sees
// HasActiveTransaction and joins this transaction rather than opening its own.
await using var transaction = await _dbContext.BeginTransactionAsync(ct);
try
{
    var result = await descriptor.Execute(command, _serviceProvider, ct);

    pending.Succeed(Serialize(result), _dateTime.UtcNow);

    await _dbContext.CommitTransactionAsync(transaction, ct);
}
catch
{
    _dbContext.RollbackTransaction();
    throw;
}
```

### 🔴 The rule: code verification happens outside the transaction

`OtpService.VerifyAsync` increments `AttemptCount` and saves it in a `finally`, precisely so a
wrong code is always counted. Inside a transaction that save only flushes — the wrong code throws,
the transaction rolls back, and **the increment disappears**. `MaxAttempts` would then never be
reached and a six digit code becomes brute-forceable at leisure.

So the boundary is: verify (its own committed save) → *then* begin the transaction → execute and
mark `Succeeded` → commit.

This is why the confirm command **carries `ISkipTransaction`** and opens its own transaction
instead. `TransactionBehavior`'s automatic boundary starts too early. Note that the marker's XML
doc currently names only one valid reason — an external side effect after saving — so **it needs a
second: a command that manages a narrower transaction boundary itself.** Add that when
implementing, or the marker here reads as misuse.

### The three failure modes, and what each does

**Lost response, user retries.** Status is `Succeeded`, so the stored result comes back. No
re-execution, no `OtpAlreadyUsed`. This is why the status check precedes code verification.

**Wrong code.** `OtpService.VerifyAsync` increments `AttemptCount` and saves in its `finally`;
the operation stays `Pending`. The user tries again. Do not touch that `finally` — happy-path-only
saving would roll the increment back and make six digits brute-forceable.

**Crash between code verification and the `Succeeded` save.** The challenge is spent but the
operation is still `Pending`. The retry reaches code verification and gets `OtpAlreadyUsed`, so
the user re-initiates.

Because execution and the `Succeeded` write share one transaction, a crash rolls **both** back —
so for a database-only handler, `Pending` reliably means the work did not happen. The user loses a
code, not consistency.

The remaining limit: a handler that calls an **external** system cannot have that call rolled back,
so for those the outcome after a crash is genuinely unknown. Say so in the registry attribute's XML
docs — an operation reaching outside the database needs its own idempotency regardless of what this
phase does.

### Deserialization across deploys

A payload written before a deploy may not bind after one. Fail **closed**: mark `Failed`, return
`PendingOperationUnavailable`, never execute a partially-bound payload. Same direction
`HmacOtpCodeHasher.HashRequest` already fails in — refuse confirmation rather than skip it.

### The OpenAPI cost

`result` is heterogeneous by construction, so NSwag types it `any` in
`WebApi/ApiClient/web-api-client.ts`. That is the unavoidable price of this shape. Document it;
clients wanting a typed result can ignore `result` and re-fetch the resource.

---

## Task 6 — Authorization is re-checked at execution

Minutes pass between initiate and confirm — long enough for a role to be revoked or a resource to
change hands.

- `UserId` is bound at initiate; a different principal at confirm gets `PendingOperationNotFound`.
- Re-evaluate `RequiredPolicies` at confirm, not only at initiate.
- Dispatching through `ISender` re-runs `ValidationBehavior`, so validators execute again against
  current state. Deliberate: the initiate-time run exists to avoid wasting a code, the
  confirm-time run is authoritative.

---

## Task 7 — Endpoints, resend, errors

`VerificationController : ApiControllerBase`, route `api/v1/[controller]`, `[Route(nameof(Action))]`
per action, per `webapi.mdc`. Actions stay thin.

### 🔴 The resend bug — do not miss this

`IOtpService.ResendAsync` issues a **brand-new challenge with a new `ChallengeId`** and invalidates
the old one. `PendingOperation.ChallengeId` would then point at an invalidated challenge and every
subsequent confirm would fail with `OtpAlreadyUsed`, permanently.

**Resend must update the operation's `ChallengeId` in the same save.** Hence `ChallengeId` is
mutable on the entity, via a named method (`RechallengedTo(Guid, DateTime)`), not a public setter.

### Error keys

Localization keys added to `WebApi/Resources/localization.json` in **both** `ka-GE` and `en-US`.
Existing `Otp*` keys are reused where the meaning matches. New:

| Key | Meaning |
|---|---|
| `UnknownVerifiableOperation` | `operationType` not registered |
| `PendingOperationNotFound` | Unknown `operationId`, or belongs to another principal |
| `PendingOperationAlreadyCompleted` | Terminal state, re-initiate required |
| `PendingOperationUnavailable` | Payload could not be bound, or its hash no longer matches |

---

## Task 8 — Wiring checklist

Easy to miss, each one fails at startup or at first use:

- **`IApplicationDbContext`** declares its DbSets by hand — add `DbSet<PendingOperation>` there
  *and* on `ApplicationDbContext`.
- **Repositories are the one thing not assembly-scanned.** `IPendingOperationRepository` needs an
  explicit `services.AddScoped<...>()` line next to the existing three, or it fails to resolve.
- `IEntityTypeConfiguration<PendingOperation>` **is** scanned — do not hand-wire it.
- `VerifiableOperationRegistry` registered as a singleton, built from the `Application` assembly.
- The service takes **`IApplicationDbContext`** as well as `IUnitOfWork` — the former for the
  transaction it opens in task 5, the latter for ordinary saves. Both are already abstractions
  visible from `Application`.

### Existing tests this breaks

Mechanical, but expected rather than discovered mid-task:

- `Domain.UnitTests/Entities/OtpVerificationTests.cs` — `OtpVerification.Create` gains `channel`;
  ~7 call sites.
- `Application.UnitTests/Otp/OtpServiceTests.cs` — mocks `ISmsSender` directly; becomes
  `IVerificationCodeSender`.
- `OtpVerificationBehavior` and `ResendOtpCommandHandler` — `IssueAsync` signature.

---

## Task 9 — Tests

Per `.cursor/rules/testing.mdc`. `Domain.UnitTests` for `PendingOperation` invariants and
transitions; `Application.UnitTests` for the service.

- Replayed confirm returns the stored result and does **not** re-execute.
- Confirm by a different principal → `PendingOperationNotFound`.
- Unregistered `operationType` → rejected before any send (assert the sender was never called).
- Validation failure at initiate costs no message (same assertion).
- **Startup throws** when a registered operation implements `IRequireOtpVerification`.
- Payload edited after issue → hash mismatch → refused.
- Unbindable payload fails closed without executing.
- Wrong code still increments `AttemptCount` and persists it — **and survives**, i.e. the
  increment is not inside the transaction. The regression test for the rule in task 5.
- A handler that throws leaves the operation `Pending`, not `Succeeded`, and the transaction is
  rolled back.
- `InitiateOperationCommand` and the confirm command both carry `ISkipTransaction`
  (`TransactionBehaviorTests` already covers that the marker is honoured).
- Resend updates `PendingOperation.ChallengeId`, and a confirm after resend succeeds.

The last one is the regression test for the bug in task 7.

There are still **no integration tests**, so the filtered unique index and the challenge
`RowVersion` concurrency guard are not covered end to end. Say so in the phase report.

---

## Migrations

```powershell
$env:SkipNSwag = "True"
dotnet ef migrations add AddVerificationChannel --project Infrastructure --startup-project WebApi
dotnet ef migrations add AddPendingOperations --project Infrastructure --startup-project WebApi
```

1. `AddVerificationChannel` — `Channel` on `OtpVerification`, `Recipient` 32 → 256, rebuild the
   filtered pending index to include `Channel`.
2. `AddPendingOperations` — the new table; unique index on `OperationId`, index on
   `(Status, Created)` for the retention script below.

## Retention

Nothing in the application deletes these rows (assumption 2). Schedule this outside the app — SQL
Agent, a maintenance job, whatever the deployment already uses:

```sql
DELETE FROM PendingOperations
WHERE Created < DATEADD(day, -30, GETUTCDATE());
```

Terminal rows are the bulk of it; `Pending` rows whose challenge expired are dead weight and the
same cutoff clears them.

## Documentation to update

- **New** `.cursor/rules/verified-operations.mdc` — the registry, the recursion trap, the replay
  rule, and the four stated assumptions.
- `.cursor/rules/otp.mdc` — two mechanisms, one core; `ISmsSender` → `IVerificationCodeSender`.
- `00-shared-context.md` — the "OTP gate is opt-in" bullet gains its counterpart; layer table
  lists the new abstractions.
- `docs/architecture.md` — both mechanisms and **when to choose which**: the MediatR gate when the
  client can re-send the payload or the payload is sensitive, the generic endpoints otherwise.
- **New skill** `.cursor/skills/add-verified-operation/`, mirroring `add-otp-gate`.

## Open items, deliberately deferred

1. **Email channel** — task 1 lists the three blockers.
2. ~~Transaction support~~ — **done** ahead of this phase (`e52f5a1`, `589ce84`).
   `IApplicationDbContext` exposes `BeginTransactionAsync` / `CommitTransactionAsync` /
   `RollbackTransaction` / `HasActiveTransaction`, and `TransactionBehavior` applies one
   automatically to any command that is not an `IQuery<T>` or `ISkipTransaction`.
3. **Two-phase challenge issue** — splitting `IOtpService.IssueAsync` into "create" and "send"
   would let initiate run inside a transaction and send only after commit, removing the orphan
   window in task 4. It touches shared OTP code used by the existing gate, so it is out of scope
   here.
4. **Purpose namespacing** — if phase 7 is ever implemented, both mechanisms write to
   `OtpVerification.Purpose` with different naming schemes and need prefixes (`op:`, `mvc:`)
   decided *before* rows exist. Not a concern while this is the only new mechanism.
