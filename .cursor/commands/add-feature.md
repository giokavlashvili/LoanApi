---
description: Scaffold a new vertical slice feature end-to-end
---

Add a new feature using the **add-vertical-slice** project skill (`.cursor/skills/add-vertical-slice/SKILL.md`).

1. Read that skill and follow its checklist in order.
2. Ask only if the aggregate name / operations are unclear.
3. Mirror `LoanApplication` patterns; do not invent a new architecture.
4. Finish with `dotnet build LoanApi.sln -p:SkipNSwag=True` (and tests if feasible).
5. For the EF migration step, use the **ef-migration** skill (SkipNSwag **environment variable**).
