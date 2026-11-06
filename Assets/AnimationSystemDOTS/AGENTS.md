# Package boundaries

This folder is the complete installable `com.vertoker.animation-system-dots` package. It supports UPM Git subpath/local-path installs and ordinary Assets copies. Do not assume either `Assets/AnimationSystemDOTS` or `Packages/com.vertoker.animation-system-dots` as a production resource path; resolve installed location or stable asset GUIDs.

Runtime must not depend on UnityEditor, NUnit, Coplay MCP, the development sandbox or root project settings. Keep optional integrations behind their assembly definitions and defines. Declare directly used package dependencies in package.json.

Preserve metadata GUIDs and serialized contracts. The package Samples~ folder is an export of the root project's editable sandbox. In this checkout use `python Tools/SyncSandbox.py` from the repository root; in installed consumers import the sample and edit its Assets copy.

Use Unity Test Runner/Coplay MCP for Editor verification. Unity CLI/Pipeline is disabled. See [setup](README.md), [supported behavior](Documentation~/SupportMatrix.md) and [manual checks](Documentation~/AnimatorValidation.md).
