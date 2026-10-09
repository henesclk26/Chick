#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Explicit, in-memory regression checks. No save files, scene saves, or input devices.
public static class JournalSectionsVerification
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    public static string Result = "Not run";

    [MenuItem("Tools/Chick/Verify Journal Sections")]
    public static void Run()
    {
        var report = new StringBuilder();
        int failures = 0;
        void Check(bool ok, string description) { if (!ok) failures++; report.AppendLine((ok ? "PASS " : "FAIL ") + description); }
        var go = new GameObject("JournalSectionsVerification_TEMP") { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);
        try
        {
            var upgrades = go.AddComponent<PlayerUpgrades>();
            Call(upgrades, "EnsureDefaultUpgrades");
            Call(upgrades, "Restore", new FarmSaveData());
            Check(upgrades.AvailableEggs == 0 && upgrades.HelperChickCount == 0, "New game: zero currency and companions");
            string first = PlayerUpgrades.HelperUpgradeId(0, PlayerUpgrades.HelperEatSpeed);
            string second = PlayerUpgrades.HelperUpgradeId(1, PlayerUpgrades.HelperEatSpeed);
            Check(!upgrades.TryPurchase(PlayerUpgrades.FasterPeck) && !upgrades.TryPurchase(first), "No egg / unowned companion purchases rejected");
            Call(upgrades, "Restore", new FarmSaveData { whiteEggsLaid = 1000 });
            Check(!upgrades.TryPurchase(first), "Unowned companion upgrade rejected even with sufficient eggs");
            for (int i = 0; i < 3; i++) Check(upgrades.TryPurchase(PlayerUpgrades.HelperChick), "Companion purchase " + (i + 1));
            Check(!upgrades.TryPurchase(PlayerUpgrades.HelperChick), "Fourth companion rejected");
            int balance = upgrades.AvailableEggs;
            int price = upgrades.GetNextPrice(first);
            Check(upgrades.TryPurchase(first) && upgrades.AvailableEggs == balance - price, "Personal purchase spends exact price");
            Check(upgrades.GetLevel(first) == 1 && upgrades.GetLevel(second) == 0 &&
                upgrades.GetHelperEatSpeedFraction(0) > upgrades.GetHelperEatSpeedFraction(1), "Only selected companion gets faster pecking");
            Check(!upgrades.TryPurchase("helper/3/helper-range") && !upgrades.TryPurchase("unknown"), "Invalid upgrade IDs rejected");
            string move = PlayerUpgrades.HelperUpgradeId(1, PlayerUpgrades.HelperMoveSpeed);
            Check(upgrades.TryPurchase(move) && upgrades.GetHelperMoveMultiplier(1) > upgrades.GetHelperMoveMultiplier(0), "Personal movement upgrade");
            string climb = PlayerUpgrades.HelperUpgradeId(2, PlayerUpgrades.HelperClimb);
            Check(upgrades.TryPurchase(climb) && upgrades.GetHelperClimbBonus(2) > 0 && upgrades.GetHelperClimbBonus(0) == 0, "Personal obstacle upgrade");
            Check(upgrades.TryPurchase(PlayerUpgrades.DoubleCollect) && upgrades.DoubleCollectChance > 0, "Double collection purchase activates chance");
            Check(upgrades.TryPurchase(PlayerUpgrades.DoubleJump) && upgrades.DoubleJumpUnlocked, "Double jump purchase activates ability");
            Check(upgrades.TryPurchase(PlayerUpgrades.Glide) && upgrades.GlideUnlocked, "Gliding purchase activates ability");
            var save = new FarmSaveData(); Call(upgrades, "Capture", save);
            Call(upgrades, "Restore", new FarmSaveData());
            Call(upgrades, "Restore", JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(save)));
            Check(upgrades.GetLevel(first) == 1 && upgrades.GetLevel(second) == 0 && upgrades.GetLevel(move) == 1, "Save round trip preserves personal levels");
            Call(upgrades, "Restore", new FarmSaveData { upgradeLevels = new[] {
                new UpgradeLevelSaveEntry { id = PlayerUpgrades.HelperChick, level = 2 },
                new UpgradeLevelSaveEntry { id = PlayerUpgrades.HelperEatSpeed, level = 1 } } });
            Check(upgrades.GetLevel(first) == 1 && upgrades.GetLevel(second) == 1 &&
                upgrades.GetHelperLevel(2, PlayerUpgrades.HelperEatSpeed) == 0, "Legacy flock purchase migrates only to previously owned chicks");

            var ui = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Journal/GameplayJournal.uxml").CloneTree();
            var controller = go.AddComponent<GameplayJournalController>();
            void Set(string field, object value) => typeof(GameplayJournalController).GetField(field, Private).SetValue(controller, value);
            Set("root", ui); Set("upgrades", upgrades); Set("upgradesPage", ui.Q("upgrades-page")); Set("upgradesStatus", ui.Q<Label>("upgrades-status"));
            GameplayJournalReferenceTheme.Apply(ui);
            Call(controller, "BuildUpgradeSections");
            upgrades.Changed += () => Call(controller, "RefreshUpgrades");
            Call(upgrades, "Restore", new FarmSaveData());
            Call(controller, "RefreshUpgrades");
            Check(ui.Query(className: "level-filled").ToList().Count == 0, "All level eggs WHITE before any upgrade");
            Check(ui.Q("chicken-section") != null && ui.Q("companions-section") != null && ui.Q("abilities-section") != null, "All three sections exist");
            Check(ui.Q("chicken-upgrade-grid").childCount == 5, "Chicken page: five cards in two-column grid");
            Check(ui.Q<Button>("companion-slot-0").enabledSelf && !ui.Q<Button>("companion-slot-1").enabledSelf, "Unowned roster sequential purchase gating");
            Call(upgrades, "Restore", new FarmSaveData { whiteEggsLaid = 400 });
            void Click(string name) {
                var clickable = ui.Q<Button>(name).clickable;
                typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Invoke(clickable, new object[] { null, 0 });
            }
            Click("companions-section-tab");
            Check(!ui.Q("companions-section").ClassListContains("hidden") && ui.Q("chicken-section").ClassListContains("hidden"), "Companion tab real click callback");
            Click("companion-slot-0"); Click("companion-slot-1");
            Check(upgrades.HelperChickCount == 2 && ui.Q<Button>("companion-slot-2").ClassListContains("companion-locked"), "Roster buttons purchase two chicks and leave third faded");
            Click("helper-move-price");
            Check(upgrades.GetHelperLevel(1, PlayerUpgrades.HelperMoveSpeed) == 1 && upgrades.GetHelperLevel(0, PlayerUpgrades.HelperMoveSpeed) == 0,
                "Upgrade button targets selected Civciv 2 only");
            Check(ui.Q("helper-move-levels").Query(className: "level-filled").ToList().Count == 1, "Exactly one purchased level turns GOLD");
            Click("companion-slot-0");
            Check(ui.Q("helper-move-levels").Query(className: "level-filled").ToList().Count == 0, "Selecting untouched chick restores WHITE pips");
            Click("abilities-section-tab"); Check(!ui.Q("abilities-section").ClassListContains("hidden"), "Abilities tab callback");
            Click("chicken-section-tab"); Check(!ui.Q("chicken-section").ClassListContains("hidden"), "Chicken tab callback");
        }
        catch (Exception e) { failures++; report.AppendLine("FAIL " + e); }
        finally { UnityEngine.Object.DestroyImmediate(go); }
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("JOURNAL SECTIONS VERIFICATION\n" + Result);
    }

    [MenuItem("Tools/Chick/Preview Journal Chicken")]
    public static void PreviewChicken() => Preview(0);
    [MenuItem("Tools/Chick/Preview Journal Companions")]
    public static void PreviewCompanions() => Preview(1);
    [MenuItem("Tools/Chick/Preview Journal Abilities")]
    public static void PreviewAbilities() => Preview(2);

    [MenuItem("Tools/Chick/Configure Journal Art")]
    public static void ConfigureArt()
    {
        foreach (string name in new[] { "ChickenAtlas", "CompanionAtlas", "AbilitiesAtlas", "ButtonGrain" })
        {
            var importer = AssetImporter.GetAtPath("Assets/Resources/Journal/" + name + ".png") as TextureImporter;
            if (importer == null) continue;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
    }

    [MenuItem("Tools/Chick/Verify Live Journal Buttons")]
    public static void VerifyLiveButtons()
    {
        if (!EditorApplication.isPlaying || UnityEngine.Object.FindFirstObjectByType<MainMenuGameplayGate>().IsReleased)
        { Debug.LogWarning("Live verification requires a temporary Play Mode session still at the main menu."); return; }
        var journal = UnityEngine.Object.FindFirstObjectByType<GameplayJournalController>();
        var upgrades = UnityEngine.Object.FindFirstObjectByType<PlayerUpgrades>();
        var root = journal.GetComponent<UIDocument>().rootVisualElement;
        int failures = 0;
        var report = new StringBuilder();
        void Check(bool ok, string description) { if (!ok) failures++; report.AppendLine((ok ? "PASS " : "FAIL ") + description); }
        void Click(string name) {
            var clickable = root.Q<Button>(name).clickable;
            typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(clickable, new object[] { null, 0 });
        }
        try
        {
            Call(upgrades, "Restore", new FarmSaveData { whiteEggsLaid = 1000 });
            journal.Open();
            Check(root.Query(className: "level-filled").ToList().Count == 0, "Live menu starts with no gold eggs");
            string[] prefixes = { "peck", "range", "sprint-speed", "sprint-duration", "double-collect", "double-jump", "glide" };
            string[] ids = { PlayerUpgrades.FasterPeck, PlayerUpgrades.CollectRange, PlayerUpgrades.SprintSpeed, PlayerUpgrades.SprintDuration, PlayerUpgrades.DoubleCollect, PlayerUpgrades.DoubleJump, PlayerUpgrades.Glide };
            for (int i = 0; i < prefixes.Length; i++)
            {
                int cost = upgrades.GetNextPrice(ids[i]), before = upgrades.AvailableEggs;
                journal.ShowUpgradeSection(i < 5 ? 0 : 2);
                Click(prefixes[i] + "-price");
                Check(upgrades.GetLevel(ids[i]) == 1 && upgrades.AvailableEggs == before - cost &&
                    root.Q(prefixes[i] + "-levels").Query(className: "level-filled").ToList().Count == 1, "Live callback and gold pip: " + prefixes[i]);
            }
            Check(root.Q<Label>("double-collect-cost").text == upgrades.GetNextPrice(PlayerUpgrades.DoubleCollect).ToString(), "Double collection price advances after purchase");
            Check(root.Q<Label>("double-jump-action").text == "Kullanımda" && !root.Q<Button>("double-jump-price").enabledSelf, "Owned ability cannot charge again");
            Check(root.Q("companions-section-tab").style.display == DisplayStyle.None, "Barn menu has no helper chick tab");
            journal.Close(); journal.Open(UpgradeStation.Kind.Coop);
            Check(!root.Q("first-chick-section").ClassListContains("hidden") && root.Q("upgrade-section-tabs").style.display == DisplayStyle.None, "Coop without chicks shows only the first chick purchase");
            Click("helper-price");
            Check(upgrades.HelperChickCount == 1 && !root.Q("companions-section").ClassListContains("hidden"), "Buying the first chick opens the helper chick upgrades");
            Click("companion-slot-1");
            Click("helper-speed-price"); Click("helper-range-price"); Click("helper-move-price"); Click("helper-climb-price");
            Check(upgrades.GetHelperLevel(1, PlayerUpgrades.HelperEatSpeed) == 1 && upgrades.GetHelperLevel(0, PlayerUpgrades.HelperEatSpeed) == 0, "Live Civciv 2 upgrades never affect Civciv 1");
            Click("shop-tab"); Check(!root.Q("shop-page").ClassListContains("hidden"), "Shop navigation retained");
            Click("statistics-tab"); Check(!root.Q("statistics-page").ClassListContains("hidden"), "Statistics navigation retained");
            Click("upgrades-tab"); Check(!root.Q("upgrades-page").ClassListContains("hidden"), "Return to upgrades");
            Click("journal-close"); Check(!GameplayJournalController.IsAnyOpen && Time.timeScale > 0, "Close restores time and gameplay state");
            journal.Open(); Check(root.Q<Button>("chicken-section-tab").ClassListContains("section-active"), "Every Tab opening defaults to Chicken upgrades");
        }
        catch (Exception e) { failures++; report.AppendLine("FAIL " + e); }
        finally { journal.Close(); Call(upgrades, "Restore", new FarmSaveData()); }
        Debug.Log("LIVE JOURNAL VERIFICATION " + (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report);
    }
    private static void Preview(int section)
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("Journal preview requires Play Mode; never writes saves."); return; }
        var journal = UnityEngine.Object.FindFirstObjectByType<GameplayJournalController>();
        var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
        if (menu != null) menu.GetComponent<UIDocument>().rootVisualElement.style.display = DisplayStyle.None;
        // Main menu gate stays closed, so day progression and autosave cannot run.
        var upgrades = UnityEngine.Object.FindFirstObjectByType<PlayerUpgrades>();
        Call(upgrades, "Restore", new FarmSaveData { whiteEggsLaid = 170, upgradeLevels = new[] {
            new UpgradeLevelSaveEntry { id = PlayerUpgrades.HelperChick, level = 2 } } });
        journal.Open(); journal.ShowUpgradeSection(section);
        journal.GetComponent<UIDocument>().rootVisualElement.style.visibility = Visibility.Visible;
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }
    [MenuItem("Tools/Chick/Capture Journal Preview")]
    public static void CapturePreview()
    {
        if (!EditorApplication.isPlaying) return;
        var journal = UnityEngine.Object.FindFirstObjectByType<GameplayJournalController>();
        var root = journal.GetComponent<UIDocument>().rootVisualElement;
        string tab = root.Query<Button>(className: "section-active").First()?.name ?? "unknown";
        // Project-relative and git-ignored, so the capture works on every machine.
        System.IO.Directory.CreateDirectory("Captures/JournalPreview");
        string output = "Captures/JournalPreview/" + tab + "-runtime.png";
        ScreenCapture.CaptureScreenshot(output);
        var metrics = root.Query<VisualElement>(className: "upgrade-card").ToList()
            .Where(c => c.worldBound.width > 0 && c.resolvedStyle.display != DisplayStyle.None)
            .Select(c => c.Q<Label>(className: "upgrade-name")?.text + " " + c.worldBound);
        Debug.Log("JOURNAL PREVIEW " + output + "\n" + string.Join("\n", metrics));
    }
}
#endif
