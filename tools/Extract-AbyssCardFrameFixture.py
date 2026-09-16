"""Read the installed card-frame sprite and export an untrimmed test fixture."""

import argparse
import hashlib
import json
from pathlib import Path

import UnityPy


def main() -> None:
    """Export the native gold frame without changing any installation asset."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    game_data = args.game_data.resolve()
    output = args.output.resolve()
    if output.is_relative_to(game_data.parent):
        raise ValueError("Test fixtures must be outside the game installation.")
    source = game_data / "data.unity3d"
    env = UnityPy.load(str(source))
    frames = [
        frame
        for obj in env.objects
        if obj.type.name == "Sprite" and (frame := obj.read()).m_Name == "金卡"
    ]
    if len(frames) != 1:
        raise ValueError(f"Expected one native gold card-frame sprite, found {len(frames)}")
    frame = frames[0]
    data = frame.object_reader.read_typetree()
    render_data = data["m_RD"]
    texture = frame.m_RD.texture.read()
    output.mkdir(parents=True, exist_ok=True)
    texture.image.save(output / "native-card-frame.png")
    with source.open("rb") as stream:
        source_hash = hashlib.file_digest(stream, "sha256").hexdigest()
    with (game_data / "Managed" / "Witch.dll").open("rb") as stream:
        witch_hash = hashlib.file_digest(stream, "sha256").hexdigest()
    metadata = {
        "schemaVersion": 1,
        "sourceSha256": source_hash,
        "witchSha256": witch_hash,
        "sourceAsset": frame.object_reader.assets_file.name,
        "spritePathId": frame.object_reader.path_id,
        "spriteName": frame.m_Name,
        "rect": data["m_Rect"],
        "pivot": data["m_Pivot"],
        "pixelsPerUnit": data["m_PixelsToUnits"],
        "textureRect": render_data["textureRect"],
        "textureRectOffset": render_data["textureRectOffset"],
        "textureWidth": texture.m_Width,
        "textureHeight": texture.m_Height,
    }
    (output / "native-card-frame.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps(metadata, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
