using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class DayTransitionController : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    [SerializeField] private float initialBlackHold = 0.3f;
    [SerializeField] private float titleFade = 0.35f;
    [SerializeField] private float titleHold = 1.15f;
    [SerializeField] private float screenFade = 0.9f;
    private VisualElement overlay;
    private Label title, saving;
    public void Bind()
    {
        overlay = document.rootVisualElement.Q("FullScreenBlackOverlay");
        title = overlay.Q<Label>("DayLabel");
        saving = overlay.Q<Label>("SavingLabel");
        overlay.style.opacity = 1;
        overlay.style.display = DisplayStyle.Flex;
    }
    public void HideImmediately()
    {
        if (document == null) document = GetComponent<UIDocument>();
        if (document == null) return;

        overlay = document.rootVisualElement.Q("FullScreenBlackOverlay");
        if (overlay == null) return;

        title = overlay.Q<Label>("DayLabel");
        saving = overlay.Q<Label>("SavingLabel");
        overlay.style.opacity = 0;
        overlay.style.display = DisplayStyle.None;
    }
    public IEnumerator InitialHold() { yield return new WaitForSecondsRealtime(initialBlackHold); }
    public IEnumerator Blackout()
    {
        overlay.style.display = DisplayStyle.Flex;
        yield return Fade(overlay, 0, 1, screenFade);
    }
    public void Saving() { saving.text = "Saving..."; saving.style.display = DisplayStyle.Flex; }
    public void Failure(string message)
    {
        overlay.style.display = DisplayStyle.Flex;
        overlay.style.opacity = 1;
        title.style.opacity = 0;
        saving.style.display = DisplayStyle.Flex;
        saving.text = "Save / load failed.\nProgress has not advanced.\nCheck available storage and restart.";
    }
    public IEnumerator Morning(int day, System.Action beforeReveal = null)
    {
        saving.style.display = DisplayStyle.None;
        title.text = "DAY " + day;
        yield return Fade(title, 0, 1, titleFade);
        yield return new WaitForSecondsRealtime(titleHold);
        yield return Fade(title, 1, 0, titleFade);
        beforeReveal?.Invoke();
        // Let UI Toolkit lay out the HUD while the opaque overlay still covers it.
        yield return null;
        yield return Fade(overlay, 1, 0, screenFade);
        overlay.style.display = DisplayStyle.None;
    }
    private static IEnumerator Fade(VisualElement element, float from, float to, float duration)
    {
        element.style.opacity = from;
        for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            element.style.opacity = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, elapsed / duration));
            yield return null;
        }
        element.style.opacity = to;
    }
}
