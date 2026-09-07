"""Extract the installed Forest sprites for local Unity acceptance; never write game assets."""

import argparse
import hashlib
import json
from pathlib import Path

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    source = args.game_data / "data.unity3d"
    if args.output.resolve().is_relative_to(args.game_data.resolve()):
        raise ValueError("Fixtures must be outside the installation.")
    env = UnityPy.load(str(source))
    candidates = [obj for obj in env.objects if obj.type.name == "GameObject" and obj.read().m_Name == "Forest"]
    if len(candidates) != 1:
        raise ValueError(f"Expected one Forest prefab, found {len(candidates)}")
    args.output.mkdir(parents=True, exist_ok=True)
    sprites = []

    def visit(obj, parent_x=0.0, parent_y=0.0):
        data = obj.read()
        parts = [part.component.deref() for part in data.m_Component]
        transform = next(part.read() for part in parts if part.type.name in {"Transform", "RectTransform"})
        position = getattr(transform, "m_AnchoredPosition", transform.m_LocalPosition)
        x, y = parent_x + position.x, parent_y + position.y
        for part in parts:
            if part.type.name != "SpriteRenderer" or not data.m_IsActive:
                continue
            renderer = part.read()
            sprite = renderer.m_Sprite.read()
            filename = f"forest-{part.path_id}.png"
            sprite.image.save(args.output / filename)
            mesh = MeshHandler(sprite.m_RD, sprite.object_reader.version)
            mesh.process()
            if not mesh.m_Vertices:
                raise ValueError(f"Native sprite mesh has no vertices: {data.m_Name}")
            # Sprite.image decodes the tight mesh bounding box. Preserve its original
            # offset instead of centering every trimmed background layer at the origin.
            center_x = (min(v[0] for v in mesh.m_Vertices) + max(v[0] for v in mesh.m_Vertices)) / 2
            center_y = (min(v[1] for v in mesh.m_Vertices) + max(v[1] for v in mesh.m_Vertices)) / 2
            sprites.append({"name": data.m_Name, "file": filename, "x": x + center_x, "y": y + center_y,
                            "pivotX": 0.5, "pivotY": 0.5,
                            "pixelsPerUnit": sprite.m_PixelsToUnits,
                            "sortingLayerId": renderer.m_SortingLayerID,
                            "sortingOrder": renderer.m_SortingOrder})
        for child in transform.m_Children:
            visit(child.read().m_GameObject.deref(), x, y)

    visit(candidates[0])
    with source.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    (args.output / "forest.json").write_text(json.dumps({"sourceSha256": digest, "sprites": sprites},
                                                       ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Extracted {len(sprites)} native Forest sprites; SHA256={digest}")


if __name__ == "__main__":
    main()
