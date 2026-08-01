# Phase 7 — Filter-triggered OTP gate (second trigger, same core)

**Depends on:** nothing in phases 1–5. **EF migration:** yes (one, shared with phase 6 task 1).
**Size:** medium. **Additive** — no existing behaviour changes.

> Design document. **Not being implemented** — phase 6 was chosen instead. Retained as a record
> of the design and the reasoning; pick it up later if the trade-offs change.
>
> Read `00-shared-context.md` first.
>
> This is an **alternative** to phase 6, not a companion to it. Both add a second way to
> two-factor an operation; phase 6 stores the payload server-side and executes it on confirm,
> this one keeps today's re-send flow and only moves the trigger to an MVC filter.
>
> **If it is ever implemented alongside phase 6, purpose namespacing must be settled first.**
> All mechanisms write to `OtpVerification.Purpose` with different naming schemes —
> `ApproveLoanCommand` (behaviour), `LoanApplication.Approve` (this phase), `ApproveLoan`
> (phase 6) — and nothing keeps them distinct. Prefixes (`cmd:`, `mvc:`, `op:`) must be decided
> *before* rows exist, or changing them later means migrating data.

## Why

`OtpVerificationBehavior` already does two-step verification correctly, and does it without
storing any payload: the client re-sends the same request with a code, and the gate lets it
through. The only real limitations are *where it is triggered from* and *how it is delivered*:

1. It is an `IPipelineBehavior`, so it only fires for requests dispatched through MediatR.
2. It forces `ChallengeId` and `OtpCode` onto every gated command's DTO.
3. SMS is the only channel.

This phase fixes all three by adding a **second trigger for the same core**. Nothing about
`OtpService`, `OtpVerification`, throttling, attempt budgets, expiry, hashing, the 428 contract or
the error keys changes. An `[RequiresOtp]` attribute on any controller action gets the same
protection the behaviour gives a command, and works for endpoints that never touch MediatR.

The wire flow is identical to today's — two calls to one endpoint:

```
POST /api/loanapplications/42/approve
{ "comment": "ok" }

→ 428  { "challengeId": "8f3c…", "expiresAt": "…" }
```

```
POST /api/loanapplications/42/approve
X-Otp-Challenge: 8f3c…
X-Otp-Code: 483920
{ "comment": "ok" }

→ 200
```

---

## ⚠ The one rule: never both gates on one operation

**An operation is gated by `[RequiresOtp]` *or* by `IRequireOtpVerification`. Never both.**

Filters run before the action, so the filter always wins the race, and the two gates cannot see
each other. Using both produces this:

1. **Call 1** — no code. Filter issues challenge #1, sends an SMS, returns 428. The action never
   runs, so the behaviour never sees anything.
2. **Call 2** — correct code in the headers. The filter verifies challenge #1 and **consumes** it,
   then calls the action. The behaviour now inspects the command, finds `ChallengeId` and
   `OtpCode` **null** — the code came in headers, not in the body — concludes no code was
   supplied, issues challenge #2, **sends a second SMS**, and throws 428.
3. **Call 3** repeats step 2 forever.

The user enters a correct code, it is spent, and they are asked for another. Every attempt costs
**two** messages, and after two or three attempts `MaxPerRecipientPerHour = 5` locks them out with
`OtpThrottled` and no path forward. The mirror case is just as bad: a client that puts the code in
the *body* is never seen by the filter, so the action is never reached at all.

The symptom — a 428 loop, then a throttle lockout — points nowhere near the cause. Task 6 makes
this impossible rather than merely documented.

### Which one to use

| Situation | Use |
|---|---|
| MediatR command, gated everywhere it is dispatched (including background jobs) | `IRequireOtpVerification` |
| Any controller action, MediatR or not | `[RequiresOtp]` |
| You want `ChallengeId`/`OtpCode` out of the command DTO | `[RequiresOtp]` |
| The operation is not reachable over HTTP | `IRequireOtpVerification` — the filter cannot fire |

The behaviour is **not** deprecated by this phase. For a MediatR command it remains the better
choice: it is automatic once the interface is implemented, and it still fires when the command is
dispatched from a non-HTTP path, which a filter can never do.

## Precondition

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

