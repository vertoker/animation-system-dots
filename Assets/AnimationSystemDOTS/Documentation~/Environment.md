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

Unity CLI/Pipeline is disabled and its package is removed from the root manifest. Coplay MCP remains development tooling only.

Confirmed public APIs: `AnimationClipPlayable.Create(PlayableGraph, AnimationClip)`, `AnimatorControllerPlayable.Create(PlayableGraph, RuntimeAnimatorController)`, `PlayableGraph.Evaluate(float)`, `EditorSceneManager.NewPreviewScene()`.

The repository root is the only permanent Unity project. Production code compiles directly from `Assets/AnimationSystemDOTS`; real development scenes and tests are in `Assets/AnimationSystemDOTS.Sandbox` and `Assets/Tests`. Pin the exact root-project MCP instance returned by `mcpforunity://instances`; do not reuse a historical instance ID. No local package self-dependency or junction setup is required. A run with zero discovered tests is a failed verification. Disposable consumers are generated under `.superpowers/consumers`.

After the blob schema 1→2 layout change on 2026-11-06, hot-reloaded native/type state rejected otherwise valid data until the development Editor was restarted. A clean script compilation alone did not resolve it. Both affected regression tests then passed unchanged, followed by 107/107 Editor+Sandbox and 45/45 Runtime tests. Reopen the development Editor after a blob layout migration before interpreting a hot-session failure as an algorithm regression.

Windows/D3D12 is the development environment. D3D12 Player results from earlier milestones are documented in AnimatorValidation.md and RenderingChecks.md. Linux, Vulkan, Metal and IL2CPP verification remain separate work; historical reports do not prove a new revision.
