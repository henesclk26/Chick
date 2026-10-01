#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Play Mode check for "Çift Zıplama". Never attached to a saved scene; never writes the player's save.
public sealed class DoubleJumpPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Id = PlayerUpgrades.DoubleJump;
    private readonly StringBuilder report = new();
    private int failures;
    private PlayerUpgrades upgrades;
    private ChickPlayerController player;
    private GameplayJournalController journal;
    private Keyboard keyboard;
    private InputSettings originalInput, testInput;
    private bool originalBackground;
    private float originalCapture;
    private GameObject floor;

    private object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    private T Field<T>(string name) => (T)typeof(ChickPlayerController).GetField(name, Private).GetValue(player);
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
    private void SetLevel(int level) => Call(upgrades, "Restore", new FarmSaveData
        { upgradeLevels = new[] { new UpgradeLevelSaveEntry { id = Id, level = level } } });
    private float Ground => TestAreaLayout.Origin.y;

    private IEnumerator PressSpace()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    private IEnumerator Settle()
    {
        player.TeleportTo(TestAreaLayout.Origin + new Vector3(100, .002f, 100), Quaternion.identity);
        yield return new WaitForSeconds(.5f);
    }

    // Ground jump, then a second press at the top of the arc. Returns [second press accepted, peak height].
    private IEnumerator JumpTwice(float[] result, bool thirdPress = false)
    {
        yield return Settle();
        yield return PressSpace();
        float deadline = Time.time + 2f;
        while (Field<float>("verticalVelocity") > 0f && Time.time < deadline) yield return null;
        float before = Field<float>("verticalVelocity");
        yield return PressSpace();
        float after = Field<float>("verticalVelocity");
        result[0] = after > before + .5f ? 1f : 0f;
        result[2] = after;
        // Presentation: the full-body wing push right after the air jump.
        result[5] = 0f;
        float peak = player.transform.position.y;
        float flapWindow = Time.time + .25f;
        while (Time.time < flapWindow)
        {
            peak = Mathf.Max(peak, player.transform.position.y);
            var anim = player.ActiveAnimator;
            if (anim.GetCurrentAnimatorStateInfo(0).IsName("flapping") ||
                (anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsName("flapping"))) result[5] = 1f;
            yield return null;
        }
        if (thirdPress)
        {
            yield return new WaitForSeconds(.05f);
            float v3 = Field<float>("verticalVelocity");
            yield return PressSpace();
            result[3] = Field<float>("verticalVelocity") > v3 + .5f ? 1f : 0f;
        }
        deadline = Time.time + 3f;
        while (Field<bool>("jumping") && Time.time < deadline)
        {
            peak = Mathf.Max(peak, player.transform.position.y);
            yield return null;
        }
        result[1] = peak - Ground;
        yield return new WaitForSeconds(.2f);
    }

    private void JournalChecks()
    {
        var root = journal.GetComponent<UIDocument>().rootVisualElement;
        var price = root.Q<Button>("double-jump-price");
        Label cost = root.Q<Label>("double-jump-cost");
        int Total() => root.Q("double-jump-levels").childCount;
        int Filled() => root.Q("double-jump-levels").Query(className: "level-filled").ToList().Count;

        Call(upgrades, "Restore", new FarmSaveData());
        journal.Open();
        Check(cost.text == "200" && Total() == 1 && Filled() == 0 && price.ClassListContains("unaffordable"),
            "Journal: one level egg, price 200, dimmed without eggs");
        CollectEggs(199);
        Call(journal, "DoubleJumpPriceClicked");
        Check(!upgrades.DoubleJumpUnlocked && root.Q<Label>("upgrades-status").text.Contains("1 yumurta"),
            "Journal: 199 eggs are not enough (1 missing)");
        CollectEggs(1);
        Call(journal, "DoubleJumpPriceClicked");
        Check(upgrades.DoubleJumpUnlocked && cost.text == "MAX" && Filled() == 1 && price.ClassListContains("max-price") &&
            upgrades.AvailableEggs == 0 && root.Q<Label>("upgrades-status").text.StartsWith("Çift Zıplama"),
            "Journal: buying spends 200 eggs and shows MAX");
        Call(journal, "DoubleJumpPriceClicked");
        Check(upgrades.GetLevel(Id) == 1, "Journal: a second purchase is refused");
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
        keyboard = InputSystem.AddDevice<Keyboard>("DoubleJumpTestKeyboard");

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
        Check(upgrades.GetMaxLevel(Id) == 1 && upgrades.GetNextPrice(Id) == 200 && !upgrades.DoubleJumpUnlocked,
            "Single level, price 200, locked at start");
        CollectEggs(199);
        Check(!upgrades.TryPurchase(Id) && !upgrades.DoubleJumpUnlocked, "199 eggs cannot buy it");
        CollectEggs(1);
        Check(upgrades.TryPurchase(Id) && upgrades.DoubleJumpUnlocked && upgrades.AvailableEggs == 0 && upgrades.IsMaxed(Id),
            "200 eggs buy it; one purchase is the maximum");

        floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "DoubleJumpTestGround_TEMP";
        floor.transform.position = TestAreaLayout.Origin + new Vector3(100, -.05f, 100); floor.transform.localScale = new Vector3(10, .1f, 10);
        var growth = player.GetComponent<PlayerGrowthController>();
        growth.SetForm(PlayerGrowthController.Form.Chick);

        float g = -Field<float>("gravity");
        float chickHeight = Field<float>("chickJumpHeight");
        float multiplier = Field<float>("secondJumpHeightMultiplier");
        float[] r = new float[6];

        SetLevel(0);
        yield return JumpTwice(r);
        Check(r[0] == 0f && r[5] == 0f, $"Before purchase: mid-air Space does nothing, no wing push (peak {r[1]:0.00} m)");
        float singlePeak = r[1];

        SetLevel(1);
        yield return JumpTwice(r, true);
        float expected = Mathf.Sqrt(2f * g * chickHeight * multiplier);
        Check(r[0] == 1f && Mathf.Abs(r[2] - expected) < .35f,
            $"After purchase: mid-air Space jumps again ({r[2]:0.00} m/s, expected {expected:0.00})");
        Check(r[5] == 1f, "The air jump plays the full-body wing push");
        Check(r[3] == 0f, "A third jump in the same flight is refused");
        Check(r[1] > singlePeak + .15f, $"Double jump peak {r[1]:0.00} m vs single jump {singlePeak:0.00} m");
        Check(!Field<bool>("airJumpUsed") && player.GetComponent<CharacterController>().isGrounded, "Landing restores the air jump");

        yield return JumpTwice(r);
        Check(r[0] == 1f, "The air jump works again on the next flight");

        growth.SetForm(PlayerGrowthController.Form.Chicken);
        yield return JumpTwice(r);
        float chickenExpected = Mathf.Sqrt(2f * g * Field<float>("jumpHeight") * multiplier);
        Check(r[0] == 1f && Mathf.Abs(r[2] - chickenExpected) < .35f && r[5] == 1f,
            $"Chicken form double jumps too, with the wing push ({r[2]:0.00} m/s, expected {chickenExpected:0.00}, peak {r[1]:0.00} m)");
        growth.SetForm(PlayerGrowthController.Form.Chick);

        var data = new FarmSaveData();
        Call(upgrades, "Capture", data);
        var loaded = JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(data));
        Call(upgrades, "Restore", new FarmSaveData());
        Check(!upgrades.DoubleJumpUnlocked, "New game starts without double jump");
        Call(upgrades, "Restore", loaded);
        Check(upgrades.DoubleJumpUnlocked, "Save/load keeps double jump");

        JournalChecks();

        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("DOUBLE JUMP VERIFICATION\n" + Result);
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
