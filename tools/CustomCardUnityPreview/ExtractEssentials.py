"""Extract Unity TMP's bundled resource package into the isolated preview project."""
from pathlib import Path
import sys
import tarfile
import io

package, project = Path(sys.argv[1]), Path(sys.argv[2]).resolve()
if package.suffix == ".tgz":
    with tarfile.open(package, "r:gz") as bundle:
        entry = next(m for m in bundle.getmembers() if m.name.endswith("TMP Essential Resources.unitypackage"))
        source = io.BytesIO(bundle.extractfile(entry).read())
    archive = tarfile.open(fileobj=source, mode="r:gz")
else:
    archive = tarfile.open(package, "r:gz")
with archive:
    entries = {member.name: member for member in archive.getmembers()}
    for member in entries.values():
        if not member.name.endswith("/pathname"):
            continue
        name = archive.extractfile(member).read().decode("utf-8").strip()
        if not name.startswith("Assets/TextMesh Pro/"):
            continue
        destination = (project / name).resolve()
        if not destination.is_relative_to(project):
            raise ValueError("Package path escaped preview project")
        prefix = member.name.removesuffix("pathname")
        asset = entries.get(prefix + "asset")
        meta = entries.get(prefix + "asset.meta")
        if asset and asset.isfile():
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(archive.extractfile(asset).read())
        else:
            destination.mkdir(parents=True, exist_ok=True)
        if meta and meta.isfile():
            Path(str(destination) + ".meta").write_bytes(archive.extractfile(meta).read())
