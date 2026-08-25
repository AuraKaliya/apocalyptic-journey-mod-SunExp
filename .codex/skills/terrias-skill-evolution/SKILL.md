---
name: terrias-skill-evolution
description: Project-local skill for distilling Terrias development traces into durable Codex skill updates, including analyzing recent commits, test failures, manual debugging lessons, user corrections, validation gaps, architecture drift, stale old-repository or retired-workflow anchors, reference restructuring, trigger tuning, and iterative improvement of project-local skills under .codex/skills.
---

# Terrias Skill Evolution

Use this skill when the user asks to update, distill, refactor, rename, or
iterate the repository skills. Pair it with `skill-creator` and the affected
Terrias skill bodies.

The goal is to convert repeated development lessons into stable, low-noise
procedural knowledge. Prefer tests or scripts for fragile invariants and
references for detailed context.

## Workflow

1. Collect evidence:
   - the runtime symptom and an asymmetry matrix: what worked, what was absent,
     which owner/target/lifecycle branch differed, and what the top-level result
     claimed;
   - the newest applicable decompile call chain for host behavior, plus current
     `Managed/` signatures for compilation;
   - recent commits: `git log --oneline -n 20 -- .codex/skills Terrias-Dev Terrias tools docs`
   - changed files and tests from relevant commits;
   - current validation failures or manual debugging notes;
   - user corrections, repeated assistant mistakes, and discarded repair
     directions whose semantics were disproved.
   - test ownership, call graph, duplicate coverage, source-snapshot assertions,
     and whether a test belongs to a product or an archived prototype.
2. Classify each lesson:
   - Trigger: frontmatter description or skill split.
   - Rule: concise SKILL.md hard rule.
   - Reference: detailed explanation loaded only when needed.
   - Script/test: deterministic check for fragile behavior.
   - Manual acceptance: Unity, host lifecycle, rendering, or real multiplayer
     evidence that cannot be established by local automated tests.
   - Asset/template: reusable output resource.
   - Staleness cleanup: old repository paths, old mode names, retired workflow
     assumptions, or memory-derived anchors that no longer match this repo.
3. Choose the smallest durable change:
   - tighten an existing trigger;
   - move verbose body content into `references/`;
   - add a focused sub-skill when one domain has its own workflow;
   - add or update validation when a rule is easy to check.
   Before promoting a lesson, apply the graduation gate below. Leave incident
   detail in `.learnings` when it does not qualify as operational guidance.
4. Use `references/evolution-log-pattern.md` when drafting a reusable evidence
   packet or patch proposal for a skill update. Use
   `references/stale-anchor-registry.md` when recording old repository roots,
   old decompile folders, retired content ids, or other historical anchors.
5. Audit for stale Terrias anchors before and after editing:
   - run `scripts/audit-terrias-skill-staleness.ps1`;
   - remove or quarantine references to retired project roots, old mode names,
     retired data-only workflows, and obsolete implementation assumptions;
   - preserve compatibility notes only when they are explicitly labeled as
     historical and cannot trigger current workflow decisions.
6. Edit skills with progressive disclosure:
   - keep `SKILL.md` short and action-oriented;
   - keep references one hop from `SKILL.md`;
   - avoid duplicated rules across skills; route instead.
7. Validate skill metadata and representative project checks.

## Graduation Gate

Promote an incident into an operational skill only when all answers are yes:

- Recurrence: does it describe a class of future tasks rather than one id,
  version, log line, protocol number, or implementation name?
- Decision impact: would this rule have changed the earlier diagnosis, design,
  ownership choice, or validation plan before the failed repair was written?
- Scope: does it name where the rule applies and a meaningful counterexample or
  non-applicable case so it does not become a universal ban?
- Enforcement: can the durable invariant live in a behavior test, script,
  compatibility/release gate, or explicit manual acceptance check?

If the lesson explains history but fails this gate, keep it as a learning or
postmortem fact. Do not enlarge `SKILL.md` merely to preserve the story.

## Distillation Rules

- Capture only durable lessons likely to recur.
- Require a counterfactual: name the earlier wrong decision the distilled rule
  would have prevented. A rule that would not change a future decision is
  documentation, not skill guidance.
- Keep the incident, the generalized invariant, and the enforcement mechanism
  distinct. Do not copy a postmortem paragraph into a top-level skill.
- Preserve the rule's applicability boundary. For example, a nested temporary
  mutation may require stack ownership while a persistent selection or
  authoritative snapshot requires a different conflict model.
- Do not preserve one-off implementation details unless they prevent a known
  regression.
- Do not promote memory-derived or old-repository facts into current skills
  without verifying them against this repository.
- Treat retired data-only Terrias workflows, retired project roots, and renamed
  mode names as stale by default. Keep them only inside an explicit migration or
  archaeology note.
- Keep old docs, old content ids, old decompile-folder versions, and historical
  corrections out of operational skills. Record them only in evolution
  references or deterministic validation scripts.
- Put brittle invariants in tests where possible.
- Put behavior harnesses in stable `*Tests` projects rather than generating
  temporary projects from PowerShell here-strings. Keep architecture gates
  declarative and limited to real namespace, dependency, hook, and entrypoint
  boundaries.
- Keep expensive worker integration, simulation acceptance, artifact, and
  archive maintenance checks as explicit matrix steps. Do not hide them behind
  ordinary product or shared behavior entrypoints.
- Let RPC security scripts enforce generic authority-registration and transport
  boundaries only. Sender scope, payload guards, duplicate suppression, and
  lifecycle cleanup belong in the owning domain's behavior tests.
- Keep only tests that prove a current behavior, public contract, boundary,
  release artifact, or owned content requirement. Replace source snapshots when
  the semantic contract remains; delete completed migrations and duplicated
  historical constraints.
- Keep `TestMods` validation isolated behind `tools/Test-TestMods.ps1`. Do not
  add prototype consumers to product/shared default validation or release
  matrices after their functionality has moved into a product MOD.
- Put detailed domain explanation in references, not in top-level `SKILL.md`.
- Keep old skill names stable unless the user explicitly approves a migration
  or the old directory can remain as a compatibility route.
- When renaming toward a generic Witch mod skill, first create the generic
  target and keep Terrias-specific skill names as forwarding/project aliases
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
pwsh -NoProfile -File .codex\skills\terrias-skill-evolution\scripts\audit-terrias-skill-staleness.ps1
py -X utf8 C:\Users\75601\.codex\skills\.system\skill-creator\scripts\quick_validate.py .codex\skills\<skill-name>
```

Run this for every changed or newly created skill. Then run representative
project checks for any skill content that encodes project behavior.
