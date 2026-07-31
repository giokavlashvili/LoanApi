# Enhancement plans

Execution plans derived from an audit of the persistence layer, repository pattern and services
(2026-07-30). Each phase is a **self-contained work order** for one agent session.

> **Revision 2** — the phase files were re-checked against the codebase after first drafting.
> Corrections landed in every file: the exception→status mapping lives in
> `WebApi/Filters/ApiExceptionFilterAttribute.cs` (not the middleware), `RowVersion` must not go
> on `BaseEntity`, `ProjectTo` requires public DTO setters, `Application` already references
> EF Core, and `Directory.Build.props` needs a `.csproj` guard because of `docker-compose.dcproj`.
> Two new items were added to phase 3 (AutoMapper version split, `Domain`'s AutoMapper
> reference). Treat what is written now as authoritative.

## How to run a phase

In a **fresh session**, with no prior context, give the agent exactly this:

```
Read docs/plans/00-shared-context.md, then execute docs/plans/<phase-file>.md in full.
Follow the file exactly. Do not do work from other phase files.
```

Read `00-shared-context.md` first in **every** session — it is short and holds the repo map,
build commands and conventions the phase files assume.

## Phases, in order

| # | File | Scope | Depends on | EF migration | Est. |
|---|------|-------|-----------|--------------|------|
| 1 | [`01-time-centralisation.md`](01-time-centralisation.md) | One source of "now", in UTC, enforced by an analyzer | — | no | S |
| 2 | [`02-correctness-fixes.md`](02-correctness-fixes.md) | Runtime bugs: `FindAsync`, sync-over-async, full-row updates, concurrency, OTP race | 1 | **yes** | M |
| 3 | [`03-dotnet-idiom.md`](03-dotnet-idiom.md) | Options pattern, cancellation, DI registration, secrets | 2 | no | M |
| 4 | [`04-repository-uow-refactor.md`](04-repository-uow-refactor.md) | Aggregate-scoped repositories, slim unit of work, projection queries | 3 | no | L |
| 5 | [`05-services-layering.md`](05-services-layering.md) | Service layer placement, `IdentityService` split, audit interceptor | 4 | no | L |
| 6 | [`06-operation-confirmation.md`](06-operation-confirmation.md) | Initiate/confirm topology: server-held payload, four-eyes approval | 5 | **yes** | L |

**Phases 1–3 are corrective** — they fix defects and inconsistencies, and are worth doing
regardless of any architectural opinion.

**Phases 4–5 are structural** — they reshape the repository/unit-of-work pattern toward DDD.
They are optional and can be deferred, but if done they must be done in order: phase 4 changes
handler constructors, phase 5 changes entity factory signatures, and doing 5 before 4 means
touching every handler twice.

**Phase 6 is additive** — it introduces a second confirmation topology beside the existing OTP
gate rather than changing anything already there. It depends on phase 5 only because it assumes
`OtpService` already sits in `Application`. It is the one phase that can be skipped outright
without leaving the repo inconsistent.

## Rules that apply to every phase

1. **One phase per session.** Do not pull work forward from a later file, even if it looks
   related and small. Later phases assume the earlier state.
2. **Green build and green tests before you finish.** Both commands in
   `00-shared-context.md` must pass. A phase that leaves the build broken is not done.
3. **Update the docs in the same phase.** `.cursor/rules/*.mdc`, `.cursor/skills/*` and
   `docs/architecture.md` describe the conventions agents follow. A phase that changes a
   convention without updating them will be silently undone by the next session. Each phase
   file lists exactly which ones it touches.
4. **Stop and report if a precondition fails.** Every phase file opens with a precondition
   check. If the repo does not match, do not improvise — report what differs.
5. **Commit at the end of the phase**, using the message given in the phase file. Do not
   push, and do not merge, unless asked.
