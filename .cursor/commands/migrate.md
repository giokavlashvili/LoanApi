---
description: Create an EF Core migration with SkipNSwag set
---

Create an EF Core migration using the **ef-migration** skill (`.cursor/skills/ef-migration/SKILL.md`).

PowerShell:

```powershell
$env:SkipNSwag = "True"
dotnet ef migrations add <Name> --project Infrastructure --startup-project WebApi
```

Replace `<Name>` with a descriptive migration name from the user (or propose one). Review the generated files. Do not apply `database update` unless the user asks.
