# Evolution Log Pattern

Use this reference to structure future skill-iteration notes.

## Evidence Packet

Record only the useful facts:

- task or bug name;
- files, shipped artifacts, and runtime surfaces involved;
- observed symptom matrix: successful paths, missing paths, owner/target or
  lifecycle asymmetry, and any misleading top-level success result;
- authoritative call chain or state owner, including the first incorrect
  owner/index/route rather than only the terminal symptom;
- rejected or reverted repair directions and the evidence that disproved them;
- final invariant and fix shape;
- counterfactual rule that would have prevented the earlier wrong decision;
- applicability boundary or counterexample;
- whether the durable result belongs in skill text, a reference, behavior
  test, deterministic script, compatibility/release gate, manual acceptance,
  or only `.learnings`.

## Graduation Decision

Before drafting a patch, answer:

1. Is this recurrent and decision-changing?
2. Is the rule narrower than the incident story and broader than one symbol?
3. Is its non-applicable case explicit?
4. What executable or observable evidence will keep it true?

If any answer is missing, keep collecting evidence or retain the item as an
incident note instead of promoting it into an operational skill.

## Patch Proposal Shape

For each proposed skill change, write:

- target skill;
- trigger change, if any;
- body change, if any;
- reference or script change, if any;
- duplicated guidance to remove or route to one authoritative home;
- validation command.

## Consolidation

When multiple lessons conflict, prefer the narrower rule tied to a verified
test or source file. If two workflows are both legitimate, split them by skill
trigger or reference instead of writing a vague universal rule.
