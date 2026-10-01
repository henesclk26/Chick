#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Play Mode check for quarter-by-quarter watermelon slice eating. Never attached to a saved scene; never
// writes the player's save.
public sealed class WatermelonBitePlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly StringBuilder report = new();
    private int failures;
    private ChickPlayerController player;
    private ChickEatingController eater;
    private FoodStatisticsTracker statistics;
    private Mouse mouse;
    private InputSettings originalInput, testInput;
    private bool originalBackground;
    private float originalCapture;

    private void Check(bool ok, string message)
    {
        if (!ok) failures++;
        report.AppendLine((ok ? "PASS " : "FAIL ") + message);
        Result = "Running\n" + report;
    }

    private int Eaten(string key) =>
        statistics.GetSnapshot(false).Where(entry => entry.key == key).Select(entry => entry.count).FirstOrDefault();

    private static EdibleObject[] Seeds(WatermelonSliceBites slice) =>
        slice.transform.Find("EdiblePieces").GetComponentsInChildren<EdibleObject>(true);

    // Stands the chick in front of one face of the slice, at the bottom centre where the flesh is pecked.
    private IEnumerator StandAt(EdibleObject proxy)
    {
        Vector3 bite = proxy.BitePosition;
        Vector3 normal = proxy.transform.TransformDirection(proxy == proxy.GetComponentInParent<WatermelonSliceBites>().FrontFlesh ? Vector3.down : Vector3.up);
        normal.y = 0f; normal.Normalize();
        Vector3 spot = bite + normal * .15f;
        spot.y = proxy.GetComponentInParent<WatermelonSliceBites>().transform.position.y + .05f;
        player.TeleportTo(spot, Quaternion.LookRotation(-normal));
        yield return new WaitForSeconds(.6f);
    }

    private IEnumerator Peck()
    {
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
        float deadline = Time.time + 3f;
        while (eater.IsBusy && Time.time < deadline) yield return null;
        yield return new WaitForSeconds(.15f);
    }

    private IEnumerator Start()
    {
        Result = "Running";
        originalBackground = Application.runInBackground; Application.runInBackground = true;
        originalCapture = Time.captureDeltaTime; Time.captureDeltaTime = 1f / 120f;
        originalInput = InputSystem.settings; testInput = Instantiate(originalInput);
        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testInput;
        mouse = InputSystem.AddDevice<Mouse>("WatermelonTestMouse");

        // Release gameplay without starting the day clock or loading/writing any save.
        foreach (var clock in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            clock.StopAllCoroutines();
            clock.enabled = false;
        }
        var menu = FindFirstObjectByType<MainMenuController>();
        if (menu != null) { menu.enabled = false; menu.GetComponent<UIDocument>().rootVisualElement.style.display = DisplayStyle.None; }
        typeof(MainMenuGameplayGate).GetField("released", Private)
            .SetValue(FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include), true);
        player = FindFirstObjectByType<ChickPlayerController>();
        eater = player.GetComponent<ChickEatingController>();
        statistics = FindFirstObjectByType<FoodStatisticsTracker>(FindObjectsInactive.Include);
        typeof(FoodStatisticsTracker).GetMethod("Restore", Private).Invoke(statistics, new object[] { new FarmSaveData() });
        player.enabled = true;
        Time.timeScale = 1f;
        player.GetComponent<PlayerGrowthController>().SetForm(PlayerGrowthController.Form.Chick);

        var slices = FindObjectsByType<WatermelonSliceBites>(FindObjectsSortMode.None).OrderBy(s => s.name).ToArray();
        Check(slices.Length >= 2, $"Watermelon_Slice_1 slices that can be eaten in quarters: {slices.Length}");
        var slice = slices.FirstOrDefault(s => s.name == "Watermelon_Slice_1 (Test 2)");
        var other = slices.FirstOrDefault(s => s.name == "Watermelon_Slice_1 (Test 6)");
        if (slice == null || other == null) { Finish(); yield break; }

        var body = slice.transform.Find("Body");
        Check(body.GetComponent<MeshFilter>().sharedMesh.name == "Rind" && slice.Flesh != null &&
              slice.Flesh.gameObject.activeSelf && slice.Flesh.sharedMesh.name == "Flesh_0Eaten",
            "A whole slice shows the rind plus the full flesh");
        Check(slice.FrontFlesh.CanBeEaten() && slice.BackFlesh.CanBeEaten() && slice.EatenQuarters == 0,
            "Both faces of the flesh can be pecked");
        int seedTotal = Seeds(slice).Length;
        int seedsBefore = Eaten("watermelon"), fleshBefore = Eaten("watermelon_flesh");

        // Peck the front face until only the rind is left, recording how many pecks each quarter took.
        yield return StandAt(slice.FrontFlesh);
        var pecksPerQuarter = new List<int>();
        var seedsLeftAfter = new List<int>();
        var meshAfter = new List<string>();
        int pecksThisQuarter = 0, fleshPecks = 0, otherPecks = 0, guard = 0;
        while (slice.EatenQuarters < WatermelonSliceBites.Quarters && guard++ < 24)
        {
            int quarterBefore = slice.EatenQuarters, pecksBefore = slice.PecksThisQuarter;
            yield return Peck();
            bool bitFlesh = slice.EatenQuarters != quarterBefore || slice.PecksThisQuarter != pecksBefore;
            if (!bitFlesh) { otherPecks++; continue; }
            fleshPecks++;
            pecksThisQuarter++;
            if (slice.EatenQuarters == quarterBefore) continue;
            pecksPerQuarter.Add(pecksThisQuarter);
            pecksThisQuarter = 0;
            seedsLeftAfter.Add(Seeds(slice).Count(s => !s.IsConsumed));
            meshAfter.Add(slice.Flesh.gameObject.activeSelf ? slice.Flesh.sharedMesh.name : "(none)");
        }
        Check(pecksPerQuarter.Count == 4 && pecksPerQuarter.All(p => p >= 2 && p <= 3),
            $"Each quarter took 2–3 pecks: {string.Join(", ", pecksPerQuarter)} ({otherPecks} pecks went elsewhere)");
        Check(meshAfter.SequenceEqual(new[] { "Flesh_25Eaten", "Flesh_50Eaten", "Flesh_75Eaten", "(none)" }),
            $"The flesh shrinks 25% → 50% → 75% → gone: {string.Join(" / ", meshAfter)}");
        Check(!slice.Flesh.gameObject.activeSelf && body.gameObject.activeInHierarchy && body.GetComponent<MeshRenderer>().enabled &&
              body.GetComponent<MeshFilter>().sharedMesh.name == "Rind",
            "Only the rind is left at the end");
        Check(seedsLeftAfter.Count == 4 && seedsLeftAfter[0] < seedTotal && seedsLeftAfter.Zip(seedsLeftAfter.Skip(1), (a, b) => b <= a).All(x => x) &&
              seedsLeftAfter.Last() == 0,
            $"Seeds go with their quarter: {seedTotal} → {string.Join(" → ", seedsLeftAfter)}");
        Check(Eaten("watermelon") - seedsBefore == seedTotal,
            $"The eaten seeds count as Karpuz çekirdeği ({Eaten("watermelon") - seedsBefore}/{seedTotal})");
        Check(Eaten("watermelon_flesh") - fleshBefore == 1, $"The finished slice counts once as Karpuz ({Eaten("watermelon_flesh") - fleshBefore})");
        Check(slice.FrontFlesh.IsConsumed && slice.BackFlesh.IsConsumed, "Both flesh faces are done");
        int statsAtEnd = statistics.GetSnapshot(false).Sum(entry => entry.count);
        yield return Peck();
        Check(slice.EatenQuarters == WatermelonSliceBites.Quarters && !slice.Flesh.gameObject.activeSelf &&
              statistics.GetSnapshot(false).Sum(entry => entry.count) == statsAtEnd,
            "Pecking the bare rind eats nothing");

        // The back face of another slice bites the same flesh.
        yield return StandAt(other.BackFlesh);
        guard = 0;
        while (other.EatenQuarters == 0 && guard++ < 8) yield return Peck();
        Check(other.EatenQuarters == 1 && other.Flesh.sharedMesh.name == "Flesh_25Eaten",
            "Pecking the back face eats the first quarter too");
        yield return Peck();
        int otherPecksNow = other.PecksThisQuarter;

        // Save round trip through the save file format; then a load puts a slice back mid-way.
        var data = new FarmSaveData { foodBites = WatermelonSliceBites.CaptureAll() };
        var loaded = JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(data));
        var savedSlice = loaded.foodBites.FirstOrDefault(e => e.id == slice.SaveId);
        var savedOther = loaded.foodBites.FirstOrDefault(e => e.id == other.SaveId);
        Check(savedSlice != null && savedSlice.stage == 4 && savedOther != null && savedOther.stage == 1 && savedOther.bites == otherPecksNow,
            $"Stages are saved (finished slice 4, other slice 1 with {otherPecksNow} peck(s) into the next quarter)");
        int statsBefore = Eaten("watermelon");
        WatermelonSliceBites.RestoreAll(new[] { new FoodBiteSaveEntry { id = other.SaveId, stage = 3, bites = 1, bitesNeeded = 3 } });
        Check(other.EatenQuarters == 3 && other.PecksThisQuarter == 1 && other.Flesh.sharedMesh.name == "Flesh_75Eaten" &&
              Seeds(other).All(s => s.IsConsumed) && Eaten("watermelon") == statsBefore,
            "Loading a 75%-eaten slice shows the 25% band, hides its eaten seeds and does not count them again");
        Finish();
    }

    private void Update() => mouse?.MakeCurrent();

    private void Finish()
    {
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("WATERMELON BITE VERIFICATION\n" + Result);
        Cleanup();
    }

    private void Cleanup()
    {
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        mouse = null;
        if (originalInput != null) InputSystem.settings = originalInput;
        if (testInput != null) Destroy(testInput);
        Application.runInBackground = originalBackground; Time.captureDeltaTime = originalCapture;
    }

    private void OnDestroy() => Cleanup();
}
#endif
