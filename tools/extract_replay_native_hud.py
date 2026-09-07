"""Read installed native HUD sprites into ignored Unity test fixtures; never modify game assets."""
import argparse
import hashlib
import json
from pathlib import Path

import UnityPy


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    packed = args.game_data / "data.unity3d"
    environment = UnityPy.load(str(packed))
    hp = [obj for obj in environment.objects
          if obj.type.name == "GameObject" and obj.read().m_Name == "HpItem"]
    if len(hp) != 1:
        raise ValueError(f"Expected one native HpItem, found {len(hp)}")
    args.output.mkdir(parents=True, exist_ok=True)
    paths = {"HpItem/background": "background", "HpItem/DefendShow/Small": "defense"}
    with packed.open("rb") as packed_file:
        source_hash = hashlib.file_digest(packed_file, "sha256").hexdigest()
    receipt = {"gameDataSha256": source_hash, "sprites": []}

    def visit(game_object, path):
        components = [part.component.deref() for part in game_object.m_Component]
        if path in paths:
            renderer = next(part.read() for part in components if part.type.name == "SpriteRenderer")
            material = renderer.m_Materials[0].read()
            shader = material.m_Shader.read().m_ParsedForm.m_Name
            if shader != "Universal Render Pipeline/2D/Sprite-Lit-Default":
                raise ValueError(f"Native HUD shader changed: {path} -> {shader}")
            output = args.output / (paths[path] + ".png")
            renderer.m_Sprite.read().image.save(output)
            receipt["sprites"].append({"path": path, "shader": shader, "file": output.name,
                                       "sha256": hashlib.sha256(output.read_bytes()).hexdigest()})
        transform = next(part.read() for part in components if part.type.name in ("Transform", "RectTransform"))
        for child in transform.m_Children:
            child_object = child.read().m_GameObject.read()
            visit(child_object, path + "/" + child_object.m_Name)

    visit(hp[0].read(), "HpItem")
    if len(receipt["sprites"]) != len(paths):
        raise ValueError("Native health/defense fixture is incomplete")
    (args.output / "manifest.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Extracted {len(paths)} native HUD sprites; source SHA256={receipt['gameDataSha256']}")


if __name__ == "__main__":
    main()
