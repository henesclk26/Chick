using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

[RequireComponent(typeof(UIDocument))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-75)]
public sealed class DeveloperPanelController : MonoBehaviour
{
    [SerializeField] private MainMenuGameplayGate gameplayGate;
    [SerializeField] private GameTimeManager timeManager;
    [SerializeField] private ChickPlayerController player;
    [SerializeField] private PlayerGrowthController growth;
    private PlayerUpgrades upgrades;
    [SerializeField] private FreeOrbitThirdPersonCamera orbitCamera;
    [SerializeField] private InGamePauseMenuController pauseMenu;
    [SerializeField] private Transform barnSpawn;
    [SerializeField] private Transform fieldSpawn;
    [SerializeField] private Transform coopSpawn;
    [SerializeField] private Transform foldSpawn;

    private readonly List<KeyValuePair<Button, Action>> bindings = new List<KeyValuePair<Button, Action>>();
    private VisualElement root;
    private Label timeLabel;
    private Label statusLabel;
    private Button chickButton;
    private Button chickenButton;
    private bool isOpen;
    private bool previousTimePaused;
    private float previousTimeScale;
    private bool previousPlayerEnabled;
    private bool previousOrbitEnabled;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;
    private static int lastClosedFrame = -1;

    public static bool IsAnyOpen { get; private set; }
    public static bool BlocksEscapeThisFrame => IsAnyOpen || lastClosedFrame == Time.frameCount;

    private void OnEnable()
    {
        ResolveReferences();
        root = GetComponent<UIDocument>().rootVisualElement;
        if (root == null) return;
        root.style.display = DisplayStyle.None;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        timeLabel = root.Q<Label>("current-time");
        statusLabel = root.Q<Label>("debug-status");
        chickButton = root.Q<Button>("form-chick");
        chickenButton = root.Q<Button>("form-chicken");
        Bind("close-debug", Close);
        Bind("hour-back", HourBack);
        Bind("hour-forward", HourForward);
        Bind("time-morning", Morning);
        Bind("time-noon", Noon);
        Bind("time-evening", Evening);
        Bind("form-chick", BecomeChick);
        Bind("form-chicken", BecomeChicken);
        Bind("spawn-barn", GoToBarn);
        Bind("spawn-field", GoToField);
        Bind("spawn-coop", GoToCoop);
        Bind("spawn-fold", GoToFold);
        Bind("infinite-sprint", ToggleInfiniteSprint);
        Bind("sprint-speed-5x", ToggleSprintSpeed);
        Bind("jump-height-2x", ToggleJumpHeight);
        Bind("grant-eggs", GrantEggs);
        if (timeLabel == null || statusLabel == null || bindings.Count != 16)
            Debug.LogError("DeveloperPanel.uxml has a missing control.", this);
#else
        enabled = false;
#endif
    }

    private void OnDisable()
    {
        foreach (var binding in bindings) binding.Key.clicked -= binding.Value;
        bindings.Clear();
        if (isOpen) Close();
        IsAnyOpen = false;
    }

    private void ResolveReferences()
    {
        if (gameplayGate == null) gameplayGate = FindFirstObjectByType<MainMenuGameplayGate>();
        if (timeManager == null) timeManager = FindFirstObjectByType<GameTimeManager>();
        if (player == null) player = FindFirstObjectByType<ChickPlayerController>();
        if (growth == null && player != null) growth = player.GetComponent<PlayerGrowthController>();
        if (orbitCamera == null) orbitCamera = FindFirstObjectByType<FreeOrbitThirdPersonCamera>();
        if (pauseMenu == null) pauseMenu = FindFirstObjectByType<InGamePauseMenuController>();
        if (upgrades == null) upgrades = FindFirstObjectByType<PlayerUpgrades>();
    }

