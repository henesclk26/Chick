#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in Play Mode check. Never attached to the saved scene or included in builds.
// Chicken jumps onto a melon-height box (helper should flap up after ~0.4 s), walks back down, then stands
// on a box too tall for the helper (safe recovery only after trying for ~6 s).
[DefaultExecutionOrder(-100)]
public sealed class HelperChickClimbVerification : MonoBehaviour
{
    public static string Result = "Not run";

    private static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly StringBuilder report = new StringBuilder();
    private readonly List<GameObject> temporaries = new List<GameObject>();
    private Keyboard testKeyboard;
    private InputSettings originalSettings, testSettings;
    private bool originalBackground;
    private ChickPlayerController player;
    private FreeOrbitThirdPersonCamera orbit;
    private HelperChickController helper;
    private Animator helperAnimator;

    private void Update()
    {
        if (testKeyboard != null && testKeyboard.added) testKeyboard.MakeCurrent();
    }

    private bool Hopping => (bool)typeof(HelperChickController).GetField("hopping", Flags).GetValue(helper);
    private bool WingFlapping => helperAnimator.GetCurrentAnimatorStateInfo(1).IsName("wing_flapping") ||
        (helperAnimator.IsInTransition(1) && helperAnimator.GetNextAnimatorStateInfo(1).IsName("wing_flapping"));

