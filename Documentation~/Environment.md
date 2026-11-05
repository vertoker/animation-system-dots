# Environment

Verified 2026-11-05 in `C:/Projects/UnityPlugins/AnimationSystemDOTS`.

| Component | Resolved version | Source |
|---|---|---|
| Unity Editor | 6000.6.8f1 (237b6593792b) | ProjectVersion.txt and live MCP project/info |
| Universal RP | 17.6.0 | Editor core package |
| Entities | 6.6.0 | Editor core package |
| Entities Graphics | 6.6.0 | Editor core package |
| Burst | 2.0.0 | Editor core package |
| Collections | 6.6.0 | Editor core package |
| Mathematics | 1.4.0 | Editor core package |
| Test Framework | 1.8.0 | Editor core package |
| Performance testing | 6.6.0 | Editor core package |
| Coplay MCP for Unity | 10.1.2 | Git commit 4ce7dd3cc54e37e2ed6dc59cb5a047f3dccb3f50 |

MCP is project tooling, not a dependency of the transferable animation package. The pinned Coplay revision matches the existing connected server. Installation follows [Coplay's Git-package workflow](https://coplaydev.github.io/unity-mcp/getting-started/install).

Jobs ships with Unity; there is no separate Jobs package to install. Physics and Netcode integration packages are reserved for stage 11. Rukhanka is not installed.

Unity CLI/Pipeline is stopped for this project at the user's request. Autostart, HTTP and watchdog are disabled in `UserSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json`. These are per-user settings and are not committed. The Pipeline package remains installed; Coplay MCP remains available.

Confirmed public APIs: `AnimationClipPlayable.Create(PlayableGraph, AnimationClip)`, `AnimatorControllerPlayable.Create(PlayableGraph, RuntimeAnimatorController)`, `PlayableGraph.Evaluate(float)`, `EditorSceneManager.NewPreviewScene()`.

Live Editor verification uses the exact MCP instance `AnimationSystemDOTS@afa625f3a3564093`. Never run a second batch Editor against the open project. Package tests are explicitly listed in the project manifest's `testables`. A run with zero discovered tests is a failed verification.

Windows/D3D12 is the development environment. No graphics correctness, Linux, Vulkan, Metal, performance or player-build claims have been verified yet.
