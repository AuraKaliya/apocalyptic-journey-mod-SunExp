"""Extract the installed game's font and Canvas settings for local UI acceptance."""

import argparse
import hashlib
import json
from pathlib import Path

import UnityPy


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    game_data = args.game_data.resolve()
    output = args.output.resolve()
    if output.is_relative_to(game_data.parent):
        raise ValueError("UI fixtures must stay outside the installed game.")
    source = game_data / "data.unity3d"
    env = UnityPy.load(str(source))
    font_rows = []
    scalers = []
    script_ids = set()
    output.mkdir(parents=True, exist_ok=True)
    exported = False
    for obj in env.objects:
        if obj.type.name == "MonoScript":
            script = obj.read()
            if script.m_ClassName == "CanvasScaler":
                script_ids.add(obj.path_id)
        elif obj.type.name == "Font":
            font = obj.read()
            raw = bytes(font.m_FontData)
            font_rows.append({"name": font.m_Name, "bytes": len(raw)})
            if font.m_Name == "HarmonyOS_Sans_SC_Medium Fallback" and raw:
                (output / "HostFont.ttf").write_bytes(raw)
                exported = True
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            behaviour = obj.read()
            if behaviour.m_Script.path_id not in script_ids:
                continue
            script = behaviour.m_Script.read()
            if script.m_ClassName != "CanvasScaler":
                continue
            data = obj.read_typetree()
            scalers.append({
                "name": behaviour.m_GameObject.read().m_Name,
                "mode": data.get("m_UiScaleMode"),
                "referenceResolution": data.get("m_ReferenceResolution"),
                "match": data.get("m_MatchWidthOrHeight"),
            })
        except (AttributeError, KeyError, ValueError):
            continue
    if not exported:
        raise RuntimeError(f"Native source font unavailable; found {font_rows}")
    with (game_data / "Managed" / "Witch.dll").open("rb") as stream:
        managed_hash = hashlib.file_digest(stream, "sha256").hexdigest()
    metadata = {"witchSha256": managed_hash, "font": "HarmonyOS_Sans_SC_Medium Fallback", "fontRole": "installed CJK fallback source", "scalers": scalers}
    (output / "host-ui.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(metadata, ensure_ascii=False))


if __name__ == "__main__":
    main()
