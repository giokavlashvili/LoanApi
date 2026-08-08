# Enhancement plans

Execution plans derived from an audit of the persistence layer, repository pattern and services
(2026-07-30). Each phase was a **self-contained work order** for one agent session.

> **Phases 1–6 and 8 are implemented (as of 2026-08-08). Phase 7 was deliberately declined.**
> These files are now a historical record of what was decided and why — not a work queue. Running
> an implemented phase against today's codebase will fail its precondition check. Read them for
> rationale; read [`docs/architecture.md`](../architecture.md) for current state. Where the two
> disagree, architecture.md wins.
>
> **Phase 7 is a design document, not pending work.** It is an *alternative* to phase 6 — an MVC
> filter trigger rather than a server-held payload — and phase 6 was chosen instead. Nothing named
> `RequiresOtp` exists in the codebase. Its own header carries the caveats that matter if the
> trade-offs ever change, chiefly that `OtpVerification.Purpose` namespacing must be settled
> before the two mechanisms can coexist.
>
> **Superseded since the phases ran — do not follow these instructions:**
> - **AutoMapper has been removed entirely.** Phase 3 task 7 (unify the AutoMapper version) and
>   phase 4's `ProjectTo` rewrite are both obsolete: the query handlers now write their own
>   `.Select(...)`, and `IMapFrom<T>` / `MappingProfile` no longer exist. AutoMapper 15+ is
>   licensed under Lucky Penny's RPL-1.5, and the last MIT release (14.0.0) is permanently
>   vulnerable to CVE-2026-32933, so the dependency was dropped rather than pinned or paid for.
> - **MediatR is pinned to 12.5.0**, the last Apache-2.0 release, for the same licensing reason.
>   Phase 6 was written against 14.2.0.
>
> **Revision 2** — the phase files were re-checked against the codebase after first drafting.
> Corrections landed in every file: the exception→status mapping lives in
> `WebApi/Filters/ApiExceptionFilterAttribute.cs` (not the middleware), `RowVersion` must not go
> on `BaseEntity`, `ProjectTo` requires public DTO setters, `Application` already references
> EF Core, and `Directory.Build.props` needs a `.csproj` guard because of `docker-compose.dcproj`.
> Two new items were added to phase 3 (AutoMapper version split, `Domain`'s AutoMapper
> reference). Treat what is written now as authoritative *for the state at the time each phase
> ran*.

`00-shared-context.md` holds the repo map, build commands and conventions the phase files
assume. It describes the layout as it was when the phases ran.

## Phases, in order

| # | File | Scope | EF migration | Status |
|---|------|-------|--------------|--------|
| 1 | [`01-time-centralisation.md`](01-time-centralisation.md) | One source of "now", in UTC, enforced by an analyzer | no | done |
| 2 | [`02-correctness-fixes.md`](02-correctness-fixes.md) | Runtime bugs: `FindAsync`, sync-over-async, full-row updates, concurrency, OTP race | **yes** | done |
| 3 | [`03-dotnet-idiom.md`](03-dotnet-idiom.md) | Options pattern, cancellation, DI registration, secrets | no | done — task 7 resolved differently (see file) |
| 4 | [`04-repository-uow-refactor.md`](04-repository-uow-refactor.md) | Aggregate-scoped repositories, slim unit of work, projection queries | no | done |
| 5 | [`05-services-layering.md`](05-services-layering.md) | Service layer placement, `IdentityService` split, audit interceptor | no | done |
| 6 | [`06-generic-verified-operations.md`](06-generic-verified-operations.md) | Generic initiate/confirm verified operations, operation registry | **yes** (two) | done |
| 7 | [`07-filter-triggered-otp-gate.md`](07-filter-triggered-otp-gate.md) | `[RequiresOtp]` filter as a second trigger over the same OTP core | shipped with 6 | **declined** — alternative to 6 |
| 8 | [`08-sensitive-payload-encryption.md`](08-sensitive-payload-encryption.md) | Field-level encryption for verified-operation payloads | no | done |

Phases 1–3 were corrective (defects and inconsistencies). Phases 4–5 were structural, reshaping
the repository/unit-of-work pattern toward DDD, and had to run in that order. Phases 6–8 are
additive and independent of 1–5; 8 depends on 6, and 7 was an alternative to 6.

## If you write a phase 9

In a **fresh session**, with no prior context, give the agent exactly this:

```
Read docs/plans/00-shared-context.md, then execute docs/plans/<phase-file>.md in full.
Follow the file exactly. Do not do work from other phase files.
```

The conventions these files established still hold:

1. **One phase per session.** Later phases assume the earlier state.
2. **Green build and green tests before you finish.** Both commands in
   `00-shared-context.md` must pass.
3. **Update the docs in the same phase.** `.cursor/rules/*.mdc`, `.cursor/skills/*` and
   `docs/architecture.md` describe the conventions agents follow. A phase that changes a
   convention without updating them will be silently undone by the next session.
4. **Stop and report if a precondition fails** rather than improvising.
5. **Commit at the end of the phase.** Do not push or merge unless asked.
