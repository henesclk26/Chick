using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// What the day brought, shown when it ends (GameTimeManager.EndDay, before the screen goes black): the evening is
/// dimmed and a card lists the food eaten, the growth made (and a stage reached), the eggs laid, the gold earned at the
/// basket, the quests finished, the regions opened and the upgrades bought that day. It waits for a key or a click
/// (after a moment, so a key still held from playing does not skip it), or goes on by itself after a while. The counts
/// start again each morning.
/// The card is in DayTransition.uxml (#DaySummary), styled by DayTransition.uss.
/// </summary>
[DisallowMultipleComponent]
public sealed class DaySummary : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    [SerializeField, Min(.05f)] private float fadeSeconds = .35f;
    [Tooltip("Keys and clicks are ignored this long after the card appears.")]
    [SerializeField, Min(0f)] private float minimumSeconds = 1.2f;
    [Tooltip("The card goes on by itself after this long.")]
    [SerializeField, Min(1f)] private float autoContinueSeconds = 20f;

    private GrowthProgressController growth;
    private PlayerGrowthController form;
    private PlayerUpgrades upgrades;
    private GameTimeManager time;
    private VisualElement root, rows;
    private Label title;

    private int foods, eggs, goldenEggs, purchases, startPoints, startStage, startGold;
    private readonly List<string> questsDone = new List<string>();
    private readonly List<string> regionsOpened = new List<string>();

    private void OnEnable()
    {
        if (document == null) document = GetComponent<UIDocument>();
        growth = FindFirstObjectByType<GrowthProgressController>(FindObjectsInactive.Include);
        form = FindFirstObjectByType<PlayerGrowthController>(FindObjectsInactive.Include);
        upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        time = FindFirstObjectByType<GameTimeManager>(FindObjectsInactive.Include);
        EdibleObject.Consumed += Eaten;
        QuestHUD.QuestFinished += QuestDone;
        FarmRegions.Opened += RegionOpened;
        if (growth != null) growth.OnEggLaid += EggLaid;
        if (upgrades != null) upgrades.Purchased += Bought;
        if (time != null) time.OnDayStarted += DayStarted;
        StartDay();
    }

    private void OnDisable()
    {
        EdibleObject.Consumed -= Eaten;
        QuestHUD.QuestFinished -= QuestDone;
        FarmRegions.Opened -= RegionOpened;
        if (growth != null) growth.OnEggLaid -= EggLaid;
        if (upgrades != null) upgrades.Purchased -= Bought;
        if (time != null) time.OnDayStarted -= DayStarted;
    }

    private void Eaten(EdibleObject food) => foods++;
    private void QuestDone(string questTitle) => questsDone.Add(questTitle);
    private void RegionOpened(string id) => regionsOpened.Add(FarmRegions.NameOf(id));
    private void Bought(string id) => purchases++;
    private void EggLaid(bool golden)
    {
        eggs++;
        if (golden) goldenEggs++;
    }
    private void DayStarted(int day) => StartDay();

    private int Gold => upgrades != null ? upgrades.WhiteEggsLaid + 5 * upgrades.GoldenEggsLaid : 0;

    // The counts start again; growth and gold are measured from where they stand now.
    private void StartDay()
    {
        foods = eggs = goldenEggs = purchases = 0;
        questsDone.Clear();
        regionsOpened.Clear();
        startPoints = growth != null ? growth.EatenSeedCount : 0;
        startStage = growth != null ? growth.ChickStage : 0;
        startGold = Gold;
    }

    /// <summary>Shows the card for the day that is ending, until a key or click (or the time runs out).</summary>
    public IEnumerator Show(int day)
    {
        if (!Bind()) yield break;
        Fill(day);
        root.style.display = DisplayStyle.Flex;
        yield return Fade(0f, 1f);
        float shown = 0f;
        while (shown < autoContinueSeconds)
        {
            shown += Time.unscaledDeltaTime;
            if (shown >= minimumSeconds && Pressed()) break;
            yield return null;
        }
        yield return Fade(1f, 0f);
        root.style.display = DisplayStyle.None;
    }

    private bool Bind()
    {
        if (root != null) return true;
        if (document == null) return false;
        root = document.rootVisualElement.Q("DaySummary");
        if (root == null) return false;
        rows = root.Q("DaySummaryRows");
        title = root.Q<Label>("DaySummaryTitle");
        return true;
    }

    private static bool Pressed()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        var pad = Gamepad.current;
        return (keyboard != null && keyboard.anyKey.wasPressedThisFrame) ||
               (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) ||
               (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
    }

    private IEnumerator Fade(float from, float to)
    {
        for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
        {
            root.style.opacity = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / fadeSeconds));
            yield return null;
        }
        root.style.opacity = to;
    }

    private void Fill(int day)
    {
        title.text = day + ". GÜN BİTTİ";
        rows.Clear();
        Row("Yenen yiyecek", foods.ToString());
        bool chick = growth != null && (form == null || form.CurrentForm == PlayerGrowthController.Form.Chick);
        if (chick)
        {
            int points = growth.EatenSeedCount;
            Row("Gelişim", $"+{Mathf.Max(0, points - startPoints)}  ·  {points} / {GrowthProgressController.SeedGoal}");
            for (int stage = startStage + 1; stage <= growth.ChickStage && stage < GrowthProgressController.StageCount; stage++)
                Row("Yeni aşama", $"{stage}. aşama", true);
        }
        if (eggs > 0) Row("Yumurta", goldenEggs > 0 ? $"{eggs}  ({goldenEggs} altın)" : eggs.ToString());
        int gold = Gold - startGold;
        if (gold > 0) Row("Kazanılan altın", "+" + gold, true);
        if (purchases > 0) Row("Alınan yükseltme", purchases.ToString());
        foreach (string region in regionsOpened) Row("Yeni bölge", region, true);
        if (questsDone.Count > 0)
        {
            Row("Tamamlanan görev", questsDone.Count.ToString());
            for (int i = 0; i < Mathf.Min(3, questsDone.Count); i++) Note("• " + questsDone[i]);
            if (questsDone.Count > 3) Note($"ve {questsDone.Count - 3} görev daha");
        }
    }

    private void Row(string label, string value, bool gold = false)
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.AddToClassList("day-summary-row");
        var l = new Label(label) { pickingMode = PickingMode.Ignore };
        l.AddToClassList("day-summary-label");
        var v = new Label(value) { pickingMode = PickingMode.Ignore };
        v.AddToClassList("day-summary-value");
        if (gold) v.AddToClassList("day-summary-value--gold");
        row.Add(l);
        row.Add(v);
        rows.Add(row);
    }

    private void Note(string text)
    {
        var l = new Label(text) { pickingMode = PickingMode.Ignore };
        l.AddToClassList("day-summary-note");
        rows.Add(l);
    }
}
