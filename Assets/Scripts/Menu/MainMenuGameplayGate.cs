using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;

// Holds the existing gameplay start sequence while the embedded SampleScene menu is visible.
[DefaultExecutionOrder(-900)]
public sealed class MainMenuGameplayGate : MonoBehaviour
{
    [SerializeField] private GameTimeManager timeManager;
    private bool released;
    public bool IsReleased => released;
    private void Awake()
    {
        if (timeManager == null) timeManager = GetComponent<GameTimeManager>();
        if (timeManager == null) timeManager = FindFirstObjectByType<GameTimeManager>();
        // GameTimeManager (-1000) has already cached its normal player/camera state.
        // Pausing it here prevents its Start coroutine from running behind the menu.
        if (timeManager != null) timeManager.enabled = false;
        HideDayTransitionOverlay();
    }
    private IEnumerator Start()
    {
        // UIDocument builds its visual tree after component Awake; retry once the tree exists.
        yield return null;
        if (!released) HideDayTransitionOverlay();
    }
    public void ReleaseGameplay()
    {
        if (released) return;
        released = true;
        if (timeManager != null) timeManager.enabled = true;
    }

    private void HideDayTransitionOverlay()
    {
        foreach (var document in GetComponentsInChildren<UIDocument>(true))
        {
            if (document.visualTreeAsset == null || document.visualTreeAsset.name != "DayTransition") continue;
            var visualRoot = document.rootVisualElement;
            if (visualRoot == null) continue;
            var overlay = visualRoot.Q<VisualElement>("FullScreenBlackOverlay");
            if (overlay != null) overlay.style.display = DisplayStyle.None;
        }
    }
}
