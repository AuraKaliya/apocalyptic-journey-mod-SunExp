# Skill Validation

## Reproducible tooling

Use Python 3.10+ and a repository-local environment. The check does not install
packages or modify global Python.

```powershell
python -m venv .venv/skills
.venv/skills/Scripts/python.exe -m pip install -r tools/requirements-skills.txt
tools/Test-ProjectSkills.ps1
```

The wrapper selects .venv/skills when present, otherwise Python on PATH.
An explicit `-PythonPath` overrides discovery. Missing dependencies produce
a setup instruction instead of interpreting an import failure as invalid skills.

The project gate validates frontmatter, UI metadata, local Markdown references,
skill resource reachability, runnable repository script paths and test-matrix
ownership. It rejects machine-specific absolute paths in operational skills.
Its isolated fixtures exercise broken references, malformed metadata and
invalid matrix entries. It does not validate internet links or Unity behavior.

```powershell
.venv/skills/Scripts/python.exe -m unittest discover -s tools/tests -p test_project_skills.py
```

The system skill-creator quick validator can additionally be run from the
actual installed skill location discovered in the active catalog. Do not
hard-code an OS username or assume the validator is installed on every machine.

## Changed helper validation

After moving a product validator, execute it against the current product and
verify its matrix caller. Compare old/new output where semantics were intended
to remain identical. Run matrix inventories without invoking unrelated builds.

After changing Get-AuraProjectContext, verify its JSON output against the
actual consumer manifest, matrices and source declarations. Current facts must
be derived rather than copied into a second inventory.

For a routing rewrite, use
[representative task evaluation](task-evaluation.md). Record what was actually
tested and any remaining runtime acceptance. Keep detailed iteration history
in a task report or Git, not in operational skill bodies.
