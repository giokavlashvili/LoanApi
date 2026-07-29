---
description: Build the solution skipping NSwag
---

Build the solution with NSwag skipped:

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

Fix any compile errors you introduced. Do not run NSwag unless explicitly asked.
