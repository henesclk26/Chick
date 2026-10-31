using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Under the quests: the eggs the chicken is carrying to the basket. One slot for each egg it can carry ("Otomatik
/// Sepet": 1, 2, then 3), the journal's golden or white egg icon in each carried one (golden first), a faint egg where
/// there is still room, and "carried / room". Pops in when an egg is picked up, fades out once they are all in the
/// basket; a slot that fills gives a little pop. <see cref="ShowFull"/> puts "inventory full" under the growth bar for
/// a moment (Z beside an egg with no room left). The eggs themselves are not shown on the chicken
/// (LaidEggPresentation). Shares the clock's document. Presentation only.
/// </summary>
public sealed class CarriedEggHUD : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    [SerializeField] private PlayerUpgrades upgrades;
    [SerializeField, Min(.5f), Tooltip("Seconds the \"inventory full\" line stays.")]
    private float fullMessageSeconds = 2.2f;

    private static CarriedEggHUD active;
    private VisualElement badge, slots, fullLine;
    private Label count;
    private Texture2D whiteEgg, goldEgg;
    private int shownGolden = -1, shownWhite = -1, shownRoom = -1;
    private IVisualElementScheduledItem hideFull;

    // the icons' transparent margins left out, so both stand the same height (as in the journal)
    private static readonly Rect WhiteUv = new Rect(.17f, .088f, .66f, .80f), GoldUv = new Rect(.12f, .047f, .76f, .90f);

    /// <summary>"Inventory full" under the growth bar for a moment.</summary>
    public static void ShowFull()
    {
        if (active == null || active.fullLine == null) return;
        active.fullLine.RemoveFromClassList("off");
        active.hideFull?.Pause();
        active.hideFull = active.fullLine.schedule.Execute(() => active.fullLine.AddToClassList("off"))
            .StartingIn((long)(active.fullMessageSeconds*1000f));
    }

    private void OnEnable() => active = this;
    private void OnDisable() { if (active == this) active = null; }

    private void Start()
    {
        if (upgrades == null) upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        var root = document.rootVisualElement;
        badge = root.Q("CarryHUD");
        slots = root.Q("CarrySlots");
        count = root.Q<Label>("CarryCount");
        fullLine = root.Q("CarryFull");
        whiteEgg = Resources.Load<Texture2D>("Journal/Egg");
        goldEgg = Resources.Load<Texture2D>("Journal/GoldEgg");
    }

    private void Update()
    {
        if (badge == null) return;
        int golden = LaidEggPresentation.CountCarried(true), white = LaidEggPresentation.CountCarried(false);
        int carried = golden + white;
        int room = Mathf.Max(upgrades != null ? upgrades.EggCarryCapacity : 1, carried);
        if (golden == shownGolden && white == shownWhite && room == shownRoom) return;
        int before = Mathf.Max(0, shownGolden) + Mathf.Max(0, shownWhite);
        shownGolden = golden; shownWhite = white; shownRoom = room;

        if (carried == 0)
        {
            badge.AddToClassList("off");
            return;
        }
        while (slots.childCount < room)
        {
            var slot = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            slot.AddToClassList("carry-slot");
            slots.Add(slot);
        }
        while (slots.childCount > room) slots.RemoveAt(slots.childCount - 1);
        for (int i = 0; i < room; i++)
        {
            var slot = (Image)slots[i];
            bool isGolden = i < golden, filled = i < carried;
            slot.image = isGolden ? goldEgg : whiteEgg;
            slot.uv = isGolden ? GoldUv : WhiteUv;
            slot.EnableInClassList("empty", !filled);
            // the slot just filled gives a little pop
            if (filled && i >= before)
            {
                slot.AddToClassList("pop");
                slot.schedule.Execute(() => slot.RemoveFromClassList("pop")).StartingIn(180);
            }
        }
        count.text = $"{carried}/{room}";
        badge.EnableInClassList("full", carried >= room);
        badge.RemoveFromClassList("off");
    }
}
