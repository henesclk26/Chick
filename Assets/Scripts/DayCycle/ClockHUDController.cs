using UnityEngine;
using UnityEngine.UIElements;

public sealed class ClockHUDController : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    private VisualElement hud;
    private Label clock;
    private int lastMinute = -1;
    public void Bind()
    {
        hud = document.rootVisualElement.Q("ClockHUD");
        clock = document.rootVisualElement.Q<Label>("ClockText");
        document.rootVisualElement.pickingMode = PickingMode.Ignore;
        hud.pickingMode = PickingMode.Ignore;
    }
    public void SetVisible(bool visible) => hud.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    public void SetMinute(int minute)
    {
        if (minute == lastMinute) return;
        lastMinute = minute;
        clock.text = $"{minute / 60:00}:{minute % 60:00}";
    }
}
