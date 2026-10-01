#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Opt-in runtime verification. Never attached to a saved scene or writes the player's save.
public sealed class EggLayingPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly StringBuilder report = new();
    private GrowthProgressController growth;
    private PlayerGrowthController form;
    private PlayerUpgrades wallet;
    private ChickPlayerController player;
    private VisualElement root;
    private int failures;
    private bool originalBackground;
    private PanelSettings panel;
    private RenderTexture originalTarget, target;
    private bool previewPickup;

    private void LateUpdate()
    {
        if (previewPickup) Call(growth, "HandleEggPickup", false, true);
    }

    private static object Call(object obj, string method, params object[] args) =>
        obj.GetType().GetMethod(method, Private).Invoke(obj, args);
    private static void Set(object obj, string name, object value) =>
        obj.GetType().GetField(name, Private).SetValue(obj, value);
    private void Check(bool ok, string message)
    {
        if (!ok) failures++;
        report.AppendLine((ok ? "PASS " : "FAIL ") + message);
        Result = "Running\n" + report;
    }
    private void Tick(float dt, bool pressed = false, bool held = false, bool available = true)
    {
        Call(growth, "TickUpgrade", dt, pressed, held, available);
        Call(growth, "PaintUpgradeUI");
        growth.RefreshForReveal();
    }
    private void Restore(FarmSaveData data) => Call(growth, "Restore", data);
    private FarmSaveData Cycle(int count, bool golden) => new FarmSaveData
    {
        eatenSeedCount = 50, eggLayingVersion = 1, eggCycleSelected = true,
        eggCycleSeedCount = count, nextEggGolden = golden
    };
    private bool Visible() => root.Q("GrowthProgressHUD").style.display.value == DisplayStyle.Flex;
    private void Eat()
    {
        var seed = Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/WheatGrain.prefab"));
        seed.transform.position = new Vector3(1000, -1000, 1000);
        seed.GetComponent<EdibleObject>().Consume();
        Destroy(seed);
    }

    private void Start()
    {
        originalBackground = Application.runInBackground;
        Application.runInBackground = true;
        Result = "Running";
        StartCoroutine(RunGuarded());
    }
    private IEnumerator RunGuarded()
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(Run());
        while (stack.Count > 0)
        {
            var current = stack.Peek();
            bool more;
            object next = null;
            try { more = current.MoveNext(); if (more) next = current.Current; }
            catch (Exception e) { Check(false, "Exception: " + e); break; }
            if (!more) stack.Pop();
            else if (next is IEnumerator nested) stack.Push(nested);
            else yield return next;
        }
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Directory.CreateDirectory("Temp/EggLayingReview");
        File.WriteAllText("Temp/EggLayingReview/verification.txt", Result);
        Debug.Log("EGG LAYING VERIFICATION\n" + Result);
        Cleanup();
    }

    private IEnumerator Run()
    {
        growth = FindFirstObjectByType<GrowthProgressController>();
        form = FindFirstObjectByType<PlayerGrowthController>();
        wallet = FindFirstObjectByType<PlayerUpgrades>();
        root = growth.GetComponent<UIDocument>().rootVisualElement;
        var menu = FindFirstObjectByType<MainMenuController>();
        menu.enabled = false;
        menu.GetComponent<UIDocument>().rootVisualElement.style.display = DisplayStyle.None;
        var time = FindFirstObjectByType<GameTimeManager>();
        Set(time, "loadExistingSave", false);
        FindFirstObjectByType<MainMenuGameplayGate>().ReleaseGameplay();
        float deadline = Time.realtimeSinceStartup + 20f;
        while (time.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!time.IsTransitioning, "Gameplay reveal completed");
        time.enabled = false; // no autosave/day rollover during verification
        player = form.GetComponent<ChickPlayerController>();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Temporary egg verification floor";
        floor.transform.position = TestAreaLayout.Origin + new Vector3(100, -.05f, 100);
        floor.transform.localScale = new Vector3(10, .1f, 10);
        player.TeleportTo(floor.transform.position + Vector3.up * .055f, Quaternion.identity);
        yield return new WaitForSecondsRealtime(.5f);
        Call(wallet, "Restore", new FarmSaveData());
        var statistics = FindFirstObjectByType<FoodStatisticsTracker>();
        int foodBefore = statistics.TotalPoints;
        form.SetForm(PlayerGrowthController.Form.Chick);
        Restore(new FarmSaveData());
        Eat(); Tick(.01f);
        Check(Visible() && growth.EatenSeedCount == 1 && growth.EggSeedCount == 0,
            "Chick seeds still fill only the original growth bar");
        Check(statistics.TotalPoints == foodBefore + 1 && wallet.AvailableEggs == 0,
            "Food statistics increase without creating any spendable eggs");
        Restore(new FarmSaveData { eatenSeedCount = 50 });
        Tick(.01f);
        Check(root.Q<Label>("UpgradeText").text == "GELİŞTİR", "Chick retains Z GELİŞTİR");
        Tick(1f, true, true);
        Check(form.GrowthPending, "Original one-second hold still requests chicken growth");
        form.SetForm(PlayerGrowthController.Form.Chicken);
        Tick(.01f);
        yield return new WaitForSecondsRealtime(.5f);
        Check(player.CanChangeForm, "Chicken is grounded and ready to sit");
        Check(Visible() && growth.EggProgress == 0f && growth.EatenSeedCount == 50,
            "Chicken starts its own empty bar, preserving chick progress");

        Restore(Cycle(0, false));
        for (int i = 0; i < 49; i++) Eat();
        Check(growth.EggSeedCount == 49 && wallet.AvailableEggs == 0,
            "Eating 49 seeds fills the laying bar but leaves the wallet at zero");
        bool selected = growth.NextEggIsGolden;
        Eat(); Tick(.01f);
        Check(growth.EggSeedCount == 50 && !selected && !growth.NextEggIsGolden,
            "50th seed fills egg bar without rerolling white reward");
        Check(growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.GrowthReadyMessage,
            "Full bar displays egg-ready notification, no automatic reward");
        Eat(); Eat();
        Check(growth.EggSeedCount == 50 && wallet.WhiteEggsLaid == 0 && wallet.AvailableEggs == 0,
            "Full bar and extra seeds award zero eggs until a world egg is collected");
        Tick(1.6f);
        Check(root.Q<Label>("UpgradeText").text == "YUMURTLA" &&
            root.Q<Image>("RightIcon").sprite.texture.name == "Egg", "White egg icon and Z YUMURTLA prompt match");
        Tick(.4f, true, true);
        player.ActiveAnimator.Update(.4f);
        Check(player.EggLayingPose && player.ActiveAnimator.GetBool("to_crouch") &&
            (player.ActiveAnimator.GetCurrentAnimatorStateInfo(0).IsName("crouch") ||
             player.ActiveAnimator.GetNextAnimatorStateInfo(0).IsName("crouch")), "Z hold uses the existing C crouch animation");
        Vector3 sitPosition = player.transform.position;
        Call(player, "Update");
        Check((player.transform.position - sitPosition).sqrMagnitude < .001f && !player.CanChangeForm,
            "Laying locks movement and form changes");
        Tick(.1f);
        Check(!player.EggLayingPose && LaidEggPresentation.ActiveEggCount == 0, "Early release stands up without creating an egg");
        Check(wallet.WhiteEggsLaid == 0 && growth.UpgradeHoldProgress == 0f, "Releasing Z early grants nothing");
        Tick(.4f, true, true); Tick(.01f, false, true, false);
        Tick(1.1f, true, true);
        Check(wallet.WhiteEggsLaid == 0, "Unavailable gameplay cancels hold and requires Z release");
        Tick(.01f);
        int balance = wallet.AvailableEggs;
        Vector3 footprint = player.transform.position;
        Tick(1f, true, true);
        var white = LaidEggPresentation.FindNearest(player.transform, .75f);
        Check(white != null && !white.IsGolden && wallet.AvailableEggs == balance && wallet.WhiteEggsLaid == 0,
            "Laying creates a white world egg without awarding currency");
        Check(white != null && Vector2.Distance(new Vector2(white.GroundPosition.x, white.GroundPosition.z),
            new Vector2(footprint.x, footprint.z)) < .001f && !player.EggLayingPose,
            "Chicken stands and leaves the egg at its exact footprint");
        Check(growth.EggProgress == 0f && growth.CurrentUpgradeState == GrowthProgressController.UpgradeState.EggLaidMessage,
            "Claim resets the bar immediately and shows reward feedback");
        Tick(4f, false, true); Tick(1f, true, true);
        Check(LaidEggPresentation.ActiveEggCount == 1 && wallet.WhiteEggsLaid == 0, "Continued Z holding cannot duplicate or auto-collect");
        yield return new WaitForSecondsRealtime(2f);
        Check(white != null && white.isActiveAndEnabled, "Egg remains after the old disappearance deadline");
        player.TeleportTo(footprint + Vector3.right * 2f, Quaternion.identity);
        Check(LaidEggPresentation.FindNearest(player.transform, .75f) == null && !white.TryCollect(player.transform, wallet, .75f),
            "Distant egg cannot be collected");
        player.TeleportTo(footprint, Quaternion.identity);
        Tick(.01f);
        Call(growth, "HandleEggPickup", false, true);
        var pickup = root.Q("EggPickupPrompt");
        Check(pickup.Q<Label>("EggPickupText").text == "TOPLA" &&
            pickup.Q<Label>(className: "interaction-key").text == "Z" &&
            pickup.Query<Label>().ToList().Count == 2 && pickup.Q<UpgradeHoldIndicator>() == null &&
            root.Q("EggPickupRoot").style.display.value == DisplayStyle.Flex,
            "Pickup shows only Z TOPLA in a static key frame with no hold-progress indicator");
        previewPickup = true;
        yield return Capture("pickup-prompt", 1280, 720);
        previewPickup = false;
        Tick(.01f);
        bool handled = (bool)Call(growth, "HandleEggPickup", true, true);
        Check(handled && wallet.WhiteEggsLaid == 1 && wallet.AvailableEggs == balance + 1 &&
            LaidEggPresentation.ActiveEggCount == 0 && !white.TryCollect(player.transform, wallet, .75f),
            "White pickup grants exactly +1 once and removes only the collected egg");
        yield return new WaitForSecondsRealtime(.4f);

        Restore(Cycle(50, true));
        var save = new FarmSaveData();
        Call(growth, "Capture", save); Call(wallet, "Capture", save);
        save = JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(save));
        Restore(null); Call(wallet, "Restore", new FarmSaveData());
        Restore(save); Call(wallet, "Restore", save); Tick(.01f);
        Check(growth.EggSeedCount == 50 && growth.NextEggIsGolden && wallet.WhiteEggsLaid == 1,
            "JSON save/load preserves full pending gold egg and wallet");
        form.SetForm(PlayerGrowthController.Form.Chick); Tick(.01f);
        Check(!root.Q("GrowthProgressHUD").ClassListContains("egg-laying") && growth.Progress == 1f,
            "Switching to chick restores its own bar and hides egg art");
        form.SetForm(PlayerGrowthController.Form.Chicken); Tick(.01f);
        Check(growth.NextEggIsGolden && growth.EggProgress == 1f,
            "Switching back to chicken preserves pending outcome and progress");
        balance = wallet.AvailableEggs;
        Tick(1f, true, true);
        var golden = LaidEggPresentation.FindNearest(player.transform, .75f);
        Check(golden != null && golden.IsGolden && wallet.AvailableEggs == balance, "Golden egg also waits for manual pickup");
        var material = golden != null ? golden.GetComponentInChildren<Renderer>().sharedMaterial : null;
        Check(material != null && material.shader.name == "Universal Render Pipeline/Lit" &&
            material.GetFloat("_Metallic") >= .65f && material.GetFloat("_Metallic") <= .85f &&
            material.GetFloat("_Smoothness") >= .45f && material.GetFloat("_Smoothness") <= .65f,
            "Golden egg has restrained metal highlights between matte paint and mirror polish");
        var eggMesh = golden != null ? golden.GetComponent<MeshFilter>()?.sharedMesh : null;
        Check(eggMesh != null && eggMesh.triangles.Length / 3 == 1792 && golden.GetComponent<SkinnedMeshRenderer>() == null,
            "Laid eggs use a prebuilt softened static mesh instead of the 112-triangle skinned model");
        Check(eggMesh != null && eggMesh.bounds.size.x / eggMesh.bounds.size.y > .65f &&
            eggMesh.bounds.size.x / eggMesh.bounds.size.y < .73f &&
            Mathf.Abs(golden.GetComponent<Renderer>().bounds.size.y - .136f) < .001f,
            "Egg has an elongated ovoid profile and a 15-percent smaller world height");
        save = new FarmSaveData(); Call(growth, "Capture", save); Call(wallet, "Capture", save);
        save = JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(save));
        string id = save.groundEggs.Length == 1 ? save.groundEggs[0].id : null;
        Restore(save); Restore(save);
        var savedAgain = LaidEggPresentation.CaptureAll();
        Check(savedAgain.Length == 1 && savedAgain[0].id == id && savedAgain[0].golden &&
            savedAgain[0].position == save.groundEggs[0].position, "Save/load preserves uncollected egg identity, type and position without duplication");
        golden = LaidEggPresentation.FindNearest(player.transform, .75f);
        CaptureEgg(golden, "gold-model");
        var whitePreview = LaidEggPresentation.Lay(player.transform, false);
        var shellMaterial = whitePreview.GetComponent<Renderer>().sharedMaterial;
        Check(shellMaterial.name == "WhiteEgg" && shellMaterial.shader.name == "Universal Render Pipeline/Lit" &&
            shellMaterial.GetFloat("_Metallic") == 0f && shellMaterial.GetFloat("_Smoothness") <= .2f &&
            Mathf.Abs(whitePreview.GetComponent<Renderer>().bounds.size.y - .136f) < .001f,
            "White shell uses a dedicated matte off-white material rather than the banded vendor shader");
        CaptureEgg(whitePreview, "white-model");
        whitePreview.gameObject.SetActive(false); Destroy(whitePreview.gameObject);
        Tick(.01f);
        Check((bool)Call(growth, "HandleEggPickup", true, true) && wallet.GoldenEggsLaid == 1 &&
            wallet.WhiteEggsLaid == 1 && wallet.AvailableEggs == balance + 5, "Golden pickup grants +5 to the same Tab egg balance");
        Check(LaidEggPresentation.CaptureAll().Length == 0, "Collected eggs are excluded from future saves");
        var journal = FindFirstObjectByType<GameplayJournalController>(FindObjectsInactive.Include);
        var balanceLabel = journal.GetComponent<UIDocument>().rootVisualElement.Q<Label>("points-label");
        Check(balanceLabel != null && balanceLabel.text.Contains(wallet.AvailableEggs.ToString()), "Tab menu balance refreshes immediately after collection");
        Restore(Cycle(23, false));
        save = new FarmSaveData(); Call(growth, "Capture", save); Call(wallet, "Capture", save);
        Restore(null); Restore(JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(save)));
        Check(growth.EggSeedCount == 23 && !growth.NextEggIsGolden && save.goldenEggsLaid == 1,
            "Partial cycle and both laid egg counts survive serialization");
        Restore(new FarmSaveData { eatenSeedCount = 17 });
        Check(growth.EggSeedCount == 0 && growth.EatenSeedCount == 17, "Legacy saves initialize a fresh egg cycle");
        Check((float)typeof(GrowthProgressController).GetField("goldenEggChance", Private).GetValue(growth) == .5f,
            "Default gold chance is 50 percent");
        var randomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(731);
        int gold = 0;
        for (int i = 0; i < 400; i++)
        {
            Set(growth, "eggCycleSelected", false); Call(growth, "EnsureEggCycle");
            if (growth.NextEggIsGolden) gold++;
        }
        UnityEngine.Random.state = randomState;
        Check(gold > 150 && gold < 250, "Seeded distribution exercises both rewards: " + gold + "/400 gold");
        Call(wallet, "Restore", new FarmSaveData { spentEggs = 500,
            upgradeLevels = new[] { new UpgradeLevelSaveEntry { id = PlayerUpgrades.FasterPeck, level = 1 } } });
        Check(wallet.GetLevel(PlayerUpgrades.FasterPeck) == 1 && wallet.AvailableEggs == 0 &&
            wallet.TryCollectEgg(false) && wallet.AvailableEggs == 1,
            "Legacy food-funded upgrades remain owned without debt swallowing new egg pickups");
        var migrated = new FarmSaveData(); Call(wallet, "Capture", migrated);
        Call(wallet, "Restore", JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(migrated)));
        Check(wallet.AvailableEggs == 1, "Migrated egg wallet stays stable across save/load");

        Restore(Cycle(50, false)); Tick(.01f);
        yield return Capture("white-ready", 1920, 1080);
        Restore(Cycle(50, true)); Tick(.01f);
        yield return Capture("gold-ready", 1920, 1080);
        Restore(Cycle(25, true)); Tick(.01f);
        yield return Capture("gold-half-small", 1280, 720);
        form.SetForm(PlayerGrowthController.Form.Chick);
        Restore(new FarmSaveData { eatenSeedCount = 25 }); Tick(.01f);
        yield return Capture("chick-unchanged", 1920, 1080);
    }

    private IEnumerator Capture(string name, int width, int height)
    {
        panel = growth.GetComponent<UIDocument>().panelSettings;
        originalTarget = panel.targetTexture;
        target = new RenderTexture(width, height, 24); target.Create();
        panel.targetTexture = target;
        yield return null; yield return new WaitForSecondsRealtime(.35f);
        yield return new WaitForEndOfFrame();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        Directory.CreateDirectory("Temp/EggLayingReview");
        File.WriteAllBytes("Temp/EggLayingReview/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = previous;
        var icon = root.Q<Image>("RightIcon");
        bool chicken = form.CurrentForm == PlayerGrowthController.Form.Chicken;
        Check(chicken ? icon.resolvedStyle.display == DisplayStyle.Flex && icon.worldBound.xMax <= root.worldBound.xMax
            : icon.resolvedStyle.display == DisplayStyle.None, name + ": endpoint visibility and screen bounds");
        Check(root.Q<GrowthProgressBarElement>().HasArtwork, name + ": original bar artwork intact");
        panel.targetTexture = originalTarget;
        target.Release(); Destroy(target); target = null; Destroy(image);
    }
    private void CaptureEgg(LaidEggPresentation egg, string name)
    {
        if (egg == null) return;
        var transforms = egg.GetComponentsInChildren<Transform>(true);
        var layers = new int[transforms.Length];
        for (int i = 0; i < transforms.Length; i++) { layers[i] = transforms[i].gameObject.layer; transforms[i].gameObject.layer = 31; }
        var cameraObject = new GameObject("Temporary egg preview camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false; camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .1f);
        camera.orthographic = true; camera.orthographicSize = .105f; camera.nearClipPlane = .01f;
        var bounds = egg.GetComponentInChildren<Renderer>().bounds;
        camera.transform.position = bounds.center + new Vector3(.2f, .09f, .3f);
        camera.transform.LookAt(bounds.center);
        var rt = new RenderTexture(384, 384, 24); rt.Create(); camera.targetTexture = rt;
        camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(384, 384, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, 384, 384), 0, 0); image.Apply();
        Directory.CreateDirectory("Temp/EggLayingReview");
        File.WriteAllBytes("Temp/EggLayingReview/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        rt.Release(); Destroy(rt); Destroy(image); Destroy(cameraObject);
        for (int i = 0; i < transforms.Length; i++) transforms[i].gameObject.layer = layers[i];
    }
    private void Cleanup()
    {
        if (panel != null) panel.targetTexture = originalTarget;
        if (target != null) { target.Release(); Destroy(target); target = null; }
        Application.runInBackground = originalBackground;
    }
    private void OnDestroy() => Cleanup();
}
#endif
