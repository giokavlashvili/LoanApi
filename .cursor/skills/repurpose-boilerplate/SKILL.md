---
name: repurpose-boilerplate
description: >-
  Guides renaming and replacing the sample loan domain when starting a new
  product from this template. Use when repurposing the boilerplate, replacing
  LoanApplication/Currency/LoanType, or bootstrapping a fresh repository.
  Prefer the /repurpose slash command; do not auto-run destructive deletes.
disable-model-invocation: true
---

# Repurpose boilerplate

The loan domain is **demo content**. Keep the skeleton; replace the sample vertical slices.

## Keep (skeleton)

- `Domain/Common`
- `Application/Common`, `Application/Authenticate`, `Application/Otp` (if OTP stays)
- `Infrastructure/Identity`, logging/OTP infrastructure you still need
- `WebApi/{Filters,Middlwares,Extensions,Localization,Services}`

## Replace / delete (sample)

- `Domain/Entities` sample aggregates (`LoanApplication`, `LoanType`, `Currency`, …) and related `Events` / `Enums` / `Repositories`
- `Application/LoanApplications`, `Application/Currencies`, `Application/LoanTypes`
- `Infrastructure/Persistence/{Configurations,Repositories}` for those aggregates
- `WebApi/Controllers/{LoanApplications,Currencies,LoanTypes}Controller.cs`
- Seed data in `ApplicationDbContextInitialiser.TrySeedAsync`
- Sample keys in `WebApi/Resources/localization.json`
- Existing `Infrastructure/Migrations` — delete and create a fresh Initial migration for the new domain

## Rename (cosmetic)

- Repo folder + `LoanApi.sln` (solution *contents* need no namespace renames — assemblies stay `Domain` / `Application` / …)
- `WebApi/appsettings.json`: `ConnectionStrings:DefaultConnection` (`Database=…`)
- Secrets: all four (`JWT`, `RefreshToken`, `Otp`, `PayloadProtection`) are **empty** in
  `appsettings.json` by design; rotate the placeholders in `appsettings.Development.json` and
  supply real values from user secrets or a secret store before any real deployment

## Procedure

```
Progress:
- [ ] 1. Rename .sln / folder; update connection string + JWT secret
- [ ] 2. Remove sample controllers, Application slices, domain types, EF configs/repos
- [ ] 3. Trim DbContext sets / seed / repository DI registrations (`IUnitOfWork` is per-app, not per-aggregate — nothing to trim there)
- [ ] 4. Clear Migrations; add new Initial (ef-migration skill + SkipNSwag env)
- [ ] 5. Add first real vertical slice (add-vertical-slice skill)
- [ ] 6. Fix unit tests; build/test with -p:SkipNSwag=True
```

## Notes

- Do not rename assemblies to the product name unless you intentionally want a large mechanical rename.
- `WebApi/Dockerfile` targets `aspnet:10.0` / `sdk:10.0`, matching `net10.0`. Not yet verified by an
  actual `docker build`; see the Dockerfile notes in `docs/architecture.md` before relying on it.
- After domain replacement, re-run `npx gitnexus analyze` so impact/explore stay accurate.
- Deep reference: [`docs/architecture.md`](../../../docs/architecture.md).
