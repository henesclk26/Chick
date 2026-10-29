using UnityEngine;
using UnityEngine.UIElements;

// Menu/gameplay music, ambience, UI clicks, footsteps and idle chicken calls.
// The settings Music slider drives only gameplay music for now; menu music and ambience follow just the master volume.
public sealed class GameAudio : MonoBehaviour
{
    [SerializeField] private AudioClip menuMusic, gameplayMusic, ambience, buttonClick, footsteps, chickenCall;
    [SerializeField, Range(0f, 1f)] private float menuMusicVolume = .5f;
    [SerializeField, Range(0f, 1f), UnityEngine.Serialization.FormerlySerializedAs("musicVolume")] private float gameplayMusicVolume = .5f;
    [SerializeField, Range(0f, 1f)] private float ambienceVolume = .6f, clickVolume = .8f, footstepVolume = .5f, chickenVolume = .7f;
    [SerializeField] private Vector2 chickenCallInterval = new Vector2(8f, 20f);
    [SerializeField] private MainMenuGameplayGate gate;
    [SerializeField] private ChickPlayerController player;
    private AudioSource menuSource, gameplaySource, ambient, sfx, steps;
    private CharacterController body;
    private PlayerGrowthController growth;
    private ChickEatingController eater;
    private float nextChickenCall;

    private void Awake()
    {
        if (gate == null) gate = FindFirstObjectByType<MainMenuGameplayGate>();
        if (player == null) player = FindFirstObjectByType<ChickPlayerController>();
        if (player != null) { body = player.GetComponent<CharacterController>(); growth = player.GetComponent<PlayerGrowthController>(); eater = player.GetComponent<ChickEatingController>(); }
        menuSource = CreateSource("Menu Music", null, menuMusicVolume, true);
        gameplaySource = CreateSource("Gameplay Music", MenuAudioChannel.Bus.Music, gameplayMusicVolume, true);
        ambient = CreateSource("Ambience", null, ambienceVolume, true);
        sfx = CreateSource("Sfx", MenuAudioChannel.Bus.Sfx, 1f, false);
        steps = CreateSource("Footsteps", MenuAudioChannel.Bus.Sfx, footstepVolume, true);
        steps.clip = footsteps;
    }

    private void Start()
    {
        // One callback per UI panel catches every Button click, including documents enabled later on that panel.
        var panels = new System.Collections.Generic.HashSet<IPanel>();
        foreach (var document in FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var panel = document.rootVisualElement?.panel;
            if (panel != null && panels.Add(panel)) panel.visualTree.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
        }
    }

    private void Update()
    {
        bool playing = gate == null || gate.IsReleased;
        PlayLoop(menuSource, playing ? null : menuMusic);
        PlayLoop(gameplaySource, playing ? gameplayMusic : null);
        PlayLoop(ambient, playing ? ambience : null);
        if (!playing || player == null) { steps.Stop(); return; }

        Vector3 velocity = body.velocity; velocity.y = 0f;
        bool walking = body.isGrounded && velocity.sqrMagnitude > .04f && Time.timeScale > 0f;
        steps.pitch = player.IsRunning ? 1.3f : 1f;
        if (walking && !steps.isPlaying) steps.Play();
        else if (!walking && steps.isPlaying) steps.Pause();

        bool chickenIdle = growth != null && growth.CurrentForm == PlayerGrowthController.Form.Chicken && (eater == null || !eater.IsBusy);
        if (!chickenIdle) nextChickenCall = Time.time + Random.Range(chickenCallInterval.x, chickenCallInterval.y);
        else if (Time.time >= nextChickenCall)
        {
            if (chickenCall != null) sfx.PlayOneShot(chickenCall, chickenVolume);
            nextChickenCall = Time.time + Random.Range(chickenCallInterval.x, chickenCallInterval.y);
        }
    }

    private void OnClick(ClickEvent evt)
    {
        if (evt.target is Button button && button.enabledInHierarchy && buttonClick != null) sfx.PlayOneShot(buttonClick, clickVolume);
    }

    private static void PlayLoop(AudioSource source, AudioClip clip)
    {
        if (source.clip == clip && (clip == null || source.isPlaying)) return;
        source.clip = clip;
        if (clip != null) source.Play(); else source.Stop();
    }

    private AudioSource CreateSource(string sourceName, MenuAudioChannel.Bus? bus, float volume, bool loop)
    {
        var child = new GameObject(sourceName);
        child.transform.SetParent(transform, false);
        var source = child.AddComponent<AudioSource>();
        source.playOnAwake = false; source.loop = loop; source.volume = volume;
        // The channel caches the source volume in Awake, so the base volume is set first.
        if (bus.HasValue) child.AddComponent<MenuAudioChannel>().SetBus(bus.Value);
        return source;
    }
}
