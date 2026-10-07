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

// Opt-in Play Mode check for the tomato plant: a peck drops a whole tomato, which is then eaten like a strawberry
// (no seeds) and counted as "Domates". Never attached to a saved scene; never writes the player's save.
public sealed class TomatoPlayVerification : MonoBehaviour
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
    private EdibleObject[] Tomatoes() => plant.GetComponentsInChildren<EdibleObject>(true);
    // Height of a fallen tomato's lowest point over the terrain under it (plants may be sunk a little into the ground).
    private static float AboveGround(EdibleObject tomato)
    {
        Bounds bounds = tomato.GetComponentInChildren<Renderer>().bounds;
        Terrain terrain = Terrain.activeTerrain;
        float ground = terrain.SampleHeight(bounds.center) + terrain.transform.position.y;
        return bounds.min.y - ground;
    }
    private int Eaten(string key) =>
        statistics.GetSnapshot(false).Where(entry => entry.key == key).Select(entry => entry.count).FirstOrDefault();

    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
    }

    private IEnumerator StandAtPlant(float distance, float angle = 0f)
    {
        Vector3 away = Quaternion.Euler(0f, angle, 0f) * Vector3.back;
        Vector3 spot = plant.transform.position + away * distance;
        player.TeleportTo(spot + Vector3.up * .05f, Quaternion.LookRotation(-away));
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
        mouse = InputSystem.AddDevice<Mouse>("TomatoTestMouse");

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

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/TomatoPlant/TomatoPlant.prefab");
        Check(prefab != null && prefab.GetComponent<BerryPlant>() != null, "The TomatoPlant prefab carries BerryPlant");
        plant = FindObjectsByType<BerryPlant>(FindObjectsSortMode.None).FirstOrDefault(p => p.FoodKey == "tomato_fruit");
        Check(plant != null, "A tomato plant is in the scene");
        if (plant == null) { Finish(); yield break; }

        var tomatoes = Tomatoes();
        int ripe = tomatoes.Length;
        Check(ripe == 17 && plant.HangingCount == 17 && tomatoes.All(t => !t.enabled && !t.CanBeEaten()),
            $"17 ripe tomatoes hang and cannot be eaten yet (found {ripe})");
        Check(plant.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Tomato_Unripe"))
            .All(t => t.GetComponentInParent<EdibleObject>() == null), "Green tomatoes stay decoration");

        yield return StandAtPlant(1.6f);
        yield return Click();
        yield return new WaitForSeconds(.2f);
        Check(plant.HangingCount == ripe, "Pecking far from the plant drops nothing");

        // First peck at the plant: a tomato falls toward the chick and can be eaten on the ground.
        yield return StandAtPlant(.62f);
        yield return Click();
        Check(plant.HangingCount == ripe - 1, $"One peck at the plant drops one tomato ({plant.HangingCount} left hanging)");
        yield return new WaitForSeconds(.7f);
        var dropped = Tomatoes().FirstOrDefault(t => t.enabled);
        Check(dropped != null && dropped.CanBeEaten(), "The fallen tomato is edible");
        if (dropped != null)
        {
            float ground = plant.transform.position.y;
            Vector3 flat = dropped.transform.position - plant.transform.position; flat.y = 0f;
            Check(Mathf.Abs(AboveGround(dropped)) < .04f && flat.magnitude > .4f,
                $"It lands on the ground ({AboveGround(dropped):0.000} m over it) clear of the mound ({flat.magnitude:0.00} m)");
            yield return new WaitForSeconds(1f); // peck lock
            Vector3 approach = flat.normalized;
            Vector3 stand = dropped.BitePosition + approach * .14f;
            player.TeleportTo(new Vector3(stand.x, ground + .05f, stand.z), Quaternion.LookRotation(-approach));
            yield return new WaitForSeconds(.6f);
            int before = Eaten("tomato_fruit");
            int seeds = Eaten("tomato");
            yield return Click();
            float deadline = Time.time + 3f;
            while (!dropped.IsConsumed && Time.time < deadline) yield return null;
            yield return new WaitForSeconds(.4f);
            Check(dropped.IsConsumed && Eaten("tomato_fruit") == before + 1 && Eaten("tomato") == seeds,
                $"Eating it counts one Domates, not tomato seeds ({before} -> {Eaten("tomato_fruit")})");
        }

        // Every tomato can be shaken down.
        yield return new WaitForSeconds(1f);
        int guard = 0;
        while (plant.HangingCount > 0 && guard++ < 40)
        {
            yield return StandAtPlant(.62f, guard * 37f);
            yield return Click();
            yield return new WaitForSeconds(1f);
        }
        Check(plant.HangingCount == 0, $"All tomatoes can be shaken down ({plant.HangingCount} left after {guard} pecks)");
        float highest = Tomatoes().Where(t => !t.IsConsumed).Select(AboveGround).DefaultIfEmpty(0f).Max();
        Check(highest < .04f, $"Every fallen tomato lies on the ground (highest {highest:0.000} m over it)");
        Check(EdibleObject.CaptureConsumedIds().Any(id => id.StartsWith("tomato_fruit-")), "Eaten tomatoes are saved by id");
        Finish();
    }

    private void Update() => mouse?.MakeCurrent();

    private void Finish()
    {
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("TOMATO VERIFICATION\n" + Result);
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
