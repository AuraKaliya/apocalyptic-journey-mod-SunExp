"""Isolated behavior tests for skill validation and project discovery tools."""
from __future__ import annotations

import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
from validate_project_skills import SkillAudit


class ProjectFixture(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="aura-skill-tests-")
        self.root = Path(temporary.name).resolve()
        self.assertTrue(self.root.is_relative_to(Path(tempfile.gettempdir()).resolve()))
        self.addCleanup(temporary.cleanup)
        self.skill = self.root / ".codex/skills/example-dev"
        self.write(".codex/skills/example-dev/SKILL.md",
                   "---\nname: example-dev\ndescription: Maintain the example domain.\n---\n\n"
                   "# Example\n\n[Contract](references/contract.md)\n"
                   + chr(96) + "tools/Test-Example.ps1" + chr(96) + "\n")
        self.write(".codex/skills/example-dev/references/contract.md", "# Contract\n")
        self.write(".codex/skills/example-dev/agents/openai.yaml",
                   'interface:\n  display_name: "Example"\n'
                   '  short_description: "Maintain example domain behavior."\n'
                   '  default_prompt: "Use $example-dev for the example."\n')
        self.write("tools/Test-Example.ps1", "param()\n")
        for name in ("terrias-test-matrix.json", "shared-release-matrix.json"):
            self.write_json("tools/" + name, {
                "schemaVersion": 2,
                "steps": [{"id": "example", "path": "tools/Test-Example.ps1", "enabled": True,
                           "owner": "Example", "category": "behavior", "cost": "fast",
                           "profiles": ["example"], "impactTags": ["example"]}],
            })

    def write(self, relative: str, text: str) -> Path:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        return path

    def write_json(self, relative: str, data) -> Path:
        return self.write(relative, json.dumps(data, ensure_ascii=False))

    def codes(self) -> set[str]:
        return {issue["code"] for issue in SkillAudit(self.root).run()["issues"]}


class SkillValidatorTests(ProjectFixture):
    def test_valid_skill_and_nested_reference_are_reachable(self):
        self.write(".codex/skills/example-dev/references/contract.md", "[Detail](detail.md)\n")
        self.write(".codex/skills/example-dev/references/detail.md", "Current contract.\n")
        self.assertEqual(set(), self.codes())

    def test_missing_reference_is_not_a_success(self):
        self.write(".codex/skills/example-dev/references/contract.md", "[Missing](missing.md)\n")
        self.assertIn("missing-link", self.codes())

    def test_relative_escape_is_rejected(self):
        self.write(".codex/skills/example-dev/references/contract.md", "[Outside](../../../../../../outside.md)\n")
        self.assertIn("escaping-link", self.codes())

    def test_local_machine_path_is_rejected(self):
        self.write(".codex/skills/example-dev/references/contract.md",
                   r"Run C:\Users\Somebody\tools\check.ps1." + "\n")
        self.assertIn("machine-path", self.codes())

    def test_historical_note_keeps_history_without_becoming_operational(self):
        self.write(".codex/skills/example-dev/references/contract.md",
                   "<!-- skill-audit: historical -->\n" + r"Old root C:\Archive\OldRepo." + "\n")
        self.assertEqual(set(), self.codes())

    def test_duplicate_yaml_keys_are_rejected(self):
        self.write(".codex/skills/example-dev/SKILL.md",
                   "---\nname: example-dev\nname: renamed-dev\ndescription: Example.\n---\n")
        self.assertIn("yaml", self.codes())

    def test_yaml_object_construction_is_rejected(self):
        self.write(".codex/skills/example-dev/agents/openai.yaml",
                   "interface: !!python/object:object {}\n")
        self.assertIn("yaml", self.codes())

    def test_folder_and_name_must_agree(self):
        path = self.skill / "SKILL.md"
        path.write_text(path.read_text(encoding="utf-8").replace("name: example-dev", "name: wrong-dev"),
                        encoding="utf-8")
        self.assertIn("name", self.codes())

    def test_unknown_policy_type_and_stale_default_prompt_are_rejected(self):
        self.write(".codex/skills/example-dev/agents/openai.yaml",
                   'interface:\n  display_name: "Example"\n'
                   '  short_description: "Maintain example domain behavior."\n'
                   '  default_prompt: "Use $old-example-dev."\n'
                   'policy:\n  allow_implicit_invocation: "false"\n')
        self.assertTrue({"policy", "default-prompt"}.issubset(self.codes()))

    def test_unreachable_resource_is_detected(self):
        self.write(".codex/skills/example-dev/references/orphan.md", "No caller.\n")
        self.assertIn("unreachable-resource", self.codes())

    def test_product_matrix_cannot_depend_on_skill_script(self):
        self.write(".codex/skills/example-dev/scripts/test.ps1", "param()\n")
        self.write_json("tools/terrias-test-matrix.json", {
            "schemaVersion": 2,
            "steps": [{"id": "wrong", "path": ".codex/skills/example-dev/scripts/test.ps1"}],
        })
        self.assertIn("matrix-owner", self.codes())

    def test_duplicate_matrix_id_and_missing_script_are_detected(self):
        self.write_json("tools/terrias-test-matrix.json", {
            "schemaVersion": 2,
            "steps": [{"id": "same", "path": "tools/Test-Example.ps1"},
                      {"id": "same", "path": "tools/Missing.ps1"}],
        })
        self.assertTrue({"matrix-id", "missing-link"}.issubset(self.codes()))

    def test_external_links_and_heading_links_are_not_local_files(self):
        self.write(".codex/skills/example-dev/references/contract.md",
                   "[Web](https://example.com/a)\n[Heading](#contract)\n")
        self.assertEqual(set(), self.codes())


