"""Read-only validation of repository skills, local references and matrix ownership."""
from __future__ import annotations

import argparse
from collections import deque
from dataclasses import asdict, dataclass
import json
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit

import yaml


class UniqueKeyLoader(yaml.SafeLoader):
    """Safe YAML with duplicate mapping keys rejected."""


def construct_mapping(loader: UniqueKeyLoader, node: yaml.MappingNode, deep: bool = False):
    loader.flatten_mapping(node)
    result = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node, deep=deep)
        if not isinstance(key, str):
            raise yaml.constructor.ConstructorError(
                None, None, "metadata keys must be strings", key_node.start_mark
            )
        if key in result:
            raise yaml.constructor.ConstructorError(
                None, None, f"duplicate key: {key}", key_node.start_mark
            )
        result[key] = loader.construct_object(value_node, deep=deep)
    return result


UniqueKeyLoader.add_constructor(
    yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, construct_mapping
)

FRONTMATTER = re.compile(r"\A---\r?\n(.*?)\r?\n---(?:\r?\n|$)", re.DOTALL)
MARKDOWN_LINK = re.compile(r"!?\[[^\]\n]*\]\(([^)\n]+)\)")
INLINE_RESOURCE = re.compile(
    chr(96) + r"((?:references|assets|scripts)/[^" + chr(96) + r"\r\n]+)" + chr(96)
)
SCRIPT_PATH = re.compile(r"\b(?:tools|scripts)[\\/][A-Za-z0-9_.\\/-]+\.ps1\b")
ABSOLUTE_MACHINE_PATH = re.compile(
    r"(?i)\b[A-Z]:[\\/]|(?<![A-Za-z0-9:/])/(?:home|Users|mnt)/"
)
ALLOWED_FRONTMATTER = {"name", "description", "license", "allowed-tools", "metadata"}
MATRIX_PATHS = ("tools/terrias-test-matrix.json", "tools/shared-release-matrix.json")
ENTRY_DOCS = ("README.md", "AGENTS.md", "docs/Terrias/README.md", "docs/AuraCombatAI/README.md")


@dataclass(frozen=True)
class Issue:
    path: str
    code: str
    message: str


