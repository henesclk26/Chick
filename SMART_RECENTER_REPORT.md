# Safe Conditional Smart Recenter

## Scope and behavior

Camera-only, reversible experiment. The existing camera-relative movement reference is **not frozen**. The camera observes actual planar travel and never writes player rotation, trajectory or input.

Important limitation: holding a camera-relative side/back key can change the world trajectory as auto yaw starts. The system deliberately abandons that correction instead of forcing a complete behind view. It is not a permanent chase camera. Holding W after manually orbiting can already put the camera behind the **new travel direction**, even if it is not behind the character's old visual facing; no correction is then appropriate.

## Requested report

1. **Files:** runtime change only in `Assets/Scripts/FreeOrbitThirdPersonCamera.cs`; `Assets/Scenes/SampleScene.unity` stores the new camera settings. Added opt-in verification sources `Assets/Editor/Camera/SmartRecenterKernelVerification.cs` and `Assets/Tests/Camera/SmartRecenterPlayVerification.cs`. Neither test is attached to the saved scene; Play probe is editor-only.
2. **PlayerController:** unchanged, SHA-256 verified against the pre-edit file. No steering, speed, acceleration, input-space or animation changes. Nineteen protected movement/eating/day/UI/animation files match their pre-edit hashes.
3. **Movement source:** `CharacterController.velocity`, projected onto world XZ. The cached controller belongs to the existing player root. No camera-forward, bone or visual-facing reference.
4. **Heading filter:** shortest-path angular SmoothDamp, with stable tie direction at 180 degrees. Camera-only stability anchor tolerates 8 degrees of noise. Start additionally requires filtered heading within 6 degrees of measured travel. No locomotion delay is introduced.
5. **Direction filter:** 0.30 seconds.
6. **Stable direction:** 0.40 seconds. Stability is evaluated during the manual cooldown as well; the two waits need not be added sequentially.
7. **Manual delay:** 1.50 seconds from RMB release; pressing RMB again resets it. Enabling/rebinding the component also starts a fresh delay.
8. **Activation:** strictly greater than 60 degrees; 0–60 degrees does nothing.
9. **Stop:** 6-degree tolerance, with angular velocity below 3 degrees/second before completion to avoid cutting off ease-out. The resulting endpoint may be closer than 6 degrees; no exact-zero snap.
10. **Recenter smoothing:** 1.00 second, critically damped yaw with 90 degrees/second maximum. No pitch contribution. The destination is a snapshot of stable world heading for one camera correction, not a cached movement reference.
11. **Movement threshold:** 0.15 world units/second, compared with the unchanged 1.25 walk / 2.25 run speeds. Vertical velocity is ignored.
12. **Cancellation:** RMB, stationary/subthreshold speed, disabled player controls, active eating/recovery, disabled camera, master OFF. During an episode, more than 10 degrees of measured heading drift or a six-second timeout enters `FeedbackHold`. Merely becoming stable does not restart the same correction: a new stable direction at least 25 degrees away, a stop, or manual orbit is required. This prevents repeated stop/recenter spirals.
13. **90-degree side:** deterministic 30/60/144 FPS stable-world tests passed. Play Mode stable-world test uses a test-only controller driver to separate camera behavior from unchanged camera-relative input. Actual side-key feedback is evaluated separately, not presented as stable-world convergence.
14. **Front:** 180-degree deterministic stable-world tests passed at 30/60/144 FPS, including smooth bounded rotation and unchanged pitch/zoom. A real held-W camera orbit does not promise continued northward travel because the approved controls remain camera-relative.
15. **Stationary front:** deterministic six-second hold passed; real Play Mode 5.2-second hold passed. Stationary branch has no timer that later rotates the camera.
16. **Rapid A/D:** deterministic alternating world headings and real virtual-keyboard A/D passed without starting automatic yaw.
17. **W–D–S:** real rapid virtual-keyboard changes passed without automatic chasing.
18. **Feedback:** a small initial curve was observed/expected with camera-relative side/back movement; it is not claimed to be zero. The first real 60-second watch produced six short episodes totaling 70.42 automatic degrees, maximum sampled rate 83.68 degrees/second, with 8,987 feedback-hold frames. No sustained recenter spiral occurred. The deterministic 60-second constant-side feedback test also enters one small correction followed by sustained hold. Visual comfort still merits the user's A/B play test.
19. **Zoom:** original wheel, clamping, profile, collision and lens method bodies match the pre-edit source. Selected zoom and pitch are not written by recenter. Bounds remain 0.45–0.83 and wheel step 0.04. Existing saved zoom preference is preserved.
20. **Eating:** eating source, clip and Animator hashes unchanged. Real offset-camera wheat consumption passed with zero auto yaw during the peck. `IsBusy` covers eating and recovery. No food logic was modified.
21. **OFF rollback:** `Main Camera > Free Orbit Third Person Camera > SMART RECENTER > Enable Smart Recenter` OFF bypasses the observer without any yaw writes. Deterministic and real movement checks passed. Original orbit/smoothing, collision, zoom, focus, lens and profile methods are unchanged. OFF does not rewind the current viewpoint; it resumes approved free orbit from that viewpoint.
22. **Inspector:** no setup required; scene stores the requested enabled defaults. The same checkbox provides immediate A/B rollback. `RecenterState` is available for development inspection/tests without per-frame Console spam.

## Verification notes

The first integration run exposed a **test-driver** issue: at uncapped ~350 FPS, a 0.30-unit/second synthetic move fell below the existing controller's 0.001 minimum movement step. Stable-world convergence/collision assertions therefore failed in that run. The test was corrected to use normal 1.25 walking speed and a temporary 60 FPS test cap; production movement/controller values were not changed. The cap, virtual input settings, temporary ground/wall/food and devices are restored/removed during cleanup and Play Mode exit.

The second run exposed a rate-measurement error in the test coroutine: it divided the preceding camera frame's yaw delta by the new frame's timestep. Corrected the probe to use the matching timestep; no camera production change was needed.

**Kernel tests: PASS. Final full integration rerun: PASS (21 checks).**

- Stable-world side convergence passed at 0.45, 0.61 and 0.83 selected zoom; pitch remained 23 degrees in all cases.
- Stable-world front convergence passed; stationary front stayed exact for 5.2 seconds.
- Real RMB interrupted active recenter and responded to mouse delta; release preserved yaw during the tested 1.3 seconds of the 1.5-second cooldown.
- Wall collision pulled distance to 0.34997 while selected zoom remained 0.83; removing the wall restored 0.83.
- Actual wheat consumption and control-lock checks passed with no automatic yaw interference.
- Final 60-second real-input watch: six bounded corrections, 74.04 total automatic degrees, maximum matched-frame rate 63.45 degrees/second, 1,546 feedback-hold frames. No sustained target chase or repeated full rotation.
- Real rapid A/D, W-D-S, master OFF and original wheel-step checks all passed.
- Zero Unity Console errors. Existing unrelated warnings remain: obsolete FindObjectOfType in the imported ChickenAnimatorScript and the pre-existing unused collisionDamping field.

Tests use synthetic device events processed by the real Input System and player, not direct calls to player movement methods. Only explicitly labeled stable-world cases use a temporary locomotion driver. User comfort/feel is not an automated assertion; use the master checkbox for subjective comparison.

No other gameplay mechanic was implemented.
