#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in integration probe. Never attached to the saved scene or included in builds.
[DefaultExecutionOrder(-100)]
public sealed class SmartRecenterPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private FreeOrbitThirdPersonCamera orbit;
    private ChickPlayerController player;
    private CharacterController body;
    private ChickEatingController eater;
    private Keyboard keyboard;
    private Mouse mouse;
    private InputSettings original, settings;
    private GameObject patch, wall;
    private readonly List<GameObject> temporary = new List<GameObject>();
    private readonly StringBuilder report = new StringBuilder();
    private Vector3 worldDrive;
    private bool drive;
    private bool oldBackground;
    private int oldFrameRate;
    private int failures;
    private float Value(string name) => (float)typeof(FreeOrbitThirdPersonCamera).GetField(name, Flags).GetValue(orbit);
    private void Set(string name, float value) => typeof(FreeOrbitThirdPersonCamera).GetField(name, Flags).SetValue(orbit, value);
    private void Pose(float yaw)
    {
        typeof(FreeOrbitThirdPersonCamera).GetMethod("ResetSmartRecenter", Flags).Invoke(orbit, null);
        Set("yaw", yaw); Set("targetYaw", yaw); Set("pitch", 23f); Set("targetPitch", 23f);
    }
    private void Check(bool ok, string message) { report.AppendLine((ok ? "PASS " : "FAIL ") + message); if (!ok) failures++; }
    private void Update()
    {
        if (keyboard != null && keyboard.added) keyboard.MakeCurrent();
        if (mouse != null && mouse.added) mouse.MakeCurrent();
    }
    private void LateUpdate()
    {
        // Test-only stable world locomotion, after player Update and before camera LateUpdate.
        // The 60-second real-input section never enables this driver.
        if (drive && body != null) body.Move(worldDrive * Time.deltaTime);
        if (wall != null && wall.activeSelf)
        { wall.transform.position = player.transform.position + new Vector3(0, .5f, -.45f); Physics.SyncTransforms(); }
    }
    private void Keys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    private IEnumerator Start()
    {
        Result = "Starting"; oldBackground = Application.runInBackground; Application.runInBackground = true;
        // Bound the test timestep: .3 m/s at uncapped 350+ FPS falls below the existing
        // CharacterController.minMoveDistance (.001). Never change the player's controller.
        oldFrameRate = Application.targetFrameRate; Application.targetFrameRate = 60;
        original = InputSystem.settings; settings = Instantiate(original);
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = settings;
        player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        body = player.GetComponent<CharacterController>(); eater = player.GetComponent<ChickEatingController>();
        orbit = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        float until = Time.realtimeSinceStartup + 20;
        while (!player.enabled && Time.realtimeSinceStartup < until) yield return null;
        if (!player.enabled) { Result = "FAIL startup"; Cleanup(); yield break; }
        keyboard = InputSystem.AddDevice<Keyboard>("RecenterTestKeyboard"); mouse = InputSystem.AddDevice<Mouse>("RecenterTestMouse");
        patch = GameObject.Find("Wheat Test Patch"); if (patch != null) patch.SetActive(false);
        var floor = new GameObject("RecenterFloor_TEMP"); temporary.Add(floor); floor.layer = 8;
        floor.transform.position = TestAreaLayout.Origin + new Vector3(0, -.02f, 0); floor.AddComponent<BoxCollider>().size = new Vector3(200, .02f, 200);
        body.enabled = false; player.transform.position = TestAreaLayout.Origin; body.enabled = true;
        yield return new WaitForSeconds(.3f);
        Pose(180); float before = Value("yaw"); Result = "Stationary front 5.2s";
        yield return new WaitForSeconds(5.2f);
        Check(Value("yaw") == before, "Real stationary front view holds 5.2 seconds");
        foreach (float zoom in new[] { .45f, .61f, .83f })
        {
            Result = "Stable world side / zoom " + zoom; Pose(90); orbit.SetZoomDistance(zoom);
            drive = true; worldDrive = Vector3.forward * 1.25f;
            yield return new WaitForSeconds(6.5f);
            Check(Mathf.Abs(Mathf.DeltaAngle(Value("yaw"), 0)) < 6, "Stable world side converges at zoom " + zoom);
            Check(Value("pitch") == 23 && Mathf.Abs(orbit.UserDesiredDistance - zoom) < .001f, "Pitch/zoom preserved " + zoom);
        }
        Pose(180); Result = "Stable world front"; yield return new WaitForSeconds(6.5f);
        Check(Mathf.Abs(Mathf.DeltaAngle(Value("yaw"), 0)) < 6, "Stable world front converges");
        Pose(90); yield return new WaitForSeconds(2f);
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
        yield return null; yield return null; before = Value("yaw");
        yield return new WaitForSeconds(.25f);
        Check(Value("yaw") == before && orbit.RecenterState == FreeOrbitThirdPersonCamera.SmartRecenterState.ManualOrbit, "RMB interrupts active yaw immediately");
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(100, 0) }.WithButton(MouseButton.Right));
        yield return new WaitForSeconds(.2f); Check(Mathf.Abs(Value("yaw") - before) > 1, "RMB orbit responds");
        InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null; yield return null; before = Value("yaw");
        yield return new WaitForSeconds(1.3f); Check(Value("yaw") == before, "No automatic yaw 1.3s after RMB release");
        wall = new GameObject("RecenterWall_TEMP"); temporary.Add(wall); wall.layer = 8;
        wall.AddComponent<BoxCollider>().size = new Vector3(2, 1, .1f);
        Pose(90); orbit.SetZoomDistance(.83f); Result = "Collision during recenter";
        yield return new WaitForSeconds(6.5f);
        Check(Value("currentDistance") < .6f && orbit.UserDesiredDistance == .83f, "Collision pulls inward without changing selected zoom: " + Value("currentDistance"));
        wall.SetActive(false); yield return new WaitForSeconds(1f);
        Check(Mathf.Abs(Value("currentDistance") - .83f) < .01f, "Collision recovers to selected zoom");
        drive = false; Keys(); yield return new WaitForSeconds(.2f); Pose(90);
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/WheatGrain_Test.prefab");
        var grain = Instantiate(prefab, player.transform.position + player.transform.forward * .095f + Vector3.up * .011f, Quaternion.identity);
        temporary.Add(grain); before = Value("yaw"); Result = "Eat integration";
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left)); yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState()); bool busy = eater.IsBusy; float eatDelta = 0;
        until = Time.time + 1.5f;
        while (Time.time < until) { if (eater.IsBusy) { busy = true; eatDelta = Mathf.Max(eatDelta, Mathf.Abs(Value("yaw") - before)); } yield return null; }
        Check(busy && grain.GetComponent<EdibleObject>().IsConsumed && eatDelta < .001f, "Offset camera eats wheat with zero auto yaw during peck");
        Pose(90); player.enabled = false; yield return new WaitForSeconds(.25f);
        Check(Value("yaw") == 90 && orbit.RecenterState == FreeOrbitThirdPersonCamera.SmartRecenterState.ControlLocked, "Control lock pauses yaw"); player.enabled = true;
        // Real camera-relative inputs for a full minute. No trajectory overrides.
        Result = "60-second actual WASD feedback watch"; Pose(0);
        float start = Time.realtimeSinceStartup, previous = Value("yaw"), autoTravel = 0, maxRate = 0;
        int block = -1, holdFrames = 0, episodes = 0; bool wasRecentering = false;
        while (Time.realtimeSinceStartup - start < 60f)
        {
            float elapsed = Time.realtimeSinceStartup - start; int next = (int)(elapsed / 5);
            if (next != block)
            {
                block = next;
                switch (block % 6) { case 0: Keys(Key.W); break; case 1: Keys(Key.W, Key.D); break; case 2: Keys(Key.D); break; case 3: Keys(Key.S); break; case 4: Keys(Key.A); break; default: Keys(Key.W); break; }
                if (block == 5 || block == 11) InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(150, 0) }.WithButton(MouseButton.Right));
                else InputSystem.QueueStateEvent(mouse, new MouseState());
            }
            if ((block == 5 || block == 11) && elapsed % 5 > .3f) InputSystem.QueueStateEvent(mouse, new MouseState());
            float sampledFrameDelta = Time.deltaTime;
            yield return null;
            bool active = orbit.RecenterState == FreeOrbitThirdPersonCamera.SmartRecenterState.Recentering;
            // Coroutine resumes before this frame's camera LateUpdate: the observed yaw
            // was integrated with the preceding frame's dt, not the new frame's dt.
            if (active) { float delta = Mathf.Abs(Mathf.DeltaAngle(previous, Value("yaw"))); autoTravel += delta; maxRate = Mathf.Max(maxRate, delta / Mathf.Max(sampledFrameDelta, .0001f)); if (!wasRecentering) episodes++; }
            if (orbit.RecenterState == FreeOrbitThirdPersonCamera.SmartRecenterState.FeedbackHold) holdFrames++;
            wasRecentering = active; previous = Value("yaw");
        }
        Check(holdFrames > 0 && maxRate <= 91 && autoTravel < 180, "60s real inputs: bounded corrections / feedback hold; auto degrees=" + autoTravel + ", max rate=" + maxRate + ", episodes=" + episodes + ", hold frames=" + holdFrames);
        Keys(); yield return new WaitForSeconds(.2f); Pose(0); float rapidStart = Value("yaw");
        for (int i = 0; i < 30; i++) { Keys(i % 2 == 0 ? Key.A : Key.D); yield return new WaitForSeconds(.1f); }
        Check(Mathf.Abs(Value("yaw") - rapidStart) < .01f, "Real rapid A/D never starts recenter");
        for (int i = 0; i < 30; i++) { Keys(i % 3 == 0 ? Key.W : i % 3 == 1 ? Key.D : Key.S); yield return new WaitForSeconds(.1f); }
        Check(Mathf.Abs(Value("yaw") - rapidStart) < .01f, "Real rapid W-D-S never chases");
        orbit.EnableSmartRecenter = false; Pose(90); Keys(Key.S); yield return new WaitForSeconds(3f);
        Check(Value("yaw") == 90, "OFF: real movement has zero automatic yaw");
        Keys(); orbit.SetZoomDistance(.83f);
        float wheel = InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms ? 1 : 120;
        InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, wheel) }); yield return null; yield return null;
        Check(Mathf.Abs(orbit.UserDesiredDistance - .79f) < .001f, "Original wheel step .04 retained");
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        orbit.EnableSmartRecenter = true; Cleanup();
    }
    private void Cleanup()
    {
        drive = false;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (original != null) InputSystem.settings = original;
        if (settings != null) Destroy(settings);
        if (patch != null) patch.SetActive(true);
        foreach (var obj in temporary) if (obj != null) Destroy(obj);
        temporary.Clear(); Application.runInBackground = oldBackground; Application.targetFrameRate = oldFrameRate;
    }
    private void OnDestroy() => Cleanup();
}
#endif
