# Phase 6 — Initiate/confirm operations (server-held confirmation)

**Depends on:** phase 5. **EF migration:** **yes**. **Size:** large. **Optional / structural.**

> This adds a *second* confirmation topology alongside the existing OTP gate. It does not replace
> it, and it does not change `IOtpService`, `OtpVerification`, or any of the OTP mechanics. If you
> find yourself editing `Application/Otp/Services/OtpService.cs`, stop — you have gone off plan.

## Why

`IRequireOtpVerification` is a **client-held** confirmation: the server persists only
`RequestHash` (`OtpVerificationBehaviour.cs:58`) and the client replays the entire command with a
code attached. `RegisterUserCommand`'s doc comment names that as the win — "no second command, no
second endpoint, no pending-registration state to reconcile."

That model is structurally unable to do four things, all of which a lending product eventually
needs:

- **Four-eyes approval.** A different user confirms. They never composed the payload, so they
  cannot replay it. For loan status changes this is usually a control requirement, not a feature.
- **Cross-device confirmation.** Initiate on web, approve in the mobile app.
- **Lifetime decoupling.** An OTP lives 5 minutes; a pending approval may legitimately sit for
  hours. Server-held state gives one operation to many challenges.
- **Audit of intent.** An abandoned challenge today leaves an `OtpVerification` row containing a
  hash and nothing else. Nobody can answer "who tried to approve loan 42 and did not finish."

So this phase adds a **server-held** confirmation: the payload is persisted once at initiate, and
a later confirm call redeems it by handle.

### What stays on the old gate, and why

**`RegisterUserCommand` does not move.** It carries `Password` and `ConfirmPassword`
(`RegisterUserCommand.cs:24-25`). Server-held confirmation means persisting those — in plaintext,
or encrypted with a key-management problem attached. Neither is acceptable. Registration keeps the
client-replay gate, which is exactly the shape that payload needs.

`UpdateApplicationStatusCommand` **does** move. Small payload, no secrets, and it is the operation
that actually benefits from a durable record and a different approver.

The result is that the boilerplate demonstrates both topologies *and* the criterion for choosing
between them, instead of two samples of the same mechanism.

## Precondition

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

```bash
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Both green; phases 1–5 committed. `Application/Otp/Services/OtpService.cs` exists (phase 5 moved
it out of `Infrastructure`) — if it is still under `Infrastructure/Services`, phase 5 is not done
and this phase must not start.

---

## Task 1 — `PendingOperation` aggregate

New `Domain/Entities/PendingOperation.cs`, mirroring `OtpVerification`'s shape — private setters,
static `Create`, mutation through named methods, `DomainValidationException` with localization
keys, `RowVersion` for optimistic concurrency.

```csharp
public class PendingOperation : BaseAuditableEntity, IAggregateRoot
{
    /// Public handle. Id is a sequential int and would let a caller walk other
    /// people's operations, so it never leaves the server — same reasoning as
    /// OtpVerification.ChallengeId.
    public Guid OperationId { get; private set; }

    /// Stable discriminator from the registry (task 2), never a CLR type name.
    public string OperationType { get; private set; }

    /// The serialized command, minus its confirmation members.
    public string Payload { get; private set; }

