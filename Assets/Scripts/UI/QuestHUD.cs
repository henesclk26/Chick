using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Under the clock: the quests still to do, on a see-through panel (ClockHUD.uss). Each shows its title, a line of what
/// to do and how far along it is; when one is done its title is struck through, then it fades and slides away, and with
/// none left the panel fades out. A quest shows once the quests it follows are done (<see cref="Quest.after"/>), in
/// the list's order, so two can be open together and a later one can come in under an earlier one still open. The
/// chick's first quests: a seed; three kinds of seed; then the growth stages one after another (each says what the
/// chick gains with it; the fifth is turning into a chicken) and, beside the first, an ant; once the ant is eaten, a
/// mushroom. Then the chicken's: lay an egg, pick one up, take it to the basket by the barn, buy the first upgrade at
/// the barn. They count from when they show: food eaten (EdibleObject.Consumed), eggs laid, picked up and delivered
/// (GrowthProgressController), upgrades bought (PlayerUpgrades.Purchased); the growth stages read the growth bar.
/// Growing into a chicken opens the woods, the first egg the barn (FarmRegions; their gates open for good), and a
/// banner under the growth bar says so
/// for any region opened; the regions open are saved with the quests. Finished quests are kept in the farm save (FarmSaveData.completedQuests), so they do not come back;
/// what is under way in one is not (it starts again after a load). A save from before quests counts the first-seed
/// quest done if a seed had been eaten. A new game's save, written from the menu, starts with none done. Shown and
/// hidden with the clock (ClockHUDController.SetVisible).
/// </summary>
public sealed class QuestHUD : MonoBehaviour
{
    // EatSeeds: seeds eaten; SeedKinds: different kinds of seed eaten; GrowthStage: the growth bar's stage `stage`
    // filled (shown as that stage's share of the bar); BecomeChicken: the last stage filled and the chick grown into a
    // chicken; EatFood: foods of `food` (a statistic key) eaten; LayEgg, CollectEgg, DeliverEgg: eggs laid, picked up,
    // put into the basket; BuyUpgrade: upgrade levels bought
    private enum Goal { EatSeeds, SeedKinds, GrowthStage, BecomeChicken, EatFood, LayEgg, CollectEgg, DeliverEgg, BuyUpgrade }

    private struct Quest
    {
        public string id, title, description;
        public Goal goal;
        public int count;
        public string food;
        public int stage;
        public string[] after;   // shows once these are done
        public string opens;     // a region (FarmRegions) this quest opens when done
    }

    // A growth stage's share of the bar: from the stage before it up to its own threshold.
    private static int StageStart(int stage) => stage <= 1 ? 0 : GrowthProgressController.StageThresholds[stage - 2];
    private static int StageWidth(int stage) => GrowthProgressController.StageThresholds[stage - 1] - StageStart(stage);