@unittest.skipUnless(shutil.which("pwsh"), "PowerShell is needed for command behavior tests.")
class ProjectCommandTests(ProjectFixture):
    def invoke(self, script: Path, *arguments: str) -> subprocess.CompletedProcess:
        return subprocess.run(
            [shutil.which("pwsh"), "-NoProfile", "-NonInteractive", "-File", str(script), *arguments],
            capture_output=True, text=True, encoding="utf-8", timeout=30, check=False,
        )

    def context_fixture(self):
        self.write_json("tools/shared-consumers.json", {
            "schemaVersion": 1,
            "consumers": [{"id": "FixtureProduct", "classification": "product"}],
        })
        self.write("AuraCgShared/AuraCgRegistry.cs",
                   "public const int CurrentRegistrySchemaVersion = 41;\n")
        self.write("AuraCgShared/AuraCgRuntime.cs", "public const int CurrentProtocolVersion = 42;\n")
        self.write("Terrias-Dev/Mechanics/CompanionAuthorityService.cs",
                   "public const int ProjectionProtocolVersion = 43;\n")
        for version in ("1.0.2", "1.0.10"):
            (self.root / ("开发参考资料/反编译文件夹v" + version)).mkdir(parents=True)

    def test_context_reads_fixtures_instead_of_copied_current_numbers(self):
        self.context_fixture()
        result = self.invoke(TOOLS / "Get-AuraProjectContext.ps1", "-RepoRoot", str(self.root), "-AsJson")
        self.assertEqual(0, result.returncode, result.stderr)
        context = json.loads(result.stdout)
        self.assertEqual("FixtureProduct", context["consumers"][0]["id"])
        self.assertEqual([41, 42, 43], [item["value"] for item in context["sourceContracts"]])
        self.assertEqual("开发参考资料/反编译文件夹v1.0.10", context["decompileCandidates"][0])

    def test_missing_contract_declaration_fails_instead_of_inventing_version(self):
        self.context_fixture()
        self.write("AuraCgShared/AuraCgRegistry.cs", "// Declaration moved; discovery must be updated.\n")
        result = self.invoke(TOOLS / "Get-AuraProjectContext.ps1", "-RepoRoot", str(self.root), "-AsJson")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("CurrentRegistrySchemaVersion", result.stderr)

    def test_tool_build_delegates_once_without_building_trainer(self):
        wrapper = self.write("tools/Build-AuraToolsExpDll.ps1",
                             (TOOLS / "Build-AuraToolsExpDll.ps1").read_text(encoding="utf-8"))
        self.write("tools/Build-MainSharedConsumers.ps1",
                   "param([string]$Configuration,[string]$ManagedPath)\n"
                   "@{configuration=$Configuration; managed=$ManagedPath} | ConvertTo-Json -Compress\n"
                   "$global:LASTEXITCODE=0\n")
        self.write("tools/Build-AuraFoundationTrainer.ps1", 'throw "Trainer must not run."\n')
        result = self.invoke(wrapper, "-Configuration", "Debug", "-ManagedPath", str(self.root / "Managed"))
        self.assertEqual(0, result.returncode, result.stderr)
        receipt = json.loads(result.stdout)
        self.assertEqual("Debug", receipt["configuration"])
        self.assertEqual(str(self.root / "Managed"), receipt["managed"])

    def test_product_build_failure_is_reported(self):
        wrapper = self.write("tools/Build-AuraToolsExpDll.ps1",
                             (TOOLS / "Build-AuraToolsExpDll.ps1").read_text(encoding="utf-8"))
        self.write("tools/Build-MainSharedConsumers.ps1",
                   "param([string]$Configuration,[string]$ManagedPath)\n$global:LASTEXITCODE=17\n")
        result = self.invoke(wrapper)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("product build transaction failed", result.stderr)


if __name__ == "__main__":
    unittest.main()
