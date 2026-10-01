# Day system implementation and verification

## Runtime components

New scripts under Assets/Scripts/DayCycle:
- GameTimeManager.cs: authoritative day/minute clock, transitions, control locks, debug time commands.
- FarmSaveSystem.cs: versioned JSON load and durable asynchronous save.
- TimeOfDayVisualController.cs: sky material instance, sun and ambient evaluation.
- ClockHUDController.cs: cached UI references, updates only on integer minute changes.
- DayTransitionController.cs: black overlay, DAY title, saving/failure display, unscaled fades.

Existing player, camera, Animator and animation files were not edited. SampleScene gained a Day Systems object with Clock HUD and Day Transition children. No legacy Canvas was added.

UI assets: Assets/UI/Clock/ClockHUD.uxml + .uss; Assets/UI/DayTransition/DayTransition.uxml + .uss; FarmTheme.tss; FarmPanelSettings.asset. All structure is UXML; C# binds labels and visibility. The clock icon uses a bordered circle and two hands defined in UXML/USS. The top-right cream rounded clock card displays HH:mm. The overlay contains FullScreenBlackOverlay, DayLabel and SavingLabel.

## Clock and flow

Fresh day 1, start 07:00 (configurable), end 19:00 (fixed requirement), 15 real minutes per day (configurable). Time is float game minutes, using deltaTime. Pause and transition flags stop advancement; Time.timeScale=0 also stops the clock. Public properties expose hour/minute/normalized progress. Minute, normalized-time, day-start and day-end events are available.

Startup: UXML is opaque black by default -> 0.3 s hold -> DAY X fades in 0.35 s -> holds 1.15 s -> fades out 0.35 s -> black fades out 0.9 s -> clock HUD and controls resume.

At >=1140 minutes: set transition guard -> lock player and orbit components, preserving previous enabled states -> hide clock -> fade to black -> display Saving... -> capture next morning snapshot -> await actual asynchronous completion -> commit next day/time in memory -> evaluate morning lighting while black -> DAY X -> fade into gameplay. No artificial save timer substitutes for I/O completion.

Input locks temporarily disable the existing player and orbit behaviours through references; their source and tuning are unchanged. They are restored after the transition. The Animator is not reconfigured. Debug Inspector context-menu commands allow morning, noon, sunset, advancing an hour and ending a day in Editor/development builds only.

## Persistence

Production file: C:/Users/buğra/AppData/LocalLow/DefaultCompany/Chick/farm-save.json (Application.persistentDataPath; company/product changes alter this path).

Data: schema version 1, next day, next morning minutes, player world position and rotation, user-selected zoom. No invented progression data. A day-end checkpoint resumes on the next morning, avoiding repeating an already completed day.

Serialize on main thread -> worker Task writes UTF-8 .tmp -> Flush(true) -> File.Replace with .bak if primary exists, otherwise File.Move. Transition polls Task completion without blocking Unity. Failure logs the exception, keeps black/error overlay and locks active, and never increments the day. Invalid primary load can fall back to valid .bak. Load Existing Save defaults true; disable for a fresh-session start (no save-slot menu added).

## Sky and light

Assets/Shaders/FarmDaySky.shader and FarmDaySky.mat: lightweight URP-compatible unlit sky, blue/colored horizon blend, sparse white procedural noise clouds with slow drift. No weather, stars, volumetrics or new post-processing. Existing Global Volume remains untouched. One runtime material is allocated and cleaned up; previous render settings and sun state are restored on exit.

Inspector gradients: sun color, sky color, horizon color. Curves: sun intensity and elevation. Normalized times 0 / .42 / .75 / .92 / 1 correspond approximately to 07:00 / 12:02 / 16:00 / 18:02 / 19:00.

| Phase | Sun RGB | Intensity | Elevation |
|---|---|---|---|
| Morning | 1,.79,.55 | 1.05 | 18 degrees |
| Noon | 1,.98,.90 | 1.8 | about 65 degrees |
| Afternoon | 1,.88,.68 | 1.35 | 34 degrees |
| Sunset | 1,.62,.36 | .8 | curve toward 7 degrees |
| Dusk endpoint | .8,.72,.78 | .45 | 7 degrees |

Sun azimuth interpolates -65 to +65 degrees. Trilight ambient follows sky/horizon colors with a readable ground shadow fill. Gradients/curves continuously evaluate authoritative progress.

## Verification

- Fresh test session reached day 1 morning, gameplay input enabled only after transition; clock visibly advanced.
- Screenshots: Captures/day-morning.png, day-noon.png, day-sunset.png, day-dusk.png. Noon 12:00, sunset 18:00 and dusk 18:45 visually inspected; 16:00 light evaluated at intensity 1.35 and RGB 1,.88,.68.
- Day 1 -> 2: real test JSON read back with day=2, minutes=420, position/rotation and zoom=.78.
- Day 2 -> 3: real replacement save read back with day=3, minutes=420.
- Restarted Play Mode and loaded test save: day=3, minutes=420, transitioning=true, player input=false during title; then gameplay resumed.
- Deliberately invalid test filename: day stayed 3 at 19:00, transition=true, player disabled; failure screen shown. Error message subsequently shortened to prevent overflow.
- Normal-run Console had no errors. The intentional failure test necessarily emitted the expected persistence error.
- Tests used farm-verification-20260912.json, not the production slot. The verification save and backup remain available for inspection.
- Final scene uses production filename, load-existing=true, normal title duration and 15-minute days. No manual assignments required.

Limitations: visual samples and accelerated end-day tests were performed, not a full 15-minute real-time playthrough. First-frame black is established by opaque default UXML, not a frame-by-frame video capture. Future progression consumers and additional save data are intentionally not implemented.
