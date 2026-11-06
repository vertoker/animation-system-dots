# Animation System DOTS agent guide

This repository root is the only permanent Unity development project. The complete installable package is `Assets/AnimationSystemDOTS`. Read its [package guide](Assets/AnimationSystemDOTS/AGENTS.md) before changing shipped code.

- Work and commit on `master` only. No additional branches/worktrees, pushes or history rewriting. Commit completed verified changes autonomously and preserve unrelated files.
- Use Coplay MCP and Unity Test Runner for Editor work. Unity CLI/Pipeline is disabled. Pin the exact instance for this project; do not operate unrelated Editors. Preserve unsaved user scenes.
- Runtime, Authoring, Rendering, Editor, required resources, package documentation and Samples~ belong inside the package. Root Packages/ProjectSettings and MCP are development-only.
- The editable sandbox is the ordinary folder `Assets/AnimationSystemDOTS.Sandbox`; developer tests are in `Assets/Tests`. No junctions or package self-dependency are needed.
- After sandbox edits, run `python Tools/SyncSandbox.py`, then `python Tools/SyncSandbox.py --check`. Samples~ is its published snapshot; do not edit it independently. Unexpected snapshot files require deliberate scoped cleanup.
- Use `.superpowers/consumers` for generated, disposable installation projects and `.superpowers/sdd` for execution experiments. Do not add another permanent Unity host. Old reports are historical evidence; perform and report fresh checks for a changed revision.
- Keep asset GUIDs, assembly boundaries and serialized compatibility. Do not change animation/rendering behavior as part of a file move.
- Run `python Tools/ValidateLayout.py` and `python -m unittest discover -s Tools/tests -v` for layout/tooling changes. Run affected Unity suites when imported content or compilation changes. Never claim checks you have not observed.

See [README](README.md), [migration plan](Docs/Superpowers/Plans/2026-11-07-project-layout.md) and [package support matrix](Assets/AnimationSystemDOTS/Documentation~/SupportMatrix.md).
