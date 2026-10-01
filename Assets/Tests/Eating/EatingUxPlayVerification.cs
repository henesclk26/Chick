#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Explicit Play Mode harness; no persistent scene component or build inclusion.
[DefaultExecutionOrder(-100)]
public sealed class EatingUxPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private ChickPlayerController player;
    private ChickEatingController eater;
    private Animator animator;
    private GameObject prefab, patch;
    private Mouse mouse;
    private Keyboard keyboard;
    private InputSettings originalSettings, testSettings;
    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly StringBuilder report = new StringBuilder();
    private int failures;

    private void Update()
    {
        // Native editor input can otherwise replace *.current while automation is running.
        if (keyboard != null && keyboard.added) keyboard.MakeCurrent();
        if (mouse != null && mouse.added) mouse.MakeCurrent();
    }

    private IEnumerator Start()
    {
        Result = "Running";
        Application.runInBackground = true;
        originalSettings = InputSystem.settings;
        testSettings = Instantiate(originalSettings);
        testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testSettings;
        player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        eater = player.GetComponent<ChickEatingController>(); animator = player.GetComponent<Animator>();
        float end = Time.realtimeSinceStartup + 20;
        while (!player.enabled && Time.realtimeSinceStartup < end) yield return null;
        if (!player.enabled) { Result = "FAIL startup"; Cleanup(); yield break; }
        patch = GameObject.Find("Wheat Test Patch"); patch.SetActive(false);
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/WheatGrain_Test.prefab");
        mouse = InputSystem.AddDevice<Mouse>("EatingUxMouse"); keyboard = InputSystem.AddDevice<Keyboard>("EatingUxKeyboard");
        ResetPlayer(); yield return new WaitForSeconds(.3f);
        var orbit = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        var cameraSO = new UnityEditor.SerializedObject(orbit);
        Check(Mathf.Approximately(cameraSO.FindProperty("defaultDistance").floatValue, .83f) &&
            Mathf.Approximately(cameraSO.FindProperty("maximumDistance").floatValue, .83f), "Camera default/max .83");
        Check(Mathf.Approximately(cameraSO.FindProperty("minimumDistance").floatValue, .45f) && Camera.main.fieldOfView == 58f,
            "Camera minimum .45 / FOV 58 preserved");
        report.AppendLine("Startup user zoom=" + orbit.UserDesiredDistance + " (saved user preference may override fresh default)");

        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
        yield return new WaitForSeconds(.18f);
        var runGrain = Grain(player.transform.position + new Vector3(.08f, .01f, .19f));
        yield return EatAndResume(runGrain, "Run W+Shift held");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D));
        yield return new WaitForSeconds(.3f);
        var diagonalGrain = Grain(player.transform.position + player.transform.forward * .19f + player.transform.right * .07f + Vector3.up * .01f);
        yield return EatAndResume(diagonalGrain, "Diagonal W+D held");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
        ResetPlayer(); yield return new WaitForSeconds(.25f);

        foreach (float side in new[] { -.13f, .13f })
        {
            var grain = Grain(TestAreaLayout.Origin + new Vector3(side, .011f, .065f));
            yield return Click(); yield return Ready();
            Check(grain.IsConsumed && eater.LastContactError <= .012f, "Wide side-front " + side + " reaches beak at event");
            grain.gameObject.SetActive(false);
        }
        var behind = Grain(TestAreaLayout.Origin + new Vector3(0, .011f, -.13f));
        var far = Grain(TestAreaLayout.Origin + new Vector3(0, .011f, .65f));
        yield return Click();
        Check(!eater.IsBusy && !behind.IsConsumed && !far.IsConsumed, "Behind and distant food rejected");
        behind.gameObject.SetActive(false); far.gameObject.SetActive(false);

        // One recovery click should start exactly one next action, even while W stays held.
        var first = Grain(TestAreaLayout.Origin + new Vector3(0, .011f, .095f));
        var second = Grain(TestAreaLayout.Origin + new Vector3(.055f, .011f, .14f));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        yield return Click();
        end = Time.time + 1f;
        bool buffered = false;
        while (eater.IsBusy && Time.time < end)
        {
            if (!eater.IsEating && animator.IsInTransition(0) && animator.GetAnimatorTransitionInfo(0).normalizedTime > .5f)
            { yield return Click(); buffered = true; break; }
            yield return null;
        }
        end = Time.time + 1.5f;
        while (!second.IsConsumed && Time.time < end) yield return null;
        Check(buffered && first.IsConsumed && second.IsConsumed, "One late recovery click buffers next target while W held");
        yield return Ready();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
        first.gameObject.SetActive(false); second.gameObject.SetActive(false);
        ResetPlayer(); yield return new WaitForSeconds(.25f);

        var holdFirst = Grain(TestAreaLayout.Origin + new Vector3(0, .011f, .095f));
        var holdSecond = Grain(TestAreaLayout.Origin + new Vector3(.06f, .011f, .15f));
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
        yield return new WaitForSeconds(1.1f);
        Check(holdFirst.IsConsumed && !holdSecond.IsConsumed, "Held LMB never becomes auto-eat or a queued combo");
        InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
        holdSecond.gameObject.SetActive(false);

        var grains = new List<EdibleObject>();
        for (int i = 0; i < 20; i++) grains.Add(Grain(TestAreaLayout.Origin + new Vector3(i % 2 == 0 ? -.09f : .09f, .011f, .23f + .30f * i)));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
        int eaten = 0;
        for (int i = 0; i < 20; i++)
        {
            end = Time.time + 3f;
            while (grains[i].transform.position.z - player.transform.position.z > .19f && Time.time < end) yield return null;
            yield return EatAndResume(grains[i], "Continuous running grain " + (i + 1), false);
            if (grains[i].IsConsumed) eaten++;
        }
        Check(eaten == 20, "Continuous W+Shift, 20 clicks, 20 grains, never released movement: " + eaten + "/20");
        Check(Mathf.Abs(player.transform.position.x) < .01f, "No manual lateral/beak alignment during patch test");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;

        orbit.SetZoomDistance(.83f);
        float wheelStep = InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms ? 1f : 120f;
        InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, wheelStep) }); yield return null; yield return null;
        Check(Mathf.Abs(orbit.UserDesiredDistance - .79f) < .001f, "Wheel one notch .83 to .79, actual=" + orbit.UserDesiredDistance);
        for (int i = 0; i < 12; i++) { InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, wheelStep) }); yield return null; }
        yield return null; Check(Mathf.Abs(orbit.UserDesiredDistance - .45f) < .001f, "Wheel clamps at unchanged minimum .45");
        for (int i = 0; i < 14; i++) { InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, -wheelStep) }); yield return null; }
        yield return null; Check(Mathf.Abs(orbit.UserDesiredDistance - .83f) < .001f, "Wheel clamps at new maximum .83");
        Quaternion beforeOrbit = Camera.main.transform.rotation;
        Quaternion beforePlayer = player.transform.rotation;
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(1500, 0) }.WithButton(MouseButton.Right));
        yield return new WaitForSeconds(.4f);
        Check(Quaternion.Angle(beforeOrbit, Camera.main.transform.rotation) > 170f &&
            Quaternion.Angle(beforePlayer, player.transform.rotation) < .01f, "RMB orbit still independent of character");
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(-1500, 0) }.WithButton(MouseButton.Right));
        yield return new WaitForSeconds(.4f);
        InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
        Check(Quaternion.Angle(beforeOrbit, Camera.main.transform.rotation) < 1f, "RMB returns to rear view unchanged");
        float beforeJump = player.transform.position.y;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return new WaitForSeconds(.15f);
        Check(player.transform.position.y > beforeJump + .1f, "Jump still works after eating");
        yield return new WaitForSeconds(.8f);
        Check(animator.GetCurrentAnimatorStateInfo(1).IsName("wing_idle"), "Landing restores wing idle");
        Check(Mathf.Approximately(animator.GetFloat("EatPlaybackSpeed"), 1.4f) && animator.speed == 1f, "Eating speed unchanged");
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("EATING UX VERIFICATION\n" + Result); Cleanup();
    }

    private IEnumerator EatAndResume(EdibleObject grain, string label, bool checkResume = true)
    {
        yield return Click();
        Check(eater.IsBusy && eater.CurrentTarget == grain && !grain.IsConsumed, label + ": LMB accepted immediately while moving");
        Vector3 brakePoint = player.transform.position;
        float maxDrift = 0f, end = Time.time + 2f;
        while (eater.IsBusy && Time.time < end)
        {
            maxDrift = Mathf.Max(maxDrift, Vector3.ProjectOnPlane(player.transform.position - brakePoint, Vector3.up).magnitude);
            yield return null;
        }
        Check(grain.IsConsumed && !eater.IsBusy && eater.LastContactError <= .012f, label + ": consumed, no stuck state");
        Check(maxDrift < .01f, label + ": short action brake, drift=" + maxDrift.ToString("F4"));
        if (checkResume)
        {
            Vector3 before = player.transform.position;
            yield return new WaitForSeconds(.10f);
            Check(Vector3.Distance(before, player.transform.position) > .05f, label + ": held movement resumed without another key press");
        }
    }
    private void ResetPlayer()
    {
        var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
        player.transform.SetPositionAndRotation(TestAreaLayout.Origin + new Vector3(0, .001f, 0), Quaternion.identity); cc.enabled = true;
    }
    private EdibleObject Grain(Vector3 position)
    {
        position.y = .011f;
        var go = Instantiate(prefab, position, Quaternion.identity); spawned.Add(go); Physics.SyncTransforms(); return go.GetComponent<EdibleObject>();
    }
    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left)); yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
    }
    private IEnumerator Ready() { float end = Time.time + 2f; while (eater.IsBusy && Time.time < end) yield return null; }
    private void Check(bool good, string message) { report.AppendLine((good ? "PASS " : "FAIL ") + message); if (!good) failures++; }
    private void OnDestroy() => Cleanup();
    private void Cleanup()
    {
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (originalSettings != null) { InputSystem.settings = originalSettings; originalSettings = null; if (testSettings != null) Destroy(testSettings); }
        foreach (var go in spawned) if (go != null) Destroy(go); spawned.Clear();
        if (patch != null) patch.SetActive(true);
    }
}
#endif
