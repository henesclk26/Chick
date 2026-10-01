using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public sealed class InGamePauseMenuController : MonoBehaviour
{
    [SerializeField] private MainMenuGameplayGate gameplayGate;
    [SerializeField] private GameTimeManager timeManager;
    [SerializeField] private ChickPlayerController player;
    [SerializeField] private FreeOrbitThirdPersonCamera orbitCamera;
    [SerializeField] private SettingsMenuController settings;
    [SerializeField] private FarmSaveSystem saves;

    private VisualElement root;
    private VisualElement home;
    private VisualElement backdrop;
    private VisualElement footer;
    private VisualElement pauseOverlay;
    private VisualElement settingsPage;
    private Button resumeButton;
    private Button settingsButton;
    private Button saveExitButton;
    private Label pauseStatus;
    private bool paused;
    private bool saving;
    private float previousTimeScale = 1f;
    private bool previousPlayerEnabled;
    private bool previousOrbitEnabled;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;

    public bool IsPaused => paused;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();
        root = document.rootVisualElement;
        home = root.Q("home");
        backdrop = root.Q("menu-backdrop");
        footer = root.Q("footer");
        pauseOverlay = root.Q("pause-overlay");
        settingsPage = root.Q("settings-page");
        resumeButton = root.Q<Button>("pause-resume");
        settingsButton = root.Q<Button>("pause-settings");
        saveExitButton = root.Q<Button>("pause-save-exit");
        pauseStatus = root.Q<Label>("pause-status");

        if (gameplayGate == null) gameplayGate = FindFirstObjectByType<MainMenuGameplayGate>();
        if (timeManager == null) timeManager = FindFirstObjectByType<GameTimeManager>();
        if (player == null) player = FindFirstObjectByType<ChickPlayerController>();
        if (orbitCamera == null) orbitCamera = FindFirstObjectByType<FreeOrbitThirdPersonCamera>();
        if (settings == null) settings = GetComponent<SettingsMenuController>();
        if (saves == null) saves = FindFirstObjectByType<FarmSaveSystem>();

        if (pauseOverlay != null) pauseOverlay.AddToClassList("hidden");
        if (settingsPage != null)
        {
            settingsPage.AddToClassList("hidden");
            settingsPage.RemoveFromClassList("in-game-settings");
        }

        if (resumeButton != null) resumeButton.clicked += Resume;
        if (settingsButton != null) settingsButton.clicked += OpenSettings;
        if (saveExitButton != null) saveExitButton.clicked += SaveAndExitClicked;
        if (pauseStatus != null) pauseStatus.text = string.Empty;
    }

    private void OnDisable()
    {
        if (resumeButton != null) resumeButton.clicked -= Resume;
        if (settingsButton != null) settingsButton.clicked -= OpenSettings;
        if (saveExitButton != null) saveExitButton.clicked -= SaveAndExitClicked;
        if (paused && !saving) Resume();
    }

    private void Update()
    {
        if (gameplayGate == null || !gameplayGate.IsReleased) return;
        if (timeManager != null && timeManager.IsTransitioning) return;
        if (GameplayJournalController.BlocksEscapeThisFrame || DeveloperPanelController.BlocksEscapeThisFrame) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

        if (settingsPage != null && !settingsPage.ClassListContains("hidden"))
        {
            BackFromSettings();
        }
        else if (paused)
        {
            Resume();
        }
        else
        {
            Pause();
        }
    }

    private void Pause()
    {
        if (paused || root == null || pauseOverlay == null) return;

        paused = true;
        previousTimeScale = Time.timeScale;
        previousPlayerEnabled = player != null && player.enabled;
        previousOrbitEnabled = orbitCamera != null && orbitCamera.enabled;
        previousCursorLockState = UnityEngine.Cursor.lockState;
        previousCursorVisible = UnityEngine.Cursor.visible;

        if (timeManager != null) timeManager.IsPaused = true;
        if (player != null) player.enabled = false;
        if (orbitCamera != null) orbitCamera.enabled = false;

        Time.timeScale = 0f;
        root.style.display = DisplayStyle.Flex;
        home?.AddToClassList("hidden");
        backdrop?.AddToClassList("hidden");
        footer?.AddToClassList("hidden");
        if (settingsPage != null)
        {
            settingsPage.AddToClassList("hidden");
            settingsPage.AddToClassList("in-game-settings");
        }
        pauseOverlay.RemoveFromClassList("hidden");

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        resumeButton?.Focus();
    }

    private void OpenSettings()
    {
        if (!paused || settingsPage == null) return;

        pauseOverlay.AddToClassList("hidden");
        settingsPage.AddToClassList("in-game-settings");
        settingsPage.RemoveFromClassList("hidden");
        settings?.Show(BackFromSettings);
        root.Q<Button>("settings-back")?.Focus();
    }

    private void SaveAndExitClicked()
    {
        if (!paused || saving) return;
        StartCoroutine(SaveAndExit());
    }

    private IEnumerator SaveAndExit()
    {
        saving = true;
        resumeButton?.SetEnabled(false);
        settingsButton?.SetEnabled(false);
        saveExitButton?.SetEnabled(false);
        if (pauseStatus != null) pauseStatus.text = "Kayıt alınıyor…";

        if (saves == null)
        {
            SaveFailed("Kayıt sistemi bulunamadı.");
            yield break;
        }

        var snapshot = new FarmSaveData
        {
            day = timeManager != null ? timeManager.CurrentDay : 1,
            minutes = timeManager != null ? timeManager.CurrentMinutes : 420f,
            playerPosition = player != null ? player.transform.position : Vector3.zero,
            playerRotation = player != null ? player.transform.rotation : Quaternion.identity,
            zoom = orbitCamera != null ? orbitCamera.UserDesiredDistance : 0.78f,
            hasCameraOrbit = orbitCamera != null,
            cameraYaw = orbitCamera != null ? orbitCamera.OrbitYaw : 0f,
            cameraPitch = orbitCamera != null ? orbitCamera.OrbitPitch : 0f,
            playerForm = player != null && player.GetComponent<PlayerGrowthController>() != null ? (int)player.GetComponent<PlayerGrowthController>().CurrentForm : 0,
            consumedEdibleIds = EdibleObject.CaptureConsumedIds(),
            foodBites = WatermelonSliceBites.CaptureAll()
        };

        System.Threading.Tasks.Task write = null;
        try
        {
            write = saves.SaveAsync(snapshot);
        }
        catch (Exception error)
        {
            SaveFailed(error.Message);
            yield break;
        }

        while (write != null && !write.IsCompleted) yield return null;
        if (write == null || write.IsFaulted || write.IsCanceled)
        {
            SaveFailed(write?.Exception?.GetBaseException()?.Message ?? "Kayıt tamamlanamadı.");
            yield break;
        }

        // SampleScene is both the gameplay scene and the main-menu scene.
        // Reloading it resets the one-way gameplay gate and shows the embedded menu.
        Time.timeScale = 1f;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void SaveFailed(string reason)
    {
        saving = false;
        resumeButton?.SetEnabled(true);
        settingsButton?.SetEnabled(true);
        saveExitButton?.SetEnabled(true);
        if (pauseStatus != null) pauseStatus.text = "Kayıt yapılamadı. Oyunda kalabilirsin.";
        Debug.LogWarning("Save and exit failed: " + reason);
    }

    private void BackFromSettings()
    {
        if (!paused || settingsPage == null) return;

        settingsPage.AddToClassList("hidden");
        pauseOverlay.RemoveFromClassList("hidden");
        resumeButton?.Focus();
    }

    private void Resume()
    {
        if (!paused || saving) return;

        if (settingsPage != null)
        {
            settingsPage.AddToClassList("hidden");
            settingsPage.RemoveFromClassList("in-game-settings");
        }
        if (pauseOverlay != null) pauseOverlay.AddToClassList("hidden");

        if (timeManager != null) timeManager.IsPaused = false;
        if (player != null) player.enabled = previousPlayerEnabled;
        if (orbitCamera != null) orbitCamera.enabled = previousOrbitEnabled;

        Time.timeScale = previousTimeScale;
        UnityEngine.Cursor.lockState = previousCursorLockState;
        UnityEngine.Cursor.visible = previousCursorVisible;
        if (root != null) root.style.display = DisplayStyle.None;
        paused = false;
        if (pauseStatus != null) pauseStatus.text = string.Empty;
    }
}