    private void Bind(string name, Action callback)
    {
        Button button = root.Q<Button>(name);
        if (button == null) return;
        button.clicked += callback;
        bindings.Add(new KeyValuePair<Button, Action>(button, callback));
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.f1Key.wasPressedThisFrame)
        {
            if (isOpen) Close();
            else if (gameplayGate != null && gameplayGate.IsReleased &&
                     timeManager != null && !timeManager.IsTransitioning && !timeManager.IsPaused &&
                     (pauseMenu == null || !pauseMenu.IsPaused) &&
                     !GameplayJournalController.IsAnyOpen)
                Open();
        }
        else if (isOpen && keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
        }
#endif
    }

    private void Open()
    {
        if (isOpen || root == null) return;
        previousTimePaused = timeManager.IsPaused;
        previousTimeScale = Time.timeScale;
        previousPlayerEnabled = player != null && player.enabled;
        previousOrbitEnabled = orbitCamera != null && orbitCamera.enabled;
        previousCursorLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;

        isOpen = true;
        IsAnyOpen = true;
        timeManager.IsPaused = true;
        if (player != null) player.enabled = false;
        if (orbitCamera != null) orbitCamera.enabled = false;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        root.style.display = DisplayStyle.Flex;
        RefreshDisplay();
        SetStatus("Bir kontrol seç.");
        root.Q<Button>("close-debug")?.Focus();
    }

    private void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        IsAnyOpen = false;
        lastClosedFrame = Time.frameCount;
        if (timeManager != null) timeManager.IsPaused = previousTimePaused;
        if (player != null) player.enabled = previousPlayerEnabled;
        if (orbitCamera != null) orbitCamera.enabled = previousOrbitEnabled;
        Time.timeScale = previousTimeScale;
        Cursor.lockState = previousCursorLockState;
        Cursor.visible = previousCursorVisible;
        if (root != null) root.style.display = DisplayStyle.None;
    }

    private void RefreshDisplay()
    {
        RefreshMovementControls();
        RefreshEggControls();
        if (timeLabel != null && timeManager != null)
            timeLabel.text = string.Format("{0:00}:{1:00}", timeManager.CurrentHour, timeManager.CurrentMinute);
        if (growth != null)
        {
            if (chickButton != null)
                chickButton.EnableInClassList("selected", growth.CurrentForm == PlayerGrowthController.Form.Chick);
            if (chickenButton != null)
                chickenButton.EnableInClassList("selected", growth.CurrentForm == PlayerGrowthController.Form.Chicken);
        }
    }

    private void RefreshEggControls()
    {
        var button = root.Q<Button>("grant-eggs");
        bool chicken = growth != null && growth.CurrentForm == PlayerGrowthController.Form.Chicken;
        if (button != null)
        {
            button.SetEnabled(chicken && upgrades != null);
            button.tooltip = !chicken ? "Yalnızca tavuk formunda kullanılabilir."
                : upgrades == null ? "Yumurta sistemi bulunamadı." : "Bakiyeye 20 harcanabilir yumurta ekler.";
        }
        var balance = root.Q<Label>("debug-egg-balance");
        if (balance != null) balance.text = upgrades != null
            ? "Bakiye: " + upgrades.AvailableEggs + " yumurta" : "Bakiye kullanılamıyor";
    }

    private void GrantEggs()
    {
        if (growth == null || growth.CurrentForm != PlayerGrowthController.Form.Chicken)
        {
            RefreshEggControls();
            SetStatus("Yumurta eklemek için tavuk formuna geç.");
            return;
        }
        bool granted = upgrades != null && upgrades.TryGrantDebugEggs(20);
        RefreshEggControls();
        SetStatus(granted ? "+20 yumurta eklendi. Bakiye: " + upgrades.AvailableEggs
            : "Yumurta eklenemedi: sistem veya bakiye sınırını kontrol et.");
    }

    private void ToggleInfiniteSprint()
    {
        if (player == null) { SetStatus("Karakter bulunamadı."); return; }
        player.DebugInfiniteSprint = !player.DebugInfiniteSprint;
        RefreshMovementControls();
        SetStatus(player.DebugInfiniteSprint ? "Sonsuz sprint staminası açık." : "Normal stamina tüketimi etkin.");
    }

    private void ToggleSprintSpeed()
    {
        if (player == null) { SetStatus("Karakter bulunamadı."); return; }
        player.DebugSprintSpeed5x = !player.DebugSprintSpeed5x;
        RefreshMovementControls();
        SetStatus(player.DebugSprintSpeed5x ? "Sprint hızı 5 katına çıkarıldı." : "Normal sprint hızı etkin.");
    }

    private void ToggleJumpHeight()
    {
        if (player == null) { SetStatus("Karakter bulunamadı."); return; }
        player.DebugJumpHeight2x = !player.DebugJumpHeight2x;
        RefreshMovementControls();
        SetStatus(player.DebugJumpHeight2x ? "Zıplama yüksekliği 2 katına çıkarıldı." : "Normal zıplama yüksekliği etkin.");
    }

    private void RefreshMovementControls()
    {
        PaintMovementToggle("infinite-sprint", player != null && player.DebugInfiniteSprint);
        PaintMovementToggle("sprint-speed-5x", player != null && player.DebugSprintSpeed5x);
        PaintMovementToggle("jump-height-2x", player != null && player.DebugJumpHeight2x);
    }

    private void PaintMovementToggle(string name, bool active)
    {
        var button = root.Q<Button>(name);
        if (button == null) return;
        button.SetEnabled(player != null);
        button.EnableInClassList("selected", active);
        var state = button.Q<Label>(className: "toggle-state");
        if (state != null) state.text = active ? "AÇIK" : "KAPALI";
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null) statusLabel.text = message;
    }

    private void SetHour(int hour)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (timeManager == null || timeManager.IsTransitioning) return;
        timeManager.DebugSetTime(Mathf.Clamp(hour, 7, 18) * 60f);
        RefreshDisplay();
        SetStatus("Saat " + timeLabel.text + " olarak ayarlandı.");
#endif
    }

    private void HourBack() { if (timeManager != null) SetHour(timeManager.CurrentHour - 1); }
    private void HourForward() { if (timeManager != null) SetHour(timeManager.CurrentHour + 1); }
    private void Morning() { SetHour(7); }
    private void Noon() { SetHour(12); }
    private void Evening() { SetHour(18); }

    private void BecomeChick() { SetForm(PlayerGrowthController.Form.Chick); }
    private void BecomeChicken() { SetForm(PlayerGrowthController.Form.Chicken); }

    private void SetForm(PlayerGrowthController.Form form)
    {
        if (growth == null) { SetStatus("Karakter bulunamadı."); return; }
        growth.SetForm(form);
        RefreshDisplay();
        SetStatus(form == PlayerGrowthController.Form.Chick ? "Civciv formuna geçildi." : "Tavuk formuna geçildi.");
    }

    private void GoToBarn() { Teleport(barnSpawn, "Ahır avlusu"); }
    private void GoToField() { Teleport(fieldSpawn, "Tarla"); }
    private void GoToCoop() { Teleport(coopSpawn, "Kümes avlusu"); }
    private void GoToFold() { Teleport(foldSpawn, "Koyun ağılı"); }

    private void Teleport(Transform destination, string label)
    {
        if (player == null || destination == null) { SetStatus("Bu bölmenin noktası bulunamadı."); return; }
        player.TeleportTo(destination.position, destination.rotation);
        SetStatus(label + " bölmesine geçildi.");
    }
}