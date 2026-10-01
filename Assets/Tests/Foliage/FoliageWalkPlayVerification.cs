#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in Play Mode check. Never attached to the saved scene or included in builds.
// Walks the player with real input in both forms, through interactive grass/flowers and, as a control, across
// open ground with no plants nearby. Reports sustained stalls (the player visibly stops), single-frame hitches
// (no horizontal progress for one frame), the contacts seen in those frames, frame-time spikes and GC allocations.
[DefaultExecutionOrder(-100)]
public sealed class FoliageWalkPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    public int runsPerGroup = 14;

    private sealed class Stats { public int runs, sustained, hitchFrames, frames; }

    private readonly StringBuilder report = new StringBuilder();
    private readonly List<string> frameHits = new List<string>();
    private readonly List<float> frameTimes = new List<float>();
    private readonly Dictionary<string, int> hitchContacts = new Dictionary<string, int>();
    private Keyboard testKeyboard;
    private InputSettings originalSettings, testSettings;
    private bool originalBackground;
    private ProfilerRecorder gcAlloc;
    private long gcMax, gcTotal;
    private int gcFrames;

    private void Update()
    {
        if (testKeyboard != null && testKeyboard.added) testKeyboard.MakeCurrent();
    }

    // Runs on ChickPlayer, so CharacterController contacts arrive here.
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        var mesh = hit.collider.GetComponent<MeshFilter>();
        string name = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.name : hit.collider.name;
        frameHits.Add(name + "(ny=" + hit.normal.y.ToString("F1") + ")");
    }

    private IEnumerator Start()
    {
        Result = "Running";
        originalBackground = Application.runInBackground;
        Application.runInBackground = true;
        originalSettings = InputSystem.settings;
        testSettings = Instantiate(originalSettings);
        testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testSettings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testSettings;
        testKeyboard = InputSystem.AddDevice<Keyboard>("FoliageWalkTestKeyboard");
        gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");

        foreach (var clock in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            clock.StopAllCoroutines();
            clock.enabled = false;
        }

        var player = GetComponent<ChickPlayerController>();
        var growth = GetComponent<PlayerGrowthController>();
        var orbit = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        player.enabled = true;
        player.GetComponent<Animator>().enabled = true;
        orbit.enabled = true;
        Time.timeScale = 1f;

        var plants = new List<Renderer>();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (r.sharedMaterial != null && r.sharedMaterial.shader.name == "Chick/InteractiveFoliage" && r.bounds.size.y > 0.25f)
                plants.Add(r);

        var random = new System.Random(11);
        foreach (var form in new[] { PlayerGrowthController.Form.Chick, PlayerGrowthController.Form.Chicken })
        {
            if (form == PlayerGrowthController.Form.Chicken) growth.SetForm(form);
            yield return new WaitForSeconds(0.5f);
            var through = new Stats();
            var open = new Stats();
            for (int n = 0; n < runsPerGroup; n++)
            {
                var plant = plants[random.Next(plants.Count)];
                float angle = random.Next(8) * 45f;
                bool run = n % 3 == 2;
                var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 start = plant.bounds.center - dir * (Mathf.Max(plant.bounds.extents.x, plant.bounds.extents.z) + 0.7f);
                yield return Walk(player, orbit, start, angle, run, through, plant.name);

                // Control: same heading and speed, sideways onto open ground away from any plant.
                if (TryFindOpenGround(plants, plant.bounds.center, out var openStart))
                    yield return Walk(player, orbit, openStart, angle, run, open, "open ground");
            }
            report.AppendLine(form + " through plants: " + Describe(through));
            report.AppendLine(form + " open ground:    " + Describe(open));
        }

        frameTimes.Sort();
        float median = frameTimes.Count > 0 ? frameTimes[frameTimes.Count / 2] : 0f;
        float p99 = frameTimes.Count > 0 ? frameTimes[Mathf.Min(frameTimes.Count - 1, (int)(frameTimes.Count * 0.99f))] : 0f;
        float worst = frameTimes.Count > 0 ? frameTimes[frameTimes.Count - 1] : 0f;
        int spikes = 0;
        foreach (float t in frameTimes) if (t > median * 2.5f) spikes++;
        report.AppendLine("frames=" + frameTimes.Count + " median=" + (median * 1000f).ToString("F2") + "ms p99=" +
            (p99 * 1000f).ToString("F2") + "ms worst=" + (worst * 1000f).ToString("F2") + "ms spikes(>2.5x median)=" + spikes);
        report.AppendLine("GC alloc per frame: avg=" + (gcFrames > 0 ? gcTotal / gcFrames : 0) + "B max=" + gcMax + "B");
        foreach (var contact in hitchContacts) report.AppendLine("hitch-frame contacts " + contact.Key + " x" + contact.Value);

        Result = "DONE\n" + report;
        Debug.Log("FOLIAGE WALK VERIFICATION\n" + Result);
        Cleanup();
    }

    private static string Describe(Stats s) =>
        "runs=" + s.runs + " sustainedStalls=" + s.sustained + " hitchFrames=" + s.hitchFrames + "/" + s.frames;

    private static bool TryFindOpenGround(List<Renderer> plants, Vector3 near, out Vector3 start)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var candidate = near + Quaternion.Euler(0f, attempt * 30f, 0f) * Vector3.forward * (3f + attempt * 0.5f);
            bool clear = true;
            foreach (var p in plants)
                if ((p.bounds.center - candidate).sqrMagnitude < 9f) { clear = false; break; }
            if (clear && !Physics.CheckSphere(candidate + Vector3.up * 0.4f, 1.8f, ~(1 << 8), QueryTriggerInteraction.Ignore))
            {
                start = candidate;
                return true;
            }
        }
        start = default;
        return false;
    }

    private IEnumerator Walk(ChickPlayerController player, FreeOrbitThirdPersonCamera orbit, Vector3 start, float angle,
        bool run, Stats stats, string label)
    {
        stats.runs++;
        var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        if (Physics.Raycast(start + Vector3.up * 2f, Vector3.down, out var ground, 4f)) start = ground.point + Vector3.up * 0.02f;
        player.TeleportTo(start, Quaternion.LookRotation(dir));
        orbit.SetOrbitAngles(angle, orbit.OrbitPitch);
        orbit.SnapToTarget();
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
        yield return new WaitForSeconds(0.25f);

        InputSystem.QueueStateEvent(testKeyboard, run ? new KeyboardState(Key.W, Key.LeftShift) : new KeyboardState(Key.W));
        var speeds = new List<float>();
        var hits = new List<string>();
        var dts = new List<float>();
        Vector3 last = player.transform.position;
        float elapsed = 0f;
        while (elapsed < 1.6f)
        {
            frameHits.Clear();
            yield return null;
            elapsed += Time.deltaTime;
            frameTimes.Add(Time.unscaledDeltaTime);
            if (gcAlloc.Valid) { long a = gcAlloc.LastValue; gcTotal += a; gcFrames++; if (a > gcMax) gcMax = a; }
            Vector3 now = player.transform.position;
            var delta = now - last; delta.y = 0f;
            last = now;
            if (elapsed > 0.35f)
            {
                speeds.Add(delta.magnitude / Mathf.Max(Time.deltaTime, 1e-4f));
                hits.Add(frameHits.Count > 0 ? string.Join(",", frameHits) : "none");
                dts.Add(Time.deltaTime);
            }
        }
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
        if (speeds.Count < 4) yield break;

        var sorted = new List<float>(speeds);
        sorted.Sort();
        float typical = sorted[sorted.Count * 3 / 4];
        int streak = 0;
        bool sustained = false;
        stats.frames += speeds.Count;
        for (int i = 0; i < speeds.Count; i++)
        {
            if (speeds[i] >= typical * 0.35f) { streak = 0; continue; }
            stats.hitchFrames++;
            string key = hits[i] + " dt=" + (dts[i] * 1000f).ToString("F0") + "ms";
            string contactKey = hits[i];
            hitchContacts[contactKey] = hitchContacts.TryGetValue(contactKey, out int c) ? c + 1 : 1;
            if (++streak >= 3 && !sustained)
            {
                sustained = true;
                stats.sustained++;
                report.AppendLine("  SUSTAINED stall " + label + " angle=" + angle + (run ? " run" : " walk") + " at " +
                    player.transform.position.ToString("F2") + " -> " + key);
            }
        }
    }

    private void Cleanup()
    {
        if (testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
        if (originalSettings != null) InputSystem.settings = originalSettings;
        Application.runInBackground = originalBackground;
        if (testSettings != null) Destroy(testSettings);
        gcAlloc.Dispose();
    }

    private void OnDestroy() => Cleanup();
}
#endif