class SkillAudit:
    def __init__(self, root: Path):
        self.root = root.resolve()
        self.issues: list[Issue] = []
        self.links: dict[Path, set[Path]] = {}
        self.bodies: dict[Path, str] = {}

    def error(self, path: Path, code: str, message: str) -> None:
        try:
            display = path.relative_to(self.root).as_posix()
        except ValueError:
            display = str(path)
        self.issues.append(Issue(display, code, message))

    def read(self, path: Path) -> str:
        try:
            return path.read_text(encoding="utf-8")
        except (OSError, UnicodeError) as exc:
            self.error(path, "read", str(exc))
            return ""

    def yaml_mapping(self, path: Path, text: str) -> dict:
        try:
            value = yaml.load(text, Loader=UniqueKeyLoader)
        except yaml.YAMLError as exc:
            self.error(path, "yaml", str(exc))
            return {}
        if not isinstance(value, dict):
            self.error(path, "yaml", "Expected a YAML mapping.")
            return {}
        return value

    def local_target(self, source: Path, raw: str, *, base: Path | None = None) -> Path | None:
        target = raw.strip()
        if target.startswith("<") and ">" in target:
            target = target[1:target.index(">")]
        else:
            target = re.split(r"""\s+["']""", target, maxsplit=1)[0]
        if ABSOLUTE_MACHINE_PATH.search(target):
            self.error(source, "absolute-link", target)
            return None
        parsed = urlsplit(target)
        if parsed.scheme or parsed.netloc or not parsed.path:
            return None
        path = ((base or source.parent) / unquote(parsed.path).replace("\\", "/")).resolve()
        if not path.is_relative_to(self.root):
            self.error(source, "escaping-link", target)
            return None
        if not path.exists():
            self.error(source, "missing-link", target)
            return None
        self.links.setdefault(source, set()).add(path)
        return path

    def audit_document(self, path: Path, skill: Path | None = None) -> None:
        text = self.read(path)
        self.bodies[path] = text
        self.links.setdefault(path, set())
        historical = "<!-- skill-audit: historical -->" in text
        if skill and not historical and ABSOLUTE_MACHINE_PATH.search(text):
            self.error(path, "machine-path", "Operational guidance contains an absolute machine path.")
        for match in MARKDOWN_LINK.finditer(text):
            self.local_target(path, match.group(1))
        if skill:
            for match in INLINE_RESOURCE.finditer(text):
                self.local_target(path, match.group(1), base=skill)
        for raw in set(SCRIPT_PATH.findall(text)):
            base = skill if raw.startswith("scripts") and skill else self.root
            self.local_target(path, raw, base=base)

    def audit_metadata(self, skill: Path) -> None:
        path = skill / "SKILL.md"
        if not path.is_file():
            self.error(path, "missing-skill", "Skill directory has no SKILL.md.")
            return
        text = self.read(path)
        match = FRONTMATTER.match(text)
        if not match:
            self.error(path, "frontmatter", "Expected YAML frontmatter at the start of SKILL.md.")
            return
        data = self.yaml_mapping(path, match.group(1))
        unknown = set(data) - ALLOWED_FRONTMATTER
        if unknown:
            self.error(path, "frontmatter-key", ", ".join(sorted(unknown)))
        name = data.get("name")
        if (
            not isinstance(name, str)
            or name != skill.name
            or not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", name)
            or len(name) > 64
        ):
            self.error(path, "name", "Name must match the folder and use hyphen-case (max 64).")
        description = data.get("description")
        if (
            not isinstance(description, str)
            or not description.strip()
            or len(description) > 1024
            or "<" in description
            or ">" in description
            or description.lstrip().startswith("[TODO:")
        ):
            self.error(path, "description", "Description is empty, unfinished or outside supported limits.")

        ui_path = skill / "agents/openai.yaml"
        ui = self.yaml_mapping(ui_path, self.read(ui_path))
        interface = ui.get("interface", {})
        if not isinstance(interface, dict):
            self.error(ui_path, "interface", "Expected an interface mapping.")
            return
        for field in ("display_name", "short_description", "default_prompt"):
            if not isinstance(interface.get(field), str) or not interface[field].strip():
                self.error(ui_path, "interface", f"Missing or invalid {field}.")
        short = interface.get("short_description", "")
        if isinstance(short, str) and not 25 <= len(short) <= 64:
            self.error(ui_path, "short-description", "Expected 25-64 characters.")
        prompt = interface.get("default_prompt", "")
        if isinstance(prompt, str) and "$" + skill.name not in prompt:
            self.error(ui_path, "default-prompt", "Prompt must name its own skill.")
        policy = ui.get("policy", {})
        if not isinstance(policy, dict) or (
            "allow_implicit_invocation" in policy
            and not isinstance(policy["allow_implicit_invocation"], bool)
        ):
            self.error(ui_path, "policy", "allow_implicit_invocation must be boolean.")
        for field in ("icon_small", "icon_large"):
            if field in interface and isinstance(interface[field], str):
                self.local_target(ui_path, interface[field], base=skill)
        if ABSOLUTE_MACHINE_PATH.search(self.read(ui_path)):
            self.error(ui_path, "machine-path", "UI metadata contains an absolute machine path.")

    def audit_matrices(self) -> None:
        for relative in MATRIX_PATHS:
            path = self.root / relative
            try:
                document = json.loads(self.read(path))
            except json.JSONDecodeError as exc:
                self.error(path, "matrix-json", str(exc))
                continue
            if not isinstance(document, dict) or document.get("schemaVersion") != 2:
                self.error(path, "matrix-schema", "Expected matrix schema 2.")
                continue
            steps = document.get("steps")
            if not isinstance(steps, list) or not steps:
                self.error(path, "matrix-steps", "Expected nonempty steps.")
                continue
            ids = set()
            for step in steps:
                if not isinstance(step, dict):
                    self.error(path, "matrix-step", "Step must be an object.")
                    continue
                if step.get("enabled", True) is False:
                    continue
                step_id = step.get("id")
                if not isinstance(step_id, str) or not step_id or step_id in ids:
                    self.error(path, "matrix-id", f"Missing/duplicate step id: {step_id}")
                else:
                    ids.add(step_id)
                target = step.get("path")
                if not isinstance(target, str) or not target:
                    self.error(path, "matrix-path", f"{step_id}: missing path.")
                    continue
                normalized = target.replace("\\", "/")
                if not normalized.startswith("tools/"):
                    self.error(path, "matrix-owner", f"{step_id}: product validator must live in tools.")
                self.local_target(path, normalized, base=self.root)

    def run(self) -> dict:
        skills_root = self.root / ".codex/skills"
        if not skills_root.is_dir():
            self.error(skills_root, "skill-root", "Project skill directory is missing.")
            skills = []
        else:
            skills = sorted(path for path in skills_root.iterdir() if path.is_dir())
        for skill in skills:
            self.audit_metadata(skill)
            for path in sorted(skill.rglob("*.md")):
                self.audit_document(path, skill)
        for relative in ENTRY_DOCS:
            path = self.root / relative
            if path.exists():
                self.audit_document(path)
        self.audit_matrices()

        reachable: set[Path] = set()
        pending = deque(skill / "SKILL.md" for skill in skills)
        pending.extend(skill / "agents/openai.yaml" for skill in skills)
        while pending:
            path = pending.popleft()
            if path in reachable:
                continue
            reachable.add(path)
            pending.extend(self.links.get(path, set()) - reachable)
        for skill in skills:
            for directory in ("references", "assets", "scripts"):
                for resource in (skill / directory).rglob("*"):
                    if resource.is_file() and resource.suffix != ".pyc" and resource not in reachable:
                        self.error(resource, "unreachable-resource", "No path from a skill entry to this resource.")

        counts = []
        for skill in skills:
            text = self.bodies.get(skill / "SKILL.md", "")
            counts.append({"name": skill.name, "lines": len(text.splitlines()), "characters": len(text)})
        return {
            "passed": not self.issues,
            "skillCount": len(skills),
            "entryLines": sum(item["lines"] for item in counts),
            "entryCharacters": sum(item["characters"] for item in counts),
            "skills": counts,
            "issues": [asdict(issue) for issue in self.issues],
        }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--json", action="store_true", dest="as_json")
    options = parser.parse_args()
    result = SkillAudit(options.repo_root).run()
    if options.as_json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        for issue in result["issues"]:
            print(f'{issue["path"]}: [{issue["code"]}] {issue["message"]}')
        status = "passed" if result["passed"] else "failed"
        print(f'Project skills {status}: {result["skillCount"]} skills, '
              f'{result["entryLines"]} entry lines, {len(result["issues"])} issue(s).')
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
