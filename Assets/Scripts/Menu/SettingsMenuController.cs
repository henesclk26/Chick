using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public sealed class SettingsMenuController : MonoBehaviour
{
    private VisualElement root, page, displayConfirm;
    private Slider master, music, sfx, sensitivity;
    private Toggle fullscreen, vsync;
    private DropdownField resolution, quality;
    private readonly List<Vector2Int> sizes = new List<Vector2Int>();
    private Action back;
    private bool awaitingDisplay;
    private float deadline;
    private int oldWidth, oldHeight, oldQuality, oldVsync;
    private FullScreenMode oldMode;
    private void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        page = root.Q("settings-page"); displayConfirm = root.Q("display-confirm");
        master = root.Q<Slider>("master"); music = root.Q<Slider>("music"); sfx = root.Q<Slider>("sfx"); sensitivity = root.Q<Slider>("sensitivity");
        fullscreen = root.Q<Toggle>("fullscreen"); vsync = root.Q<Toggle>("vsync");
        resolution = root.Q<DropdownField>("resolution"); quality = root.Q<DropdownField>("quality");
        root.Q<Button>("settings-back").clicked += Back;
        root.Q<Button>("settings-apply").clicked += Apply;
        root.Q<Button>("display-keep").clicked += KeepDisplay;
        root.Q<Button>("display-revert").clicked += RevertDisplay;
        foreach (var slider in new[] { master, music, sfx, sensitivity }) slider.RegisterValueChangedCallback(SliderChanged);
    }
    private void OnDisable()
    {
        if (awaitingDisplay) RevertDisplay();
        if (root == null) return;
        root.Q<Button>("settings-back").clicked -= Back; root.Q<Button>("settings-apply").clicked -= Apply;
        root.Q<Button>("display-keep").clicked -= KeepDisplay; root.Q<Button>("display-revert").clicked -= RevertDisplay;
        foreach (var slider in new[] { master, music, sfx, sensitivity }) slider.UnregisterValueChangedCallback(SliderChanged);
    }
    public void Show(Action onBack)
    {
        back = onBack; page.RemoveFromClassList("hidden"); Fill(); root.Q<Button>("settings-back").Focus();
        root.Q<Label>("settings-status").text = "Değişiklikleri kaydetmek için UYGULA'ya bas.";
    }
    private void Fill()
    {
        master.SetValueWithoutNotify(MenuPreferences.Master * 100); music.SetValueWithoutNotify(MenuPreferences.Music * 100);
        sfx.SetValueWithoutNotify(MenuPreferences.Sfx * 100); sensitivity.SetValueWithoutNotify(MenuPreferences.Sensitivity);
        fullscreen.SetValueWithoutNotify(Screen.fullScreen); vsync.SetValueWithoutNotify(QualitySettings.vSyncCount > 0);
        sizes.Clear(); sizes.AddRange(Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct());
        var current = new Vector2Int(Screen.width, Screen.height);
        if (!sizes.Contains(current)) sizes.Add(current);
        if (sizes.Count == 1) foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) }) if (!sizes.Contains(size)) sizes.Add(size);
        resolution.choices = sizes.Select(s => s.x + " × " + s.y).ToList(); resolution.index = sizes.IndexOf(current);
        quality.choices = QualitySettings.names.ToList(); quality.index = QualitySettings.GetQualityLevel();
        Values();
    }
    private void SliderChanged(ChangeEvent<float> evt) => Values();
    private void Values()
    {
        foreach (var slider in new[] { master, music, sfx }) root.Q<Label>(slider.name + "-value").text = Mathf.RoundToInt(slider.value) + "%";
        root.Q<Label>("sensitivity-value").text = sensitivity.value.ToString("0.00") + "×";
    }
    private void Apply()
    {
        if (awaitingDisplay) return;
        oldWidth = Screen.width; oldHeight = Screen.height; oldMode = Screen.fullScreenMode;
        oldQuality = QualitySettings.GetQualityLevel(); oldVsync = QualitySettings.vSyncCount;
        Vector2Int size = sizes[Mathf.Clamp(resolution.index, 0, sizes.Count - 1)];
        bool displayChanged = size.x != oldWidth || size.y != oldHeight || fullscreen.value != Screen.fullScreen;
        QualitySettings.SetQualityLevel(quality.index); QualitySettings.vSyncCount = vsync.value ? 1 : 0;
        if (displayChanged && !Application.isEditor)
        {
            // Borderless FullScreenWindow always uses the desktop mode and internally rescales
            // non-native render sizes. On URP this can recreate a differently-tonemapped target.
            // Exclusive fullscreen applies the selected display mode directly instead.
            Screen.SetResolution(size.x, size.y, fullscreen.value ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed);
            StartCoroutine(StabilizeDisplayLayers());
            awaitingDisplay = true; deadline = Time.unscaledTime + 15;
            page.SetEnabled(false); displayConfirm.RemoveFromClassList("hidden"); root.Q<Button>("display-revert").Focus();
        }
        else Save();
    }
    private System.Collections.IEnumerator StabilizeDisplayLayers()
    {
        // A resolution/full-screen switch can rebuild UI Toolkit panels over several frames.
        // Keep the day-transition blackout dormant unless a real day transition is running.
        for (int frame = 0; frame < 3; frame++)
        {
            yield return null;
            GameTimeManager time = FindFirstObjectByType<GameTimeManager>();
            if (time != null && time.IsTransitioning) yield break;

            if (time != null) time.RefreshVisuals();

            foreach (DayTransitionController transition in FindObjectsByType<DayTransitionController>(FindObjectsSortMode.None))
                transition.HideImmediately();
        }
    }
    private void Save()
    {
        var size = sizes[Mathf.Clamp(resolution.index, 0, sizes.Count - 1)];
        MenuPreferences.Save(master.value / 100, music.value / 100, sfx.value / 100, sensitivity.value,
            fullscreen.value, size.x, size.y, vsync.value, quality.index);
        foreach (var camera in FindObjectsByType<FreeOrbitThirdPersonCamera>(FindObjectsSortMode.None))
            camera.SetLookSensitivity(MenuPreferences.Sensitivity);
        root.Q<Label>("settings-status").text = Application.isEditor ? "Kaydedildi. Çözünürlük ve tam ekran derlemede uygulanır." : "Ayarlar kaydedildi. İyi eğlenceler!";
    }
    private void Update()
    {
        if (!awaitingDisplay) return;
        root.Q<Label>("display-countdown").text = "Bu görüntü ayarları korunsun mu?  " + Mathf.CeilToInt(deadline - Time.unscaledTime) + " sn";
        if (Time.unscaledTime >= deadline) RevertDisplay();
    }
    private void KeepDisplay() { if (!awaitingDisplay) return; awaitingDisplay = false; displayConfirm.AddToClassList("hidden"); page.SetEnabled(true); Save(); }
    private void RevertDisplay()
    {
        if (!awaitingDisplay) return; awaitingDisplay = false;
        Screen.SetResolution(oldWidth, oldHeight, oldMode); QualitySettings.SetQualityLevel(oldQuality); QualitySettings.vSyncCount = oldVsync;
        displayConfirm.AddToClassList("hidden"); page.SetEnabled(true); Fill();
        root.Q<Label>("settings-status").text = "Önceki görüntü ayarlarına dönüldü.";
    }
    private void Back()
    {
        if (awaitingDisplay) return;
        page.AddToClassList("hidden"); back?.Invoke();
    }
}