```bash
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Both green.

---

## Task 1 — Generalise the channel

**Identical to phase 6 task 1.** If phase 6 was implemented, skip this. Summary:

- `ISmsSender` → `IVerificationCodeSender` with a `Channel` property, resolved by keyed DI.
- `OtpVerification` gains a `Channel` column; `Recipient` `HasMaxLength(32)` → **256** (an email
  address does not fit in 32); the filtered pending index becomes `(Recipient, Purpose, Channel)`.
- `OtpOptions.MessageTemplate` → a per-channel section, because an email needs a subject and both
  an HTML and a plain-text body.
- **Email is not free.** `AddIdentityCore` sets `RequireUniqueEmail = false` and the comment at
  `Infrastructure/Common/Extensions/ConfigureServices.cs:73-80` is explicit that email is not part
  of the account — users are identified by `PersonalNumber`. An Email channel needs an `Email`
  input, a format + uniqueness validator running **before** the gate, and a migration. Ship the
  abstraction and the SMS implementation now; leave the email sender registered but unreachable
  until the account model catches up. The abstraction is what avoids a second migration later.

See phase 6 task 1 for the full detail.

---

## Task 2 — The `[RequiresOtp]` attribute

```csharp
/// <summary>
/// Requires phone (or other channel) confirmation before this action runs. The first call
/// without a code is answered with 428 and a challenge; the same call repeated with
/// <c>X-Otp-Challenge</c> and <c>X-Otp-Code</c> executes.
/// <para>
/// <strong>Never combine with <see cref="IRequireOtpVerification"/>.</strong> An operation is
/// gated by one mechanism or the other. Using both consumes the caller's code in this filter
/// and then issues a second challenge inside the MediatR pipeline, which costs two messages
/// per attempt and can never succeed. Startup validation and a runtime guard both reject it —
/// see phase 7 task 6.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequiresOtpAttribute : Attribute, IFilterFactory
{
    /// <summary>
    /// Scopes a code to one operation. Defaults to <c>"{Controller}.{Action}"</c>. Set it
    /// explicitly for anything long-lived — renaming the action otherwise invalidates challenges
    /// that are already in flight.
    /// </summary>
    public string? Purpose { get; init; }

    public VerificationChannel[] AllowedChannels { get; init; } = [VerificationChannel.Sms];

    /// <summary>
    /// Action argument holding the recipient. Leave null — the number then comes from the
    /// authenticated account, which is what stops a caller redirecting their own code to a phone
    /// they control. Set it only where no account exists yet, e.g. a registration endpoint.
    /// </summary>
    public string? RecipientArgument { get; init; }

    /// <summary>
    /// Argument names excluded from the request hash. Escape hatch for a payload carrying
    /// something non-deterministic (a client timestamp, a generated correlation id) that would
    /// otherwise differ between the two calls and reject a valid code with
    /// <c>OtpRequestMismatch</c>. Excluding a field means a code confirms a request that could
    /// have carried a different value for it — keep the list as short as possible.
    /// </summary>
    public string[] ExcludeArguments { get; init; } = [];

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider services) => /* resolve RequiresOtpFilter */;
}
```

`IFilterFactory` rather than a plain attribute: the filter needs `IOtpService`, `IOtpCodeHasher`,
`ICurrentUserService` and `IUserService` from DI, and the attribute carries the per-action config.

---

## Task 3 — The filter

`WebApi/Filters/RequiresOtpFilter.cs`, an `IAsyncActionFilter`. It mirrors
`OtpVerificationBehavior` step for step — read that first; the differences are noted below.

```csharp
public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
{
    var purpose   = _attribute.Purpose ?? $"{ControllerName(context)}.{ActionName(context)}";
    var challenge = ReadChallengeHeader(context);       // X-Otp-Challenge
    var code      = ReadCodeHeader(context);            // X-Otp-Code
    var hash      = _codeHasher.HashArguments(Hashable(context.ActionArguments));

    if (challenge is null || string.IsNullOrWhiteSpace(code))
    {
        var channel   = ResolveChannel(context);        // X-Otp-Channel, restricted to AllowedChannels
        var recipient = await ResolveRecipientAsync(context);
        var issued    = await _otpService.IssueAsync(purpose, channel, recipient, userId, hash, ct);

        throw new OtpRequiredException(issued);         // reuses the existing 428 handler verbatim
    }

    await _otpService.VerifyAsync(challenge.Value, purpose, code, hash, ct);

    await next();
}
```

Three things worth calling out:

**Throw `OtpRequiredException`, do not build the response.** MVC exception filters handle
exceptions thrown from action filters, so `ApiExceptionFilterAttribute.HandleOtpRequiredException`
formats the 428 with `challengeId` and `expiresAt` exactly as it does for the MediatR path. Two
triggers, one response contract, zero duplication. Clients cannot tell which mechanism gated an
endpoint, which is the point.

**Recipient resolution is the same rule as the behaviour.** From the authenticated user unless
`RecipientArgument` is set. See `OtpVerificationBehavior.ResolveRecipientAsync` — do not invent a
second rule here.

**Ordering.** The filter must run **after** authorization (it needs the principal) and after model
binding (it needs `ActionArguments`). An `IAsyncActionFilter` satisfies both by construction. Model
validation, however, runs *inside* the action pipeline: give the filter an `Order` that places it
after `ApiController`'s automatic 400 for invalid model state, so a malformed body never costs a
message. This is the filter-side equivalent of registering `OtpVerificationBehavior` after
`ValidationBehavior`, and it exists for the same reason.

### Headers

| Header | Meaning |
|---|---|
| `X-Otp-Challenge` | Challenge being answered. Absent on the first call. |
| `X-Otp-Code` | The received code. Absent on the first call. |
| `X-Otp-Channel` | Optional. Must be in `AllowedChannels`; defaults to the first. |

Headers rather than body fields is the ergonomic win over `IRequireOtpVerification`: the action's
DTO stays exactly what the operation needs, with no OTP members and no `[JsonIgnore]` juggling.

`X-Otp-Code` must be redacted from request logging. Add it to the header redaction path alongside
the existing `SensitiveProperties` handling — a live code in a log sink is the one thing this whole
subsystem exists to prevent.

### OpenAPI / generated client

The headers are read from `HttpContext`, not bound as action parameters, so **NSwag will not know
they exist** and the generated TypeScript client will offer no way to send them. Add an NSwag
`IOperationProcessor` that appends the three header parameters to any operation whose action
carries `[RequiresOtp]`. Without it the frontend has to hand-roll an interceptor, which is exactly
the kind of undocumented step a boilerplate should not ship.

---

## Task 4 — Hashing action arguments

`IOtpCodeHasher` gains an overload; the existing `HashRequest(object)` is untouched and keeps
serving the behaviour.

```csharp
string HashArguments(IReadOnlyDictionary<string, object?> arguments);
```

`HmacOtpCodeHasher` implements it by serializing the arguments **sorted by name, ordinal**, then
hashing exactly as `HashRequest` does. Sorting matters: `ActionArguments` ordering is not a
contract, and an unstable order would reject valid codes intermittently — the worst kind of bug to
reproduce.

Excluded from the hash:

- names listed in `ExcludeArguments`
- `CancellationToken`
- `[FromServices]` parameters
- `IFormFile`, `IFormFileCollection`, `Stream` — not meaningfully serializable

**Note the simplification:** unlike `HashRequest`, nothing needs stripping for `ChallengeId` /
`OtpCode`, because they never appear in the payload. That removes the `OtpMembers` special case
from this path entirely.

**Gotcha to document:** a file-upload endpoint carrying `[RequiresOtp]` binds only its *other*
arguments, so the code does not confirm the file's contents. Either accept that explicitly or do
not gate upload endpoints this way.

Same standing caveat as the behaviour: a payload containing anything non-deterministic between the
two calls produces a different hash and is refused with `OtpRequestMismatch`. `ExcludeArguments` is
the escape hatch, and it is a real weakening — an excluded field is one a code no longer confirms.

---

## Task 5 — Fix the error mapping — ✅ **DONE, landed ahead of this phase**

> Implemented as a standalone commit before any phase 7 work, because it is a latent defect in
> its own right rather than something this phase introduces. Kept here for the reasoning; nothing
> is left to do. What shipped: `ApiExceptionFilterAttribute` now registers
> `typeof(DomainValidationException)` instead of the wrapper and reads the key from
> `context.Exception.Message`; `ValidationBehaviour`'s rethrow is gone; and
> `DomainValidationExceptionWrapper` is deleted. The original analysis follows.

Every OTP failure — `InvalidOtpCode`, `OtpExpired`, `OtpLocked`, `OtpThrottled`,
`OtpRequestMismatch` — is a `DomainValidationException` thrown by `OtpService` /
`OtpVerification`. Today those reach the client as localized 400s **only because
`ValidationBehaviour` catches them and rethrows as `DomainValidationExceptionWrapper`**
(`Application/Common/Behaviours/ValidationBehaviour.cs:49`), and `ApiExceptionFilterAttribute`
maps the *wrapper*.

`ValidationBehaviour` runs inside the MediatR pipeline. A `DomainValidationException` thrown from
an MVC filter is never wrapped, and `HandleException` matches on **exact type**
(`ApiExceptionFilterAttribute.cs:43` — no base-type walking), so it falls through to
`UnhandledExceptionHandlerMiddlware` and surfaces as a **500**.

Without this task, every wrong code entered against a `[RequiresOtp]` endpoint is a 500.

Fix: register `typeof(DomainValidationException)` in `_exceptionHandlers` alongside the wrapper,
and change `HandleDomainValidationException` to read the key from `context.Exception.Message`
rather than casting to the wrapper type — both carry the localization key as `Message`.

This is worth doing regardless of which phase you implement: *any* `DomainValidationException`
raised outside the MediatR pipeline is a 500 today.

---

## Task 6 — Make the double gate impossible

Three layers, because none of them is complete alone.

**6a — Startup validation.** Walk the application's controller actions; for each carrying
`[RequiresOtp]`, inspect the parameter types. If any implements `IRequireOtpVerification`, throw
at boot with a message naming the action and both mechanisms. Fails in every environment, before
the first request.

Partial by nature: an action that takes a request DTO and maps it to a command internally is
invisible to static inspection. Hence 6b.

**6b — Runtime guard.** The filter records what it handled:

```csharp
context.HttpContext.Items[OtpGate.Marker] = purpose;
```

`OtpVerificationBehavior` checks the marker at the point it is about to issue a challenge, and
throws instead:

```csharp
if (_gateMarker.IsAlreadyGated)
    throw new InvalidOperationException(
        $"{typeof(TRequest).Name} implements IRequireOtpVerification but the endpoint is already " +
        "gated by [RequiresOtp]. Use one or the other, not both.");
