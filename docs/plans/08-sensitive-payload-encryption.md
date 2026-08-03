# Phase 8 — Field-level encryption for verified-operation payloads

**Depends on:** phase 6 (implemented). **EF migration:** none. **Size:** small–medium.
**Additive** — no existing operation changes behaviour.

> **Implemented.** Key management **option B** was chosen: AES-256-GCM over a key derived from
> `PayloadProtection:Secret`. No package, no migration, no key history — see *The open decision*
> below for what that costs and when to revisit it.
>
> Lifts phase 6's stated assumption 1 ("stored payloads are not encrypted") from *never* to
> *opt-in per property*, so an operation carrying sensitive data can use `[VerifiableOperation]`
> instead of being forced onto `IRequireOtpVerification`.

## Why

Phase 6 recorded a deliberate trade: `PendingOperation.Payload` holds the request body as plain
JSON between `initiate` and `confirm`, and anything genuinely sensitive should use
`IRequireOtpVerification`, which persists nothing. That rule works, but it forces a choice the two
mechanisms should not have to arbitrate: *"the client shouldn't have to re-send this body"* and
*"this body contains a password"* are both reasonable, and today they are mutually exclusive.

This phase encrypts **only the properties marked `[SensitiveData]`**, leaving the rest of the row
readable. That keeps the operational property that matters — you can still glance at a pending row
and see which loan it refers to — while the password, personal number or date of birth inside it
are ciphertext.

`SensitiveDataAttribute` already exists (`Application/Common/Logging/`) and already means "this
value must never reach a log sink". Extending it to "…and must not sit in the database in the
clear" is one concept, not two, and reuses a convention the codebase and its skills already teach.

## The open decision — key management (settled: **B**)

