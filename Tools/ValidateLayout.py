"""Check the root development project and the independently installable package."""
import json
import os
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = ROOT / "Assets/AnimationSystemDOTS"


def validate(root=ROOT):
    errors = []
    package = root / "Assets/AnimationSystemDOTS"
    required = ["Packages/manifest.json", "ProjectSettings/ProjectVersion.txt",
                "Assets/AnimationSystemDOTS/package.json",
                "Assets/AnimationSystemDOTS/Runtime", "Assets/AnimationSystemDOTS/Editor",
                "Assets/AnimationSystemDOTS.Sandbox/Scenes/06_AnimatorECS.unity", "Assets/Tests"]
    errors.extend("Missing " + path for path in required if not (root / path).exists())
    for path in ["package.json", "Runtime", "Editor", "Authoring", "Rendering", "DevProject~", "Tests~", "Samples~"]:
        if (root / path).exists():
            errors.append("Obsolete root entry: " + path)
    if (package / "package.json").exists():
        data = json.loads((package / "package.json").read_text(encoding="utf-8"))
        manifest = json.loads((root / "Packages/manifest.json").read_text(encoding="utf-8"))
        if data["name"] in manifest["dependencies"]:
            errors.append("The root project must compile its Assets package without a self-dependency")
        if "com.unity.pipeline" in manifest["dependencies"]:
            errors.append("Disabled Unity CLI/Pipeline package is still installed")
        for name, version in data["dependencies"].items():
            if manifest["dependencies"].get(name) != version:
                errors.append("Root dependency differs from package: " + name)
        for name in ("Packages", "ProjectSettings", "Tools", "Library", "DevProject~"):
            if (package / name).exists():
                errors.append("Host content inside package: " + name)
    for path in [root / "Assets", *root.glob("Assets/*")]:
        if path.is_symlink() or (hasattr(path, "is_junction") and path.is_junction()):
            errors.append("Root development assets must be ordinary folders: " + str(path))
    guids = {}
    for folder, directories, names in os.walk(root / "Assets"):
        directories[:] = [name for name in directories if not name.endswith("~") and not name.startswith(".")]
        for name in names:
            if not name.endswith(".meta"):
                continue
            path = Path(folder) / name
            match = re.search(r"^guid: (\S+)", path.read_text(encoding="utf-8"), re.MULTILINE)
            if match:
                guid = match.group(1)
                if guid in guids:
                    errors.append("Duplicate imported asset GUID: " + str(path) + " / " + str(guids[guid]))
                guids[guid] = path
    return errors


if __name__ == "__main__":
    errors = validate()
    print("\n".join(errors) if errors else "Layout valid: one root project, independent Assets package, real sandbox and tests")
    raise SystemExit(bool(errors))
