# Aura project

Terrias and AuraToolsExp are sibling product MODs. Shared foundations are
compiled once into Aura.Shared; product consumers are declared in
[tools/shared-consumers.json](tools/shared-consumers.json). TestMods is an
isolated prototype archive.

For repository orientation or work spanning products, read
[the project skill](.codex/skills/aura-project-dev/SKILL.md). Choose the owning
domain skill from its routing table; load additional references only for
boundaries actually changed.

For defect repair, migration, compatibility work, or technical-debt cleanup,
apply [the complete-solution gate](.codex/skills/aura-complete-solution-gate/SKILL.md)
to the affected capability. Existing unrelated architecture exceptions do not
expand a task into a repository-wide refactor.

Use current source and manifests for product facts, repository Managed
assemblies for compilation, and a matching decompile plus runtime evidence for
host behavior. Historical notes in .learnings are evidence to revalidate.

Select checks through the
[validation guide](.codex/skills/aura-project-dev/references/validation.md).
Product DLL publication has one writer: tools/Publish-MainSharedConsumers.ps1.
Serialize commands sharing DLL outputs. Do not repeat product builds or run
training/archive maintenance merely because a task touches a shared file.

Skill maintenance uses tools/Test-ProjectSkills.ps1. Product validators live
under tools, independently of skill folder names.
