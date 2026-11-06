# Humanoid representation probes

These `.cs.template` files preserve exploratory native-oracle experiments. They are excluded from Unity compilation and normal regression suites. To reproduce one, copy it to the package's Editor test directory in the development host and run the named explicit NUnit case, then remove that temporary copy. Import `Sources~/Mixamo_Bots100` through the sample importer first.

- `HumanoidBlendParityTests`: baked target TRS vs native blending, inverse HumanPose reconstruction, and authored muscle blending with unbound native retargeting. The first two deliberately fail the precision criterion for blends; they are not supported behavior claims.
- `HumanoidCalibrationProbeTests`: influence mapping and anatomical body-frame approximation. The latter is a characterization experiment and was not adopted as an exact runtime solver.
- `HumanoidBodyCenterProbeTests`: confirms that joint-centroid calibration can reproduce native body center (0.61 micrometers worst in the measured holdout set). It does not solve the separate body-orientation correction and is not part of runtime.

The maintained acceptance fixture is `Tests~/Editor/HumanoidRuntimeParityTests.cs`: it evaluates real ECS buffers against native Mixamo poses. Reports from this development run are under `DevProject~/Artifacts/Verification/AnimatorValidation/`. The implementation decision and limits are documented in `Documentation~/Humanoid.md` and the Animator validation plan.
