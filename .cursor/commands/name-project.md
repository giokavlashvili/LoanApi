---
description: Rename product identity (LoanApi → Name) without touching sample domain
---

Rename this template’s product identity using the **name-project** skill (`.cursor/skills/name-project/SKILL.md`).

1. Read the skill.
2. `$ARGUMENTS` is **Raw** — derive `Short` / `Name` / catalog per the skill. If missing or invalid, stop and ask.
3. Execute the skill procedure checklist. Do not commit unless asked. Do not run `/repurpose`.
