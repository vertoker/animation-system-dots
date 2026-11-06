"""Publish the editable root sandbox as the package's importable sample snapshot."""
import argparse
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "Assets/AnimationSystemDOTS.Sandbox"
DESTINATION = ROOT / "Assets/AnimationSystemDOTS/Samples~/Sandbox"


def files(root):
    return {p.relative_to(root).as_posix(): p for p in root.rglob("*") if p.is_file()}


def differences(source, destination):
    source_files, destination_files = files(source), files(destination)
    result = ["Missing: " + name for name in source_files.keys() - destination_files.keys()]
    result += ["Unexpected: " + name for name in destination_files.keys() - source_files.keys()]
    result += ["Changed: " + name for name in source_files.keys() & destination_files.keys()
               if source_files[name].read_bytes() != destination_files[name].read_bytes()]
    return sorted(result)


def export(source, destination):
    if not source.is_dir():
        raise ValueError("Missing sandbox source: " + str(source))
    unexpected = files(destination).keys() - files(source).keys()
    if unexpected:
        raise ValueError("Unexpected snapshot files; remove obsolete owned files explicitly: " + ", ".join(sorted(unexpected)))
    for name, path in files(source).items():
        target = destination / name
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or path.read_bytes() != target.read_bytes():
            shutil.copy2(path, target)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Reject drift without writing anything")
    args = parser.parse_args()
    if not args.check:
        export(SOURCE, DESTINATION)
        shutil.copy2(ROOT / "Assets/AnimationSystemDOTS.Sandbox.meta", DESTINATION.parent / "Sandbox.meta")
    errors = differences(SOURCE, DESTINATION)
    source_meta = ROOT / "Assets/AnimationSystemDOTS.Sandbox.meta"
    destination_meta = DESTINATION.parent / "Sandbox.meta"
    if not destination_meta.exists() or source_meta.read_bytes() != destination_meta.read_bytes():
        errors.append("Sandbox folder metadata differs")
    print("\n".join(errors) if errors else "Sandbox snapshot matches editable Assets source")
    raise SystemExit(bool(errors))
