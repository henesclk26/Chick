using System;
using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public sealed class GameTimeManager : MonoBehaviour
{
    [SerializeField, Range(0, 18)] private int dayStartHour = 7;
    [SerializeField, Min(0.01f)] private float realMinutesPerGameDay = 10;
    [SerializeField] private bool loadExistingSave = true;
    [SerializeField] private ChickPlayerController player;
    [SerializeField] private FreeOrbitThirdPersonCamera orbit;
    [SerializeField] private FarmSaveSystem saves;
    [SerializeField] private ClockHUDController hud;
    [SerializeField] private DayTransitionController transition;
    [SerializeField] private TimeOfDayVisualController visuals;
    public const int DayEndMinutes = 19 * 60;
    public int CurrentDay { get; private set; } = 1;
    public float CurrentMinutes { get; private set; }
    public int CurrentHour => (int)CurrentMinutes / 60;
    public int CurrentMinute => (int)CurrentMinutes % 60;
    public float NormalizedDayTime => Mathf.InverseLerp(dayStartHour * 60, DayEndMinutes, CurrentMinutes);
    public bool IsTransitioning { get; private set; } = true;
    public bool IsRevealingGameplay { get; private set; }
    public bool IsPaused { get; set; }
    public event Action<int> OnMinuteChanged;
    public event Action<int> OnDayStarted;
    public event Action<int> OnDayEnding;
    public event Action<float> OnTimeNormalizedChanged;
    private int lastMinute = -1;
    private bool playerWasEnabled, orbitWasEnabled;
    private void Awake()
    {
        playerWasEnabled = player.enabled;
        orbitWasEnabled = orbit.enabled;
        player.enabled = false;
        orbit.enabled = false;
        CurrentMinutes = dayStartHour * 60;
    }
    private IEnumerator Start()
    {
        hud.Bind(); transition.Bind(); hud.SetVisible(false);
        FarmSaveData data = null;
        try { if (loadExistingSave) data = saves.Load(); }
        catch (Exception error) { Fail(error); }
        if (failed) yield break;
        if (data != null)
        {
            CurrentDay = data.day;
            CurrentMinutes = Mathf.Clamp(data.minutes, dayStartHour * 60, DayEndMinutes);
            var capsule = player.GetComponent<CharacterController>();
            bool enabled = capsule.enabled;
            capsule.enabled = false;
            player.transform.SetPositionAndRotation(data.playerPosition, data.playerRotation);
            capsule.enabled = enabled;
            var growth = player.GetComponent<PlayerGrowthController>();
            if (growth != null) growth.SetForm(data.playerForm == 1 ? PlayerGrowthController.Form.Chicken : PlayerGrowthController.Form.Chick);
            orbit.SetZoomDistance(data.zoom);
            if (data.hasCameraOrbit)
                orbit.SetOrbitAngles(data.cameraYaw, data.cameraPitch);
            orbit.SnapToTarget();
            EdibleObject.RestoreConsumedIds(data.consumedEdibleIds);
            WatermelonSliceBites.RestoreAll(data.foodBites);
        }
        Publish();
        yield return transition.InitialHold();
        yield return transition.Morning(CurrentDay, PrepareHudForReveal);
        ResumeGameplay();
    }
    private void Update()
    {
        if (IsTransitioning || IsPaused) return;
        CurrentMinutes = Mathf.Min(DayEndMinutes, CurrentMinutes + Time.deltaTime *
            (DayEndMinutes - dayStartHour * 60) / (Mathf.Max(0.01f, realMinutesPerGameDay) * 60));
        Publish();
        if (CurrentMinutes >= DayEndMinutes) StartCoroutine(EndDay());
    }
    private void Publish()
    {
        int minute = (int)CurrentMinutes;
        if (minute != lastMinute)
        {
            lastMinute = minute;
            hud.SetMinute(minute);
            OnMinuteChanged?.Invoke(minute);
        }
        visuals.Evaluate(NormalizedDayTime);
        OnTimeNormalizedChanged?.Invoke(NormalizedDayTime);
    }
    public void RefreshVisuals()
    {
        if (visuals != null) visuals.Evaluate(NormalizedDayTime);
    }
    private IEnumerator EndDay()
    {
        IsTransitioning = true;
        IsRevealingGameplay = false;
        playerWasEnabled = player.enabled; orbitWasEnabled = orbit.enabled;
        player.enabled = false; orbit.enabled = false;
        hud.SetVisible(false);
        OnDayEnding?.Invoke(CurrentDay);
        yield return transition.Blackout();
        transition.Saving();
        yield return null; // Paint the saving state before dispatching I/O.
        var snapshot = new FarmSaveData
        {
            day = CurrentDay + 1, minutes = dayStartHour * 60,
            playerPosition = player.transform.position, playerRotation = player.transform.rotation,
            zoom = orbit.UserDesiredDistance,
            hasCameraOrbit = true,
            cameraYaw = orbit.OrbitYaw,
            cameraPitch = orbit.OrbitPitch,
            playerForm = player.GetComponent<PlayerGrowthController>() != null ? (int)player.GetComponent<PlayerGrowthController>().CurrentForm : 0,
            consumedEdibleIds = EdibleObject.CaptureConsumedIds(),
            foodBites = WatermelonSliceBites.CaptureAll()
        };
        System.Threading.Tasks.Task task = null;
        try { task = saves.SaveAsync(snapshot); }
        catch (Exception error) { Fail(error); }
        if (failed) yield break;
        while (!task.IsCompleted) yield return null;
        if (task.IsFaulted || task.IsCanceled)
        {
            Fail(task.Exception ?? new Exception("Save cancelled."));
            yield break;
        }
        CurrentDay = snapshot.day;
        CurrentMinutes = snapshot.minutes;
        Publish();
        yield return transition.Morning(CurrentDay, PrepareHudForReveal);
        ResumeGameplay();
    }
    private void PrepareHudForReveal()
    {
        // The blackout covers both panels; make them visible before it fades away.
        IsRevealingGameplay = true;
        hud.SetVisible(true);
        var growth = FindFirstObjectByType<GrowthProgressController>();
        if (growth != null) growth.RefreshForReveal();
    }
    private void ResumeGameplay()
    {
        player.enabled = playerWasEnabled; orbit.enabled = orbitWasEnabled;
        hud.SetVisible(true); IsTransitioning = false; IsRevealingGameplay = false;
        OnDayStarted?.Invoke(CurrentDay);
    }
    private bool failed;
    private void Fail(Exception error)
    {
        failed = true; IsTransitioning = true; IsRevealingGameplay = false;
        Debug.LogError("Day flow stopped: " + error);
        transition.Failure(error.Message);
    }
    private void OnDestroy()
    {
        if (player != null) player.enabled = playerWasEnabled;
        if (orbit != null) orbit.enabled = orbitWasEnabled;
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void DebugSetTime(float minutes)
    {
        if (IsTransitioning) return;
        CurrentMinutes = Mathf.Clamp(minutes, dayStartHour * 60, DayEndMinutes);
        Publish();
    }
    [ContextMenu("Debug/Advance One Hour")] private void AdvanceHour() => DebugSetTime(CurrentMinutes + 60);
    [ContextMenu("Debug/End Day")] private void EndDayNow() => DebugSetTime(DayEndMinutes);
    [ContextMenu("Debug/Morning")] private void MorningNow() => DebugSetTime(dayStartHour * 60);
    [ContextMenu("Debug/Noon")] private void NoonNow() => DebugSetTime(720);
    [ContextMenu("Debug/Sunset")] private void SunsetNow() => DebugSetTime(1080);
#endif
}
