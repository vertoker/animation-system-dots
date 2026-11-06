# Root Unity project and Assets package migration

> **For agentic workers:** Use superpowers:executing-plans inline on master. Preserve unrelated files. No branches, worktrees, Unity CLI, pushes or additional permanent Unity projects.

**Goal:** Open the repository itself in Unity and install its self-contained package through `?path=/Assets/AnimationSystemDOTS`, following the CullingSystemDOTS repository layout requested by the user.

**Architecture:** The root owns `Assets`, `Packages`, `ProjectSettings`, development tooling and evidence. `Assets/AnimationSystemDOTS` owns Runtime, Authoring, Rendering, Editor, package documentation, package.json and importable Samples~. The ordinary development sandbox and regression tests are real folders in Assets. A deterministic export/check tool publishes the sandbox into Samples~; no junctions, package self-dependency or retained DevProject~ host are needed. Temporary consumer projects belong under ignored `.superpowers/consumers`.

**Tech stack:** Unity 6000.6.8f1; existing Entities/Graphics/Collections 6.6, Burst 2, Mathematics 1.4, URP 17.6; PowerShell and Python portable tooling.

**Spec:** User request of 2026-11-07 and inspected `CullingSystemDOTS/Docs/Superpowers/Plans/2026-11-06-package-layout-and-cleanup.md`.

## Constraints and decisions

- Retain all existing asset GUIDs, serialized references, assembly names and runtime behavior. No animation or rendering redesign.
- Use the existing DevProject~ settings/dependencies as the root development configuration, removing the package's local self-dependency and disabled Pipeline package.
- Back up the pre-existing untracked root configuration before replacing it. Keep prior reports/builds as historical evidence under root Artifacts. Do not count old tests as fresh migration verification.
- The package remains independently installable; MCP, Input System and host-only tests remain development concerns. Optional Input adapter behavior remains unchanged.
- Unlike a repository-wide submodule placed inside another project's Assets, Git subpath installation exports only the package. Document the correct submodule checkout outside Assets plus a local UPM path, or a sparse standalone package checkout inside Assets; do not advertise importing a nested Unity project as a package.
- Root sandbox is the editable source; package Samples~ is the committed import snapshot. Export is explicit and refuses stale unexpected destination files. Verification checks byte parity including metadata.
- Temporary consumers are generated on demand; no permanent secondary Unity host and no Unity CLI/batch automation.

## Review focus

1. Identical metadata GUIDs before/after moving; no duplicate imported GUIDs.
2. No self-dependency or host folders in the installed package payload.
3. UPM sample import and Assets package import resolve resources without root-project paths.
4. Root scenes/settings/tests compile without setup, symlinks or junctions.
5. Snapshot drift and obsolete consumer/build paths fail portable checks.

## Tasks

### Task 1: Relocate package and promote root host

- [ ] Add a layout verifier and run it against the old structure; expect failure on missing Assets package and root configuration.
- [ ] Capture tracked GUID inventory and preserve root configuration. Move package files with metadata; move the sandbox and tests into ordinary Assets folders; move tooling to Tools and maintainer plans to Docs/Superpowers.
- [ ] Promote host settings/dependencies/assets and retain historical caches/evidence outside the package. Remove old tracked host and obsolete junction setup.
- [ ] Verify preserved metadata and package boundaries; launch the root Editor normally and check fresh compilation through MCP.
- [ ] Commit exact owned paths on master.

### Task 2: Publish sample and validate installations

- [ ] Implement `Tools/SyncSandbox.py --check` and explicit export with root source and package destination. Test missing, changed, extra files and exact byte parity with temporary fixtures.
- [ ] Update acceptance tools to root Artifacts and consumer generation to `.superpowers/consumers`, targeting the Assets package. Preserve both UPM and Assets checks, add a pinned Git-subpath mode.
- [ ] Run portable checks, root EditMode/PlayMode suites and clean consumer import/smoke. Keep fresh reports with actual counts and limitations.
- [ ] Commit tooling and verified package snapshot.

### Task 3: Documentation, review and final state

- [ ] Update root/package README, installation/development instructions and maintained documentation paths. Keep earlier design/report paths explicitly historical where appropriate.
- [ ] Add concise root/package agent guidance for the architecture and temporary consumer workflow.
- [ ] Fresh whole-change review; resolve substantive findings and rerun affected checks.
- [ ] Commit documentation/evidence, leave root project open on the existing scene, report observed results and any remaining manual checks.

## Execution record

- 2026-11-07: Inspected CullingSystemDOTS read-only. No Unity Editor process is currently running, so no live scene must be interrupted. Existing root untracked settings are an incomplete generated project; they will be preserved before promotion.
- Authorization: user explicitly requested the architecture, plan and implementation together. Proceed under that authorization, without an additional design approval gate. Master-only work remains required.
