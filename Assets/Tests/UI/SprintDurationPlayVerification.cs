#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Play Mode check for sprint stamina and "Sprint Süresi". Never attached to a saved scene; never writes the player's save.
public sealed class SprintDurationPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Duration = PlayerUpgrades.SprintDuration;
    private readonly StringBuilder report = new();
    private int failures;
    private PlayerUpgrades upgrades;
    private FoodStatisticsTracker statistics;
    private ChickPlayerController player;
    private GameplayJournalController journal;
    private SprintStaminaHUD hud;
    private Keyboard keyboard;
    private InputSettings originalInput, testInput;
    private bool originalBackground;
    private float originalCapture;
    private GameObject floor;
    private float exhaustedAt;

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
        { upgradeLevels = new[] { new UpgradeLevelSaveEntry { id = Duration, level = level } } });
    private void ResetStamina()
    {
        typeof(ChickPlayerController).GetField("sprintStamina", Private).SetValue(player, -1f);
        typeof(ChickPlayerController).GetField("sprintExhausted", Private).SetValue(player, false);
    }
    private void Keys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));

    private IEnumerator Prepare()
    {
        player.TeleportTo(TestAreaLayout.Origin + new Vector3(100, .002f, 100), Quaternion.identity);
        Keys();
        ResetStamina();
        yield return new WaitForSeconds(.5f);
    }

    // Holds W+Shift and returns how many seconds of sprint the stamina lasted.
    private IEnumerator SprintUntilExhausted(float[] seconds)
    {
        yield return Prepare();
        Keys(Key.W, Key.LeftShift);
        yield return null;
        float start = Time.time, deadline = Time.time + 20f;
        while (!player.SprintExhausted && Time.time < deadline) yield return null;
        seconds[0] = Time.time - start;
        exhaustedAt = Time.time;
    }

    private IEnumerator MeasureSpeed(float[] speed)
    {
        Vector3 start = player.transform.position; float t0 = Time.time;
        yield return new WaitForSeconds(.4f);
        Vector3 delta = player.transform.position - start; delta.y = 0f;
        speed[0] = delta.magnitude / (Time.time - t0);
    }

    private void JournalChecks()
    {
        var root = journal.GetComponent<UIDocument>().rootVisualElement;
        var price = root.Q<Button>("sprint-duration-price");
        Label cost = root.Q<Label>("sprint-duration-cost");
        int Total() => root.Q("sprint-duration-levels").childCount;
        int Filled() => root.Q("sprint-duration-levels").Query(className: "level-filled").ToList().Count;

        Call(statistics, "Restore", new FarmSaveData());
        Call(upgrades, "Restore", new FarmSaveData());
        journal.Open();
        Check(cost.text == "15" && Total() == 3 && Filled() == 0 && price.ClassListContains("unaffordable"),
            "Journal: three level eggs, price 15, dimmed without eggs");
        GrantEggs(15);
        Call(journal, "SprintDurationPriceClicked");
        Check(upgrades.GetLevel(Duration) == 1 && cost.text == "30" && Filled() == 1 &&
            root.Q<Label>("upgrades-status").text.StartsWith("Sprint Süresi 1."), "Journal: buying level 1 shows next price 30");
        GrantEggs(75);
        Call(journal, "SprintDurationPriceClicked");
        Call(journal, "SprintDurationPriceClicked");
        Check(upgrades.IsMaxed(Duration) && cost.text == "MAX" && Filled() == 3 && price.ClassListContains("max-price"),
            "Journal: level 3 shows MAX with three eggs");
        Check(upgrades.GetLevel(PlayerUpgrades.SprintSpeed) == 0 && upgrades.GetLevel(PlayerUpgrades.FasterPeck) == 0,
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
        keyboard = InputSystem.AddDevice<Keyboard>("SprintDurationTestKeyboard");

        upgrades = FindFirstObjectByType<PlayerUpgrades>();
        statistics = FindFirstObjectByType<FoodStatisticsTracker>();
        player = FindFirstObjectByType<ChickPlayerController>();
        journal = FindFirstObjectByType<GameplayJournalController>(FindObjectsInactive.Include);
        hud = FindFirstObjectByType<SprintStaminaHUD>();
        Check(hud != null, "Sprint stamina HUD exists in the scene");

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
        Check(upgrades.GetMaxLevel(Duration) == 3 && upgrades.GetNextPrice(Duration) == 15 &&
            Mathf.Approximately(player.SprintSeconds, 4f), "Starts at level 0 of 3 (4 s sprint), next price 15");
        GrantEggs(90);
        int[] prices = { 15, 30, 45 };
        float[] seconds = { 6f, 8f, 10f };
        bool ok = true;
        for (int i = 0; i < 3; i++)
        {
            int before = upgrades.AvailableEggs;
            ok &= upgrades.GetNextPrice(Duration) == prices[i] && upgrades.TryPurchase(Duration) &&
                upgrades.AvailableEggs == before - prices[i] && Mathf.Approximately(player.SprintSeconds, seconds[i]);
        }
        Check(ok, "Three levels cost 15/30/45 and sprint 6 / 8 / 10 s");
        Check(upgrades.IsMaxed(Duration) && !upgrades.TryPurchase(Duration), "Level 3 is the maximum");

        floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "SprintDurationTestGround_TEMP";
        floor.transform.position = TestAreaLayout.Origin + new Vector3(100, -.05f, 100); floor.transform.localScale = new Vector3(60, .1f, 60);
        player.GetComponent<PlayerGrowthController>().SetForm(PlayerGrowthController.Form.Chick);

        // Full stamina: the round gauge stays on screen with a full ring.
        SetLevel(0);
        yield return Prepare();
        yield return new WaitForSeconds(.5f);
        Check(hud.IsShown && Mathf.Approximately(hud.FillFraction, 1f), "Full stamina: gauge is shown with a full ring");

        // Walking never drains.
        Keys(Key.W);
        yield return new WaitForSeconds(1.5f);
        Check(Mathf.Approximately(player.SprintStamina01, 1f), "Walking without Shift does not drain stamina");

        float[] lasted = { 0 };
        yield return SprintUntilExhausted(lasted);
        Check(Mathf.Abs(lasted[0] - 4f) < .15f, $"Level 0: sprint lasts {lasted[0]:0.00} s (expected 4)");
        yield return null; // let the HUD read the exhausted state this frame
        Check(hud.IsShown && hud.FillFraction < .01f && Gauge().ChickIcon.tintColor == Color.white,
            "Ring is empty when stamina runs out; the runner keeps its full color");
        float[] speed = { 0 };
        yield return MeasureSpeed(speed);
        Check(player.SprintExhausted && Mathf.Abs(speed[0] - .8f) < .1f, $"Out of stamina, holding Shift walks at {speed[0]:0.00} m/s");

        // Refill starts once sprinting stops (even with Shift still held); sprint unlocks at 25%.
        Keys();
        float deadline = Time.time + 5f;
        float twinkle = 0f;
        bool colorKept = true;
        while (player.SprintExhausted && Time.time < deadline)
        {
            twinkle = Mathf.Max(twinkle, Gauge().Sparkle);
            colorKept &= Gauge().ChickIcon.tintColor == Color.white;
            yield return null;
        }
        Check(twinkle > .1f, "Refill twinkle shows while the ring climbs back");
        Check(colorKept, "Runner never greys out while the ring refills");
        float unlock = Time.time - exhaustedAt;
        Check(Mathf.Abs(unlock - 1.75f) < .15f, $"Sprint unlocks {unlock:0.00} s after running out (0.75 s delay + 25% of 4 s refill)");
        deadline = Time.time + 6f;
        while (player.SprintStamina01 < 1f && Time.time < deadline) yield return null;
        float refill = Time.time - exhaustedAt;
        Check(Mathf.Abs(refill - 4.75f) < .15f, $"Full refill {refill:0.00} s after running out (0.75 s delay + 4 s)");
        yield return null;
        yield return null;
        Check(hud.IsShown && Mathf.Approximately(hud.FillFraction, 1f) && Gauge().Sparkle == 0f,
            "Full again: ring complete, twinkle off");

        SetLevel(3);
        yield return SprintUntilExhausted(lasted);
        Check(Mathf.Abs(lasted[0] - 10f) < .2f, $"Level 3: sprint lasts {lasted[0]:0.00} s (expected 10)");
        Keys();

        var data = new FarmSaveData();
        Call(upgrades, "Capture", data);
        var loaded = JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(data));
        Call(upgrades, "Restore", new FarmSaveData());
        Call(upgrades, "Restore", loaded);
        Check(upgrades.GetLevel(Duration) == 3, "Save/load keeps Sprint Süresi at level 3");

        JournalChecks();
        yield return GaugeChecks();

        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("SPRINT DURATION VERIFICATION\n" + Result);
        Cleanup();
    }

    private SprintStaminaGauge Gauge() => hud.GetComponent<UIDocument>().rootVisualElement.Q<SprintStaminaGauge>();

    private IEnumerator GaugeChecks()
    {
        var root = hud.GetComponent<UIDocument>().rootVisualElement;
        var gauge = Gauge();
        Rect frame = root.Q("SprintStaminaHUD").worldBound, panel = root.worldBound;
        Check(frame.xMin < panel.width * .2f && frame.yMax > panel.height * .8f, $"Gauge sits in the lower-left corner ({frame})");
        Check(gauge.ChickIcon.image != null && gauge.ChickenIcon.image != null &&
            gauge.Q<Label>("sprint-gauge-key").text == "SHIFT", "Gauge has chick and chicken icons and a SHIFT key");

        journal.Open();
        yield return null;
        Check(!hud.IsShown, "Gauge hides while the journal is open");
        journal.Close();

        var growth = player.GetComponent<PlayerGrowthController>();
        growth.SetForm(PlayerGrowthController.Form.Chick);
        yield return null;
        Check(hud.IconMorph == 0f, "Chick form shows the chick runner");
        growth.SetForm(PlayerGrowthController.Form.Chicken);
        yield return null;
        Check(hud.IconMorph == 1f, "Instant form change (save/developer) snaps to the chicken runner");

        // Natural growth: the icon changes gradually during the growth presentation.
        growth.SetForm(PlayerGrowthController.Form.Chick);
        yield return Prepare();
        growth.RequestChickenGrowth();
        float deadline = Time.time + 4f, midway = -1f;
        while (growth.CurrentForm == PlayerGrowthController.Form.Chick && Time.time < deadline) yield return null;
        while (growth.IsTransforming && Time.time < deadline)
        {
            if (hud.IconMorph > .05f && hud.IconMorph < .95f) midway = hud.IconMorph;
            yield return null;
        }
        yield return null;
        Check(growth.CurrentForm == PlayerGrowthController.Form.Chicken && midway > 0f && hud.IconMorph == 1f,
            $"Natural growth cross-fades chick to chicken (seen at {midway:0.00}, ends at {hud.IconMorph:0.00})");
        growth.SetForm(PlayerGrowthController.Form.Chick);
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
