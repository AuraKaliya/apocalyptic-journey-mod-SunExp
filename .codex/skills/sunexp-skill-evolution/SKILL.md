---
name: sunexp-skill-evolution
description: Project-local skill for distilling SunExp development traces into durable Codex skill updates, including analyzing recent commits, test failures, manual debugging lessons, user corrections, validation gaps, architecture drift, stale old-repository or retired-workflow anchors, reference restructuring, trigger tuning, and iterative improvement of project-local skills under .codex/skills.
---

# SunExp Skill Evolution

Use this skill when the user asks to update, distill, refactor, rename, or
iterate the repository skills. Pair it with `skill-creator` and the affected
SunExp skill bodies.

The goal is to convert repeated development lessons into stable, low-noise
procedural knowledge. Prefer tests or scripts for fragile invariants and
references for detailed context.

## Workflow

1. Collect evidence:
   - recent commits: `git log --oneline -n 20 -- .codex/skills SunExp-Dev SunExp tools docs`
   - changed files and tests from relevant commits;
   - current validation failures or manual debugging notes;
   - user corrections and repeated assistant mistakes.
2. Classify each lesson:
   - Trigger: frontmatter description or skill split.
   - Rule: concise SKILL.md hard rule.
   - Reference: detailed explanation loaded only when needed.
   - Script/test: deterministic check for fragile behavior.
   - Asset/template: reusable output resource.
   - Staleness cleanup: old repository paths, old mode names, retired workflow
     assumptions, or memory-derived anchors that no longer match this repo.
3. Choose the smallest durable change:
   - tighten an existing trigger;
   - move verbose body content into `references/`;
   - add a focused sub-skill when one domain has its own workflow;
   - add or update validation when a rule is easy to check.
4. Use `references/evolution-log-pattern.md` when drafting a reusable evidence
   packet or patch proposal for a skill update. Use
   `references/stale-anchor-registry.md` when recording old repository roots,
   old decompile folders, retired content ids, or other historical anchors.
5. Audit for stale SunExp anchors before and after editing:
   - run `scripts/audit-sunexp-skill-staleness.ps1`;
   - remove or quarantine references to retired project roots, old mode names,
     retired data-only workflows, and obsolete implementation assumptions;
   - preserve compatibility notes only when they are explicitly labeled as
     historical and cannot trigger current workflow decisions.
6. Edit skills with progressive disclosure:
   - keep `SKILL.md` short and action-oriented;
   - keep references one hop from `SKILL.md`;
   - avoid duplicated rules across skills; route instead.
7. Validate skill metadata and representative project checks.

## Distillation Rules

- Capture only durable lessons likely to recur.
- Do not preserve one-off implementation details unless they prevent a known
  regression.
- Do not promote memory-derived or old-repository facts into current skills
  without verifying them against this repository.
- Treat retired data-only SunExp workflows, retired project roots, and renamed
  mode names as stale by default. Keep them only inside an explicit migration or
  archaeology note.
- Keep old docs, old content ids, old decompile-folder versions, and historical
  corrections out of operational skills. Record them only in evolution
  references or deterministic validation scripts.
- Put brittle invariants in tests where possible.
- Put detailed domain explanation in references, not in top-level `SKILL.md`.
- Keep old skill names stable unless the user explicitly approves a migration
  or the old directory can remain as a compatibility route.
- When renaming toward a generic Witch mod skill, first create the generic
  target and keep SunExp-specific skill names as forwarding/project aliases
  until triggers prove stable.

## Review Checklist

- Does each new or changed skill have a clear frontmatter description with
  concrete trigger contexts?
- Is the body under control, with detailed material moved to references?
- Are references linked directly from `SKILL.md`?
- Are examples and generated template placeholders removed?
- Are validation commands serial when they share DLL outputs?
- Did the update avoid reverting unrelated user work?

## Validation

Run:

```powershell
 .codex\skills\sunexp-skill-evolution\scripts\audit-sunexp-skill-staleness.ps1
python C:\Users\75601\.codex\skills\.system\skill-creator\scripts\quick_validate.py .codex\skills\<skill-name>
```

Run this for every changed or newly created skill. Then run representative
project checks for any skill content that encodes project behavior.
