#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in runtime verification. Never attached to a saved scene; no save files are written.
public sealed class HelperChickFlockVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private static HelperChickFlockVerification activeTest;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly StringBuilder report = new StringBuilder();
    private readonly List<GameObject> temporary = new List<GameObject>();
    private PlayerUpgrades upgrades;
    private GameplayJournalController journal;
    private FarmSaveData original;
    private bool originalBackground;
    private Keyboard keyboard;
    private InputSettings originalInput, testInput;
    private int failures;
    private bool cleaned;

    private static HelperChickController[] Helpers() =>
        FindObjectsByType<HelperChickController>(FindObjectsSortMode.None);

    private void Check(bool ok, string message)
    {
        report.AppendLine((ok ? "PASS " : "FAIL ") + message);
        if (!ok) { failures++; Debug.LogError("FLOCK TEST: " + message); }
    }

    private VisualElement Root => (VisualElement)typeof(GameplayJournalController).GetField("root", Flags).GetValue(journal);

    private IEnumerator ClickWithPointer(VisualElement root, string name)
    {
        var button = root.Q<Button>(name);
        bool clicked = false;
        System.Action callback = () => clicked = true;
        button.clicked += callback;
        Vector2 center = button.worldBound.center;
        // Exercise UITK hit-testing/bubbling without depending on desktop focus during automation.
        var picked = root.panel.Pick(center);
        using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = center }))
        { down.target = picked; picked.SendEvent(down); }
        yield return null;
        using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = center }))
        { up.target = picked; picked.SendEvent(up); }
        yield return new WaitForSecondsRealtime(.2f);
        button.clicked -= callback;
        Check(clicked, "UITK picked pointer route reaches " + name);
    }

    private void CheckMenu(int level)
    {
        VisualElement row = Root.Q("helper-levels");
        int filled = 0;
        foreach (VisualElement egg in row.Children()) if (egg.ClassListContains("level-filled")) filled++;
        Check(row.childCount == 3 && filled == level, "menu eggs " + filled + "/" + row.childCount + " expected=" + level + "/3");
        Check(Root.Q<Label>("helper-cost").text == (level == 3 ? "MAX" : "20"), "next price / MAX at level " + level);
    }

    private IEnumerator Start()
    {
        // MCP may retry a request across a domain reload; never run two input owners at once.
        if (activeTest != null && activeTest != this) { Destroy(gameObject); yield break; }
        activeTest = this;
        Result = "Running";
        originalBackground = Application.runInBackground;
        Application.runInBackground = true;
        originalInput = InputSystem.settings;
        testInput = Instantiate(originalInput);
        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testInput;
        keyboard = InputSystem.AddDevice<Keyboard>("FlockTestKeyboard");
        var mainMenu = FindFirstObjectByType<MainMenuController>();
        if (mainMenu != null)
        {
            var menuRoot = mainMenu.GetComponent<UIDocument>().rootVisualElement;
            yield return new WaitForSecondsRealtime(.5f);
            yield return ClickWithPointer(menuRoot, "settings-button");
            yield return ClickWithPointer(menuRoot, "settings-back");
        }
        foreach (var clock in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { clock.StopAllCoroutines(); clock.enabled = false; }
        var gate = FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include);
        typeof(MainMenuGameplayGate).GetField("released", Flags).SetValue(gate, true);
        var player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        var growth = player.GetComponent<PlayerGrowthController>();
        upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        journal = FindFirstObjectByType<GameplayJournalController>(FindObjectsInactive.Include);
        original = new FarmSaveData();
        typeof(PlayerUpgrades).GetMethod("Capture", Flags).Invoke(upgrades, new object[] { original });
        typeof(PlayerUpgrades).GetMethod("Restore", Flags).Invoke(upgrades, new object[] { new FarmSaveData { bonusEggs = 120 } });
        // Exercise migration from an old serialized one-helper definition.
        var definitions = (System.Array)typeof(PlayerUpgrades).GetField("upgrades", Flags).GetValue(upgrades);
        foreach (object definition in definitions)
            if ((string)definition.GetType().GetField("id").GetValue(definition) == PlayerUpgrades.HelperChick)
                definition.GetType().GetField("levelPrices").SetValue(definition, new[] { 20 });
        typeof(PlayerUpgrades).GetMethod("EnsureDefaultUpgrades", Flags).Invoke(upgrades, null);
        Check(upgrades.GetMaxLevel(PlayerUpgrades.HelperChick) == 3, "legacy one-level definition migrates to three");

        player.enabled = true;
        player.GetComponent<Animator>().enabled = true;
        Time.timeScale = 1f;
        growth.SetForm(PlayerGrowthController.Form.Chicken);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "FlockTestGround_TEMP";
        floor.transform.position = new Vector3(500f, 299.95f, 500f);
        floor.transform.localScale = new Vector3(80f, .1f, 80f);
        temporary.Add(floor);
        player.TeleportTo(new Vector3(500f, 300.01f, 500f), Quaternion.identity);
        var orbit = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        orbit.enabled = true;
        orbit.SetOrbitAngles(0f, orbit.OrbitPitch);
        orbit.SnapToTarget();
        Physics.SyncTransforms();
        yield return new WaitForSeconds(.7f);

        journal.Open();
        yield return new WaitForSecondsRealtime(.5f);
        yield return ClickWithPointer(Root, "shop-tab");
        yield return ClickWithPointer(Root, "statistics-tab");
        yield return ClickWithPointer(Root, "upgrades-tab");
        CheckMenu(0);
        yield return ClickWithPointer(Root, "journal-close");
        Check(Helpers().Length == 0, "zero purchased means no helper");
        for (int level = 1; level <= 3; level++)
        {
            journal.Open();
            int before = upgrades.AvailableEggs;
            typeof(GameplayJournalController).GetMethod("HelperPriceClicked", Flags).Invoke(journal, null);
            Check(upgrades.HelperChickCount == level && before - upgrades.AvailableEggs == 20, "menu purchase " + level + " costs 20 and increments count");
            CheckMenu(level);
            journal.Close();
            yield return new WaitForSeconds(.6f);
            Check(Helpers().Length == level, "live independent helpers after purchase " + level + " = " + Helpers().Length);
        }
        int balance = upgrades.AvailableEggs;
        Check(!upgrades.TryPurchase(PlayerUpgrades.HelperChick) && balance == upgrades.AvailableEggs, "fourth purchase rejected without charging");
        var idleFlock = Helpers();
        var lastPositions = new Vector3[idleFlock.Length];
        var idleTravel = new float[idleFlock.Length];
        float farthestIdle = 0f;
        int asynchronousFrames = 0;
        for (int i = 0; i < idleFlock.Length; i++) lastPositions[i] = idleFlock[i].transform.position;
        for (float t = 0f; t < 10f; t += Time.deltaTime)
        {
            yield return null;
            int moving = 0;
            for (int i = 0; i < idleFlock.Length; i++)
            {
                Vector3 position = idleFlock[i].transform.position;
                float step = Vector3.Distance(position, lastPositions[i]);
                idleTravel[i] += step;
                if (step / Mathf.Max(.001f, Time.deltaTime) > .12f) moving++;
                lastPositions[i] = position;
                farthestIdle = Mathf.Max(farthestIdle, Vector3.Distance(position, player.transform.position));
            }
            if (moving > 0 && moving < idleFlock.Length) asynchronousFrames++;
        }
        for (int i = 0; i < idleTravel.Length; i++)
            Check(idleTravel[i] > .6f, "independent idle walking helper " + i + " distance=" + idleTravel[i].ToString("F2"));
        Check(farthestIdle > .75f && farthestIdle < 1.6f, "wider bounded idle radius=" + farthestIdle.ToString("F2"));
        Check(asynchronousFrames > 10, "helpers alternate walking and pauses independently");
        float minGap = float.MaxValue, maxDistance = 0f;
        var sideMin = new Dictionary<int, float>();
        var sideMax = new Dictionary<int, float>();
        var firstFollow = new Dictionary<int, float>();
        var previousSide = new Dictionary<int, int>();
        var crossings = new Dictionary<int, int>();
        Vector3 walkStart = player.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        for (float t = 0; t < 12f; t += Time.deltaTime)
        {
            yield return null;
            var flock = Helpers();
            for (int i = 0; i < flock.Length; i++)
            {
                maxDistance = Mathf.Max(maxDistance, Vector3.Distance(player.transform.position, flock[i].transform.position));
                int id = flock[i].GetInstanceID();
                if (!firstFollow.ContainsKey(id) && (bool)typeof(HelperChickController).GetField("following", Flags).GetValue(flock[i]))
                    firstFollow[id] = t;
                if (t > 6f)
                {
                    float side = player.transform.InverseTransformPoint(flock[i].transform.position).x;
                    sideMin[id] = sideMin.TryGetValue(id, out float lo) ? Mathf.Min(lo, side) : side;
                    sideMax[id] = sideMax.TryGetValue(id, out float hi) ? Mathf.Max(hi, side) : side;
                    if (Mathf.Abs(side) > .2f)
                    {
                        int sign = side > 0f ? 1 : -1;
                        if (previousSide.TryGetValue(id, out int oldSign) && sign != oldSign)
                            crossings[id] = crossings.TryGetValue(id, out int count) ? count + 1 : 1;
                        previousSide[id] = sign;
                    }
                }
                for (int j = i + 1; j < flock.Length; j++)
                    minGap = Mathf.Min(minGap, Vector3.Distance(flock[i].transform.position, flock[j].transform.position));
            }
        }
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        Check(Vector3.Distance(walkStart, player.transform.position) > 8f, "test owner actually walks=" + Vector3.Distance(walkStart, player.transform.position).ToString("F2"));
        Check(maxDistance > 2f && maxDistance < 4.5f, "wider but bounded following; max distance=" + maxDistance.ToString("F3"));
        Check(minGap > .16f, "companions stay separated; min gap=" + minGap.ToString("F3"));
        Check(firstFollow.Count == 3, "all three decide to catch up");
        foreach (var entry in firstFollow)
            Check(entry.Value > .2f, "owner departure is not immediately copied; delay=" + entry.Value.ToString("F2"));
        foreach (var pair in sideMin)
        {
            Check(sideMax[pair.Key] - pair.Value < .8f, "no artificial orbit in straight walking; lateral span=" + (sideMax[pair.Key] - pair.Value).ToString("F2"));
            Check(!crossings.TryGetValue(pair.Key, out int count) || count <= 1, "no repeated side swapping");
        }

        // A sharp owner turn must not rotate every helper's destination along with the camera.
        var turnFlock = Helpers();
        var destinations = new Vector3[turnFlock.Length];
        for (int i = 0; i < turnFlock.Length; i++)
        {
            typeof(HelperChickController).GetField("destinationTimer", Flags).SetValue(turnFlock[i], .5f);
            destinations[i] = (Vector3)typeof(HelperChickController).GetField("slotWorld", Flags).GetValue(turnFlock[i]);
        }
        orbit.SetOrbitAngles(90f, orbit.OrbitPitch);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        yield return new WaitForSeconds(.25f);
        for (int i = 0; i < turnFlock.Length; i++)
            Check(Vector3.Distance(destinations[i], (Vector3)typeof(HelperChickController).GetField("slotWorld", Flags).GetValue(turnFlock[i])) < .01f,
                "world destination retained briefly after owner turn");
        float runMaxDistance = 0f;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
        for (float t = 0f; t < 6f; t += Time.deltaTime)
        {
            yield return null;
            foreach (var helper in Helpers())
                runMaxDistance = Mathf.Max(runMaxDistance, Vector3.Distance(helper.transform.position, player.transform.position));
        }
        Check(runMaxDistance < 4.8f, "turn and sprint catch-up stays bounded=" + runMaxDistance.ToString("F2"));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return new WaitForSeconds(5f);
        foreach (var helper in Helpers())
            Check(!(bool)typeof(HelperChickController).GetField("following", Flags).GetValue(helper), "returns to independent roaming after owner stops; gap=" + Vector3.Distance(helper.transform.position, player.transform.position).ToString("F2"));

        // Round-trip the existing save representation in memory, not on disk.
        var saved = new FarmSaveData();
        typeof(PlayerUpgrades).GetMethod("Capture", Flags).Invoke(upgrades, new object[] { saved });
        typeof(PlayerUpgrades).GetMethod("Restore", Flags).Invoke(upgrades, new object[] { new FarmSaveData() });
        yield return new WaitForSeconds(.2f);
        Check(Helpers().Length == 0, "restoring level zero removes all helpers");
        typeof(PlayerUpgrades).GetMethod("Restore", Flags).Invoke(upgrades, new object[] { saved });
        yield return new WaitForSeconds(.5f);
        Check(upgrades.HelperChickCount == 3 && Helpers().Length == 3 && upgrades.AvailableEggs == balance, "save round-trip restores three and wallet");
        growth.SetForm(PlayerGrowthController.Form.Chick);
        yield return new WaitForSeconds(.5f);
        Check(Helpers().Length == 0, "chick form removes companions");
        growth.SetForm(PlayerGrowthController.Form.Chicken);
        yield return new WaitForSeconds(.5f);
        Check(Helpers().Length == 3, "chicken form restores exactly three");

        // The same food cannot be reserved by two helpers in the same frame.
        var allFood = FindObjectsByType<EdibleObject>(FindObjectsSortMode.None);
        if (allFood.Length > 0)
        {
            var flock = Helpers();
            typeof(HelperChickController).GetField("foodTarget", Flags).SetValue(flock[0], allFood[0]);
            bool reserved = (bool)typeof(HelperChickController).GetMethod("FoodClaimedByCompanion", Flags)
                .Invoke(flock[1], new object[] { allFood[0] });
            Check(reserved, "shared food reservation excludes another helper");
            typeof(HelperChickController).GetMethod("CancelFood", Flags).Invoke(flock[0], null);
            bool released = !(bool)typeof(HelperChickController).GetMethod("FoodClaimedByCompanion", Flags)
                .Invoke(flock[1], new object[] { allFood[0] });
            Check(released, "cancelled food reservation is released");
        }
        Result = (failures == 0 ? "PASS\n" : "FAIL\n") + report;
        Debug.Log("HELPER FLOCK VERIFICATION\n" + Result);
        Cleanup();
    }

    private void Update()
    {
        if (keyboard != null && keyboard.added) keyboard.MakeCurrent();
    }

    private void Cleanup()
    {
        if (cleaned || activeTest != this) return;
        cleaned = true;
        if (journal != null) journal.Close();
        if (upgrades != null && original != null)
            typeof(PlayerUpgrades).GetMethod("Restore", Flags).Invoke(upgrades, new object[] { original });
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (originalInput != null) InputSystem.settings = originalInput;
        if (testInput != null) Destroy(testInput);
        Application.runInBackground = originalBackground;
        foreach (var item in temporary) if (item != null) Destroy(item);
        temporary.Clear();
        activeTest = null;
    }
    private void OnDestroy() => Cleanup();
}
#endif
