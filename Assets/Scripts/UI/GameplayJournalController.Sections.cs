using UnityEngine;
using UnityEngine.UIElements;

// The existing UXML cards retain their live callbacks; this layer groups them into
// the approved three-page journal. Art is cropped from references, never baked UI text.
public sealed partial class GameplayJournalController
{
    // Sections: 0 chicken, 1 helper chicks, 2 abilities (barn tabs: 0 and 2), 3 first helper chick (coop, none owned).
    private const int ChickenSection = 0, CompanionSection = 1, AbilitySection = 2, FirstChickSection = 3;
    private readonly Button[] sectionTabs = new Button[4];
    private readonly VisualElement[] sectionPages = new VisualElement[4];
    private VisualElement sectionTabBar;
    private readonly Button[] companionSlots = new Button[PlayerUpgrades.MaxHelperChicks];
    private int selectedCompanion;
    private Label companionCount, companionTitle, companionHint;
    private VisualElement companionDetails;
    private Texture2D companionAtlas, abilitiesAtlas;
    private static readonly Rect OwnedPortrait = new Rect(385, 342, 101, 101);
    private static readonly Rect LockedPortrait = new Rect(385, 628, 101, 101);
    private static readonly System.Globalization.CultureInfo Turkish = new System.Globalization.CultureInfo("tr-TR");

    private string ResolveCardId(string id) => PlayerUpgrades.IsHelperUpgrade(id)
        ? PlayerUpgrades.HelperUpgradeId(selectedCompanion, id) : id;

    private void BuildUpgradeSections()
    {
        if (root.Q("upgrade-section-tabs") != null) return;
        root.AddToClassList("section-journal");
        var sheet = Resources.Load<StyleSheet>("Journal/UpgradeSections");
        if (sheet != null) root.styleSheets.Add(sheet);
        companionAtlas = Resources.Load<Texture2D>("Journal/CompanionAtlas");
        abilitiesAtlas = Resources.Load<Texture2D>("Journal/AbilitiesAtlas");
        var tabs = Element("upgrade-section-tabs", "upgrade-section-tabs");
        sectionTabBar = tabs;
        upgradesPage.Insert(0, tabs);
        string[] names = { "Tavuk", "Yardımcı Civcivler", "Yetenekler" };
        string[] keys = { "chicken", "companions", "abilities" };
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            var button = new Button(() => ShowUpgradeSection(index)) { name = keys[i] + "-section-tab" };
            button.AddToClassList("upgrade-section-tab");
            GameplayJournalReferenceTheme.AddOliveSurface(button);
            button.Add(new Label(names[i]) { pickingMode = PickingMode.Ignore });
            tabs.Add(button);
            sectionTabs[i] = button;
            var page = Element(keys[i] + "-section", "upgrade-section");
            sectionPages[i] = page;
            upgradesPage.Add(page);
        }
        var chickenScroll = Scroll("chicken-upgrade-scroll");
        sectionPages[0].Add(chickenScroll);
        var chickenGrid = Element("chicken-upgrade-grid", "upgrade-grid");
        chickenScroll.Add(chickenGrid);
        foreach (string prefix in new[] { "peck", "range", "sprint-speed", "sprint-duration", "double-collect" })
            MoveCard(prefix, chickenGrid, "grid-card");
        chickenGrid.Add(NewUpgradeCard("auto-basket", "Otomatik Sepet", "Yumurtlayınca yumurta kendiliğinden sepete gider.",
            () => PurchaseClicked(LiveUpgrades[12])));
        SetImage("auto-basket-art", Resources.Load<Texture2D>("Journal/BasketArt"));
        root.Q<Button>("double-collect-price").Q<Label>(className: "price-text").name = "double-collect-cost";
        var chickenAtlas = Resources.Load<Texture2D>("Journal/ChickenAtlas");
        SetAtlasArt(root.Q<Image>("sprint-speed-art"), chickenAtlas, new Rect(381, 487, 119, 119));
        SetAtlasArt(root.Q<Image>("sprint-duration-art"), chickenAtlas, new Rect(929, 487, 116, 120));
        SetAtlasArt(root.Q<Image>("double-collect-art"), chickenAtlas, new Rect(381, 693, 119, 120));

