#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Play Mode check for the grape vine: a peck at the row eats the nearest bunch right where it hangs (nothing
// falls) and counts it as "Üzüm", only from the side of the row the bird stands on (the chicken cannot reach through
// to the far side); every bunch can be eaten from its own side. Never attached to a saved
// scene; never writes the player's save.
public sealed class GrapePlayVerification : MonoBehaviour
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
    private EdibleObject[] Bunches() => plant.GetComponentsInChildren<EdibleObject>(true);
    // Bunches eaten on every vine in the scene (the chicken's reach at a row's end takes in the next row's end).
    private static int EatenOnAllVines() => FindObjectsByType<BerryPlant>(FindObjectsSortMode.None)
        .Where(p => p.FoodKey == "grape").Sum(p => p.GetComponentsInChildren<EdibleObject>(true).Count(b => b.IsConsumed));
    private int Eaten(string key) =>
        statistics.GetSnapshot(false).Where(entry => entry.key == key).Select(entry => entry.count).FirstOrDefault();
    // Any bunch lying edible off the vine (there should never be one).
    private bool AnyDropped() => Bunches().Any(b => b.enabled && !b.IsConsumed);

    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
    }

    // Stand beside the row, off its bed, level with `along` (m along the row) on side +1 / -1, facing the row.
    private IEnumerator StandBeside(float along, int side, float off = .6f)
    {
        Transform row = plant.transform;
        Vector3 away = row.forward * side;  // the row runs along its local X
        Vector3 spot = row.position + row.right * along + away * off;
        float ground = Terrain.activeTerrain.SampleHeight(spot) + Terrain.activeTerrain.transform.position.y;
        player.TeleportTo(new Vector3(spot.x, ground + .05f, spot.z), Quaternion.LookRotation(-away));
        yield return new WaitForSeconds(.9f);
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
        mouse = InputSystem.AddDevice<Mouse>("GrapeTestMouse");

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

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/GrapeVine/GrapeVine.prefab");
        Check(prefab != null && prefab.GetComponent<BerryPlant>() != null, "The GrapeVine prefab carries BerryPlant");
        plant = FindObjectsByType<BerryPlant>(FindObjectsSortMode.None).FirstOrDefault(p => p.FoodKey == "grape");
        Check(plant != null, "A grape vine is in the scene");
        if (plant == null) { Finish(); yield break; }

        int total = Bunches().Length;
        int modelled = plant.GetComponentsInChildren<Transform>(true)
            .Count(t => t.name.StartsWith("Grape_") && t.GetComponent<Renderer>() != null);
        Check(total > 0 && total == modelled && plant.HangingCount == total && Bunches().All(b => !b.enabled && !b.CanBeEaten()),
            $"Every bunch ({total} of {modelled}) hangs and is not food on the ground");

        yield return StandBeside(0f, 1, 1.6f);
        yield return Click();
        yield return new WaitForSeconds(.4f);
        Check(plant.HangingCount == total, "Pecking away from the row eats nothing");

        // One peck at the row: the nearest bunch is eaten right there, in that head bob; nothing falls.
        yield return StandBeside(0f, 1);
        int before = Eaten("grape");
        yield return Click();
        Check(plant.HangingCount == total - 1, $"One peck at the row takes one bunch ({plant.HangingCount} left hanging)");
        yield return new WaitForSeconds(.4f);
        Check(Eaten("grape") == before + 1 && Bunches().Count(b => b.IsConsumed) == 1,
            $"It is eaten where it hangs and counted as one Üzüm ({before} -> {Eaten("grape")})");
        Check(!AnyDropped(), "Nothing falls to the ground");

        // From one side of the row only that side's bunches are eaten, even by the chicken with its longer reach: once
        // they are gone, pecking there takes nothing from the far side.
        player.GetComponent<PlayerGrowthController>().SetForm(PlayerGrowthController.Form.Chicken);
        int Side(EdibleObject b) => plant.transform.InverseTransformPoint(b.transform.position).z >= 0f ? 1 : -1;
        int farHanging = Bunches().Count(b => Side(b) < 0 && !b.IsConsumed);
        int nearHanging = Bunches().Count(b => Side(b) > 0 && !b.IsConsumed);
        for (int i = 0; i < nearHanging + 4; i++)
        {
            yield return StandBeside(-.8f + 1.6f * (i % 5) / 4f, 1, .7f);
            yield return Click();
            yield return new WaitForSeconds(.5f);
        }
        Check(Bunches().Count(b => Side(b) > 0 && !b.IsConsumed) == 0,
            $"The chicken eats every bunch on its side ({nearHanging} of them)");
        Check(Bunches().Count(b => Side(b) < 0 && !b.IsConsumed) == farHanging,
            $"...and none on the far side ({farHanging} still hang there)");

        // Every bunch can be eaten, from both sides and all along the row.
        yield return new WaitForSeconds(.6f);
        int guard = 0;
        while (plant.HangingCount > 0 && guard++ < 80)
        {
            EdibleObject next = Bunches().First(b => !b.IsConsumed);
            Vector3 local = plant.transform.InverseTransformPoint(next.transform.position);
            yield return StandBeside(local.x, local.z >= 0f ? 1 : -1);
            yield return Click();
            yield return new WaitForSeconds(.5f);
        }
        Check(plant.HangingCount == 0 && Bunches().All(b => b.IsConsumed),
            $"Every bunch can be eaten ({plant.HangingCount} left after {guard} more pecks)");
        Check(Eaten("grape") - before == EatenOnAllVines(),
            $"Each counts once: {Eaten("grape") - before} Üzüm for {EatenOnAllVines()} bunches eaten (this vine's {total})");
        Check(!AnyDropped(), "No bunch ever fell to the ground");
        Check(EdibleObject.CaptureConsumedIds().Count(id => id.StartsWith("grape-")) >= total, "Eaten bunches are saved by id");
        Finish();
    }

    private void Update() => mouse?.MakeCurrent();

    private void Finish()
    {
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("GRAPE VERIFICATION\n" + Result);
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
