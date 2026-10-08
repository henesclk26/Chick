#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Play Mode check for the lettuce in Assets/Art/Lettuce (a BerryPlant with stubs and half leaves): a peck
// knocks a leaf off its own side (just clear of the mound) and shows its stub; on the ground the first peck leaves
// half the leaf and the second eats it, counted once as "Marul"; when every leaf is eaten only the stubs remain.
// Never attached to a saved scene; never writes the player's save.
public sealed class LettuceHeadPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly StringBuilder report = new();
    private int failures;
    private ChickPlayerController player;
    private FoodStatisticsTracker statistics;
    private BerryPlant plant;
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

    private EdibleObject[] Leaves() => plant.GetComponentsInChildren<EdibleObject>(true);
    private Transform[] Stubs() => plant.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Stub_")).ToArray();
    private static string MeshName(EdibleObject leaf) => leaf.GetComponentInChildren<MeshFilter>(true).sharedMesh.name;

    private int Eaten(string key) =>
        statistics.GetSnapshot(false).Where(entry => entry.key == key).Select(entry => entry.count).FirstOrDefault();

    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
    }

    private IEnumerator StandAtPlant(float distance, float angle)
    {
        Vector3 away = Quaternion.Euler(0f, angle, 0f) * Vector3.back;
        player.TeleportTo(plant.transform.position + away * distance + Vector3.up * .05f, Quaternion.LookRotation(-away));
        yield return new WaitForSeconds(.9f);
    }

    private IEnumerator StandAtLeaf(EdibleObject leaf)
    {
        Vector3 approach = leaf.transform.position - plant.transform.position; approach.y = 0f; approach.Normalize();
        Vector3 stand = leaf.BitePosition + approach * .14f;
        player.TeleportTo(new Vector3(stand.x, plant.transform.position.y + .05f, stand.z), Quaternion.LookRotation(-approach));
        yield return new WaitForSeconds(.6f);
    }

    // Horizontal distance from the plant's centre to the nearest point of a lying leaf's own (oriented) mesh bounds.
    private float NearestToCentre(Renderer renderer)
    {
        Transform t = renderer.transform;
        Bounds b = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
        Vector3 c = plant.transform.position;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i <= 10; i++)
        for (int k = 0; k <= 10; k++)
        {
            Vector3 local = new Vector3(Mathf.Lerp(b.min.x, b.max.x, i / 10f), b.center.y, Mathf.Lerp(b.min.z, b.max.z, k / 10f));
            Vector3 p = t.TransformPoint(local) - c;
            nearest = Mathf.Min(nearest, new Vector2(p.x, p.z).magnitude);
        }
        return nearest;
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
        mouse = InputSystem.AddDevice<Mouse>("LettuceHeadTestMouse");

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
        statistics = FindFirstObjectByType<FoodStatisticsTracker>(FindObjectsInactive.Include);
        typeof(FoodStatisticsTracker).GetMethod("Restore", Private).Invoke(statistics, new object[] { new FarmSaveData() });
        player.enabled = true;
        Time.timeScale = 1f;
        player.GetComponent<PlayerGrowthController>().SetForm(PlayerGrowthController.Form.Chick);

        plant = FindObjectsByType<BerryPlant>(FindObjectsSortMode.None)
            .FirstOrDefault(p => p.transform.Find("Stub_01") != null && p.transform.Find("Half_01") != null);
        Check(plant != null, "The lettuce (leaves, half leaves and stubs) is in the scene");
        if (plant == null) { Finish(); yield break; }
        int total = Leaves().Length;
        Check(total > 0 && plant.HangingCount == total && Stubs().All(s => !s.gameObject.activeSelf) &&
              plant.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Half_")).All(t => !t.gameObject.activeSelf),
            $"All {total} leaves hang, stubs and half leaves are hidden");

        yield return StandAtPlant(1.6f, 0f);
        yield return Click();
        yield return new WaitForSeconds(.3f);
        Check(plant.HangingCount == total, "Pecking far from the plant drops nothing");

        // A peck at the plant: one leaf falls off its own side and its stub shows.
        var before = Leaves().ToDictionary(l => l, l => l.transform.position);
        yield return StandAtPlant(.7f, 30f);
        yield return Click();
        Check(plant.HangingCount == total - 1, $"One peck at the plant drops one leaf ({plant.HangingCount} left on it)");
        yield return new WaitForSeconds(.8f);
        var dropped = Leaves().FirstOrDefault(l => l.enabled && !l.IsConsumed);
        Check(dropped != null && dropped.CanBeEaten(), "The fallen leaf is edible");
        if (dropped == null) { Finish(); yield break; }
        Vector3 c = plant.transform.position;
        Vector3 from = before[dropped] - c; from.y = 0f;
        Vector3 to = dropped.transform.position - c; to.y = 0f;
        Check(Vector3.Angle(from, to) < 20f, $"It falls on its own side of the plant ({Vector3.Angle(from, to):0}° from where it hung)");
        Transform stub = plant.transform.Find("Stub_" + dropped.name.Replace("Lettuce_", "").Replace(" Pickup", ""));
        Check(stub != null && stub.gameObject.activeSelf, $"Its stub shows on the plant ({stub?.name})");
        Renderer lying = dropped.GetComponentInChildren<Renderer>();
        Check(lying.bounds.size.y < Mathf.Max(lying.bounds.size.x, lying.bounds.size.z) * .6f,
            $"It lies down flat ({lying.bounds.size.y:0.00} m tall, {Mathf.Max(lying.bounds.size.x, lying.bounds.size.z):0.00} m long)");
        Check(NearestToCentre(lying) > .4f, $"It lies clear of the mound (nearest {NearestToCentre(lying):0.00} m from the centre)");

        // On the ground: two pecks, half the leaf after the first, eaten (and counted once) after the second.
        int eaten = Eaten("lettuce");
        string whole = MeshName(dropped);
        yield return new WaitForSeconds(1f); // peck lock
        yield return StandAtLeaf(dropped);
        yield return Click();
        yield return new WaitForSeconds(1f);
        Check(!dropped.IsConsumed && MeshName(dropped).StartsWith("Half_") && Eaten("lettuce") == eaten,
            $"The first peck leaves half of it ({whole} -> {MeshName(dropped)}), not counted yet");
        yield return StandAtLeaf(dropped);   // what is left lies nearer the plant
        yield return Click();
        float deadline = Time.time + 3f;
        while (!dropped.IsConsumed && Time.time < deadline) yield return null;
        yield return new WaitForSeconds(.4f);
        Check(dropped.IsConsumed && Eaten("lettuce") == eaten + 1, $"The second peck eats it: one Marul ({eaten} -> {Eaten("lettuce")})");

        // Knock every other leaf off, then eat them all.
        int guard = 0;
        while (plant.HangingCount > 0 && guard++ < 60)
        {
            yield return new WaitForSeconds(.3f);
            yield return StandAtPlant(.7f, guard * 37f);
            yield return Click();
            yield return new WaitForSeconds(.7f);
        }
        Check(plant.HangingCount == 0 && Stubs().All(s => s.gameObject.activeSelf),
            $"Every leaf can be knocked off and every stub shows ({plant.HangingCount} left after {guard} pecks)");
        float nearest = Leaves().Where(l => !l.IsConsumed).Select(l => NearestToCentre(l.GetComponentInChildren<Renderer>())).DefaultIfEmpty(1f).Min();
        Check(nearest > .35f, $"The fallen leaves lie around the mound, not on it (nearest {nearest:0.00} m)");
        guard = 0;
        while (Leaves().Any(l => !l.IsConsumed) && guard++ < 120)
        {
            var next = Leaves().Where(l => !l.IsConsumed && l.enabled)
                .OrderBy(l => (l.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault();
            if (next == null) break;
            yield return StandAtLeaf(next);
            yield return Click();
            yield return new WaitForSeconds(.9f);
        }
        Check(Leaves().All(l => l.IsConsumed) && Eaten("lettuce") == eaten + total,
            $"All leaves eaten, two pecks each: {Eaten("lettuce") - eaten} Marul in {guard} more pecks");
        Check(Stubs().All(s => s.gameObject.activeSelf) && plant.transform.Find("Mound").gameObject.activeSelf,
            "Only the mound and the stubs are left");
        Check(EdibleObject.CaptureConsumedIds().Count(id => id.StartsWith("lettuce-")) >= total, "Eaten leaves are saved by id");
        Finish();
    }

    private void Update() => mouse?.MakeCurrent();

    private void Finish()
    {
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("LETTUCE HEAD VERIFICATION\n" + Result);
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