**Option A — `IDataProtector` with keys persisted to the database (recommended).**
Costs `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, `ApplicationDbContext :
IDataProtectionKeyContext`, and a migration for the `DataProtectionKeys` table. In exchange: no
hand-written crypto, automatic 90-day key rotation with old keys retained for decryption, and —
the part that matters most — **the keys travel with the database they protect**, so restoring a
backup or standing up a second instance cannot strand ciphertext.

The default key ring is the trap this avoids. Out of the box, keys go to the file system or
registry; in a container without a persistent volume they are regenerated on every start, and
behind a load balancer each instance has its own. Either way `initiate` on one node and `confirm`
on another, or `confirm` after a redeploy, fails to decrypt. `PersistKeysToDbContext` removes the
whole class of failure.

**Option B — AES-GCM with a configured secret**, mirroring the existing `Otp:Secret`.
No package, no migration, and the secret is shared across instances the same way the OTP secret
already is. `AesGcm` with a fresh random nonce per encryption, nonce prepended, is roughly forty
lines and is a standard AEAD rather than home-made crypto. The cost is rotation: change the secret
and every pending row becomes undecryptable. Rows live minutes, so a quiet-window rotation is
survivable — but it is a manual, undocumented-by-default procedure, and this is a template that
will seed projects whose operators never think about it.

**Chosen: B**, as sufficient for now. The one thing to know about that choice, because it is not
reversible after the fact: **there is no key history, so changing `PayloadProtection:Secret` is
destructive.** Pending payloads survive it fine — they live minutes and are nulled on completion —
but `PendingOperation.ResultPayload` is never nulled, so encrypted results written under the old
secret are unrecoverable. `VerifiedOperationEncryptionTests.Confirm_AfterTheSecretIsRotated_...`
pins that behaviour down: the operation fails closed and is marked `Failed` rather than executing
on a payload it cannot read.

Switch to A when either becomes true: results need to stay readable across a key change, or the
secret starts living somewhere that rotates on its own (a vault with expiry). Only task 1 changes.

## Stated decisions

1. **Encryption is per-property, not per-payload.** A whole-blob approach is less code, but it
   makes every pending row opaque to support and debugging for the sake of one field. Field-level
   keeps the non-sensitive remainder queryable by eye.
2. **What is stored changes from the caller's raw JSON to the re-serialized bound command.**
   Unavoidable: encrypting per property means writing through the serializer. Consequence — unknown
   extra properties the caller sent are dropped rather than stored. Harmless, since binding at
   confirm discards them anyway, and it gains a guarantee: whatever is in the row is known to bind
   back. Call this out in review; it is the one silent behaviour change in this phase.
3. **The tamper hash is unaffected.** `HashRequest` runs over whatever is stored, and the same
   value is re-hashed at confirm. Ciphertext hashes as consistently as plaintext.
4. ~~**`ResultPayload` gets the same treatment.**~~ **Reversed during review — results are not
   encrypted.** The original reasoning (it is never nulled, so a token in a result outlives the
   operation) ignored that a result is *returned to the caller* and captured in the response log:
   encrypting the row protects a value that has already left the server. It also broke the endpoint,
   since `ToResult` replays stored text verbatim and callers therefore received ciphertext — on the
   first confirm and on every replay. The rule that replaces it: **do not return secrets.** The
   request payload is different in kind precisely because it never leaves.
5. **Encryption does not replace redaction.** The wire body is still plaintext on the way in, and
   `LoggingMiddleware` captures it. See task 4 — without it, this phase encrypts at rest while
   writing the same value to the `Logs` table, which would be theatre.

---

## Task 1 — The protector

`Application` declares the contract, `Infrastructure` supplies the mechanism — the same split as
`IOtpCodeHasher` / `HmacOtpCodeHasher`.

```csharp
// Application/Common/Interfaces/IPayloadProtector.cs
public interface IPayloadProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);   // throws CryptographicException on failure
}
```

Option A implementation wraps `IDataProtectionProvider.CreateProtector("LoanApi.PendingOperationPayload")`.
The purpose string matters: it domain-separates this from any other protector added later, so a
value encrypted for one purpose cannot be decrypted under another.

Option B implementation takes `AesGcm` with a key derived from configuration, generates a fresh
12-byte nonce per call, and returns `base64(nonce || tag || ciphertext)`.

Registration goes in `AddInfrastructureServices` next to `IOtpCodeHasher`.

## Task 2 — The converter and the contract modifier

The mechanism is System.Text.Json contract customization, not a manual tree walk. A
`DefaultJsonTypeInfoResolver` modifier inspects each property's `AttributeProvider` and swaps in an
encrypting converter for anything marked `[SensitiveData]`:

```csharp
private void EncryptSensitiveProperties(JsonTypeInfo typeInfo)
{
    foreach (var property in typeInfo.Properties)
    {
        if (property.AttributeProvider?.IsDefined(typeof(SensitiveDataAttribute), inherit: true) != true)
            continue;

        property.CustomConverter = ProtectedConverter.For(property.PropertyType, _protector);
    }
}
```

Two properties of this approach are worth stating, because they are why it is preferred over
walking a `JsonNode`:

- **It recurses for free.** Every type in the graph gets its own `JsonTypeInfo`, so the modifier
  runs for nested objects and collection elements without any traversal code.
- **It cannot drift.** The same options instance is used to write and to read, so encrypt and
  decrypt are the same converter in two directions. There is no way to add encryption on one side
  and forget the other.

The converter should encrypt the *serialized* value rather than assume `string`:
`Protect(JsonSerializer.Serialize(value))` on write, `Deserialize<T>(Unprotect(text))` on read.
That costs nothing for strings and means `[SensitiveData] DateTime? BirthDate` works — which
matters, because `RegisterUserCommand.BirthDate` is exactly that shape.

## Task 3 — Wire it into the service

`VerifiableOperationService` holds a second `JsonSerializerOptions` — the existing
`SerializerOptions` plus the modifier — and uses it in exactly three places:

| Location | Today | Becomes |
|---|---|---|
| `InitiateAsync` | `payload.GetRawText()` | `JsonSerializer.Serialize(command, descriptor.PayloadType, ProtectedOptions)` |
| `BindStoredAsync` | `Deserialize(..., SerializerOptions)` | `Deserialize(..., ProtectedOptions)` |
| `Serialize(result)` | `SerializerOptions` | `ProtectedOptions` |

`ValidateAsync` still runs on the **plaintext bound command**, before serialization — password
rules must see the password. It already sits in the right place; do not move it.

`BindStoredAsync`'s catch list gains `CryptographicException`. It already fails closed by marking
the operation `Failed` and telling the caller to start over, which is the correct response to a key
that no longer decrypts: the code is spent, and no amount of retrying will bind that row.

## Task 4 — Close the logging gap (do not skip)

`LogRedactor.RedactNode` **does** recurse into nested JSON objects, so a `password` inside
`payload` is masked in the request log by the name rules. Verified, not assumed.

But `RedactObject` derives its extra key names from `GetAttributeMarkedProperties(value.GetType())`
— **the top-level type only**. For `initiate` that type is `InitiateOperationCommand`, whose
`Payload` is a `JsonElement` with no static shape. So `[SensitiveData]` on a nested payload
property contributes **no name** to the redaction key set, and only the hard-coded
`DefaultSensitiveProperties` list protects it.

Concretely, for `RegisterUserCommand`: `password`, `confirmPassword`, `personalNumber` and
`phoneNumber` are on the list. `firstName`, `lastName`, `birthDate` and `userName` are not. Mark
`BirthDate` `[SensitiveData]` for encryption and, without this task, it is encrypted at rest and
written in the clear to the `Logs` table on every initiate.

**Fix, in the codebase's existing idiom: throw at startup.** `VerifiableOperationRegistry.Build`
already refuses to boot on a duplicate name, a double-gated command and a non-`IRequest`. Add: a
registered payload type carrying a `[SensitiveData]` property whose name is not covered by the
redaction key set fails startup, naming the property and telling the developer to add it to
`RequestLogging:SensitiveProperties` and `LogRedactor.DefaultSensitiveProperties`.

This is the same rule the `add-otp-gate` skill already states for OTP properties ("must appear in
**both**"), enforced instead of documented. Auto-wiring the names would be less friction but would
hide the coupling; failing loudly at boot is consistent with every other check in `Build`.

## Task 5 — Startup guard for an unconfigured protector

If a registered payload type has any `[SensitiveData]` property and no `IPayloadProtector` is
registered, throw at startup. Without it, removing the registration silently downgrades every
sensitive payload to plaintext storage — the exact failure this phase exists to prevent, arriving
quietly.

## Task 6 — Tests

The load-bearing assertion is the third one; the rest are supporting.

1. Round-trip — a marked property survives initiate → confirm and the handler receives the
   plaintext value.
2. Unmarked properties are untouched, still readable as plain JSON in the row. This is what
   distinguishes field-level from whole-blob and is worth asserting so a later refactor to
   blob-encryption is caught.
3. **The stored row does not contain the plaintext.** Assert on `PendingOperation.Payload` directly:
   `Does.Not.Contain("hunter2")`. Everything else could pass while the feature does nothing.
4. The caller receives the **plaintext** result, on the first confirm and on replay.
5. A payload that will not decrypt → `DomainValidationException`, operation marked `Failed`,
   handler never runs.
6. `Build` throws when a marked property is not redaction-covered (task 4) and when no protector is
   registered (task 5).

**Found while writing test 5, and worth recording.** The obvious way to produce an undecryptable
payload — edit the stored row — does not test this path at all. `ConfirmAsync` calls `VerifyAsync`
*before* binding, and `VerifyAsync` checks the request hash recorded when the challenge was issued,
so an edited row is rejected there and the cipher is never reached. The operation stays `Pending`,
not `Failed`.

The first draft of this test asserted the right outcome for the wrong reason and would have gone on
passing if decryption had never been wired up at all. Rotating the secret is the honest way to
produce the condition, since it leaves the stored text — and therefore its hash — untouched. Both
cases are now pinned separately, because the tempting inference from "the cipher is authenticated,
so it catches tampering" is that the request hash is redundant. It is not: it is what catches it,
and it catches it first.

## Found in review, after the first implementation

Three defects, all introduced by this phase, all now fixed and covered by tests.

1. **Callers received ciphertext instead of their result.** Encrypting `ResultPayload` broke
   `ToResult`, which replays stored text verbatim — on the first confirm *and* on every replay. The
   original test only asserted that the row lacked plaintext, which it did, so the endpoint was
   broken while the suite stayed green. Fixed by dropping result encryption entirely; see stated
   decision 4.
2. **The redaction guard skipped collections.** `FindSensitiveProperties` rejected any property
   whose type sat in a `System.*` namespace, which includes `List<T>` — so a marked property inside
   a collection element was encrypted by the serializer (contracts apply per type, nesting included)
   while its name was never checked against the redactor. Encrypted at rest, plaintext in the
   `Logs` table, no warning: precisely the outcome the guard exists to prevent. Fixed by unwrapping
   element types before the namespace test.
3. **A pre-encryption row could crash rather than fail cleanly.** For an operation still pending
   across the deployment that added `[SensitiveData]`, the stored value is bare. A bare *string*
   degraded correctly, but a bare number made `Utf8JsonReader.GetString()` throw
   `InvalidOperationException`, which `BindStoredAsync` does not catch — a 500 and a permanently
   stuck operation. Fixed by rejecting a non-string token as a `CryptographicException`, which is
   already handled.

The pattern worth noting across all three: each was a case where the *storage* side worked and the
*read* side did not, and where a test written against the storage side alone passed anyway.

`VerifiedOperationFlowTests` already runs the real container against a real context, so 1–5 belong
there with a purpose-built fixture command rather than in a mock-based test — the point is that the
bytes in the database are ciphertext, which a mocked repository cannot demonstrate.

## Task 7 — Documentation

- `docs/architecture.md` — amend the mechanism-comparison section. The "payload stored server-side"
  row currently reads *"yes, as plain JSON"* for `[VerifiableOperation]`; it becomes *"yes;
  `[SensitiveData]` properties encrypted"*, and the deciding question changes from "is it
  sensitive?" to "does the client need to re-send it?".
- `docs/plans/06-generic-verified-operations.md` — stated assumption 1 is superseded. Leave the
  text and add a pointer, per the convention already used for phase 7.
- `.cursor/skills/add-verified-operation/SKILL.md` — the "Do not register an operation carrying
  sensitive data" bullet becomes "mark it `[SensitiveData]`", plus the redaction-list requirement.
- `.cursor/rules/verified-operations.mdc` — same, in the trade-offs section.

---

## If the motivating operation is registration

Routing `RegisterUserCommand` through `[VerifiableOperation]` is not just adding an attribute —
`VerifiableOperationRegistry.Build` throws if a registered type also implements
`IRequireOtpVerification`, and for good reason. The full move:

1. Remove `IRequireOtpVerification`, and with it `ChallengeId`, `OtpCode` and `OtpRecipient`.
2. Add `[VerifiableOperation(VerifiableOperationType.RegisterUser, RequiresAuthentication = false,
   AllowsCallerSuppliedRecipient = true)]`. Both flags are required and neither is cosmetic: there
   is no account yet, so there is nothing to authenticate and no number to resolve from.
3. Mark `Password`, `ConfirmPassword`, `PersonalNumber` and `BirthDate` `[SensitiveData]`.
4. Remove the direct `RegisterUser` route, per the standing rule that registering an operation does
   not gate its existing endpoint.
5. The client now sends `recipient` explicitly at initiate — the phone number is no longer derived
   from the payload by `OtpRecipient`.

**Worth weighing before doing it.** Registration is the one case where re-sending the payload costs
the client almost nothing (it is a form the user just filled in), and `IRequireOtpVerification`
persists nothing at all — which is strictly stronger than encrypting it. This phase makes the move
*safe*; it does not make it *better*. The operations that actually want this are the ones with a
large or awkward body the client should not have to hold, that happen to contain one sensitive
field.