    private static readonly Quest[] Quests =
    {
        new Quest { id = "first-seed", title = "İlk tohumu ye", description = "Yerdeki tohumlardan birini gagala.", goal = Goal.EatSeeds, count = 1 },
        new Quest { id = "seed-kinds", title = "Farklı tohumlar tat", description = "3 farklı tohum türü ye: buğday, mısır, kızıl buğday...",
            goal = Goal.SeedKinds, count = 3, after = new[] { "first-seed" } },
        // the stages' rewards as GrowthProgressController.StageUpDetails gives them
        new Quest { id = "first-stage", title = "İlk büyüme aşaması", description = "Büyüme çubuğunun 1. bölmesini doldur.\nKazanç: Gagalama hızı 0.5x → 1x",
            goal = Goal.GrowthStage, stage = 1, count = StageWidth(1), after = new[] { "seed-kinds" } },
        new Quest { id = "stage-2", title = "2. büyüme aşaması", description = "Büyüme çubuğunun 2. bölmesini doldur.\nKazanç: Koşma süresi 0.5x → 1x",
            goal = Goal.GrowthStage, stage = 2, count = StageWidth(2), after = new[] { "first-stage" } },
        new Quest { id = "stage-3", title = "3. büyüme aşaması", description = "Büyüme çubuğunun 3. bölmesini doldur.\nKazanç: Yeme menzili 0.5x → 1x",
            goal = Goal.GrowthStage, stage = 3, count = StageWidth(3), after = new[] { "stage-2" } },
        new Quest { id = "stage-4", title = "4. büyüme aşaması", description = "Büyüme çubuğunun 4. bölmesini doldur.\nKazanç: Yürüme hızı 0.75x → 1.5x",
            goal = Goal.GrowthStage, stage = 4, count = StageWidth(4), after = new[] { "stage-3" } },
        new Quest { id = "become-chicken", title = "Kümesin Yeni Tavuğu", description = "Son bölmeyi doldur, kanatlarını aç ve tavuğa dönüş!",
            goal = Goal.BecomeChicken, stage = 5, count = StageWidth(5), after = new[] { "stage-4" }, opens = FarmRegions.Woods },
        new Quest { id = "first-ant", title = "Protein Bar", description = "İlk karıncanı ye. Yuvaların çevresine bak.",
            goal = Goal.EatFood, food = "ant", count = 1, after = new[] { "seed-kinds" } },
        new Quest { id = "mushrooms", title = "Mantarları Tanı", description = "Bir mantar ye. Ağaçların ve kayaların dibinde bulunur.",
            goal = Goal.EatFood, food = EdibleMushroom.StatisticKey, count = 1, after = new[] { "first-ant" } },
        // the chicken
        new Quest { id = "first-egg", title = "Büyük An", description = "İlk yumurtanı yumurtla! Yumurta barı dolunca Z'yi basılı tut.",
            goal = Goal.LayEgg, count = 1, after = new[] { "become-chicken" }, opens = FarmRegions.Barn },
        new Quest { id = "collect-egg", title = "Hazine Avcısı", description = "Yumurtanın yanına git ve Z ile topla. Altın olanlar daha değerli!",
            goal = Goal.CollectEgg, count = 1, after = new[] { "first-egg" } },
        new Quest { id = "deliver-egg", title = "Sepete Teslim", description = "Yumurtayı ahırın yanındaki sepete götür ve Z ile bırak. Her yumurta yükseltme parası!",
            goal = Goal.DeliverEgg, count = 1, after = new[] { "collect-egg" } },
        new Quest { id = "first-upgrade", title = "Ahırın Yıldızı", description = "Ahırdaki yükseltme köşesinde TAB'a bas, kazandığın yumurtalarla ilk yükseltmeni al.",
            goal = Goal.BuyUpgrade, count = 1, after = new[] { "deliver-egg" } },
    };

    [SerializeField] private UIDocument document;
    [SerializeField] private FarmSaveSystem saveSystem;
    [SerializeField, Min(0f), Tooltip("Seconds the struck-through quest stays before it fades away.")]
    private float strikeHold = 1f;

    private sealed class Row
    {
        public VisualElement root;
        public Label count;
        public VisualElement fill;   // the progress line, absent for one-step quests
        public int progress;
        public bool finishing;
        public readonly HashSet<string> kinds = new HashSet<string>();
    }

    private readonly HashSet<string> done = new HashSet<string>();
    private readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();

    /// <summary>A quest finished while playing, with its title (the day's summary lists them).</summary>
    public static event System.Action<string> QuestFinished;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetQuestFinished() => QuestFinished = null;
    private VisualElement panel, list;
    private MainMenuGameplayGate gameplayGate;
    private VisualElement regionBanner;
    private Label regionName, regionHint;
    private IVisualElementScheduledItem hideBanner;
    [SerializeField, Min(1f), Tooltip("Seconds the \"new region\" banner stays.")]
    private float regionBannerSeconds = 4.5f;
    private GrowthProgressController growth;
    private PlayerGrowthController form;
    private PlayerUpgrades upgrades;

    private void Awake()
    {
        if (saveSystem == null) saveSystem = FindFirstObjectByType<FarmSaveSystem>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        EdibleObject.Consumed += Eaten;
        GrowthProgressController.EggCollected += EggCollected;
        GrowthProgressController.EggsDelivered += EggsDelivered;
        if (growth != null) growth.OnEggLaid += EggLaid;
        if (upgrades != null) upgrades.Purchased += Bought;
        FarmRegions.Opened += RegionOpened;
        if (saveSystem == null) return;
        saveSystem.Loaded += Restore;
        saveSystem.Saving += Capture;
    }

    private void OnDisable()
    {
        EdibleObject.Consumed -= Eaten;
        GrowthProgressController.EggCollected -= EggCollected;
        GrowthProgressController.EggsDelivered -= EggsDelivered;
        if (growth != null) growth.OnEggLaid -= EggLaid;
        if (upgrades != null) upgrades.Purchased -= Bought;
        FarmRegions.Opened -= RegionOpened;
        if (saveSystem == null) return;
        saveSystem.Loaded -= Restore;
        saveSystem.Saving -= Capture;
    }