```

A silent infinite loop that bills SMS becomes a loud 500 with the cause named, on the developer's
first run.

Introduce a small `IRequestGateMarker` abstraction in `Application/Common/Interfaces` implemented
by an `HttpContext`-backed class in `WebApi`. Do **not** inject `IHttpContextAccessor` into
`Application` directly — `ICurrentUserService` is the existing precedent for wrapping exactly this.

**6c — Documentation at both declaration sites.** XML doc comments stating the rule, on:

- `RequiresOtpAttribute` (drafted in task 2 above)
- `IRequireOtpVerification` — add a matching paragraph pointing the other way

Both should name the failure, not just forbid the combination. A rule without its consequence gets
"improved" away by the next person.

---

## Task 7 — Tests

Following `.cursor/rules/testing.mdc`. `Application.UnitTests` for the behaviour-side guard,
`WebApi` tests (a new project, or `Application.UnitTests` with a faked `ActionExecutingContext`) for
the filter.

- First call without headers → 428, challenge issued, sender called exactly once.
- Second call with the correct code and an identical payload → action executes, sender not called
  again.
- Second call with a **changed** payload → `OtpRequestMismatch`, action does not execute.
- Argument order shuffled → hash unchanged (guards the sort in task 4).
- Wrong code → `AttemptCount` incremented **and persisted**, surfacing as a localized 400, not a
  500 (guards task 5).
- Excluded argument changed between calls → still accepted (documents the weakening honestly).
- Channel outside `AllowedChannels` → rejected before anything is sent.
- Recipient resolution falls back to the authenticated user when `RecipientArgument` is null.
- **Double gate: startup throws** when an action with `[RequiresOtp]` takes an
  `IRequireOtpVerification` parameter.
- **Double gate: runtime throws** `InvalidOperationException` when the marker is present.

The last two are the ones that stop the rule rotting.

There are still **no integration tests** in this repo, so the filter's real position in the MVC
pipeline, the 428 body shape, and header redaction are not covered end to end. Say so in the phase
report rather than implying otherwise.

---

## Migration

One, and only if phase 6 task 1 has not already been applied:

```powershell
$env:SkipNSwag = "True"
dotnet ef migrations add AddVerificationChannel --project Infrastructure --startup-project WebApi
```

---

## Documentation to update

- `.cursor/rules/otp.mdc` — **two triggers, one core**; the selection table from this document;
  the never-both rule.
- `.cursor/rules/webapi.mdc` — `[RequiresOtp]`, the three headers, and the note that
  `DomainValidationException` is now mapped directly.
- `.cursor/rules/otp-infrastructure.mdc` — channel senders and keyed DI.
- `00-shared-context.md` — the "OTP gate is opt-in" bullet gains its filter counterpart.
- `docs/architecture.md` — both mechanisms and when to choose which.
- **New skill** `.cursor/skills/add-otp-gate` — extend rather than duplicate: one skill, two paths,
  with the selection table as its first step.

## Open decisions

1. **Email now or later** — see task 1. Ship the abstraction regardless.
2. **Whether the filter should also support minimal APIs.** An `IEndpointFilter` sibling is a small
   addition and would make the mechanism genuinely framework-wide, but the template has no minimal
   API endpoints today, so it would ship untested.
3. **Whether `[RequiresOtp]` should be allowed at controller level.** `AttributeUsage` permits it
   above, which gates every action in the controller with a shared purpose — convenient, and easy
   to apply by accident to a GET.