        sectionPages[1].AddToClassList("companion-layout");
        var roster = Element("companion-roster", "companion-roster");
        companionCount = Text("Civcivlerim 0 / 3", "companion-roster-title");
        roster.Add(companionCount);
        for (int i = 0; i < companionSlots.Length; i++)
        {
            int index = i;
            var slot = new Button(() => CompanionSlotClicked(index)) { name = "companion-slot-" + i };
            slot.AddToClassList("companion-slot");
            var portrait = new Image { name = "portrait", pickingMode = PickingMode.Ignore };
            portrait.AddToClassList("companion-portrait");
            SetAtlasArt(portrait, companionAtlas, OwnedPortrait);
            slot.Add(portrait);
            var copy = Element(null, "companion-slot-copy");
            copy.Add(Text("Civciv " + (i + 1), "companion-slot-name"));
            var status = Text("Satın alınmadı", "companion-slot-status"); status.name = "status";
            copy.Add(status);
            var price = Element("purchase", "companion-purchase");
            var egg = GameplayJournalReferenceTheme.CreateCoin(); egg.AddToClassList("slot-price-egg");
            price.Add(egg);
            var cost = Text("", "slot-price-text"); cost.name = "cost"; price.Add(cost);
            copy.Add(price); slot.Add(copy);
            roster.Add(slot); companionSlots[i] = slot;
        }
        sectionPages[1].Add(roster);
        companionDetails = Element("companion-details", "companion-details");
        sectionPages[1].Add(companionDetails);
        var heading = Element(null, "companion-heading");
        var hero = new Image { pickingMode = PickingMode.Ignore };
        hero.AddToClassList("companion-hero");
        SetAtlasArt(hero, companionAtlas, new Rect(722, 275, 131, 114));
        heading.Add(hero);
        var headingText = Element(null, "companion-heading-copy");
        companionTitle = Text("Civciv 1", "companion-title"); headingText.Add(companionTitle);
        companionHint = Text("Yalnızca bu civciv gelişir.", "companion-hint"); headingText.Add(companionHint);
        heading.Add(headingText); companionDetails.Add(heading);
        var helperScroll = Scroll("companion-upgrade-scroll"); companionDetails.Add(helperScroll);
        var helperGrid = Element("companion-upgrade-grid", "upgrade-grid"); helperScroll.Add(helperGrid);
        MoveCard("helper-speed", helperGrid, "grid-card");
        MoveCard("helper-range", helperGrid, "grid-card");
        helperGrid.Add(NewUpgradeCard("helper-move", "Takip Hızı", "Tavuğa daha hızlı yetişir.", () => PurchaseClicked(LiveUpgrades[10])));
        helperGrid.Add(NewUpgradeCard("helper-climb", "Engel Aşma", "Daha yüksek engelleri aşar.", () => PurchaseClicked(LiveUpgrades[11])));
        var peckCard = Card("helper-speed");
        peckCard.Q<Label>(className: "upgrade-name").text = "Gagalama Hızı";
        peckCard.Q<Label>(className: "upgrade-description").text = "Yemleri daha hızlı yer.";
        var rangeCard = Card("helper-range");
        rangeCard.Q<Label>(className: "upgrade-name").text = "Yem Arama Alanı";
        rangeCard.Q<Label>(className: "upgrade-description").text = "Daha uzaktaki yemleri bulur.";
        string[] helperPrefixes = { "helper-speed", "helper-range", "helper-move", "helper-climb" };
        Rect[] helperArt = { new Rect(731,420,101,105), new Rect(1106,420,101,108), new Rect(731,657,101,105), new Rect(1106,657,101,105) };
        for (int i = 0; i < helperPrefixes.Length; i++)
        {
            string prefix = helperPrefixes[i];
            SetAtlasArt(root.Q<Image>(prefix + "-art"), companionAtlas, helperArt[i]);
            var stat = Text("", "upgrade-stat"); stat.name = prefix + "-stat";
            Card(prefix).Q(className: "upgrade-details").Insert(2, stat);
            var count = Text("", "upgrade-level-count"); count.name = prefix + "-count";
            Card(prefix).Add(count);
        }

