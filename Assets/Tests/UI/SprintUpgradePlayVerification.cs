#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Play Mode check for "Sprint Hızı". Never attached to a saved scene; never writes the player's save.
public sealed class SprintUpgradePlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Sprint = PlayerUpgrades.SprintSpeed;
    private static readonly int RunPlaybackSpeed = Animator.StringToHash("RunPlaybackSpeed");
    private readonly StringBuilder report = new();
    private int failures;
    private PlayerUpgrades upgrades;
    private FoodStatisticsTracker statistics;
    private ChickPlayerController player;
    private GameplayJournalController journal;
    private Keyboard keyboard;
    private InputSettings originalInput, testInput;
    private bool originalBackground;
    private float originalCapture;
    private GameObject floor;

    private object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    private void Check(bool ok, string message)
    {
        if (!ok) failures++;
        report.AppendLine((ok ? "PASS " : "FAIL ") + message);
        Result = "Running\n" + report;
    }
    private void GrantEggs(int count)
    {
        for (int i = 0; i < count; i++)
            upgrades.TryCollectEgg(false);
    }
    private void SetLevel(int level) => Call(upgrades, "Restore", new FarmSaveData
        { upgradeLevels = new[] { new UpgradeLevelSaveEntry { id = Sprint, level = level } } });

    // Holds W (and Shift when running) with a real keyboard device; returns steady ground speed in m/s.
    private IEnumerator MeasureSpeed(bool run, float[] speed, float[] playback)
    {
        player.TeleportTo(TestAreaLayout.Origin + new Vector3(100, .002f, 100), Quaternion.identity);
        // Start each measurement with full sprint stamina so earlier runs cannot exhaust this one.
        typeof(ChickPlayerController).GetField("sprintStamina", Private).SetValue(player, -1f);
        typeof(ChickPlayerController).GetField("sprintExhausted", Private).SetValue(player, false);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return new WaitForSeconds(.35f);
        InputSystem.QueueStateEvent(keyboard, run ? new KeyboardState(Key.W, Key.LeftShift) : new KeyboardState(Key.W));
        yield return new WaitForSeconds(.4f); // steering and animation blend settle
        Vector3 start = player.transform.position;
        float t0 = Time.time;
        yield return new WaitForSeconds(.6f);
        Vector3 delta = player.transform.position - start;
        delta.y = 0f;
        speed[0] = delta.magnitude / (Time.time - t0);
        var state = player.ActiveAnimator.GetCurrentAnimatorStateInfo(0);
        playback[0] = state.IsName("run") ? state.speedMultiplier : -1f;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return new WaitForSeconds(.2f);
    }

    private void JournalChecks()
    {
        var root = journal.GetComponent<UIDocument>().rootVisualElement;
        var price = root.Q<Button>("sprint-speed-price");
        Label cost = root.Q<Label>("sprint-speed-cost");
        int Total() => root.Q("sprint-speed-levels").childCount;
        int Filled() => root.Q("sprint-speed-levels").Query(className: "level-filled").ToList().Count;

        Call(statistics, "Restore", new FarmSaveData());
        Call(upgrades, "Restore", new FarmSaveData());
        journal.Open();
        Check(cost.text == "15" && Total() == 3 && Filled() == 0 && price.ClassListContains("unaffordable"),
            "Journal: three level eggs, price 15, dimmed without eggs");
        GrantEggs(15);
        Call(journal, "SprintSpeedPriceClicked");
        Check(upgrades.GetLevel(Sprint) == 1 && cost.text == "30" && Filled() == 1 &&
            root.Q<Label>("upgrades-status").text.StartsWith("Sprint Hızı 1."), "Journal: buying level 1 shows next price 30");
        GrantEggs(75);
        Call(journal, "SprintSpeedPriceClicked");
        Call(journal, "SprintSpeedPriceClicked");
        Check(upgrades.IsMaxed(Sprint) && cost.text == "MAX" && Filled() == 3 && price.ClassListContains("max-price"),
            "Journal: level 3 shows MAX with three eggs");
        Check(upgrades.GetLevel(PlayerUpgrades.FasterPeck) == 0 && upgrades.GetLevel(PlayerUpgrades.CollectRange) == 0,
            "Journal: other upgrades stay untouched");
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
        keyboard = InputSystem.AddDevice<Keyboard>("SprintUpgradeTestKeyboard");

        upgrades = FindFirstObjectByType<PlayerUpgrades>();
        statistics = FindFirstObjectByType<FoodStatisticsTracker>();
        player = FindFirstObjectByType<ChickPlayerController>();
        journal = FindFirstObjectByType<GameplayJournalController>(FindObjectsInactive.Include);

        var menu = FindFirstObjectByType<MainMenuController>(); menu.enabled = false;
        menu.GetComponent<UIDocument>().rootVisualElement.style.display = DisplayStyle.None;
        var time = FindFirstObjectByType<GameTimeManager>();
        typeof(GameTimeManager).GetField("loadExistingSave", Private).SetValue(time, false);
        FindFirstObjectByType<MainMenuGameplayGate>().ReleaseGameplay();
        float wait = Time.realtimeSinceStartup + 20f;
        while (time.IsTransitioning && Time.realtimeSinceStartup < wait) yield return null;
        // No day end or autosave can run while the test changes progress in memory.
        time.enabled = false;

        Call(statistics, "Restore", new FarmSaveData());
        Call(upgrades, "Restore", new FarmSaveData());
        Check(upgrades.GetMaxLevel(Sprint) == 3 && upgrades.GetNextPrice(Sprint) == 15 &&
            Mathf.Approximately(upgrades.SprintSpeedMultiplier, 1f), "Starts at level 0 of 3, next price 15");
        GrantEggs(90);
        int[] prices = { 15, 30, 45 };
        float[] multipliers = { 1.1f, 1.2f, 1.3f };
        bool ok = true;
        for (int i = 0; i < 3; i++)
        {
            int before = upgrades.AvailableEggs;
            ok &= upgrades.GetNextPrice(Sprint) == prices[i] && upgrades.TryPurchase(Sprint) &&
                upgrades.AvailableEggs == before - prices[i] && Mathf.Approximately(upgrades.SprintSpeedMultiplier, multipliers[i]);
        }
        Check(ok, "Three levels cost 15/30/45 and run 1.1x / 1.2x / 1.3x");
        Check(upgrades.IsMaxed(Sprint) && !upgrades.TryPurchase(Sprint) && upgrades.AvailableEggs == 0, "Level 3 is the maximum");

        floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "SprintUpgradeTestGround_TEMP";
        floor.transform.position = TestAreaLayout.Origin + new Vector3(100, -.05f, 100); floor.transform.localScale = new Vector3(12, .1f, 12);
        var form = player.GetComponent<PlayerGrowthController>();
        form.SetForm(PlayerGrowthController.Form.Chick);

        float[] s = { 0 }, p = { 0 };
        SetLevel(0);
        yield return MeasureSpeed(true, s, p); float baseRun = s[0], basePlayback = p[0];
        yield return MeasureSpeed(false, s, p); float baseWalk = s[0];
        SetLevel(3);
        yield return MeasureSpeed(true, s, p); float fastRun = s[0], fastPlayback = p[0];
        yield return MeasureSpeed(false, s, p); float fastWalk = s[0];
        Check(Mathf.Abs(fastRun / baseRun - 1.3f) < .06f, $"Chick run speed {baseRun:0.00} -> {fastRun:0.00} m/s (x{fastRun / baseRun:0.00})");
        Check(Mathf.Abs(fastWalk / baseWalk - 1f) < .05f, $"Walking is unchanged ({baseWalk:0.00} -> {fastWalk:0.00} m/s)");
        Check(Mathf.Approximately(basePlayback, 1f) && Mathf.Approximately(fastPlayback, 1.3f),
            $"Run animation plays x{basePlayback:0.00} -> x{fastPlayback:0.00} with the speed");

        form.SetForm(PlayerGrowthController.Form.Chicken);
        SetLevel(0);
        yield return MeasureSpeed(true, s, p); float chickenRun = s[0];
        SetLevel(3);
        yield return MeasureSpeed(true, s, p); float chickenFast = s[0], chickenPlayback = p[0];
        Check(Mathf.Abs(chickenFast / chickenRun - 1.3f) < .06f && Mathf.Approximately(chickenPlayback, 1.3f),
            $"Chicken run speed {chickenRun:0.00} -> {chickenFast:0.00} m/s, animation x{chickenPlayback:0.00}");
        form.SetForm(PlayerGrowthController.Form.Chick);

        var data = new FarmSaveData();
        Call(upgrades, "Capture", data);
        var loaded = JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(data));
        Call(upgrades, "Restore", new FarmSaveData());
        Call(upgrades, "Restore", loaded);
        Check(upgrades.GetLevel(Sprint) == 3, "Save/load keeps Sprint Hızı at level 3");

        JournalChecks();

        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("SPRINT UPGRADE VERIFICATION\n" + Result);
        Cleanup();
    }

    private void Update() => keyboard?.MakeCurrent();

    private void Cleanup()
    {
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        if (originalInput != null) InputSystem.settings = originalInput;
        if (testInput != null) Destroy(testInput);
        Application.runInBackground = originalBackground; Time.captureDeltaTime = originalCapture;
        if (floor != null) Destroy(floor);
    }

    private void OnDestroy() => Cleanup();
}
#endif
