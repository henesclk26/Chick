#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Opt-in Play Mode verification. Never attached to the saved scene or included in a build.</summary>
public sealed class EatingSlicePlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private readonly List<GameObject> temporary = new List<GameObject>();
    private Mouse mouse;
    private Keyboard keyboard;
    private GameObject patch;
    private ChickPlayerController player;
    private ChickEatingController eater;
    private Animator animator;
    private GameObject prefab;
    private readonly StringBuilder report = new StringBuilder();
    private int failures;
    private InputSettings originalInputSettings;
    private InputSettings testInputSettings;

    private IEnumerator Start()
    {
        Result = "Running";
        Application.runInBackground = true;
        originalInputSettings = InputSystem.settings;
        testInputSettings = Instantiate(originalInputSettings);
        testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testInputSettings;
        player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        eater = player.GetComponent<ChickEatingController>();
        animator = player.GetComponent<Animator>();
        float deadline = Time.realtimeSinceStartup + 20f;
        while (!player.enabled && Time.realtimeSinceStartup < deadline) yield return null;
        if (!player.enabled) { Result = "FAIL: day startup did not release player"; yield break; }
        patch = GameObject.Find("Wheat Test Patch");
        patch.SetActive(false);
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/WheatGrain_Test.prefab");
        mouse = InputSystem.AddDevice<Mouse>("WheatVerificationMouse");
        keyboard = InputSystem.AddDevice<Keyboard>("WheatVerificationKeyboard");
        Teleport();
        yield return null; yield return null;

        yield return Click();
        Check(!eater.IsBusy, "No target: no action / no lock");
        var behind = Grain(TestAreaLayout.Origin + new Vector3(0, 0.011f, -0.09f));
        var distant = Grain(TestAreaLayout.Origin + new Vector3(0, 0.011f, 0.4f));
        yield return Click();
        Check(!eater.IsBusy && !behind.IsConsumed && !distant.IsConsumed, "Behind and distant targets rejected");
        behind.gameObject.SetActive(false); distant.gameObject.SetActive(false);

        var grains = new List<EdibleObject>();
        for (int i = 0; i < 10; i++)
        {
            float angle = i * Mathf.PI * 2f / 10f;
            float radius = i == 0 ? 0f : 0.018f + i * 0.001f;
            grains.Add(Grain(TestAreaLayout.Origin + new Vector3(Mathf.Sin(angle) * radius, 0.011f,
                0.09f + Mathf.Cos(angle) * radius)));
        }
        Physics.SyncTransforms();
        yield return new WaitForSeconds(0.4f);
        var cameraTransform = Camera.main.transform;
        Vector3 eatCameraPosition = cameraTransform.position;
        Quaternion eatCameraRotation = cameraTransform.rotation;
        float maxEatCameraShift = 0f;
        float minDuration = float.MaxValue, maxDuration = 0f;
        for (int action = 0; action < 10; action++)
        {
            float started = Time.time;
            yield return Click();
            var target = eater.CurrentTarget;
            Check(eater.IsBusy && target != null && !target.IsConsumed, "Action " + (action + 1) + ": visible at input");
            if (target == null) break;
            if (action == 0)
            {
                Check(target == grains[0], "Closest forward target chosen");
                yield return Click();
                Check(eater.CurrentTarget == target, "Rapid extra click cannot replace target");
            }
            float impactAt = -1f, impactNormalized = -1f;
            bool premature = false;
            deadline = Time.time + 2f;
            while (eater.IsBusy && Time.time < deadline)
            {
                maxEatCameraShift = Mathf.Max(maxEatCameraShift,
                    Vector3.Distance(eatCameraPosition, cameraTransform.position));
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (target.IsConsumed && impactAt < 0f)
                {
                    impactAt = Time.time - started;
                    impactNormalized = state.normalizedTime;
                    eater.OnEatImpact(); // Duplicate event must not consume a second target.
                }
                if (target.IsConsumed && state.IsName("eat") && state.normalizedTime < 0.2624f)
                    premature = true;
                yield return null;
            }
            int consumed = grains.FindAll(g => g.IsConsumed).Count;
            float duration = Time.time - started;
            minDuration = Mathf.Min(minDuration, duration); maxDuration = Mathf.Max(maxDuration, duration);
            Check(!premature && target.IsConsumed && !eater.IsBusy && consumed == action + 1,
                "Action " + (action + 1) + ": one impact, clean exit, consumed=" + consumed);
            report.AppendLine("  impact=" + impactAt.ToString("F3") + "s normalized=" + impactNormalized.ToString("F4") +
                " ready=" + duration.ToString("F3") + "s");
            Check(!target.Consume(), "Consumed grain rejects duplicate Consume");
        }
        report.AppendLine("10-grain duration range=" + minDuration.ToString("F3") + ".." + maxDuration.ToString("F3"));
        Check(maxEatCameraShift < 0.001f && Quaternion.Angle(eatCameraRotation, cameraTransform.rotation) < 0.01f,
            "Eating does not move/rotate camera; max shift=" + maxEatCameraShift.ToString("F6"));

        // Target invalidation between input and the actual Animation Event.
        var invalid = Grain(TestAreaLayout.Origin + new Vector3(0, 0.011f, 0.09f));
        Physics.SyncTransforms(); yield return Click();
        invalid.gameObject.SetActive(false);
        yield return WaitForReady();
        Check(!invalid.IsConsumed && !eater.IsBusy && !eater.LastImpactSucceeded, "Disabled target safely finishes without consuming");
        invalid.ResetForReuse(); Physics.SyncTransforms(); yield return Click();
        invalid.transform.position += Vector3.forward;
        Physics.SyncTransforms(); yield return WaitForReady();
        Check(!invalid.IsConsumed && !eater.IsBusy, "Target moved out of reach safely rejected at impact");
        invalid.gameObject.SetActive(false);

        var level = Grain(TestAreaLayout.Origin + new Vector3(0, 0.011f, 0.09f));
        var so = new UnityEditor.SerializedObject(level);
        so.FindProperty("requiredEatLevel").intValue = 1; so.ApplyModifiedPropertiesWithoutUndo();
        Physics.SyncTransforms(); yield return Click();
        Check(!eater.IsBusy && !level.IsConsumed, "Future requirement hook rejects level 1; current chick is 0");
        level.gameObject.SetActive(false);

        Vector3 before = player.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        yield return new WaitForSeconds(0.25f);
        Check((player.transform.position - before).magnitude > 0.2f && animator.GetCurrentAnimatorStateInfo(0).IsName("move"), "WASD movement restored after eating");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
        yield return new WaitForSeconds(0.25f);
        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("run"), "Run animation restored");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null; yield return null;
        float groundY = player.transform.position.y;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return new WaitForSeconds(0.15f);
        Check(player.transform.position.y > groundY + 0.1f, "Space jump restored");
        yield return new WaitForSeconds(0.8f);
        Check(animator.GetCurrentAnimatorStateInfo(1).IsName("wing_idle"), "Jump wing layer returns to idle");

        Quaternion beforeTurn = player.transform.rotation;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A));
        yield return new WaitForSeconds(0.35f);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        Check(Quaternion.Angle(beforeTurn, player.transform.rotation) > 45f, "Character turning still responds after eating");
        yield return new WaitForSeconds(0.25f);
        var orbit = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        float zoomBefore = orbit.UserDesiredDistance;
        InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 120) });
        yield return null; yield return null;
        Check(orbit.UserDesiredDistance < zoomBefore, "Mouse wheel zoom in still works");
        InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, -120) });
        yield return null; yield return null;
        Check(Mathf.Abs(orbit.UserDesiredDistance - zoomBefore) < 0.001f, "Mouse wheel zoom out restores distance");
        Quaternion cameraBeforeOrbit = cameraTransform.rotation;
        Quaternion playerBeforeOrbit = player.transform.rotation;
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(1500, 0) }.WithButton(MouseButton.Right));
        yield return new WaitForSeconds(0.4f);
        Check(Quaternion.Angle(cameraBeforeOrbit, cameraTransform.rotation) > 170f,
            "RMB orbit reaches opposite/front view");
        Check(Quaternion.Angle(playerBeforeOrbit, player.transform.rotation) < 0.01f,
            "RMB orbit does not rotate player");
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(-1500, 0) }.WithButton(MouseButton.Right));
        yield return new WaitForSeconds(0.4f);
        Check(Quaternion.Angle(cameraBeforeOrbit, cameraTransform.rotation) < 1f,
            "RMB orbit returns to rear view");
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
        var cameraSO = new UnityEditor.SerializedObject(orbit);
        Check((cameraSO.FindProperty("collisionMask").intValue & (1 << 9)) == 0,
            "Camera collision still excludes Edible layer");

        Teleport(); yield return null; yield return null;
        var interrupt = Grain(TestAreaLayout.Origin + new Vector3(0, 0.011f, 0.09f));
        Physics.SyncTransforms(); yield return Click();
        player.enabled = false; yield return null; yield return null;
        Check(!eater.IsBusy && !interrupt.IsConsumed, "Disabling player cancels pending action without eating");
        player.enabled = true;
        interrupt.gameObject.SetActive(false);
        yield return new WaitForSeconds(0.25f);

        // Static/inactive grain overhead sanity check; bounded local query only.
        for (int i = 0; i < 500; i++)
        {
            var grain = Grain(TestAreaLayout.Origin + new Vector3((i % 25) * 0.25f - 3f, 0.011f, 2f + i / 25 * 0.25f));
            if (i >= 250) grain.gameObject.SetActive(false);
        }
        var local = Grain(TestAreaLayout.Origin + new Vector3(0, 0.011f, 0.09f)); Physics.SyncTransforms();
        var query = typeof(ChickEatingController).GetMethod("FindTarget", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        bool correct = true;
        for (int i = 0; i < 1000; i++) correct &= (EdibleObject)query.Invoke(eater, null) == local;
        watch.Stop();
        Check(correct && !eater.LastQuerySaturated, "500-grain stress: 1000 local queries keep selecting nearby grain");
        report.AppendLine("1000 queries incl. reflection overhead=" + watch.Elapsed.TotalMilliseconds.ToString("F2") + "ms");
        Check(prefab.GetComponent<Rigidbody>() == null && prefab.GetComponent<Animator>() == null,
            "Prefab has no Rigidbody/Animator; EdibleObject has no frame callbacks");
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("WHEAT VERIFICATION\n" + Result);
        Cleanup();
    }

    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
    }

    private IEnumerator WaitForReady()
    {
        float end = Time.time + 2f;
        while (eater.IsBusy && Time.time < end) yield return null;
    }

    private void Teleport()
    {
        var cc = player.GetComponent<CharacterController>();
        cc.enabled = false;
        player.transform.SetPositionAndRotation(TestAreaLayout.Origin + new Vector3(0, 0.001f, 0), Quaternion.identity);
        cc.enabled = true;
    }

    private EdibleObject Grain(Vector3 position)
    {
        var instance = Instantiate(prefab, position, Quaternion.identity);
        temporary.Add(instance);
        return instance.GetComponent<EdibleObject>();
    }

    private void Check(bool condition, string description)
    {
        report.AppendLine((condition ? "PASS " : "FAIL ") + description);
        if (!condition) failures++;
    }

    private void OnDestroy() => Cleanup();

    private void Cleanup()
    {
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (originalInputSettings != null)
        {
            InputSystem.settings = originalInputSettings;
            originalInputSettings = null;
            if (testInputSettings != null) Destroy(testInputSettings);
        }
        foreach (var instance in temporary) if (instance != null) Destroy(instance);
        temporary.Clear();
        if (patch != null) patch.SetActive(true);
    }
}
#endif
