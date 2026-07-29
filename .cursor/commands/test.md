---
description: Run unit tests skipping NSwag
---

Run the test suite with NSwag skipped:

```bash
dotnet test LoanApi.sln -p:SkipNSwag=True
```

If the user named a filter or project, narrow with `--filter` or a specific `.csproj`. Report failures with the failing test name and likely cause.