    public string InitiatedBy { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public PendingOperationStatus Status { get; private set; }
    public string? ConfirmedBy { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();
}
```

`Domain/Enums/PendingOperationStatus.cs`: `Pending`, `Confirmed`, `Cancelled`, `Expired`.

Methods, each raising the matching domain event:

- `Create(...)` → `PendingOperationCreatedEvent`. Guards: empty `OperationId`, blank
  `OperationType`, blank `Payload`, blank `InitiatedBy`, `expiresAt <= created`.
- `Confirm(string confirmedBy, DateTime now)` → `PendingOperationConfirmedEvent`. Throws
  `PendingOperationAlreadyResolved` if `Status != Pending`; sets `Status = Expired` and throws
  `PendingOperationExpired` if `now >= ExpiresAt`. **Single use** — the state transition is what
  stops a confirmed operation executing twice.
- `Cancel(DateTime now)` → `PendingOperationCancelledEvent`. No-op if already resolved, matching
  `OtpVerification.Invalidate`.

**The actor check does not live here.** `Confirm` records who confirmed; whether that user was
*allowed* to is a policy question decided in the behaviour (task 4), because it needs the
registry. Keep the domain ignorant of it.

## Task 2 — The marker, the policy, and the type registry

`Application/Common/Confirmation/IRequireOperationConfirmation.cs`:

```csharp
/// Pure marker — deliberately no members. See the trap below.
public interface IRequireOperationConfirmation
{
}
```

> ### Trap — a confirmation flag on the command is a total bypass
>
> The obvious design is `Guid? OperationId { get; }` on the marker, with the behaviour reading
> "null → initiate, set → already confirmed, pass through". **Do not do this.** Every command
> reaches the pipeline by model binding from the request body — `UpdateApplicationStatus`
> takes `UpdateApplicationStatusCommand` straight off the wire
> (`WebApi/Controllers/LoanApplicationController.cs:34`). Any authenticated caller could POST
> `{ "id": 42, "status": "Accepted", "operationId": "<any guid>" }` to the existing endpoint,
> the behaviour would see a non-null value and fall through, and the loan would be approved with
> **no pending operation, no code, and no confirmation of any kind**. `LoanApplicationController`
> carries a bare `[Authorize]`, so "any authenticated caller" is the entire user base.
>
> `[JsonIgnore]` would close it — that is how `RegisterUserCommand.OtpRecipient` is handled — but
> it is a per-command opt-in whose failure mode is silent total bypass, on boilerplate other
> people will copy. Not good enough.
>
> Instead the confirmed-execution signal travels **out of band**, in a scoped ambient context that
> nothing on the wire can set:
>
> ```csharp
> // Application/Common/Confirmation/IOperationExecutionContext.cs — registered Scoped
> public interface IOperationExecutionContext
> {
>     bool IsConfirmedExecution { get; }
>     IDisposable BeginConfirmedExecution(Guid operationId);
> }
> ```
>
> Only `ConfirmOperationCommandHandler` calls `BeginConfirmedExecution`, immediately around its
> re-dispatch. The marker stays empty, there is no property to forge, and a new confirmable
> command cannot forget to protect one.

`Application/Common/Confirmation/ConfirmableOperationAttribute.cs`:

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class ConfirmableOperationAttribute(string discriminator) : Attribute
{
    public string Discriminator { get; } = discriminator;
    public ConfirmationPolicy Policy { get; init; } = ConfirmationPolicy.SameUser;
    public TimeSpan? Lifetime { get; init; }
}
```

`ConfirmationPolicy`: `SameUser` (default), `AnyAuthorized`, `DifferentUser`.

**`SameUser` must be the default, and the enum must exist from day one.** If the actor rule is
implicit, someone eventually ships self-approval on a four-eyes operation and nothing in the type
system objects.

`IOperationTypeRegistry` (Application) resolves `discriminator → Type` and back, built once at
startup by scanning the Application assembly for `[ConfirmableOperation]`. Register it as a
singleton.

**Resolve types only through this registry. Never `Type.GetType(storedString)`.** The primary
reason is not gadget deserialization — the string is yours — it is that this is *boilerplate
people fork*, and namespaces will get renamed. A stable discriminator keeps pending rows
readable across a rename; a CLR type name does not.

**Version the discriminator**: `"loan.status.update.v1"`. A command's shape can change while
operations are pending, and a payload deserializing with a default for a newly-required field is
a silent wrong answer. Bump the suffix when the shape changes incompatibly, and let the old
discriminator fall out of the registry so pending rows fail closed at confirm time rather than
executing something unintended.

### Startup validation — fail fast

Follow the `ValidateOnStart` habit already used for `OtpOptions`
(`Application/Extensions/ConfigureServices.cs:27-30`). Registry construction throws if:

1. Two commands declare the same discriminator.
2. A `[ConfirmableOperation]` type also implements `IRequireOtpVerification` — the two topologies
   are mutually exclusive and stacking them would issue two codes for one action.
3. **A `[ConfirmableOperation]` type has any property carrying `[SensitiveData]`, or any property
   whose name appears in `LogRedactor.DefaultSensitiveProperties`.** This is the guard that stops
   someone persisting a secret into `Payload` two years from now. It is cheap, it is checked at
   boot, and it is the single most valuable rule in this task.

   Check the redactor list as well as the attribute: `RegisterUserCommand.Password` carries no
   `[SensitiveData]` — it is caught by name (`LogRedactor.cs`, `DefaultSensitiveProperties`).
   An attribute-only scan would wave it through, which is precisely the case the rule exists for.

   **Recurse into complex property types**, or say plainly in the report that it does not. A
   shallow scan misses a nested DTO holding a password, and a guard with a silent hole is worse
   than a documented absence.

## Task 3 — Persistence

`Infrastructure/Persistence/Configurations/PendingOperationConfiguration.cs`, following
`OtpVerificationConfiguration`:

- `OperationType nvarchar(128)`, `InitiatedBy`/`ConfirmedBy` `nvarchar(128)`,
  `Payload nvarchar(max)`, `Status int`, `RowVersion` via `IsRowVersion()`.
- **Unique index on `OperationId`** — every lookup goes through the handle, never the PK.
- Index on `(InitiatedBy, Status)` to back a "my pending operations" query.

Do **not** add a filtered unique index like `UX_OtpVerifications_Recipient_Purpose_Pending`. Many
concurrent pending operations per user is the intended state here, not a race to suppress.

`Domain/Repositories/IPendingOperationRepository.cs` + implementation in
`Infrastructure/Persistence/Repositories/`: `GetByOperationIdAsync`, `ListPendingForUserAsync`.

Migration:

```powershell
$env:SkipNSwag = "True"
dotnet ef migrations add AddPendingOperation --project Infrastructure --startup-project WebApi
```

## Task 4 — `OperationConfirmationBehavior`

New `Application/Common/Behaviours/OperationConfirmationBehaviour.cs`, registered in
`AddApplicationServices` **immediately after** `OtpVerificationBehavior` (so it is innermost;
same reasoning — a command that fails validation must not cost an SMS).

```csharp
if (request is not IRequireOperationConfirmation) return await next();

// Confirm path: ConfirmOperationCommandHandler has already redeemed the code and
// marked the operation Confirmed, and re-dispatches inside BeginConfirmedExecution.
// The signal is ambient, never on the request — see the trap in task 2.
if (_executionContext.IsConfirmedExecution) return await next();

// Initiate path: persist the payload, issue a code bound to the operation, park it.
```

Initiate does, in order:

1. `_currentUserService.RequireUserId()` — an unauthenticated initiate has no `InitiatedBy`.
2. Enforce `PendingOperationOptions.MaxPendingPerUser` by counting the initiator's `Pending` rows;
   throw `TooManyPendingOperations` past the cap. **Do this before issuing anything** — otherwise
   the cap exists in configuration and nowhere in the code.
3. Serialize the request with `JsonSerializer.Serialize(request, request.GetType())`. Plain
   serialization — with the marker empty there is nothing to strip, so **`HmacOtpCodeHasher`'s
   `OtpMembers` set is not involved and must not be touched.**
4. `PendingOperation.Create(...)`, resolving lifetime from the attribute or
   `PendingOperationOptions.DefaultLifetime`.
5. Issue the OTP: `_otpService.IssueAsync(purpose, recipient, userId, requestHash, ct)` — **no
   signature change**, with:
   - `purpose` = the **discriminator alone** (see the trap below),
   - `requestHash` = `_codeHasher.Hash(operationId, discriminator)` — this is what binds a code to
     one specific operation.
6. `SaveChangesAsync`, then throw `OperationConfirmationRequiredException` carrying `OperationId`
   and the `OtpChallengeDto`.

Note the ordering mirrors `OtpService.IssueAsync`: the row is committed before the SMS goes out,
so a failed save never texts a code for an operation that does not exist.

> ### Trap — `Purpose` is doing two jobs
>
> `OtpService` uses `Purpose` both to scope replay *and* as the throttle key for
> `MaxPerRecipientPerHour` and `ResendCooldown`. Putting `OperationId` into the purpose would scope
> challenges perfectly per-operation **and silently disable the SMS rate limit**, since every
> operation would get its own fresh bucket.
>
> So: `purpose` = discriminator only, preserving the rate limit. The consequence is that
> `IssueAsync` retires the previous pending challenge, so a user can hold only **one live code per
> operation type** at a time — initiating a second approval invalidates the first one's code, and
> the user resends. It is not a security hole: the code is bound to a single operation through
> `requestHash`, so a code issued for operation A can never confirm operation B.
>
> Two consequences to state in the report rather than discover in use:
>
> - **Resend ping-pong.** Recovering operation A's code via `ResendOtp` retires operation B's, and
>   vice versa. `ResendAsync` re-issues against the stored purpose, recipient and `RequestHash`
>   (`OtpService.ResendAsync`), so each operation *is* individually recoverable — but only one at
>   a time. Approving in sequence works; approving in parallel does not.
> - **`MaxPerRecipientPerHour` is tuned for registration, not for a job.** The default of 5 means
>   a loan officer is throttled after five approvals in an hour. That default is fine for the
>   sample, but it is a per-recipient-per-purpose cap in a single `OtpOptions`, so it cannot be
>   raised for approvals without raising it for registration too. Flag it; do not silently raise
>   the global value.
>
> If concurrent live codes or per-purpose throttles are genuinely needed later, both require a
> separate throttle key on `IOtpService` — a deliberate signature change, not something to slip
> in here.

### Actor policy

Enforced at confirm time, in `ConfirmOperationCommandHandler` (task 5), not here:

- `SameUser` → `confirmerId == operation.InitiatedBy`, else `PendingOperationWrongConfirmer`.
- `DifferentUser` → `confirmerId != operation.InitiatedBy`, else `PendingOperationSelfConfirm`.
- `AnyAuthorized` → authenticated is enough.

> ### Trap — ambient identity flips on replay
>
> When the confirm handler re-dispatches the command, `ICurrentUserService.UserId` is the
> **confirmer**, not the initiator. For approval-shaped operations that is correct — the approver
> is whose authority matters, and `AuditableEntityInterceptor` stamping `LastModifiedBy` with the
> approver is what you want.
>
> It is wrong for any command that derives *ownership* from ambient identity. A confirmed
> "create" would attribute the record to whoever approved it.
>
> The rule: **commands that derive ownership from ambient identity must stay on `SameUser`**,
> where initiator and confirmer are the same principal and the question does not arise. `SameUser`
> being the default is what makes this safe by construction. Put this in the XML doc on
> `ConfirmationPolicy`, not only in this plan.

## Task 5 — `ConfirmOperationCommand` and the endpoint

`Application/Confirmation/Commands/ConfirmOperationCommand.cs` — one generic command for every
confirmable operation:

```csharp
public record ConfirmOperationCommand : IRequest
{
    public Guid OperationId { get; set; }

    /// The challenge being answered. Supplied by the caller, not stored on the
    /// operation — see the note below.
    public Guid ChallengeId { get; set; }

    [SensitiveData]
    public string? OtpCode { get; set; }
}
```

> **Why `ChallengeId` is on the command and not on `PendingOperation`.** The obvious move is to
> store the live challenge id on the operation row so the confirm call need only carry
> `OperationId`. It does not survive resend: `OtpService.ResendAsync` delegates to `IssueAsync`,
> which **mints a new `challengeId` and retires the old one**. A stored id would be stale the
> moment a user asks for a new message, and fixing that means teaching `ResendOtpCommand` about
> pending operations — coupling the OTP layer to this one for no gain.
>
> Having the caller pass it costs nothing in security. `VerifyAsync` loads by `ChallengeId`, then
> `OtpVerification.Verify` compares the stored `RequestHash` against the one recomputed from
> `OperationId`. A challenge belonging to a different operation fails with `OtpRequestMismatch`.
> **The binding is the request hash, never the secrecy of the challenge id** — which is also why
> the existing anonymous `ResendOtp` endpoint is safe today.

Handler, in order — **the order is the correctness of this task**:

1. Load by `OperationId`; `PendingOperationNotFound` if absent.
2. Resolve the discriminator through the registry. **Not in the registry → refuse.** This is the
   fail-closed path for a payload whose command shape has since changed.
3. Enforce the actor policy against `_currentUserService.UserId`.
4. `_otpService.VerifyAsync(request.ChallengeId, discriminator, code, requestHash, ct)` with
   `requestHash = _codeHasher.Hash(operationId, discriminator)` — the same value as at initiate.
5. `operation.Confirm(confirmerId, now)` and `SaveChangesAsync`. **Before executing**, so a
   crash mid-execution cannot leave the operation replayable.
6. Deserialize `Payload` to the registered type and, inside
   `using (_executionContext.BeginConfirmedExecution(operationId))`, `_sender.Send(...)`.

Add `Application/Confirmation/Validators/ConfirmOperationCommandValidator.cs` alongside it —
`OperationId` and `ChallengeId` not empty, `OtpCode` not blank — matching
`ResendOtpCommandValidator`. Validators are discovered by assembly scan; do not hand-wire it.

Step 6 going back through MediatR is deliberate: the command gets `ValidationBehavior` again, so
state that changed since initiate is re-checked. `LoanApplication.UpdateStatus` already throws
`ApplicationAlreadyProcessed` when the loan is `Accepted`/`Rejected`
(`Domain/Entities/LoanApplication.cs:86-87`), so a stale approval confirmed after someone else
actioned the loan fails in the domain rather than silently overwriting. Confirm that guard still
holds after your changes — it is the safety net for the entire deferred-execution model.

**Exception filter.** Add `HandleOperationConfirmationRequiredException` to
`WebApi/Filters/ApiExceptionFilterAttribute.cs`, following `HandleOtpRequiredException`
(~line 189), returning **202 Accepted** with `operationId`, `challengeId`, `expiresAt`,
`recipient` and `maxAttempts` on `ProblemDetails.Extensions`.

**202, not 428.** 428 means "resend this same request with the missing precondition" — which is
precisely the client-replay behaviour this topology exists to avoid. Returning 428 here would
train clients to hold and resend the payload. 202 says the request was accepted and parked, and
the follow-up is a *different* call.

It is still thrown as an exception for the same reason `OtpRequiredException` is: a pipeline
behaviour cannot synthesize a `TResponse`. Say so in the XML doc.

**Controller.** New `WebApi/Controllers/OperationsController.cs`:

- `POST api/v1/Operations/{operationId}/confirm` → `ConfirmOperationCommand`
- `POST api/v1/Operations/{operationId}/cancel` → `CancelOperationCommand` (initiator only)
- `GET api/v1/Operations/pending` → the caller's pending operations

Resend already works through the existing `POST api/v1/Authenticate/ResendOtp` — `ResendAsync`
re-issues for the same purpose, recipient and payload hash. **Do not add a second resend endpoint.**

## Task 6 — Migrate `UpdateApplicationStatusCommand`

Drop `IRequireOtpVerification`, `ChallengeId` and `OtpCode`. Add
`[ConfirmableOperation("loan.status.update.v1", Policy = ConfirmationPolicy.DifferentUser)]` and
`IRequireOperationConfirmation`. **Add no members** — the command shrinks to `Id` and `Status`,
which is the visible payoff of the ambient-context decision in task 2: the wire contract of a
confirmable command is exactly its business payload, with no confirmation plumbing on it.

The endpoint signature does not change. `UpdateApplicationStatus` still takes the command off the
body; the same call now returns 202 with a handle instead of 428 asking for a replay.

Rewrite the class doc comment. It currently sells the command as the second sample of the *inline*
gate; it becomes the sample of the *server-held* one, and should say why an approval belongs on
this topology while registration does not.

`DifferentUser` here is the point of the exercise — it is what makes four-eyes real rather than
theoretical. If the sample data makes a two-user flow awkward to demo, say so in the report; do
not quietly downgrade it to `SameUser`.

## Task 7 — Configuration and localization

`Application/Common/Models/PendingOperationOptions.cs`, section `"PendingOperations"`, bound with
`ValidateDataAnnotations().ValidateOnStart()` alongside `OtpOptions`:

| Property | Default | Notes |
|---|---|---|
| `DefaultLifetime` | `24:00:00` | Overridable per-operation via the attribute |
| `MaxPendingPerUser` | 20 | Stops unbounded parking of operations |

Add to `WebApi/appsettings.json`. Keep `DefaultLifetime` short enough that pending rows drain
across a normal deploy cadence — a payload outliving its command's shape is the failure mode
task 2's versioning exists to catch, and a shorter lifetime means it catches less.

New localization keys in `WebApi/Resources/localization.json`, **both `ka-GE` and `en-US`**:
`OperationConfirmationRequired`, `PendingOperationNotFound`, `PendingOperationExpired`,
`PendingOperationAlreadyResolved`, `PendingOperationWrongConfirmer`,
`PendingOperationSelfConfirm`, `PendingOperationTypeUnknown`, `TooManyPendingOperations`.

Add `"payload"` to the redaction list in `Application/Common/Logging/LogRedactor.cs` — the confirm
response and any operation listing would otherwise put a serialized command body into the `Logs`
table.

## Task 8 — Two limitations to record, not fix

Both are real, both are out of scope, and both must land in `docs/architecture.md` as known
limitations — the same treatment phase 5 gave the missing outbox. An unrecorded limitation is
indistinguishable from an oversight to the next reader.

**Confirmation and execution are two transactions.** Step 5 of task 5 commits `Confirmed`, step 6
dispatches the command, which saves again. If execution fails — a transient database error, or
`ApplicationAlreadyProcessed` because someone else actioned the loan first — the operation is
already consumed and the initiator must start over. That is the *safe* direction (an operation
never executes twice, and never executes unconfirmed), and it is deliberate. Wrapping both in one
transaction is not possible without transaction support the repo does not have: domain events
dispatch **before** `SaveChanges` with no outbox, so a rollback would unpublish nothing.

Say so in the XML doc on the handler, next to the ordering, so nobody "fixes" the ordering later.

**Controller authorization does not re-run on the confirm path.** `_sender.Send()` re-enters the
MediatR pipeline, not MVC, so `[Authorize]` on `LoanApplicationController` is not evaluated when
the wrapped command finally executes — only the `[Authorize]` on `OperationsController` was.

Today this changes nothing: both controllers carry a bare `[Authorize]` with no roles or policies
(`grep -rn "Authorize" WebApi/Controllers/` returns three bare attributes), so "authenticated" is
the only requirement either way, and the actor policy from task 2 is a *stricter* check than the
endpoint's own.

It becomes a hole the moment someone adds `[Authorize(Roles = "Approver")]` to a controller
holding a confirmable command — the initiate call would be gated and the confirm call would not.
Record it, and note the fix if it is ever needed: carry the requirement on
`[ConfirmableOperation(..., RequiredRole = "...")]` and check it in the confirm handler beside the
actor policy, so authorization travels with the operation rather than with the route.

## Task 9 — Documentation

- `.cursor/rules/otp.mdc` — the two topologies and the criterion for choosing; `IRequireOtpVerification`
  is no longer the only answer
- `.cursor/rules/mediatr-behaviors.mdc` — the new behaviour, its pipeline position, and **why the
  confirmed-execution signal is ambient rather than a request member**
- `.cursor/rules/domain-entities.mdc` — `PendingOperation` as an aggregate
- `.cursor/rules/infrastructure-ef.mdc` — the new configuration and repository
- `.cursor/rules/webapi.mdc` — `OperationsController`, and 202 vs 428
- `.cursor/skills/add-vertical-slice/{SKILL.md,reference.md}` — **most important**: how a new
  operation opts into confirmation, the `SameUser` default, and that a confirmable command
  carries no confirmation members of its own
- `docs/architecture.md` — a section on the two topologies, the ambient-identity rule, the
  `Purpose`/throttle trap, and the two limitations from task 8
- `docs/plans/README.md` — add phase 6 to the table

## Verification

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

```bash
dotnet test LoanApi.sln -p:SkipNSwag=True
```

New tests — these mirror where `OtpVerification` and `OtpVerificationBehavior` are already
covered, and the security-relevant ones are not optional:

`Domain.UnitTests/Entities/PendingOperationTests.cs`
- `Create` guards; `Confirm` happy path; expired; already-resolved; `Cancel` idempotence

`Application.UnitTests/Confirmation/ConfirmOperationCommandHandlerTests.cs`
- **A challenge issued for operation A cannot confirm operation B** — pass A's `ChallengeId` with
  B's `OperationId` and assert `OtpRequestMismatch`. This is the test that proves the request-hash
  binding, and therefore that carrying `ChallengeId` on the wire is safe. It is not optional.
- `DifferentUser` rejects the initiator; `SameUser` rejects a third party
- An unknown discriminator fails closed
- A second confirm on a confirmed operation does not re-execute
- Expiry is enforced independently of OTP expiry
- A failed inner dispatch leaves the operation `Confirmed`, not `Pending` (pins the documented
  two-transaction behaviour from task 8 so nobody "fixes" it into a replay)

`Application.UnitTests/Common/Behaviours/OperationConfirmationBehaviorTests.cs`
- Commands not implementing the marker pass through untouched
- Initiate persists and throws without running the handler
- **The handler does not run when `IsConfirmedExecution` is false, however the request is
  shaped.** This is the regression guard for the bypass in task 2 — a command deserialized
  straight from a request body must never reach its handler on the initiate path.
- Passes through when `IsConfirmedExecution` is true (the re-dispatch path)
- `MaxPendingPerUser` is enforced before a challenge is issued
- **Void `IRequest` commands are still gated** — same regression guard as
  `OtpVerificationBehaviorTests`; `UpdateApplicationStatusCommand` is a void command and would be
  silently dropped by a `TRequest : IRequest<TResponse>` constraint

`Application.UnitTests/Confirmation/OperationTypeRegistryTests.cs`
- Duplicate discriminators throw at construction
- A type with a `[SensitiveData]` property throws at construction
- A type with a property named like one in `LogRedactor.DefaultSensitiveProperties` (use
  `Password`) throws at construction
- A type implementing both marker interfaces throws at construction

## Definition of done

- [ ] `PendingOperation` aggregate + `PendingOperationStatus`, with `RowVersion` and single-use `Confirm`
- [ ] `IRequireOperationConfirmation` is an **empty** marker; no confirmation member on any command
- [ ] `IOperationExecutionContext` registered scoped; `BeginConfirmedExecution` called **only** by
      `ConfirmOperationCommandHandler`
- [ ] `ConfirmableOperationAttribute` + `ConfirmationPolicy` (default `SameUser`)
- [ ] `IOperationTypeRegistry` with startup validation for duplicates, `[SensitiveData]` members,
      redactor-named members, and double-marking
- [ ] EF configuration, repository and `AddPendingOperation` migration; unique index on `OperationId`
- [ ] `OperationConfirmationBehavior` registered innermost; `MaxPendingPerUser` enforced; `purpose` =
      discriminator only; `requestHash` binds the operation
- [ ] `ConfirmOperationCommand` carries `ChallengeId`; resolves → checks policy → verifies OTP →
      marks confirmed → **then** dispatches inside `BeginConfirmedExecution`
- [ ] `ConfirmOperationCommandValidator` added
- [ ] 202 Accepted from the filter, with the reasoning documented
- [ ] `OperationsController` with confirm/cancel/pending; **no** new resend endpoint
- [ ] `UpdateApplicationStatusCommand` migrated with `DifferentUser` and **no** confirmation members;
      `RegisterUserCommand` untouched
- [ ] `PendingOperationOptions` bound and validated; all localization keys in both locales; `"payload"` redacted
- [ ] `IOtpService`, `OtpService`, `OtpVerification` and `HmacOtpCodeHasher` unchanged
- [ ] The two limitations from task 8 recorded in `docs/architecture.md`
- [ ] All documentation updated
- [ ] Build green, tests green, including the cross-operation binding test and the bypass guard

## Out of scope

- **Do not build an approval workflow.** Multi-step chains, delegation, escalation, "approver
  groups" — all real, all a separate phase. This phase is one initiate and one confirm.
- **Do not add a background job to expire pending operations.** Expiry is checked on read, the
  same way `OtpVerification` does it. A sweeper is a scheduling concern and the repo has no
  scheduler.
- **Do not migrate `RegisterUserCommand`.** The "what stays on the old gate" rationale in *Why* is
  the whole reason it stays, and the task 2 registry check will refuse it anyway.
- Do not add notifications, real-time push, or an email channel for approvers.
- Do not change `IOtpService`'s signatures, or the OTP throttle keys, to work around the `Purpose`
  trap. If it genuinely blocks you, stop and report.
- **Do not add `RequiredRole` to `ConfirmableOperationAttribute`.** Task 8 explains why it would
  be needed *if* role-based authorization ever reaches these controllers. It has not, and adding
  an unused authorization mechanism now means shipping a security control nothing exercises.
- Do not raise `OtpOptions.MaxPerRecipientPerHour` to make the approval flow more comfortable. It
  is shared with registration, where it is the anti-SMS-pumping control.

## Commit

```
Add server-held operation confirmation alongside the inline OTP gate

The existing IRequireOtpVerification gate keeps the payload on the client and
replays it with a code attached, which cannot express an approval confirmed by
a different user, on a different device, or hours later. PendingOperation
persists the command once at initiate and a generic confirm endpoint redeems it
by handle, so the confirmer never handles the payload.

Loan status change moves to the new topology with a four-eyes policy.
Registration stays on the inline gate deliberately: its payload carries a
password, which must not be persisted. The OTP core is reused unchanged --
codes are bound to one operation through the existing request hash, and the
purpose stays the type discriminator so the SMS rate limit keeps working.
```
