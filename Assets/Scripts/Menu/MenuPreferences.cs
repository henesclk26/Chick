using UnityEngine;
using UnityEngine.SceneManagement;

// Preferences only. Game progress continues to belong exclusively to FarmSaveSystem.
public static class MenuPreferences
{
    private const string Prefix = "Chick.Menu.";
    public static float Master => PlayerPrefs.GetFloat(Prefix + "Master", 1);
    public static float Music => PlayerPrefs.GetFloat(Prefix + "Music", 1);
    public static float Sfx => PlayerPrefs.GetFloat(Prefix + "Sfx", 1);
    public static float Sensitivity => PlayerPrefs.GetFloat(Prefix + "Sensitivity", 1);
    public static void Save(float master, float music, float sfx, float sensitivity, bool fullscreen, int width, int height, bool vsync, int quality)
    {
        PlayerPrefs.SetFloat(Prefix + "Master", master); PlayerPrefs.SetFloat(Prefix + "Music", music);
        PlayerPrefs.SetFloat(Prefix + "Sfx", sfx); PlayerPrefs.SetFloat(Prefix + "Sensitivity", sensitivity);
        PlayerPrefs.SetInt(Prefix + "Fullscreen", fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "Width", width); PlayerPrefs.SetInt(Prefix + "Height", height);
        PlayerPrefs.SetInt(Prefix + "VSync", vsync ? 1 : 0); PlayerPrefs.SetInt(Prefix + "Quality", quality);
        PlayerPrefs.Save(); ApplyAudio();
    }
    public static void ApplyAudio() { AudioListener.volume = Master; MenuAudioChannel.RefreshAll(); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded;
        if (PlayerPrefs.HasKey(Prefix + "Quality"))
            QualitySettings.SetQualityLevel(Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Quality"), 0, QualitySettings.names.Length - 1));
        if (PlayerPrefs.HasKey(Prefix + "VSync")) QualitySettings.vSyncCount = PlayerPrefs.GetInt(Prefix + "VSync");
        if (!Application.isEditor && PlayerPrefs.HasKey(Prefix + "Width"))
            Screen.SetResolution(PlayerPrefs.GetInt(Prefix + "Width"), PlayerPrefs.GetInt(Prefix + "Height"),
                PlayerPrefs.GetInt(Prefix + "Fullscreen") == 1 ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed);
        ApplyAudio();
    }
    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyAudio();
        foreach (var root in scene.GetRootGameObjects())
        foreach (var camera in root.GetComponentsInChildren<FreeOrbitThirdPersonCamera>(true))
            camera.SetLookSensitivity(Sensitivity);
    }
}
