#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Play Mode check for "Süzülme". Never attached to a saved scene; never writes the player's save.
public sealed class GlidePlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Id = PlayerUpgrades.Glide;
    private const float PlatformHeight = 1.5f;
    private readonly StringBuilder report = new();
    private int failures;
    private PlayerUpgrades upgrades;
    private ChickPlayerController player;
    private GameplayJournalController journal;
    private Keyboard keyboard;
    private InputSettings originalInput, testInput;
    private bool originalBackground;
    private float originalCapture;
    private GameObject floor, platform;

    private struct Flight { public float airTime, distance, maxFall; public bool sawGlide, sawFlapState; }

    private object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    private T Field<T>(string name) => (T)typeof(ChickPlayerController).GetField(name, Private).GetValue(player);
    private void SetField(string name, object value) => typeof(ChickPlayerController).GetField(name, Private).SetValue(player, value);
    private void Check(bool ok, string message)
    {
        if (!ok) failures++;
        report.AppendLine((ok ? "PASS " : "FAIL ") + message);
        Result = "Running\n" + report;
    }
    private void CollectEggs(int eggs)
    {
        for (int i = 0; i < eggs / 5; i++) upgrades.TryCollectEgg(true);
        for (int i = 0; i < eggs % 5; i++) upgrades.TryCollectEgg(false);
    }
    private void SetLevels(int glide, int doubleJump, int helper = 0) => Call(upgrades, "Restore", new FarmSaveData
    {
        upgradeLevels = new[]
        {
            new UpgradeLevelSaveEntry { id = Id, level = glide },
            new UpgradeLevelSaveEntry { id = PlayerUpgrades.DoubleJump, level = doubleJump },
            new UpgradeLevelSaveEntry { id = PlayerUpgrades.HelperChick, level = helper }
        }
    });

    // Set while a flight runs: records whether the helper chick went into its own jump.
    private bool watchHelper, helperJumped;
    private void Keys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));

    // From the platform edge: hold W, jump, and keep Space held (optionally releasing it after a while).
    private IEnumerator JumpOffPlatform(Flight[] result, float releaseAfter = -1f, bool doubleJump = false)
    {
        Keys();
        // W walks along the camera's view, so stand at the platform edge the camera faces.
        Vector3 top = TestAreaLayout.Origin + new Vector3(100, PlatformHeight + .002f, 100);
        player.TeleportTo(top, Quaternion.identity);
        yield return null;
        Vector3 view = Camera.main.transform.forward; view.y = 0f;
        view = view.sqrMagnitude > 1e-4f ? view.normalized : Vector3.forward;
        player.TeleportTo(top + view * .85f, Quaternion.LookRotation(view));
        yield return new WaitForSeconds(.5f);
        Vector3 start = player.transform.position;
        Keys(Key.W);
        yield return new WaitForSeconds(.15f);
        Keys(Key.W, Key.Space);
        float t0 = Time.time;
        yield return null;
        if (doubleJump)
        {
            while (Field<float>("verticalVelocity") > 0f) yield return null;
            Keys(Key.W);
            yield return null;
            Keys(Key.W, Key.Space); // fresh press for the air jump, then keep holding to glide
            yield return null;
        }
        var flight = new Flight();
        float deadline = Time.time + 8f;
        bool released = false;
        while (Field<bool>("jumping") && Time.time < deadline)
        {
            if (releaseAfter >= 0f && !released && Time.time - t0 >= releaseAfter) { Keys(Key.W); released = true; }
            flight.maxFall = Mathf.Max(flight.maxFall, -Field<float>("verticalVelocity"));
            flight.sawGlide |= player.IsGliding;
            var state = player.ActiveAnimator.GetCurrentAnimatorStateInfo(0);
            var next = player.ActiveAnimator.GetNextAnimatorStateInfo(0);
            flight.sawFlapState |= player.IsGliding && (state.IsName("flapping") || next.IsName("flapping"));
            yield return null;
        }
        flight.airTime = Time.time - t0;
        Vector3 delta = player.transform.position - start; delta.y = 0f;
        flight.distance = delta.magnitude;
        Keys();
        result[0] = flight;
        yield return new WaitForSeconds(.3f);
    }

    // Walks off the platform edge without jumping; optionally presses and holds Space partway down.
    private IEnumerator WalkOffPlatform(Flight[] result, bool pressSpaceWhileFalling)
    {
        Keys();
        Vector3 top = TestAreaLayout.Origin + new Vector3(100, PlatformHeight + .002f, 100);
        player.TeleportTo(top, Quaternion.identity);
        yield return null;
        Vector3 view = Camera.main.transform.forward; view.y = 0f;
        view = view.sqrMagnitude > 1e-4f ? view.normalized : Vector3.forward;
        player.TeleportTo(top + view * .85f, Quaternion.LookRotation(view));
        yield return new WaitForSeconds(.5f);
        var body = player.GetComponent<CharacterController>();
        Keys(Key.W);
        float deadline = Time.time + 3f;
        while (body.isGrounded && Time.time < deadline) yield return null;
        float t0 = Time.time;
        Vector3 edge = player.transform.position;
        var flight = new Flight();
        bool pressed = false;
        deadline = Time.time + 8f;
        while (Time.time < deadline)
        {
            float fall = -Field<float>("verticalVelocity");
            if (pressSpaceWhileFalling && !pressed && fall > 2.5f) { Keys(Key.W, Key.Space); pressed = true; }
            if (!pressSpaceWhileFalling || pressed) flight.maxFall = Mathf.Max(flight.maxFall, fall);
            flight.sawGlide |= player.IsGliding;
            var state = player.ActiveAnimator.GetCurrentAnimatorStateInfo(0);
            var next = player.ActiveAnimator.GetNextAnimatorStateInfo(0);
            flight.sawFlapState |= player.IsGliding && (state.IsName("flapping") || next.IsName("flapping"));
            yield return null;
            if (body.isGrounded && player.transform.position.y < TestAreaLayout.Origin.y + .05f) break;
        }
        flight.airTime = Time.time - t0;
        Vector3 delta = player.transform.position - edge; delta.y = 0f;
        flight.distance = delta.magnitude;
        Keys();
        result[0] = flight;
        yield return new WaitForSeconds(.3f);
    }

    private void JournalChecks()
    {
        var root = journal.GetComponent<UIDocument>().rootVisualElement;
        var price = root.Q<Button>("glide-price");
        Label cost = root.Q<Label>("glide-cost");
        int Total() => root.Q("glide-levels").childCount;
        int Filled() => root.Q("glide-levels").Query(className: "level-filled").ToList().Count;

        Call(upgrades, "Restore", new FarmSaveData());
        journal.Open();
        Check(cost.text == "200" && Total() == 1 && Filled() == 0 && price.ClassListContains("unaffordable"),
            "Journal: one level egg, price 200, dimmed without eggs");
        CollectEggs(199);
        Call(journal, "GlidePriceClicked");
        Check(!upgrades.GlideUnlocked && root.Q<Label>("upgrades-status").text.Contains("1 yumurta"),
            "Journal: 199 eggs are not enough (1 missing)");
        CollectEggs(1);
        Call(journal, "GlidePriceClicked");
        Check(upgrades.GlideUnlocked && cost.text == "MAX" && Filled() == 1 && price.ClassListContains("max-price") &&
            upgrades.AvailableEggs == 0 && root.Q<Label>("upgrades-status").text.StartsWith("Süzülme"),
            "Journal: buying spends 200 eggs and shows MAX");
        Check(upgrades.GetLevel(PlayerUpgrades.DoubleJump) == 0, "Journal: Çift Zıplama stays separate");
        journal.Close();
    }

    private IEnumerator Start()
    {
        Result = "Running";
        originalBackground = Application.runInBackground; Application.runInBackground = true;
        originalCapture = Time.captureDeltaTime; Time.captureDeltaTime = 1f / 120f;
        originalInput = InputSystem.settings; testInput = Instantiate(originalInput);
        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testInput;
        keyboard = InputSystem.AddDevice<Keyboard>("GlideTestKeyboard");

        upgrades = FindFirstObjectByType<PlayerUpgrades>();
        player = FindFirstObjectByType<ChickPlayerController>();
        journal = FindFirstObjectByType<GameplayJournalController>(FindObjectsInactive.Include);

        // Release gameplay without starting the day clock or loading/writing any save.
        foreach (var clock in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            clock.StopAllCoroutines();
            clock.enabled = false;
        }
        var menu = FindFirstObjectByType<MainMenuController>();
        if (menu != null) { menu.enabled = false; menu.GetComponent<UIDocument>().rootVisualElement.style.display = DisplayStyle.None; }
        typeof(MainMenuGameplayGate).GetField("released", Private)
            .SetValue(FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include), true);
        player.enabled = true;
        Time.timeScale = 1f;

        Call(upgrades, "Restore", new FarmSaveData());
        Check(upgrades.GetMaxLevel(Id) == 1 && upgrades.GetNextPrice(Id) == 200 && !upgrades.GlideUnlocked,
            "Single level, price 200, locked at start");
        CollectEggs(199);
        Check(!upgrades.TryPurchase(Id), "199 eggs cannot buy it");
        CollectEggs(1);
        Check(upgrades.TryPurchase(Id) && upgrades.GlideUnlocked && upgrades.AvailableEggs == 0 && upgrades.IsMaxed(Id),
            "200 eggs buy it; one purchase is the maximum");

        floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "GlideTestGround_TEMP";
        floor.transform.position = TestAreaLayout.Origin + new Vector3(100, -.05f, 100); floor.transform.localScale = new Vector3(40, .1f, 40);
        platform = GameObject.CreatePrimitive(PrimitiveType.Cube); platform.name = "GlideTestPlatform_TEMP";
        platform.transform.position = TestAreaLayout.Origin + new Vector3(100, PlatformHeight / 2f, 100); platform.transform.localScale = new Vector3(2, PlatformHeight, 2);
        Physics.SyncTransforms();
        var growth = player.GetComponent<PlayerGrowthController>();
        growth.SetForm(PlayerGrowthController.Form.Chick);
        float glideSpeed = Field<float>("glideFallSpeed");
        float glideBoost = Field<float>("glideSpeedMultiplier");
        var f = new Flight[1];

        SetLevels(0, 0);
        yield return JumpOffPlatform(f);
        Flight plain = f[0];
        Check(!plain.sawGlide && plain.maxFall > 3f,
            $"Before purchase: holding Space falls normally (max fall {plain.maxFall:0.00} m/s, air {plain.airTime:0.00} s, {plain.distance:0.00} m)");

        SetLevels(1, 0);
        // The previous tuning (0.6 m/s fall, no forward boost) as the reference for "1.5x faster, 2x farther".
        SetField("glideFallSpeed", .6f);
        SetField("glideSpeedMultiplier", 1f);
        yield return JumpOffPlatform(f);
        Flight previous = f[0];
        SetField("glideFallSpeed", glideSpeed);
        SetField("glideSpeedMultiplier", glideBoost);

        yield return JumpOffPlatform(f);
        Flight glide = f[0];
        Check(glide.sawGlide && glide.maxFall < glideSpeed + .35f,
            $"Glide caps the fall at {glide.maxFall:0.00} m/s (setting {glideSpeed:0.00})");
        Check(Mathf.Abs(glideSpeed / .6f - 1.5f) < .01f && Mathf.Abs(glide.maxFall / previous.maxFall - 1.5f) < .15f,
            $"Glide falls 1.5x faster than before ({previous.maxFall:0.00} -> {glide.maxFall:0.00} m/s)");
        Check(Mathf.Approximately(glideBoost, 1.2f),
            $"Glide forward boost is x{glideBoost:0.00} (distance {previous.distance:0.00} -> {glide.distance:0.00} m; air {previous.airTime:0.00} -> {glide.airTime:0.00} s)");
        Check(glide.airTime > plain.airTime * 2.5f && glide.distance > plain.distance * 2f,
            $"Glide stays up {glide.airTime:0.00} s vs {plain.airTime:0.00} s and travels {glide.distance:0.00} m vs {plain.distance:0.00} m");
        Check(glide.sawFlapState, "Gliding plays the flapping animation");

        yield return JumpOffPlatform(f, 1f);
        Check(f[0].maxFall > 2f && f[0].airTime < glide.airTime,
            $"Releasing Space ends the glide (fall back to {f[0].maxFall:0.00} m/s, air {f[0].airTime:0.00} s)");

        // Walking off the edge without jumping, then pressing Space partway down, still starts the glide.
        yield return WalkOffPlatform(f, false);
        Flight drop = f[0];
        int jumpsBefore = player.GroundJumpCount;
        yield return WalkOffPlatform(f, true);
        Check(player.GroundJumpCount == jumpsBefore, "A ledge glide does not count as a jump");
        Check(!drop.sawGlide && f[0].sawGlide && f[0].maxFall < drop.maxFall && f[0].airTime > drop.airTime * 2f,
            $"Falling off a ledge, Space starts a glide (air {drop.airTime:0.00} -> {f[0].airTime:0.00} s, " +
            $"{drop.distance:0.00} -> {f[0].distance:0.00} m, fall after press {f[0].maxFall:0.00} m/s)");
        Check(f[0].sawFlapState, "A ledge glide flaps like a jump glide");

        SetLevels(1, 1);
        yield return JumpOffPlatform(f, -1f, true);
        Check(Field<bool>("airJumpUsed") == false && f[0].sawGlide && f[0].airTime > glide.airTime,
            $"Double jump then glide combine (air {f[0].airTime:0.00} s vs glide only {glide.airTime:0.00} s)");

        SetLevels(1, 0);
        growth.SetForm(PlayerGrowthController.Form.Chicken);
        yield return JumpOffPlatform(f);
        Check(f[0].sawGlide && f[0].maxFall < glideSpeed + .35f,
            $"Chicken form glides too (max fall {f[0].maxFall:0.00} m/s, air {f[0].airTime:0.00} s, {f[0].distance:0.00} m)");

        // With a helper chick along: a ledge glide must not make it jump, a real jump still does.
        SetLevels(1, 0, 1);
        float spawnDeadline = Time.time + 2f;
        while (FindFirstObjectByType<HelperChickController>() == null && Time.time < spawnDeadline) yield return null;
        Check(FindFirstObjectByType<HelperChickController>() != null, "Helper chick is along for the chicken");
        helperJumped = false; watchHelper = true;
        yield return WalkOffPlatform(f, true);
        watchHelper = false;
        Check(f[0].sawGlide && !helperJumped, "Chicken ledge glide: the helper chick does not jump");
        helperJumped = false; watchHelper = true;
        yield return JumpOffPlatform(f);
        watchHelper = false;
        Check(helperJumped, "Chicken real jump: the helper chick still copies it");
        SetLevels(1, 0);
        growth.SetForm(PlayerGrowthController.Form.Chick);

        var data = new FarmSaveData();
        Call(upgrades, "Capture", data);
        var loaded = JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(data));
        Call(upgrades, "Restore", new FarmSaveData());
        Check(!upgrades.GlideUnlocked, "New game starts without glide");
        Call(upgrades, "Restore", loaded);
        Check(upgrades.GlideUnlocked, "Save/load keeps glide");

        JournalChecks();

        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("GLIDE VERIFICATION\n" + Result);
        Cleanup();
    }

    private void Update()
    {
        keyboard?.MakeCurrent();
        if (!watchHelper) return;
        var helper = FindFirstObjectByType<HelperChickController>();
        if (helper != null && (bool)typeof(HelperChickController).GetField("jumping", Private).GetValue(helper) &&
            !(bool)typeof(HelperChickController).GetField("hopping", Private).GetValue(helper))
            helperJumped = true;
    }

    private void Cleanup()
    {
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        if (originalInput != null) InputSystem.settings = originalInput;
        if (testInput != null) Destroy(testInput);
        Application.runInBackground = originalBackground; Time.captureDeltaTime = originalCapture;
        if (floor != null) Destroy(floor);
        if (platform != null) Destroy(platform);
    }

    private void OnDestroy() => Cleanup();
}
#endif
