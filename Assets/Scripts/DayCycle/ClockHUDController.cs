using UnityEngine;
using UnityEngine.UIElements;

public sealed class ClockHUDController : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    private VisualElement hud;
    private VisualElement location;
    private VisualElement quests;
    private Label clock;
    private int lastMinute = -1;
    public void Bind()
    {
        hud = document.rootVisualElement.Q("ClockHUD");
        location = document.rootVisualElement.Q("LocationHUD");
        quests = document.rootVisualElement.Q("SideStack");
        clock = document.rootVisualElement.Q<Label>("ClockText");
        document.rootVisualElement.pickingMode = PickingMode.Ignore;
        hud.pickingMode = PickingMode.Ignore;
        lastMinute = -1;
    }
    // The farm location (FarmLocationHUD), the quests (QuestHUD) and the carried egg (CarriedEggHUD) share the clock's
    // document and show and hide with it.
    public void SetVisible(bool visible)
    {
        hud.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (location != null) location.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (quests != null) quests.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
    public void SetMinute(int minute)
    {
        if (minute == lastMinute) return;
        lastMinute = minute;
        clock.text = $"{minute / 60:00}:{minute % 60:00}";
    }
}