    private void Box(string name, Vector3 center, Vector3 size)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = center;
        box.transform.localScale = size;
        temporaries.Add(box);
    }

    private void Place(Vector3 position, float yaw)
    {
        player.TeleportTo(position, Quaternion.Euler(0f, yaw, 0f));
        orbit.SetOrbitAngles(yaw, orbit.OrbitPitch);
        orbit.SnapToTarget();
        Physics.SyncTransforms();
    }

    private IEnumerator Start()
    {
        Result = "Running";
        originalBackground = Application.runInBackground;
        Application.runInBackground = true;
        originalSettings = InputSystem.settings;
        testSettings = Instantiate(originalSettings);
        testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testSettings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testSettings;
        testKeyboard = InputSystem.AddDevice<Keyboard>("HelperClimbTestKeyboard");

        foreach (var clock in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            clock.StopAllCoroutines();
            clock.enabled = false;
        }
        var gate = FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include);
        typeof(MainMenuGameplayGate).GetField("released", Flags).SetValue(gate, true);

        player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        var growth = player.GetComponent<PlayerGrowthController>();
        var upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        orbit = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        player.enabled = true;
        player.GetComponent<Animator>().enabled = true;
        orbit.enabled = true;
        Time.timeScale = 1f;
        growth.SetForm(PlayerGrowthController.Form.Chicken);
        yield return new WaitForSeconds(.5f);
        if (upgrades.HelperChickCount == 0)
        {
            upgrades.TryGrantDebugEggs(40);
            upgrades.TryPurchase(PlayerUpgrades.HelperChick);
        }
        report.AppendLine("chicken jump height=" + player.CurrentJumpHeight.ToString("F2"));

        Box("ClimbTestGround_TEMP", new Vector3(500f, 300f - .05f, 500f), new Vector3(60f, .1f, 60f));
        Vector3 start = new Vector3(500f, 300f + .002f, 490f);
        Place(start, 0f);
        yield return new WaitForSeconds(1.5f);
        helper = FindFirstObjectByType<HelperChickController>();
        if (helper == null) { report.AppendLine("helper missing"); Finish(); yield break; }
        helperAnimator = helper.GetComponent<Animator>();

        // G: melon-height box directly ahead; the chicken walks and jumps onto it.
        const float melonTop = .42f;
        float edgeZ = start.z + .4f;
        Box("ClimbTestMelon_TEMP", new Vector3(start.x, 300f + melonTop * .5f, edgeZ + .6f), new Vector3(1.2f, melonTop, 1.2f));
        Physics.SyncTransforms();
        yield return new WaitForSeconds(.3f);
        yield return Observe("G melon .42m", 300f + melonTop, 3.5f, true);

        // I: walk back down off the box.
        Vector3 before = helper.transform.position;
        int jumpsDown = 0; bool sawDescent = false;
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.S));
        for (float t = 0f; t < 1.6f; t += Time.deltaTime)
        {
            yield return null;
            if ((helper.transform.position - before).magnitude > .5f) jumpsDown++;
            before = helper.transform.position;
            if (helperAnimator.GetCurrentAnimatorStateInfo(0).IsName("jump_descent")) sawDescent = true;
        }
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
        yield return new WaitForSeconds(1f);
        report.AppendLine("I down: player y=" + (player.transform.position.y - 300f).ToString("F2") +
            " helper y=" + (helper.transform.position.y - 300f).ToString("F2") + " teleports=" + jumpsDown +
            " sawDescentAnim=" + sawDescent);

        // H: box too tall for the helper; put the chicken on it directly.
        Place(start, 0f);
        yield return new WaitForSeconds(1.2f);
        const float tallTop = .9f;
        Box("ClimbTestTall_TEMP", new Vector3(start.x, 300f + tallTop * .5f, edgeZ + .6f), new Vector3(1.2f, tallTop, 1.2f));
        Physics.SyncTransforms();
        yield return new WaitForSeconds(.3f);
        player.TeleportTo(new Vector3(start.x, 300f + tallTop + .01f, edgeZ + .5f), Quaternion.identity);
        Physics.SyncTransforms();
        yield return Observe("H tall .9m", 300f + tallTop, 8f, false);

        Finish();
    }

    // Records when the chicken settles on top, when the helper hops, reaches the top and whether it teleported.
    private IEnumerator Observe(string label, float topY, float seconds, bool jumpOn)
    {
        float t0 = Time.time;
        float chickenOnTop = -1f, hopStart = -1f, helperOnTop = -1f, teleportAt = -1f;
        bool flapDuringHop = false, wasHopping = false, released = false;
        int hops = 0;
        Vector3 last = helper.transform.position;
        if (jumpOn)
        {
            InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.W, Key.Space));
            yield return null;
            InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.W));
        }
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            yield return null;
            float now = Time.time - t0;
            if (jumpOn && !released && now > .45f) { InputSystem.QueueStateEvent(testKeyboard, new KeyboardState()); released = true; }
            if (chickenOnTop < 0f && !player.IsJumping && player.transform.position.y > topY - .05f) chickenOnTop = now;
            bool hopping = Hopping;
            if (hopping && !wasHopping) { hops++; if (hopStart < 0f) hopStart = now; }
            wasHopping = hopping;
            if (hopping && WingFlapping) flapDuringHop = true;
            Vector3 p = helper.transform.position;
            if ((p - last).magnitude > .4f && teleportAt < 0f) teleportAt = now;
            last = p;
            if (helperOnTop < 0f && p.y > topY - .05f && !hopping) helperOnTop = now;
        }
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
        string Since(float at) => at >= 0f && chickenOnTop >= 0f ? (at - chickenOnTop).ToString("F2") : "-";
        report.AppendLine(label + ": chickenOnTop=" + chickenOnTop.ToString("F2") + "s hops=" + hops +
            " firstHop=+" + Since(hopStart) + "s helperOnTop=+" + Since(helperOnTop) + "s teleport=+" + Since(teleportAt) +
            "s flapDuringHop=" + flapDuringHop +
            " finalGap=" + Vector3.Distance(helper.transform.position, player.transform.position).ToString("F2"));
        if (jumpOn)
            Debug.Assert(helperOnTop >= 0f && hops > 0 && teleportAt < 0f && flapDuringHop,
                "Helper must reach the ledge by a real flapping hop, without teleporting");
    }

    private void Finish()
    {
        Result = "DONE\n" + report;
        Debug.Log("HELPER CLIMB VERIFICATION\n" + Result);
        Cleanup();
    }

    private void Cleanup()
    {
        if (testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
        if (originalSettings != null) InputSystem.settings = originalSettings;
        Application.runInBackground = originalBackground;
        if (testSettings != null) Destroy(testSettings);
        foreach (var t in temporaries) if (t != null) Destroy(t);
        temporaries.Clear();
    }

    private void OnDestroy() => Cleanup();
}
#endif
