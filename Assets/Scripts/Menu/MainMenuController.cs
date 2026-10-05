using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public sealed class MainMenuController : MonoBehaviour
{
    [SerializeField] private Vector3 newGameSpawn = Vector3.zero;
    [SerializeField] private Transform newGameSpawnPoint;
    public Vector3 NewGameSpawnPosition => newGameSpawnPoint != null ? newGameSpawnPoint.position : newGameSpawn;
    public Quaternion NewGameSpawnRotation => newGameSpawnPoint != null ? Quaternion.Euler(0f, newGameSpawnPoint.eulerAngles.y, 0f) : Quaternion.identity;
    [SerializeField] private SettingsMenuController settings;
    [SerializeField] private FarmSaveSystem saves;
    [SerializeField] private MainMenuGameplayGate gameplayGate;
    private VisualElement root, home, popup, loading;
    private Button resume;
    private Label status;
    private bool busy;
    public bool HasValidSave { get; private set; }
    public bool IsBusy => busy;
    private void OnEnable()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None; UnityEngine.Cursor.visible = true;
        root = GetComponent<UIDocument>().rootVisualElement;
        home = root.Q("home"); popup = root.Q("confirmation"); loading = root.Q("loading");
        resume = root.Q<Button>("continue"); status = root.Q<Label>("save-status");
        if (saves == null) saves = GetComponent<FarmSaveSystem>();
        if (saves == null) saves = FindFirstObjectByType<FarmSaveSystem>();
        if (gameplayGate == null) gameplayGate = FindFirstObjectByType<MainMenuGameplayGate>();
        if (gameplayGate != null && !gameplayGate.enabled) gameplayGate.enabled = true;
        root.Q<Button>("new-game").clicked += RequestNew;
        resume.clicked += Continue;
        root.Q<Button>("settings-button").clicked += OpenSettings;
        root.Q<Button>("quit").clicked += Quit;
        root.Q<Button>("confirm-yes").clicked += ConfirmNew;
        root.Q<Button>("confirm-no").clicked += CancelNew;
        root.RegisterCallback<KeyDownEvent>(KeyDown);
        RefreshSave();
        root.Q("menu-shell").schedule.Execute(() => root.Q("menu-shell").AddToClassList("visible"));
    }
    private void OnDisable()
    {
        if (root == null) return;
        root.Q<Button>("new-game").clicked -= RequestNew; resume.clicked -= Continue;
        root.Q<Button>("settings-button").clicked -= OpenSettings; root.Q<Button>("quit").clicked -= Quit;
        root.Q<Button>("confirm-yes").clicked -= ConfirmNew; root.Q<Button>("confirm-no").clicked -= CancelNew;
        root.UnregisterCallback<KeyDownEvent>(KeyDown);
    }
    public void RefreshSave()
    {
        try
        {
            var data = saves.Load(); HasValidSave = data != null;
            ShowStatus(string.Empty);
        }
        catch (Exception) { HasValidSave = false; ShowStatus("Kayıt okunamadı. Yeni oyun başlatabilirsin."); }
        resume.SetEnabled(HasValidSave && !busy);
    }
    public void RequestNew()
    {
        if (busy) return;
        // Also confirm unreadable saves: invalid does not mean disposable.
        if (File.Exists(saves.SavePath) || File.Exists(saves.SavePath + ".bak"))
        {
            home.SetEnabled(false); popup.RemoveFromClassList("hidden"); root.Q<Button>("confirm-no").Focus();
        }
        else ConfirmNew();
    }
    public void CancelNew()
    {
        if (busy) return;
        popup.AddToClassList("hidden"); home.SetEnabled(true); root.Q<Button>("new-game").Focus();
    }
    public void ConfirmNew() { if (!busy) StartCoroutine(StartGame(true)); }
    public void Continue()
    {
        if (busy) return; RefreshSave(); if (HasValidSave) StartCoroutine(StartGame(false));
    }
    private IEnumerator StartGame(bool fresh)
    {
        if (gameplayGate == null) { ShowStatus("SampleScene oyun başlangıç bağlantısı eksik."); CancelNew(); yield break; }
        busy = true; home.SetEnabled(false); popup.AddToClassList("hidden"); loading.RemoveFromClassList("hidden");
        if (fresh)
        {
            System.Threading.Tasks.Task write = null;
            try { write = saves.SaveAsync(new FarmSaveData { day = 1, minutes = 420, playerPosition = NewGameSpawnPosition, playerRotation = NewGameSpawnRotation, zoom = .83f }); }
            catch (Exception error) { Fail(error); }
            if (write == null) yield break;
            while (!write.IsCompleted) yield return null;
            if (write.IsFaulted || write.IsCanceled) { Fail(write.Exception); yield break; }
        }
        // Menu and gameplay share SampleScene; release its existing day flow in place.
        loading.AddToClassList("hidden");
        root.style.display = DisplayStyle.None;
        gameplayGate.ReleaseGameplay();
    }
    private void Fail(Exception error)
    {
        busy = false; home.SetEnabled(true); loading.AddToClassList("hidden");
        ShowStatus("İşlem tamamlanamadı. Depolama alanını kontrol edip tekrar dene.");
        Debug.LogWarning("Menu operation failed: " + error?.Message);
    }
    // The status line only appears for problems; a healthy menu shows no extra text.
    private void ShowStatus(string message)
    {
        status.text = message;
        status.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
    }
    private void OpenSettings() { if (!busy) { home.AddToClassList("hidden"); settings.Show(BackFromSettings); } }
    private void BackFromSettings() { home.RemoveFromClassList("hidden"); root.Q<Button>("settings-button").Focus(); }
    private void KeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Escape && !popup.ClassListContains("hidden")) { CancelNew(); evt.StopPropagation(); }
    }
    public void Quit()
    {
        if (busy) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

}
