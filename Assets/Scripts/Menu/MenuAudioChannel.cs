using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class MenuAudioChannel : MonoBehaviour
{
    public enum Bus { Music, Sfx }
    [SerializeField] private Bus bus = Bus.Sfx;
    private AudioSource source;
    private float originalVolume;
    private void Awake() { source = GetComponent<AudioSource>(); originalVolume = source.volume; }
    private void OnEnable() => Apply();
    private void Apply() { if (source != null) source.volume = originalVolume * (bus == Bus.Music ? MenuPreferences.Music : MenuPreferences.Sfx); }
    public static void RefreshAll()
    {
        foreach (var channel in Object.FindObjectsByType<MenuAudioChannel>(FindObjectsInactive.Include, FindObjectsSortMode.None)) channel.Apply();
    }
}
