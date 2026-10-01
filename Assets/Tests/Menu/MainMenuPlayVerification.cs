#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Explicit smoke run, isolated save files and restored preference/runtime settings.
public sealed class MainMenuPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private readonly StringBuilder report = new StringBuilder();
    private readonly Dictionary<string, object> preferences = new Dictionary<string, object>();
    private readonly List<GameObject> temporary = new List<GameObject>();
    private string testName, path;
    private bool background, cleaned;
    private int quality, vsync;
    private float volume;
    private int failures;
    private void Check(bool ok, string name) { report.AppendLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }
    private MainMenuController Menu => UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
    private VisualElement Root => Menu.GetComponent<UIDocument>().rootVisualElement;
    private void Click(string name)
    {
        var button = Root.Q<Button>(name);
        using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
    }
    private void Isolate(Scene scene, LoadSceneMode mode)
    {
        foreach (var root in scene.GetRootGameObjects())
        foreach (var saves in root.GetComponentsInChildren<FarmSaveSystem>(true))
        {
            typeof(FarmSaveSystem).GetField("fileName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(saves, testName);
            path = saves.SavePath;
        }
        if (Menu != null) Menu.RefreshSave();
    }
    private IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject); Result = "Running";
        background = Application.runInBackground; Application.runInBackground = true;
        quality = QualitySettings.GetQualityLevel(); vsync = QualitySettings.vSyncCount; volume = AudioListener.volume;
        foreach (var name in new[] { "Master", "Music", "Sfx", "Sensitivity", "Fullscreen", "Width", "Height", "VSync", "Quality" })
        {
            string key = "Chick.Menu." + name;
            preferences[key] = !PlayerPrefs.HasKey(key) ? null :
                (name == "Master" || name == "Music" || name == "Sfx" || name == "Sensitivity") ? (object)PlayerPrefs.GetFloat(key) : PlayerPrefs.GetInt(key);
        }
        testName = "menu-verification-" + Guid.NewGuid().ToString("N") + ".json";
        SceneManager.sceneLoaded += Isolate; Isolate(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        yield return null;
        Check(Menu != null && Root.Q<Button>("new-game") != null, "1 Menu loads from authored UXML");
        Check(!Menu.HasValidSave && !Root.Q<Button>("continue").enabledSelf, "2 No save: Continue visible and disabled");
        Click("continue"); yield return null; Check(SceneManager.GetActiveScene().name == "SampleScene" && !UnityEngine.Object.FindFirstObjectByType<MainMenuGameplayGate>().IsReleased, "Disabled Continue keeps the SampleScene menu open");
        var saves = UnityEngine.Object.FindFirstObjectByType<FarmSaveSystem>();
        var write = saves.SaveAsync(new FarmSaveData { day = 4, minutes = 600, playerPosition = TestAreaLayout.Origin + new Vector3(.3f, 0, .4f), zoom = .61f });
        while (!write.IsCompleted) yield return null;
        if (write.IsFaulted) { Result = "FAIL isolated test save: " + write.Exception; Cleanup(); yield break; }
        Menu.RefreshSave(); Check(Menu.HasValidSave && Root.Q<Button>("continue").enabledSelf, "3 Valid existing save enables Continue");
        string originalSave = File.ReadAllText(path);
        Click("new-game"); yield return null;
        Check(!Root.Q("confirmation").ClassListContains("hidden") && !Root.Q("home").enabledSelf, "4 New Game opens UI Toolkit confirmation and locks underlying home");
        Click("confirm-no"); yield return null;
        Check(Root.Q("confirmation").ClassListContains("hidden") && File.ReadAllText(path) == originalSave, "5 HAYIR leaves save byte-for-byte unchanged");
        Click("settings-button"); yield return null;
        Check(!Root.Q("settings-page").ClassListContains("hidden") && Root.Q<Slider>("master") != null && Root.Q<Toggle>("vsync") != null && Root.Q<DropdownField>("resolution") != null, "7 Settings uses sliders/toggles/dropdowns in UI Toolkit");
        Root.Q<Slider>("master").value = 65; Root.Q<Slider>("music").value = 35; Root.Q<Slider>("sfx").value = 75; Root.Q<Slider>("sensitivity").value = 1.25f;
        Root.Q<Toggle>("vsync").value = true;
        var musicObject = new GameObject("MusicChannel_TEMP"); temporary.Add(musicObject);
        var musicSource = musicObject.AddComponent<AudioSource>(); var musicChannel = musicObject.AddComponent<MenuAudioChannel>();
        typeof(MenuAudioChannel).GetField("bus", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(musicChannel, MenuAudioChannel.Bus.Music);
        var sfxObject = new GameObject("SfxChannel_TEMP"); temporary.Add(sfxObject); var sfxSource = sfxObject.AddComponent<AudioSource>(); sfxObject.AddComponent<MenuAudioChannel>();
        Click("settings-apply"); yield return null;
        Check(Mathf.Abs(AudioListener.volume - .65f) < .001f && Mathf.Abs(musicSource.volume - .35f) < .001f && Mathf.Abs(sfxSource.volume - .75f) < .001f, "Audio settings apply independently to master/music/SFX routes");
        Check(QualitySettings.vSyncCount == 1 && Mathf.Abs(MenuPreferences.Sensitivity - 1.25f) < .001f, "Settings persist VSync and sensitivity preference");
        Click("settings-back"); yield return null;
        Check(Root.Q("settings-page").ClassListContains("hidden") && !Root.Q("home").ClassListContains("hidden"), "8 GERİ returns to home");
        Click("settings-button"); yield return null;
        Check(Root.Q<Slider>("master").value == 65 && Root.Q<Slider>("sensitivity").value == 1.25f, "Reopening settings restores saved values"); Click("settings-back");
        Check(UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0 &&
            UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None).All(c => c == null || (c.GetType().Namespace != "UnityEngine.UI" && c.GetType().Name != "TextMeshProUGUI")), "10 No Canvas/TMP/legacy UI components in menu scene");
        Result = "Testing Continue in SampleScene"; Click("continue"); yield return null;
        float limit = Time.realtimeSinceStartup + 20;
        while (SceneManager.GetActiveScene().name != "SampleScene" && Time.realtimeSinceStartup < limit) yield return null;
        var time = UnityEngine.Object.FindFirstObjectByType<GameTimeManager>();
        while (time != null && time.IsTransitioning && Time.realtimeSinceStartup < limit) yield return null;
        Check(time != null && time.CurrentDay == 4 && time.CurrentMinutes >= 600 && time.CurrentMinutes < 620 && !time.IsTransitioning, "3 Continue restores day 4/time via existing day flow");
        var player = GameObject.Find("ChickPlayer");
        Check(player != null && (player.transform.position - (TestAreaLayout.Origin + new Vector3(.3f, 0, .4f))).sqrMagnitude < .01f, "Continue restores saved player position");
        var camera = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        Check(Mathf.Abs(camera.UserDesiredDistance - .61f) < .001f && Mathf.Abs((float)typeof(FreeOrbitThirdPersonCamera).GetField("horizontalSensitivity", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(camera) - .15f) < .001f, "Continue preserves saved zoom and applies only requested sensitivity");
        yield return SceneManager.LoadSceneAsync("SampleScene"); yield return null;
        Result = "Testing New Game DAY 1"; Click("new-game"); yield return null; Click("confirm-yes");
        limit = Time.realtimeSinceStartup + 20;
        while (!UnityEngine.Object.FindFirstObjectByType<MainMenuGameplayGate>().IsReleased && Time.realtimeSinceStartup < limit) yield return null;
        yield return null;
        time = UnityEngine.Object.FindFirstObjectByType<GameTimeManager>(); bool titleSeen = false;
        while (time != null && time.IsTransitioning && Time.realtimeSinceStartup < limit)
        {
            foreach (var doc in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            { var title = doc.rootVisualElement.Q<Label>("DayLabel"); if (title != null && title.text == "DAY 1" && title.resolvedStyle.opacity > .5f) titleSeen = true; }
            yield return null;
        }
        Check(time != null && time.CurrentDay == 1 && time.CurrentMinutes >= 420 && time.CurrentMinutes < 440 && titleSeen, "6 EVET atomically saves fresh day 1 and runs existing visible DAY 1 transition");
        var restored = UnityEngine.Object.FindFirstObjectByType<FarmSaveSystem>().Load();
        var spawnMenu = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
        Check(restored.day == 1 && restored.minutes == 420 &&
            (restored.playerPosition - spawnMenu.NewGameSpawnPosition).sqrMagnitude < .0001f &&
            Quaternion.Angle(restored.playerRotation, spawnMenu.NewGameSpawnRotation) < .01f,
            "New Game uses the configured spawn marker through FarmSaveSystem, not a second system");
        yield return SceneManager.LoadSceneAsync("SampleScene"); yield return null;
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Cleanup(); Menu.RefreshSave();
    }
    private void Cleanup()
    {
        if (cleaned) return; cleaned = true;
        SceneManager.sceneLoaded -= Isolate;
        foreach (var item in preferences)
        { if (item.Value == null) PlayerPrefs.DeleteKey(item.Key); else if (item.Value is float) PlayerPrefs.SetFloat(item.Key, (float)item.Value); else PlayerPrefs.SetInt(item.Key, (int)item.Value); }
        PlayerPrefs.Save(); QualitySettings.SetQualityLevel(quality); QualitySettings.vSyncCount = vsync; AudioListener.volume = volume;
        foreach (var go in temporary) if (go != null) Destroy(go);
        // Only unique files created by this test; never the user's farm-save.json.
        if (path != null && Path.GetFileName(path) == testName && testName.StartsWith("menu-verification-"))
            foreach (var suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        Application.runInBackground = background;
    }
    private void OnDestroy() => Cleanup();
}
#endif
