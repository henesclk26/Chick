#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Explicit temporary runner, not included in builds or saved scenes.
public sealed class EdibleTargetingPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private readonly List<GameObject> temporary = new List<GameObject>();
    private readonly StringBuilder report = new StringBuilder();
    private ChickEatingController eater;
    private ChickPlayerController player;
    private Animator animator;
    private GameObject prefab, patch;
    private Mouse mouse;
    private Keyboard keyboard;
    private InputSettings originalSettings, testSettings;
    private int failures;
    private bool previousBackground;

    private IEnumerator Start()
    {
        Result = "Running";
        previousBackground = Application.runInBackground;
        Application.runInBackground = true;
        originalSettings = InputSystem.settings;
        testSettings = Instantiate(originalSettings);
        testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testSettings;
        player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        eater = player.GetComponent<ChickEatingController>();
        animator = player.GetComponent<Animator>();
        float deadline = Time.realtimeSinceStartup + 20f;
        while (!player.enabled && Time.realtimeSinceStartup < deadline) yield return null;
        if (!player.enabled) { Result = "FAIL: startup did not release player"; Cleanup(); yield break; }
        patch = GameObject.Find("Wheat Test Patch"); patch.SetActive(false);
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/WheatGrain_Test.prefab");
        mouse = InputSystem.AddDevice<Mouse>("TargetingTestMouse");
        keyboard = InputSystem.AddDevice<Keyboard>("TargetingTestKeyboard");
        var cc = player.GetComponent<CharacterController>();
        cc.enabled = false; player.transform.SetPositionAndRotation(TestAreaLayout.Origin + new Vector3(0, .001f, 0), Quaternion.identity); cc.enabled = true;
        yield return new WaitForSeconds(.4f);

        Vector3[] offsets = { TestAreaLayout.Origin + new Vector3(0, .011f, .087f), TestAreaLayout.Origin + new Vector3(0, .011f, .15f),
            TestAreaLayout.Origin + new Vector3(-.055f, .011f, .11f), TestAreaLayout.Origin + new Vector3(.055f, .011f, .11f),
            TestAreaLayout.Origin + new Vector3(.065f, .011f, .105f), TestAreaLayout.Origin + new Vector3(0, .011f, -.12f), TestAreaLayout.Origin + new Vector3(0, .011f, .28f) };
        string[] labels = { "Direct", "Forward comfortable", "Front-left", "Front-right", "Contact edge", "Behind", "Too far" };
        for (int i = 0; i < offsets.Length; i++)
        {
            var grain = Grain(offsets[i]);
            yield return EatAndObserve(grain, i < 5, labels[i]);
            grain.gameObject.SetActive(false);
        }

        var chosen = Grain(TestAreaLayout.Origin + new Vector3(-.04f, .011f, .11f));
        yield return Click();
        var newcomer = Grain(TestAreaLayout.Origin + new Vector3(0, .011f, .087f));
        yield return Click();
        Check(eater.CurrentTarget == chosen, "Target remains locked despite closer newcomer and extra click");
        yield return Ready();
        Check(chosen.IsConsumed && !newcomer.IsConsumed, "One input consumes only locked target: chosen=" + chosen.IsConsumed +
            " newcomer=" + newcomer.IsConsumed + " impact=" + eater.LastImpactSucceeded + " error=" + eater.LastContactError +
            " state=" + animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
        chosen.gameObject.SetActive(false);
        newcomer.gameObject.SetActive(false);

        var cancel = Grain(TestAreaLayout.Origin + new Vector3(.05f, .011f, .11f));
        yield return new WaitForSeconds(.25f);
        Vector3 cancelOrigin = cancel.ContactVisual.localPosition;
        yield return Click();
        yield return new WaitForSeconds(.17f);
        Check(cancel.ContactVisual.localPosition != cancelOrigin, "Assist actually started before cancellation test");
        eater.CancelEat();
        Check(!cancel.IsConsumed && cancel.ContactVisual.localPosition == cancelOrigin && cancel.GetComponent<Collider>().enabled,
            "Cancel restores visual and preserves collider");
        cancel.gameObject.SetActive(false);
        yield return new WaitForSeconds(.25f);

        var moved = Grain(TestAreaLayout.Origin + new Vector3(-.045f, .011f, .11f));
        yield return Click(); yield return new WaitForSeconds(.17f);
        moved.transform.position += Vector3.forward;
        Physics.SyncTransforms();
        yield return Ready();
        Check(!moved.IsConsumed && moved.ContactVisual.localPosition == Vector3.zero && moved.transform.position.z > TestAreaLayout.Origin.z + 1f,
            "External move aborts assist without undoing external root move");
        moved.gameObject.SetActive(false);

        // 20 genuinely scattered grains: actual W movement between them, no per-grain teleport/alignment.
        var scatter = new List<EdibleObject>();
        for (int i = 0; i < 20; i++)
            scatter.Add(Grain(TestAreaLayout.Origin + new Vector3((i % 2 == 0 ? -.045f : .045f), .011f, .12f + i * .23f)));
        int total = 0;
        for (int i = 0; i < 20; i++)
        {
            float stopZ = scatter[i].transform.position.z - .12f;
            if (player.transform.position.z < stopZ - .008f)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                deadline = Time.time + 2f;
                while (player.transform.position.z < stopZ - .008f && Time.time < deadline) yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null; yield return null;
            }
            yield return EatAndObserve(scatter[i], true, "Walking scatter " + (i + 1));
            if (scatter[i].IsConsumed) total++;
        }
        Check(total == 20, "20 scattered grains eaten with forward walking only, no tiny side adjustments: " + total + "/20");
        Check(Mathf.Abs(player.transform.position.x) < .01f, "Scatter test needed no sideways repositioning");
        Check(Mathf.Approximately(animator.GetFloat("EatPlaybackSpeed"), 1.4f) && Mathf.Approximately(animator.speed, 1f),
            "Approved 1.4x eat / 1x global speed preserved");
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("TARGETING VERIFICATION\n" + Result);
        Cleanup();
    }

    private IEnumerator EatAndObserve(EdibleObject grain, bool expected, string label)
    {
        Vector3 root = grain.transform.position;
        Vector3 visual = grain.ContactVisual.position;
        Vector3 local = grain.ContactVisual.localPosition;
        Vector3 cameraPosition = Camera.main.transform.position;
        Quaternion cameraRotation = Camera.main.transform.rotation;
        float time = Time.time;
        yield return Click();
        Check(eater.IsBusy == expected && !grain.IsConsumed, label + ": initiation, food retained at input");
        if (!expected) yield break;
        Check(eater.CurrentTarget == grain, label + ": expected best target");
        float maxShift = 0, maxLift = 0;
        bool earlyShift = false;
        float deadline = Time.time + 2f;
        while (eater.IsBusy && Time.time < deadline)
        {
            float shift = Vector3.Distance(visual, grain.ContactVisual.position);
            maxShift = Mathf.Max(maxShift, shift);
            maxLift = Mathf.Max(maxLift, Mathf.Abs(visual.y - grain.ContactVisual.position.y));
            if (Time.time - time < .13f && shift > .0001f) earlyShift = true;
            yield return null;
        }
        Check(grain.IsConsumed && !eater.IsBusy, label + ": consumed at event and recovered");
        Check(!earlyShift && maxShift <= .0751f && maxLift <= .0061f,
            label + ": late bounded assist, shift=" + maxShift.ToString("F4") + " lift=" + maxLift.ToString("F4"));
        Check(eater.LastContactError <= .012f, label + ": impact beak distance=" + eater.LastContactError.ToString("F5"));
        Check(Vector3.Distance(root, grain.transform.position) < .00001f && grain.ContactVisual.localPosition == local,
            label + ": static root / visual reset for reuse");
        Check(Quaternion.Angle(cameraRotation, Camera.main.transform.rotation) < .001f,
            label + ": no camera auto-rotation");
    }

    private EdibleObject Grain(Vector3 position)
    {
        var obj = Instantiate(prefab, position, Quaternion.identity); temporary.Add(obj);
        Physics.SyncTransforms(); return obj.GetComponent<EdibleObject>();
    }
    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left)); yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
    }
    private IEnumerator Ready()
    {
        float deadline = Time.time + 2f;
        while (eater.IsBusy && Time.time < deadline) yield return null;
    }
    private void Check(bool success, string text)
    {
        report.AppendLine((success ? "PASS " : "FAIL ") + text); if (!success) failures++;
    }
    private void OnDestroy() => Cleanup();
    private void Cleanup()
    {
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (originalSettings != null)
        {
            InputSystem.settings = originalSettings; originalSettings = null;
            if (testSettings != null) Destroy(testSettings);
        }
        foreach (var go in temporary) if (go != null) Destroy(go); temporary.Clear();
        if (patch != null) patch.SetActive(true);
        Application.runInBackground = previousBackground;
    }
}
#endif