        var abilityScroll = Scroll("ability-scroll"); sectionPages[2].Add(abilityScroll);
        var abilityGrid = Element("ability-grid", "ability-grid"); abilityScroll.Add(abilityGrid);
        MoveCard("double-jump", abilityGrid, "ability-card");
        MoveCard("glide", abilityGrid, "ability-card");
        string[] abilities = { "double-jump", "glide" };
        Rect[] abilityArt = { new Rect(436,281,213,190), new Rect(813,281,214,190) };
        for (int i = 0; i < abilities.Length; i++)
        {
            var card = Card(abilities[i]);
            SetAtlasArt(root.Q<Image>(abilities[i] + "-art"), abilitiesAtlas, abilityArt[i]);
            var keyRow = Element(null, "ability-keys");
            keyRow.Add(Text("SPACE", "keycap"));
            keyRow.Add(i == 0 ? Text("→", "key-joiner") : Text("basılı tut", "key-hint"));
            if (i == 0) keyRow.Add(Text("SPACE", "keycap"));
            card.Add(keyRow);
            // Price sits above the button, as in the reference; RefreshUpgrades still finds it by name.
            var button = root.Q<Button>(abilities[i] + "-price");
            var priceRow = Element(abilities[i] + "-price-row", "ability-price-row");
            priceRow.Add(button.Q(abilities[i] + "-egg"));
            priceRow.Add(button.Q(abilities[i] + "-cost"));
            card.Add(priceRow);
            var status = Element(abilities[i] + "-status", "ability-status");
            status.Add(new CheckGlyph());
            status.Add(Text("Açıldı", "ability-status-text"));
            card.Add(status);
            var buttonEgg = GameplayJournalReferenceTheme.CreateCoin();
            buttonEgg.name = abilities[i] + "-action-egg"; buttonEgg.AddToClassList("action-egg");
            button.Add(buttonEgg);
            var action = Text("Yeteneği Aç", "ability-action"); action.name = abilities[i] + "-action";
            button.Add(action);
        }
        var future = Element("future-ability", "upgrade-card"); future.AddToClassList("ability-card");
        var futureArt = new Image { pickingMode = PickingMode.Ignore }; futureArt.AddToClassList("upgrade-art");
        SetAtlasArt(futureArt, abilitiesAtlas, new Rect(1193,281,214,190)); future.Add(futureArt);
        var details = Element(null, "upgrade-details");
        details.Add(Text("Yeni Yetenek", "upgrade-name"));
        details.Add(Text("Keşfederek yeni yetenekler aç.", "upgrade-description")); future.Add(details);
        var futureButton = new Button { text = "Kilitli" }; futureButton.AddToClassList("upgrade-price"); futureButton.SetEnabled(false);
        future.Add(futureButton); abilityGrid.Add(future);
        sectionPages[2].Add(Text("Yeni yetenekler keşfettikçe burada görünür.", "ability-footer"));
        // The coop before the first chick: only the original "Yardımcı Civciv" purchase card.
        sectionPages[FirstChickSection] = Element("first-chick-section", "upgrade-section");
        upgradesPage.Add(sectionPages[FirstChickSection]);
        var firstChickGrid = Element("first-chick-grid", "upgrade-grid");
        sectionPages[FirstChickSection].Add(firstChickGrid);
        MoveCard("helper", firstChickGrid, "grid-card");
        SetAtlasArt(root.Q<Image>("helper-art"), companionAtlas, new Rect(722, 275, 131, 114));
        // The obsolete list keeps the old helper-price binding but is no longer visible.
        root.Q("upgrade-list").style.display = DisplayStyle.None;
        foreach (string className in new[] { "upgrade-price", "companion-purchase", "upgrade-section-tab", "side-button", "owned-card", "selected-badge" })
            root.Query(className: className).ForEach(GameplayJournalReferenceTheme.AddWear);
        ShowUpgradeSection(0);
    }

    // The barn shows chicken upgrades and abilities; the coop shows the helper chicks, or only the first
    // chick's purchase while none is owned.
    private void ShowStationSections()
    {
        bool coop = station == UpgradeStation.Kind.Coop;
        if (sectionTabBar != null) sectionTabBar.style.display = coop ? DisplayStyle.None : DisplayStyle.Flex;
        if (sectionTabs[CompanionSection] != null) sectionTabs[CompanionSection].style.display = DisplayStyle.None;
        if (!coop) ShowUpgradeSection(ChickenSection);
        else ShowUpgradeSection(upgrades != null && upgrades.HelperChickCount > 0 ? CompanionSection : FirstChickSection);
    }

    public void ShowUpgradeSection(int index)
    {
        for (int i = 0; i < sectionPages.Length; i++)
        {
            sectionPages[i]?.EnableInClassList("hidden", i != index);
            sectionTabs[i]?.EnableInClassList("section-active", i == index);
        }
    }

    private void CompanionSlotClicked(int index)
    {
        if (upgrades == null) return;
        if (index < upgrades.HelperChickCount)
        {
            selectedCompanion = index;
            if (upgradesStatus != null) upgradesStatus.text = "";
            RefreshUpgrades();
            return;
        }
        if (index != upgrades.HelperChickCount) return;
        int before = upgrades.HelperChickCount;
        HelperPriceClicked();
        if (upgrades.HelperChickCount > before) selectedCompanion = index;
        RefreshUpgrades();
    }

    private void RefreshUpgradeSections()
    {
        if (companionCount == null || upgrades == null) return;
        int owned = upgrades.HelperChickCount;
        // Buying the first chick at the coop moves on to its upgrades.
        if (isOpen && station == UpgradeStation.Kind.Coop && owned > 0 && !sectionPages[FirstChickSection].ClassListContains("hidden"))
            ShowUpgradeSection(CompanionSection);
        companionCount.text = $"Civcivlerim {owned} / {PlayerUpgrades.MaxHelperChicks}";
        companionTitle.text = owned > 0 ? "Civciv " + (selectedCompanion + 1) : "Yardımcı Civciv";
        companionHint.text = owned > 0 ? "Yalnızca bu civciv gelişir." : "Başlamak için soldan bir civciv al.";
        companionDetails.EnableInClassList("no-companion", owned == 0);
        for (int i = 0; i < companionSlots.Length; i++)
        {
            bool bought = i < owned;
            var slot = companionSlots[i];
            slot.EnableInClassList("companion-owned", bought);
            slot.EnableInClassList("companion-locked", !bought);
            slot.EnableInClassList("companion-selected", bought && selectedCompanion == i);
            slot.SetEnabled(bought || i == owned);
            int total = 0;
            foreach (string kind in new[] { PlayerUpgrades.HelperEatSpeed, PlayerUpgrades.HelperRange, PlayerUpgrades.HelperMoveSpeed, PlayerUpgrades.HelperClimb })
                total += upgrades.GetHelperLevel(i, kind);
            slot.Q<Label>("status").text = bought ? (total == 0 ? "Geliştirilmedi" : total + " geliştirme") : "Satın alınmadı";
            slot.Q("purchase").style.display = bought ? DisplayStyle.None : DisplayStyle.Flex;
            // The locked crop already carries the grey chick and padlock from the reference art.
            SetAtlasArt(slot.Q<Image>("portrait"), companionAtlas, bought ? OwnedPortrait : LockedPortrait);
            slot.Q<Label>("cost").text = i == owned ? upgrades.GetNextPrice(PlayerUpgrades.HelperChick).ToString() : "Önceki civcivi al";
            slot.Q("purchase").EnableInClassList("unaffordable", i != owned || AvailableEggs < upgrades.GetNextPrice(PlayerUpgrades.HelperChick));
            slot.tooltip = bought ? "Bu civcivin yükseltmelerini göster" : i == owned ? "Altın karşılığında civciv al" : "Civcivler sırayla alınır";
        }
        string[] prefixes = { "helper-speed", "helper-range", "helper-move", "helper-climb" };
        string[] ids = { PlayerUpgrades.HelperEatSpeed, PlayerUpgrades.HelperRange, PlayerUpgrades.HelperMoveSpeed, PlayerUpgrades.HelperClimb };
        string[] units = { " kat", " kat", " kat", " m" };
        for (int i = 0; i < prefixes.Length; i++)
        {
            int level = upgrades.GetHelperLevel(selectedCompanion, ids[i]);
            int max = upgrades.GetMaxLevel(ids[i]);
            string now = FormatEffect(upgrades.HelperEffectAt(ids[i], level), ids[i]);
            root.Q<Label>(prefixes[i] + "-stat").text = level < max
                ? now + "  →  " + FormatEffect(upgrades.HelperEffectAt(ids[i], level + 1), ids[i]) + units[i]
                : now + units[i];
            root.Q<Label>(prefixes[i] + "-count").text = $"{level} / {max}";
        }
        foreach (string prefix in new[] { "double-jump", "glide" })
        {
            bool unlocked = upgrades.GetLevel(prefix) > 0;
            Card(prefix).EnableInClassList("ability-owned", unlocked);
            root.Q(prefix + "-status").style.display = unlocked ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q(prefix + "-price-row").style.display = unlocked ? DisplayStyle.None : DisplayStyle.Flex;
            root.Q(prefix + "-action-egg").style.display = unlocked ? DisplayStyle.None : DisplayStyle.Flex;
            root.Q<Label>(prefix + "-action").text = unlocked ? "Kullanımda" : "Yeteneği Aç";
        }
    }

    private VisualElement Card(string prefix) => root.Q(prefix + "-art")?.GetFirstAncestorOfType<VisualElement>();
    private void MoveCard(string prefix, VisualElement parent, string extraClass)
    {
        var card = Card(prefix);
        card.AddToClassList(extraClass);
        parent.Add(card);
    }
    private static VisualElement Element(string name, string className)
    {
        var element = new VisualElement { name = name };
        element.AddToClassList(className);
        return element;
    }
    private static Label Text(string value, string className)
    {
        var label = new Label(value) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(className);
        return label;
    }
    private static ScrollView Scroll(string name)
    {
        var scroll = new ScrollView(ScrollViewMode.Vertical) { name = name, horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Auto };
        scroll.AddToClassList("section-scroll");
        scroll.AddToClassList("upgrade-list");
        return scroll;
    }
    private static void SetAtlasArt(Image image, Texture2D atlas, Rect source)
    {
        if (image == null || atlas == null) return;
        image.image = atlas;
        image.uv = new Rect(source.x / 1570f, 1f - source.yMax / 1002f, source.width / 1570f, source.height / 1002f);
        image.scaleMode = ScaleMode.StretchToFill;
        // Image does not repaint on a uv change alone; without this a bought chick kept its locked portrait.
        image.MarkDirtyRepaint();
    }
    private static string FormatEffect(float value, string kind) =>
        (kind == PlayerUpgrades.HelperClimb ? "+" : "") + value.ToString("0.0#", Turkish);

    // Drawn rather than a font glyph: the reference fonts have no check mark.
    private sealed class CheckGlyph : VisualElement
    {
        public CheckGlyph()
        {
            AddToClassList("check-glyph");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += context =>
            {
                float w = contentRect.width, h = contentRect.height;
                var painter = context.painter2D;
                painter.strokeColor = new Color32(67, 122, 35, 255);
                painter.lineWidth = Mathf.Max(3f, w * .17f);
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                painter.BeginPath();
                painter.MoveTo(new Vector2(w * .12f, h * .55f));
                painter.LineTo(new Vector2(w * .4f, h * .82f));
                painter.LineTo(new Vector2(w * .9f, h * .18f));
                painter.Stroke();
            };
        }
    }

    private VisualElement NewUpgradeCard(string prefix, string title, string description, System.Action clicked)
    {
        var card = Element(null, "upgrade-card"); card.AddToClassList("grid-card");
        var art = new Image { name = prefix + "-art", pickingMode = PickingMode.Ignore }; art.AddToClassList("upgrade-art"); card.Add(art);
        var details = Element(null, "upgrade-details");
        details.Add(Text(title, "upgrade-name")); details.Add(Text(description, "upgrade-description"));
        details.Add(Element(prefix + "-levels", "level-row")); card.Add(details);
        var button = new Button(clicked) { name = prefix + "-price" }; button.AddToClassList("upgrade-price");
        var egg = GameplayJournalReferenceTheme.CreateCoin(); egg.name = prefix + "-egg"; egg.AddToClassList("price-egg"); button.Add(egg);
        var cost = Text("", "price-text"); cost.name = prefix + "-cost"; button.Add(cost); card.Add(button);
        return card;
    }
}
