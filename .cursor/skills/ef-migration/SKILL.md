---
name: ef-migration
description: >-
  Adds or applies EF Core migrations with the SkipNSwag environment variable
  required by this solution. Use when creating migrations, updating the
  database, or when NSwag breaks dotnet ef.
---

# EF Core migrations

Migrations project: `Infrastructure`. Startup project: `WebApi`.

## PowerShell (this machine)

```powershell
$env:SkipNSwag = "True"
dotnet ef migrations add <Name> --project Infrastructure --startup-project WebApi
dotnet ef database update --project Infrastructure --startup-project WebApi
```

## Why SkipNSwag must be an env var

`dotnet ef` forwards `-p:` / trailing args to the **application**, not MSBuild. Without `$env:SkipNSwag = "True"`, the WebApi build runs NSwag and fails if the tool is missing.

## Expected noise (ignore)

- `dotnet-ef` tools version warning vs EF Core 10.x
- `HostAbortedException` stack after the provider is built (tooling shutdown)

## Checklist

```
- [ ] Domain + configurations already compile
- [ ] Set SkipNSwag env var
- [ ] migrations add with a descriptive name (e.g. AddOtpVerification)
- [ ] Review generated Up/Down and snapshot
- [ ] database update (or rely on Dev startup migrate)
- [ ] Build/test with -p:SkipNSwag=True
```

## Fresh domain reset

When repurposing: delete `Infrastructure/Migrations/*`, then `migrations add Initial` as above.