    private void Start()
    {
        growth = FindFirstObjectByType<GrowthProgressController>(FindObjectsInactive.Include);
        upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        if (growth != null) growth.OnEggLaid += EggLaid;
        if (upgrades != null) upgrades.Purchased += Bought;
        var root = document.rootVisualElement;
        panel = root.Q("QuestHUD");
        list = root.Q("QuestList");
        regionBanner = root.Q("RegionUnlock");
        regionName = root.Q<Label>("RegionUnlockName");
        regionHint = root.Q<Label>("RegionUnlockHint");
        if (saveSystem != null && saveSystem.LastLoadedData != null) Restore(saveSystem.LastLoadedData);
        else Rebuild();
    }

    private void Capture(FarmSaveData data)
    {
        // The menu writes a new game's empty save while it is still up; what was read for its Continue button must
        // not go into that save. Only saves made while playing carry the quests done.
        if (data == null || AtMenu()) return;
        var ids = new List<string>(done);
        data.completedQuests = ids.ToArray();
        data.openRegions = FarmRegions.Capture();
    }

    // Not the menu's visibility: the pause menu shares that UIDocument, so "Kaydet ve Çık" would look like the menu.
    private bool AtMenu()
    {
        if (gameplayGate == null) gameplayGate = FindFirstObjectByType<MainMenuGameplayGate>();
        return gameplayGate != null && !gameplayGate.IsReleased;
    }

    private void Restore(FarmSaveData data)
    {
        done.Clear();
        if (data?.completedQuests != null) foreach (var id in data.completedQuests) done.Add(id);
        // saves from before quests: a seed eaten already counts
        if (data != null && data.eatenSeedCount > 0) done.Add("first-seed");
        FarmRegions.Restore(data?.openRegions);
        // a quest done opens its region, also in saves from before regions
        foreach (var q in Quests) if (q.opens != null && done.Contains(q.id)) FarmRegions.OpenQuietly(q.opens);
        Rebuild();
    }

    private void Rebuild()
    {
        if (list == null) return;
        list.Clear();
        rows.Clear();
        Open();
    }

    private bool Unlocked(Quest q)
    {
        if (q.after != null) foreach (var id in q.after) if (!done.Contains(id)) return false;
        return true;
    }

    // Adds every quest now open and not shown yet, in the list's order.
    private void Open()
    {
        if (list == null) return;
        foreach (var q in Quests)
        {
            if (done.Contains(q.id) || rows.ContainsKey(q.id) || !Unlocked(q)) continue;
            var row = MakeRow(q);
            rows[q.id] = row;
            // after the open quests that come before it in the list
            int at = 0;
            foreach (var earlier in Quests)
            {
                if (earlier.id == q.id) break;
                if (rows.TryGetValue(earlier.id, out var shown) && shown.root.parent == list) at = list.IndexOf(shown.root) + 1;
            }
            list.Insert(at, row.root);
            if (q.goal == Goal.GrowthStage || q.goal == Goal.BecomeChicken) UpdateGrowth(q, row);
        }
        bool any = rows.Count > 0;
        panel.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
        panel.EnableInClassList("empty", !any);
    }

    // the growth stage quest follows the growth bar itself (a chicken has long passed it)
    private void Update()
    {
        foreach (var q in Quests)
            if ((q.goal == Goal.GrowthStage || q.goal == Goal.BecomeChicken) && rows.TryGetValue(q.id, out var row) && !row.finishing)
                UpdateGrowth(q, row);
    }

    private void UpdateGrowth(Quest q, Row row)
    {
        if (growth == null) growth = FindFirstObjectByType<GrowthProgressController>();
        if (form == null) form = FindFirstObjectByType<PlayerGrowthController>();
        if (growth == null) return;
        bool chicken = form != null && form.CurrentForm == PlayerGrowthController.Form.Chicken;
        // this stage's share of the bar
        int progress = chicken ? q.count : Mathf.Clamp(growth.EatenSeedCount - StageStart(q.stage), 0, q.count);
        bool finished = q.goal == Goal.BecomeChicken ? chicken : progress >= q.count;
        if (progress == row.progress && !finished) return;
        row.progress = progress;
        ShowProgress(q, row);
        if (finished) Finish(q.id, row);
    }

