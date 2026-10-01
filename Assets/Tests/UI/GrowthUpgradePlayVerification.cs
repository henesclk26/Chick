#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Play Mode check. Never attached to a saved scene; never writes the player's save.
public sealed class GrowthUpgradePlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly StringBuilder report = new();
    private int failures;
    private GrowthProgressController growth;
    private ChickPlayerController player;
    private PlayerGrowthController form;
    private VisualElement root;
    private Keyboard keyboard;
    private InputSettings originalInput, testInput;
    private bool originalBackground;
    private float originalCapture;
    private GameObject floor;
    private bool renderHalfRing;

    private object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    private T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private void Check(bool ok, string message)
    {
        if (!ok) failures++;
        report.AppendLine((ok ? "PASS " : "FAIL ") + message);
        Result = "Running\n" + report;
    }
    private void Tick(float seconds, bool pressed = false, bool held = false, bool available = true)
    {
        Call(growth, "TickUpgrade", seconds, pressed, held, available);
        Call(growth, "PaintUpgradeUI");
    }
    private void Restore(FarmSaveData data) => Call(growth, "Restore", data);
    private bool HudVisible()
    {
        growth.RefreshForReveal();
        return root.Q("GrowthProgressHUD").style.display.value == DisplayStyle.Flex;
    }
    private bool PromptHidden() => root.Q("UpgradeNotificationRoot").style.display.value == DisplayStyle.None;
    private void Eat()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/WheatGrain.prefab");
        var seed = Instantiate(prefab, TestAreaLayout.Origin + new Vector3(100, -10, 100), Quaternion.identity);
        seed.GetComponent<EdibleObject>().Consume();
        Destroy(seed);
    }
    private FarmSaveData RoundTrip()
    {
        var data = new FarmSaveData();
        Call(growth, "Capture", data);
        Directory.CreateDirectory("Temp/GrowthUpgradeReview");
        const string path = "Temp/GrowthUpgradeReview/test-save.json";
        File.WriteAllText(path, JsonUtility.ToJson(data));
        // Production JSON validation and production Loaded subscribers, without changing SavePath.
        data = (FarmSaveData)typeof(FarmSaveSystem).GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { path });
        Call(FindFirstObjectByType<FarmSaveSystem>(), "NotifyLoaded", data);
        return data;
    }
    private void MechanicRemovalChecks()
    {
        Check(typeof(ChickPlayerController).GetProperty("DoubleJumpUnlocked") == null &&
            typeof(ChickPlayerController).GetMethod("SetDoubleJumpUnlocked") == null &&
            typeof(ChickPlayerController).GetField("jumpCount", Private) == null,
            "Double jump API and jump counter are removed from the player");
        Check(typeof(GrowthProgressController).GetProperty("DoubleJumpUnlocked") == null &&
            typeof(GrowthProgressController).GetNestedType("UpgradeStage") == null,
            "Growth has a single chick-to-chicken stage");
        Check(root.Q<Label>("GrowthCount") == null, "No percentage label above the bar");
        var journal = FindFirstObjectByType<GameplayJournalController>(FindObjectsInactive.Include);
        Check(journal == null || journal.GetComponent<UIDocument>().rootVisualElement.Q("double-jump-price") == null,
            "Journal no longer offers a double jump upgrade");
    }
    private IEnumerator StateChecks()
    {
        Restore(new FarmSaveData { growthUpgradeVersion = 1, currentGrowthProgress = .99f });
        Tick(.01f);
        Check(growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.Progressing && PromptHidden(),
            "Below 100% has no upgrade prompt (99% save cannot round up)");
        Check(HudVisible(), "Bar is visible while the player is a chick");
        Eat();
        Check(growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.GrowthReadyMessage &&
            form.CurrentForm == PlayerGrowthController.Form.Chick, "Final seed shows ready notification without growing");
        Tick(.3f); Check(root.Q("GrowthReadyLabel").style.opacity.value > .9f, "Ready pop/fade visible");
        Tick(1.19f); Check(growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.GrowthReadyMessage, "Ready stays for 1.5 seconds");
        Tick(.02f); Check(growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.WaitingForUpgrade, "Ready becomes persistent Z prompt");
        Eat(); Eat();
        Check(growth.EatenSeedCount == 50 && growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.WaitingForUpgrade,
            "Extra food is capped and does not replay ready message");
        Tick(.05f, true, true); Tick(.01f);
        Check(!form.GrowthPending, "Short tap grants nothing");
        Tick(.25f);
        Tick(.5f, true, true); Tick(.01f);
        var ring = root.Q<UpgradeHoldIndicator>();
        Check(Mathf.Abs(ring.Progress - .5f) < .001f, "Early release starts ring reset from 50%");
        Tick(.1f); Check(ring.Progress > 0f && ring.Progress < .5f, "Ring eases back instead of snapping");
        Tick(.1f); Check(ring.Progress == 0f && !form.GrowthPending, "Ring returns to zero after 0.2 seconds");
        Tick(.4f, true, true); Tick(.01f, false, true, false);
        Check(!form.GrowthPending && growth.UpgradeHoldProgress == 0f, "Pause/focus loss cancels hold");
        var pending = RoundTrip();
        Check(pending.growthUpgradePending && growth.Progress == 1f &&
            growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.WaitingForUpgrade,
            "Save/load full unclaimed bar restores prompt without replaying notification");
        Check(root.Q<Image>("RightIcon").sprite.texture.name == "ChickenIcon", "Right icon shows the chicken goal");
        Tick(.01f); Tick(.5f, true, true);
        Check(Mathf.Abs(ring.Progress - .5f) < .001f && !form.GrowthPending, "Half hold is not enough");
        Tick(.5f, false, true);
        Check(form.GrowthPending && growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.UpgradeCompleted,
            "One-second hold requests chicken growth");
        Check(PromptHidden() && ring.Progress == 0f, "Successful claim hides prompt and clears ring");
        Tick(2f, false, true);
        Check(growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.UpgradeCompleted,
            "Continued holding cannot claim twice");

        float deadline = Time.realtimeSinceStartup + 3f;
        while (form.CurrentForm != PlayerGrowthController.Form.Chicken && Time.realtimeSinceStartup < deadline) yield return null;
        Check(form.CurrentForm == PlayerGrowthController.Form.Chicken && !form.GrowthPending, "Player grows into a chicken");
        Tick(.01f);
        Check(HudVisible() && PromptHidden() && growth.EggProgress == 0f &&
            growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.Progressing, "Chicken starts an empty egg bar");
        var grown = RoundTrip();
        Check(!grown.growthUpgradePending, "Chicken save has no pending growth");

        Restore(new FarmSaveData { growthUpgradeVersion = 1, currentGrowthProgress = .34f });
        Eat(); Eat();
        Check(growth.EatenSeedCount == 17 && growth.EggSeedCount == 2 && HudVisible(), "Chicken food fills only the separate egg bar");

        // Developer panel path: switching back to the chick shows the bar and counts again.
        form.SetForm(PlayerGrowthController.Form.Chick);
        Eat();
        Check(HudVisible() && growth.EatenSeedCount == 18, "Developer switch back to chick shows the bar and counts seeds");
        for (int i = 0; i < 40; i++) Eat();
        form.SetForm(PlayerGrowthController.Form.Chicken);
        Tick(.01f);
        Check(PromptHidden() && HudVisible() && growth.EggSeedCount == 2, "Developer switch to chicken selects the unfinished egg bar");
        form.SetForm(PlayerGrowthController.Form.Chick);
        Tick(.01f);
        Check(growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.WaitingForUpgrade && !PromptHidden(),
            "Developer switch back with a full bar restores the Z prompt");

        Restore(JsonUtility.FromJson<FarmSaveData>(
            "{\"version\":1,\"growthUpgradeVersion\":1,\"doubleJumpUnlocked\":true,\"currentGrowthStage\":1,\"currentGrowthProgress\":0.34}"));
        Check(growth.EatenSeedCount == 17, "Save from the old double jump build still loads its growth progress");
        Restore(JsonUtility.FromJson<FarmSaveData>("{\"version\":1,\"eatenSeedCount\":17}"));
        Check(growth.EatenSeedCount == 17, "Oldest save format remains compatible");
        Check(root.Q<UpgradeHoldIndicator>() != null && growth.GetComponent<UIDocument>().visualTreeAsset != null &&
            growth.GetComponentInChildren<Canvas>(true) == null, "Authored UXML + custom VisualElement; no Canvas created");
    }
    private IEnumerator PressSpace()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }
    private IEnumerator JumpChecks()
    {
        yield return new WaitForSeconds(.6f);
        yield return PressSpace();
        Check(Field<bool>(player, "jumping"), "Ground jump works");
        yield return new WaitForSeconds(.06f);
        float before = Field<float>(player, "verticalVelocity");
        yield return PressSpace();
        float after = Field<float>(player, "verticalVelocity");
        Check(after <= before + .001f, "Airborne Space does not jump again (no double jump)");
        yield return new WaitForSeconds(.8f);
        Check(!Field<bool>(player, "jumping") && player.GetComponent<CharacterController>().isGrounded, "Player lands normally");
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
        keyboard = InputSystem.AddDevice<Keyboard>("GrowthUpgradeTestKeyboard");
        growth = FindFirstObjectByType<GrowthProgressController>(); player = FindFirstObjectByType<ChickPlayerController>();
        form = player.GetComponent<PlayerGrowthController>();
        root = growth.GetComponent<UIDocument>().rootVisualElement;
        var menu = FindFirstObjectByType<MainMenuController>(); menu.enabled = false;
        menu.GetComponent<UIDocument>().rootVisualElement.style.display = DisplayStyle.None;
        var time = FindFirstObjectByType<GameTimeManager>(); Set(time, "loadExistingSave", false);
        FindFirstObjectByType<MainMenuGameplayGate>().ReleaseGameplay();
        float deadline = Time.realtimeSinceStartup + 20f;
        while (time.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!time.IsTransitioning, "Normal day reveal completed"); time.enabled = false;
        MechanicRemovalChecks();
        // Open test ground: growth waits for a grounded, unobstructed player.
        floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "GrowthUpgradeTestGround_TEMP";
        floor.transform.position = TestAreaLayout.Origin + new Vector3(100, -.05f, 100); floor.transform.localScale = new Vector3(10, .1f, 10);
        form.SetForm(PlayerGrowthController.Form.Chick);
        var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
        player.transform.position = TestAreaLayout.Origin + new Vector3(100, .002f, 100); cc.enabled = true; Physics.SyncTransforms();
        yield return new WaitForSeconds(.4f);
        yield return StateChecks();
        form.SetForm(PlayerGrowthController.Form.Chick);
        yield return JumpChecks();
        Restore(new FarmSaveData { eatenSeedCount = 50 });
        growth.RefreshForReveal();
        renderHalfRing = true;
        yield return RenderHud("pending-half-ring");
        renderHalfRing = false;
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("GROWTH UPGRADE VERIFICATION\n" + Result);
        Cleanup();
    }
    private void Update() => keyboard?.MakeCurrent();
    private void LateUpdate() { if (renderHalfRing) root.Q<UpgradeHoldIndicator>().Progress = .5f; }
    private IEnumerator RenderHud(string name)
    {
        var panel = growth.GetComponent<UIDocument>().panelSettings;
        var previous = panel.targetTexture;
        var target = new RenderTexture(1920, 1080, 24); target.Create(); panel.targetTexture = target;
        // A changed render target needs a panel layout/repaint cycle before capture.
        yield return null; yield return new WaitForSecondsRealtime(.35f);
        root.Q<GrowthProgressBarElement>().MarkDirtyRepaint();
        root.Q<UpgradeHoldIndicator>().MarkDirtyRepaint();
        yield return null; yield return new WaitForEndOfFrame();
        var active = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
        File.WriteAllBytes("Temp/GrowthUpgradeReview/" + name + ".png", image.EncodeToPNG());
        Check(root.Q("UpgradeNotificationRoot").worldBound.yMin >= root.Q("GrowthBarRoot").worldBound.yMax,
            "Notification is below the existing bar: " + name);
        RenderTexture.active = active; panel.targetTexture = previous;
        target.Release(); Destroy(target); Destroy(image);
    }
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
