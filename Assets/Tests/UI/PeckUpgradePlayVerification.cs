#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Play Mode check for "Daha Hızlı Gagalama". Never attached to a saved scene; never writes the player's save.
public sealed class PeckUpgradePlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Peck = PlayerUpgrades.FasterPeck;
    private static readonly int EatSpeed = Animator.StringToHash("EatPlaybackSpeed");
    private readonly StringBuilder report = new();
    private int failures;
    private PlayerUpgrades upgrades;
    private FoodStatisticsTracker statistics;
    private ChickPlayerController player;
    private ChickEatingController eater;
    private GameplayJournalController journal;
    private Mouse mouse;
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
    private static GameObject Spawn(Vector3 position)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/WheatGrain.prefab");
        return Instantiate(prefab, position, Quaternion.identity);
    }
    private void GrantEggs(int count)
    {
        for (int i = 0; i < count; i++)
            upgrades.TryCollectEgg(false);
    }
    private void ResetProgress()
    {
        Call(statistics, "Restore", new FarmSaveData());
        Call(upgrades, "Restore", new FarmSaveData());
    }

    // Real left click on a grain in front of the chick; returns how long the peck locks the player.
    private IEnumerator MeasurePeck(float[] result, float[] playback)
    {
        yield return new WaitForSeconds(.3f);
        Transform body = player.transform;
        var seed = Spawn(body.position + body.forward * .1f);
        Physics.SyncTransforms();
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        float deadline = Time.time + 1f;
        while (!eater.IsBusy && Time.time < deadline) yield return null;
        float start = Time.time;
        playback[0] = eater.IsBusy ? player.ActiveAnimator.GetFloat(EatSpeed) : 0f;
        deadline = Time.time + 5f;
        while (eater.IsBusy && Time.time < deadline) yield return null;
        result[0] = playback[0] > 0f ? Time.time - start : float.PositiveInfinity;
        Check(seed == null || seed.GetComponent<EdibleObject>().IsConsumed, "Real click peck consumes the grain");
        if (seed != null) Destroy(seed);
    }

    private void JournalChecks()
    {
        var root = journal.GetComponent<UIDocument>().rootVisualElement;
        var price = root.Q<Button>("peck-price");
        Label cost = root.Q<Label>("peck-cost");
        int Filled() => root.Q("peck-levels").Query(className: "level-filled").ToList().Count;

        ResetProgress();
        journal.Open();
        Check(cost.text == "10" && Filled() == 0 && price.ClassListContains("unaffordable") &&
            root.Q<Label>("points-label").text == "0", "Journal: level 0 shows price 10, empty eggs, dimmed without eggs");
        Call(journal, "PeckPriceClicked");
        Check(upgrades.GetLevel(Peck) == 0 && root.Q<Label>("upgrades-status").text.Contains("10 yumurta"),
            "Journal: buying without eggs explains the missing amount");
        GrantEggs(15);
        Check(!price.ClassListContains("unaffordable") && root.Q<Label>("points-label").text == "15",
            "Journal: collected eggs update the balance and enable the button");
        Call(journal, "PeckPriceClicked");
        Check(upgrades.GetLevel(Peck) == 1 && cost.text == "20" && Filled() == 1 &&
            root.Q<Label>("points-label").text == "5" && price.ClassListContains("unaffordable"),
            "Journal: purchase fills one egg, shows next price 20, spends 10");
        GrantEggs(90);
        for (int i = 0; i < 3; i++) Call(journal, "PeckPriceClicked");
        Check(upgrades.IsMaxed(Peck) && cost.text == "MAX" && Filled() == 4 && price.ClassListContains("max-price") &&
            root.Q<Image>("peck-egg").style.display.value == DisplayStyle.None && root.Q<Label>("points-label").text == "5",
            "Journal: level 4 shows MAX with all eggs filled");
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
        mouse = InputSystem.AddDevice<Mouse>("PeckUpgradeTestMouse");

        upgrades = FindFirstObjectByType<PlayerUpgrades>();
        statistics = FindFirstObjectByType<FoodStatisticsTracker>();
        player = FindFirstObjectByType<ChickPlayerController>();
        eater = player.GetComponent<ChickEatingController>();
        journal = FindFirstObjectByType<GameplayJournalController>(FindObjectsInactive.Include);
        Check(upgrades != null, "PlayerUpgrades exists in the scene");

        var menu = FindFirstObjectByType<MainMenuController>(); menu.enabled = false;
        menu.GetComponent<UIDocument>().rootVisualElement.style.display = DisplayStyle.None;
        var time = FindFirstObjectByType<GameTimeManager>();
        typeof(GameTimeManager).GetField("loadExistingSave", Private).SetValue(time, false);
        FindFirstObjectByType<MainMenuGameplayGate>().ReleaseGameplay();
        float wait = Time.realtimeSinceStartup + 20f;
        while (time.IsTransitioning && Time.realtimeSinceStartup < wait) yield return null;
        // No day end or autosave can run while the test changes progress in memory.
        time.enabled = false;

        ResetProgress();
        Check(upgrades.GetLevel(Peck) == 0 && upgrades.AvailableEggs == 0 && upgrades.GetNextPrice(Peck) == 10 &&
            upgrades.GetMaxLevel(Peck) == 4 && Mathf.Approximately(upgrades.PeckSpeedMultiplier, 1f), "Starts at level 0 of 4, next price 10");
        Check(!upgrades.TryPurchase(Peck) && upgrades.GetLevel(Peck) == 0, "Cannot buy without eggs");

        floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "PeckUpgradeTestGround_TEMP";
        floor.transform.position = TestAreaLayout.Origin + new Vector3(100, -.05f, 100); floor.transform.localScale = new Vector3(10, .1f, 10);
        player.GetComponent<PlayerGrowthController>().SetForm(PlayerGrowthController.Form.Chick);
        var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
        player.transform.SetPositionAndRotation(TestAreaLayout.Origin + new Vector3(100, .002f, 100), Quaternion.identity);
        cc.enabled = true; Physics.SyncTransforms();

        float[] baseTime = { 0 }, basePlayback = { 0 };
        yield return MeasurePeck(baseTime, basePlayback);

        // Food no longer funds upgrades; explicitly grant collected eggs for these checks.
        int startEggs = upgrades.AvailableEggs;
        GrantEggs(100);
        Check(upgrades.AvailableEggs == startEggs + 100, "Collected eggs fund upgrades (+100)");
        int[] prices = { 10, 20, 30, 40 };
        bool pricesOk = true;
        for (int i = 0; i < 4; i++)
        {
            int before = upgrades.AvailableEggs;
            pricesOk &= upgrades.GetNextPrice(Peck) == prices[i] && upgrades.TryPurchase(Peck) &&
                upgrades.GetLevel(Peck) == i + 1 && upgrades.AvailableEggs == before - prices[i];
        }
        Check(pricesOk, "Four levels cost 10/20/30/40 and each purchase spends its price");
        Check(upgrades.IsMaxed(Peck) && upgrades.GetNextPrice(Peck) == -1 && !upgrades.TryPurchase(Peck) &&
            upgrades.AvailableEggs == startEggs, "Level 4 is the maximum; no fifth purchase");
        Check(Mathf.Approximately(upgrades.PeckSpeedMultiplier, 1.5f), "Level 4 pecks 1.5x faster");

        float[] fastTime = { 0 }, fastPlayback = { 0 };
        yield return MeasurePeck(fastTime, fastPlayback);
        Check(Mathf.Approximately(fastPlayback[0], basePlayback[0] * 1.5f),
            $"Peck animation speed {basePlayback[0]:0.00} -> {fastPlayback[0]:0.00}");
        Check(fastTime[0] < baseTime[0] * .8f, $"Peck lock time {baseTime[0]:0.000}s -> {fastTime[0]:0.000}s");

        var data = new FarmSaveData();
        Call(upgrades, "Capture", data);
        var loaded = JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(data));
        Check(loaded.spentEggs == 100 && loaded.upgradeLevels.Length == 1 && loaded.upgradeLevels[0].id == Peck &&
            loaded.upgradeLevels[0].level == 4, "Save stores spent eggs and level");
        Call(upgrades, "Restore", new FarmSaveData());
        Check(upgrades.GetLevel(Peck) == 0, "New game resets the upgrade");
        Call(upgrades, "Restore", loaded);
        Check(upgrades.GetLevel(Peck) == 4, "Load restores level 4");
        Call(upgrades, "Restore", JsonUtility.FromJson<FarmSaveData>("{\"version\":1,\"eatenSeedCount\":17}"));
        Check(upgrades.GetLevel(Peck) == 0 && upgrades.AvailableEggs == 0, "Old save loads with no phantom food-funded eggs");
        Call(upgrades, "Restore", JsonUtility.FromJson<FarmSaveData>("{\"version\":1,\"spentEggs\":5,\"upgradeLevels\":[{\"id\":\"faster-peck\",\"level\":9}]}"));
        Check(upgrades.GetLevel(Peck) == 4, "Corrupt level is clamped to the maximum");

        JournalChecks();

        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("PECK UPGRADE VERIFICATION\n" + Result);
        Cleanup();
    }

    private void Update() => mouse?.MakeCurrent();

    private void Cleanup()
    {
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        mouse = null;
        if (originalInput != null) InputSystem.settings = originalInput;
        if (testInput != null) Destroy(testInput);
        Application.runInBackground = originalBackground; Time.captureDeltaTime = originalCapture;
        if (floor != null) Destroy(floor);
    }

    private void OnDestroy() => Cleanup();
}
#endif