    private Row MakeRow(Quest q)
    {
        var root = new VisualElement { pickingMode = PickingMode.Ignore };
        root.AddToClassList("quest");
        var line = new VisualElement { pickingMode = PickingMode.Ignore };
        line.AddToClassList("quest-line");
        var bullet = new VisualElement { pickingMode = PickingMode.Ignore };
        bullet.AddToClassList("quest-bullet");
        var name = new VisualElement { pickingMode = PickingMode.Ignore };
        name.AddToClassList("quest-name");
        var title = new Label(q.title) { pickingMode = PickingMode.Ignore };
        title.AddToClassList("quest-title");
        var strike = new VisualElement { pickingMode = PickingMode.Ignore };
        strike.AddToClassList("quest-strike");
        name.Add(title);
        name.Add(strike);
        var gap = new VisualElement { pickingMode = PickingMode.Ignore };
        gap.AddToClassList("quest-gap");
        var count = new Label($"0 / {q.count}") { pickingMode = PickingMode.Ignore };
        count.AddToClassList("quest-count");
        line.Add(bullet);
        line.Add(name);
        line.Add(gap);
        line.Add(count);
        var text = new Label(q.description) { pickingMode = PickingMode.Ignore };
        text.AddToClassList("quest-text");
        root.Add(line);
        root.Add(text);
        var row = new Row { root = root, count = count };
        // a one-step quest's bar would only ever be empty or full; the count says that already
        if (q.count > 1)
        {
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("quest-bar");
            row.fill = new VisualElement { pickingMode = PickingMode.Ignore };
            row.fill.AddToClassList("quest-bar-fill");
            bar.Add(row.fill);
            root.Add(bar);
        }
        ShowProgress(q, row);
        return row;
    }

    private static void ShowProgress(Quest q, Row row)
    {
        row.count.text = $"{row.progress} / {q.count}";
        if (row.fill != null) row.fill.style.width = Length.Percent(100f * row.progress / q.count);
    }

    private void Eaten(EdibleObject food)
    {
        if (food == null) return;
        bool seed = food.Category == EdibleCategory.Seed;
        string key = food.StatisticKey;
        foreach (var q in Quests)
        {
            if (done.Contains(q.id) || !rows.TryGetValue(q.id, out var row) || row.finishing) continue;
            switch (q.goal)
            {
                case Goal.EatSeeds: if (!seed) continue; row.progress++; break;
                case Goal.SeedKinds: if (!seed || !row.kinds.Add(key)) continue; row.progress = row.kinds.Count; break;
                case Goal.EatFood: if (key != q.food) continue; row.progress++; break;
                default: continue;
            }
            row.progress = Mathf.Min(row.progress, q.count);
            ShowProgress(q, row);
            if (row.progress >= q.count) Finish(q.id, row);
        }
    }

    private void EggLaid(bool golden) => Advance(Goal.LayEgg, 1);
    private void EggCollected() => Advance(Goal.CollectEgg, 1);
    private void EggsDelivered(int count) => Advance(Goal.DeliverEgg, count);
    private void Bought(string id) => Advance(Goal.BuyUpgrade, 1);

    // the open quests of this goal move on by `amount`
    private void Advance(Goal goal, int amount)
    {
        foreach (var q in Quests)
        {
            if (q.goal != goal || done.Contains(q.id) || !rows.TryGetValue(q.id, out var row) || row.finishing) continue;
            row.progress = Mathf.Min(row.progress + amount, q.count);
            ShowProgress(q, row);
            if (row.progress >= q.count) Finish(q.id, row);
        }
    }

    // "New region: ..." under the growth bar for a moment
    private void RegionOpened(string id)
    {
        if (regionBanner == null) return;
        regionName.text = FarmRegions.NameOf(id);
        regionHint.text = FarmRegions.HintOf(id);
        regionBanner.RemoveFromClassList("off");
        hideBanner?.Pause();
        hideBanner = regionBanner.schedule.Execute(() => regionBanner.AddToClassList("off")).StartingIn((long)(regionBannerSeconds*1000f));
    }

    // struck through, a moment to read it, then away
    private void Finish(string id, Row row)
    {
        done.Add(id);
        foreach (var q in Quests) if (q.id == id && q.opens != null) FarmRegions.Open(q.opens);
        foreach (var q in Quests) if (q.id == id) QuestFinished?.Invoke(q.title);
        row.finishing = true;
        row.root.AddToClassList("done");
        // what this one opens comes in at once, while it is still being struck through
        Open();
        row.root.schedule.Execute(() => row.root.AddToClassList("gone")).StartingIn(450 + (long)(strikeHold*1000f));
        row.root.schedule.Execute(() =>
        {
            row.root.RemoveFromHierarchy();
            rows.Remove(id);
            if (rows.Count == 0)
            {
                panel.AddToClassList("empty");
                panel.schedule.Execute(() => { if (rows.Count == 0) panel.style.display = DisplayStyle.None; }).StartingIn(420);
            }
        }).StartingIn(900 + (long)(strikeHold*1000f));
    }
}
