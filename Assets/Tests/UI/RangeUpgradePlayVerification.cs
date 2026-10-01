#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Play Mode check for "Toplama Menzili". Never attached to a saved scene; never writes the player's save.
public sealed class RangeUpgradePlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Range = PlayerUpgrades.CollectRange;
    // Measured with the wheat grain: reach ends at 25 / 27.5 / 30 / 32 cm for levels 0-3.
    private const float FarDistance = .31f;
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
    private static GameObject SpawnSeed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/WheatGrain.prefab");
        return Instantiate(prefab, TestAreaLayout.Origin + new Vector3(100, -10, 100), Quaternion.identity);
    }
    private void GrantEggs(int count)
    {
        for (int i = 0; i < count; i++)
            upgrades.TryCollectEgg(false);
    }
    private void SetLevel(int level)
    {
        var data = new FarmSaveData { upgradeLevels = new[] { new UpgradeLevelSaveEntry { id = Range, level = level } } };
        Call(upgrades, "Restore", data);
    }
    // Places a grain so its bite point sits the given distance straight ahead of the chick.
    private GameObject PlaceSeedAhead(float distance, float lift = 0f)
    {
        var seed = SpawnSeed();
        var edible = seed.GetComponent<EdibleObject>();
        Transform body = player.transform;
        Vector3 wanted = body.position + body.forward * distance;
        Vector3 bite = edible.BitePosition;
        // Horizontal offset by bite point; the grain root rests on the chick's ground (+ optional lift).
        Vector3 root = seed.transform.position + new Vector3(wanted.x - bite.x, 0f, wanted.z - bite.z);
        seed.transform.position = new Vector3(root.x, body.position.y + lift, root.z);
        Physics.SyncTransforms();
        return seed;
    }
    private bool Targets(GameObject seed)
    {
        Call(eater, "ApplyReach");
        return (EdibleObject)Call(eater, "FindTarget") == seed.GetComponent<EdibleObject>();
    }
    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        float deadline = Time.time + 3f;
        yield return null;
        while (eater.IsBusy && Time.time < deadline) yield return null;
    }

    private void JournalChecks()
    {
        var root = journal.GetComponent<UIDocument>().rootVisualElement;
        var price = root.Q<Button>("range-price");
        Label cost = root.Q<Label>("range-cost");
        int Total() => root.Q("range-levels").childCount;
        int Filled() => root.Q("range-levels").Query(className: "level-filled").ToList().Count;

        Call(statistics, "Restore", new FarmSaveData());
        Call(upgrades, "Restore", new FarmSaveData());
        journal.Open();
        Check(cost.text == "15" && Total() == 3 && Filled() == 0 && price.ClassListContains("unaffordable"),
            "Journal: three level eggs, price 15, dimmed without eggs");
        GrantEggs(15);
        Call(journal, "RangePriceClicked");
        Check(upgrades.GetLevel(Range) == 1 && cost.text == "30" && Filled() == 1 &&
            root.Q<Label>("upgrades-status").text.StartsWith("Toplama Menzili 1."), "Journal: buying level 1 shows next price 30");
        GrantEggs(75);
        Call(journal, "RangePriceClicked");
        Call(journal, "RangePriceClicked");
        Check(upgrades.IsMaxed(Range) && cost.text == "MAX" && Filled() == 3 && price.ClassListContains("max-price") &&
            root.Q<Image>("range-egg").style.display.value == DisplayStyle.None, "Journal: level 3 shows MAX with three eggs");
        Check(upgrades.GetLevel(PlayerUpgrades.FasterPeck) == 0 && root.Q<Label>("peck-cost").text == "10",
            "Journal: buying range leaves Daha Hızlı Gagalama untouched");
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
        mouse = InputSystem.AddDevice<Mouse>("RangeUpgradeTestMouse");

        upgrades = FindFirstObjectByType<PlayerUpgrades>();
        statistics = FindFirstObjectByType<FoodStatisticsTracker>();
        player = FindFirstObjectByType<ChickPlayerController>();
        eater = player.GetComponent<ChickEatingController>();
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
        Check(upgrades.GetMaxLevel(Range) == 3 && upgrades.GetNextPrice(Range) == 15 &&
            Mathf.Approximately(upgrades.CollectRangeMultiplier, 1f), "Starts at level 0 of 3, next price 15");
        GrantEggs(90);
        int[] prices = { 15, 30, 45 };
        float[] multipliers = { 1.15f, 1.3f, 1.45f };
        bool ok = true;
        for (int i = 0; i < 3; i++)
        {
            int before = upgrades.AvailableEggs;
            ok &= upgrades.GetNextPrice(Range) == prices[i] && upgrades.TryPurchase(Range) &&
                upgrades.AvailableEggs == before - prices[i] && Mathf.Approximately(upgrades.CollectRangeMultiplier, multipliers[i]);
        }
        Check(ok, "Three levels cost 15/30/45 and reach 1.15x / 1.30x / 1.45x");
        Check(upgrades.IsMaxed(Range) && !upgrades.TryPurchase(Range) && upgrades.AvailableEggs == 0, "Level 3 is the maximum");

        floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "RangeUpgradeTestGround_TEMP";
        floor.transform.position = TestAreaLayout.Origin + new Vector3(100, -.05f, 100); floor.transform.localScale = new Vector3(10, .1f, 10);
        player.GetComponent<PlayerGrowthController>().SetForm(PlayerGrowthController.Form.Chick);
        var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
        player.transform.SetPositionAndRotation(TestAreaLayout.Origin + new Vector3(100, .002f, 100), Quaternion.identity);
        cc.enabled = true; Physics.SyncTransforms();
        yield return new WaitForSeconds(.4f);

        var far = PlaceSeedAhead(FarDistance);
        SetLevel(0);
        Check(!Targets(far), $"Level 0: grain {FarDistance * 100:0} cm ahead is out of reach");
        SetLevel(2);
        Check(!Targets(far), "Level 2 (1.30x) is still short of it");
        SetLevel(3);
        Check(Targets(far), "Level 3 (1.45x) reaches it");
        yield return Click();
        Check(far == null || far.GetComponent<EdibleObject>().IsConsumed, "Level 3: a real click eats the far grain");
        if (far != null) Destroy(far);

        var near = PlaceSeedAhead(.12f);
        SetLevel(0);
        Check(Targets(near), "Level 0: a close grain is still reachable");
        Destroy(near);
        var high = PlaceSeedAhead(.12f, .07f);
        SetLevel(3);
        Check(!Targets(high), "Level 3 does not raise the vertical limit (7 cm high grain stays out of reach)");
        Destroy(high);

        SetLevel(3);
        var data = new FarmSaveData();
        Call(upgrades, "Capture", data);
        var loaded = JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(data));
        Call(upgrades, "Restore", new FarmSaveData());
        Call(upgrades, "Restore", loaded);
        Check(upgrades.GetLevel(Range) == 3, "Save/load keeps Toplama Menzili at level 3");

        JournalChecks();

        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("RANGE UPGRADE VERIFICATION\n" + Result);
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
