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

// Opt-in Play Mode check for the pepper plant: a peck drops a pepper, which lies down on its side and is then eaten
// like a strawberry and counted as "Biber". Never attached to a saved scene; never writes the player's save.
public sealed class PepperPlayVerification : MonoBehaviour
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
    private EdibleObject[] Peppers() => plant.GetComponentsInChildren<EdibleObject>(true);
    // Height of a fallen pepper's lowest point over the terrain under it (plants may be sunk a little into the ground).
    private static float AboveGround(EdibleObject pepper)
    {
        Bounds bounds = pepper.GetComponentInChildren<Renderer>().bounds;
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
        mouse = InputSystem.AddDevice<Mouse>("PepperTestMouse");

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

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/PepperPlant/PepperPlant.prefab");
        Check(prefab != null && prefab.GetComponent<BerryPlant>() != null, "The PepperPlant prefab carries BerryPlant");
        plant = FindObjectsByType<BerryPlant>(FindObjectsSortMode.None).FirstOrDefault(p => p.FoodKey == "pepper");
        Check(plant != null, "A pepper plant is in the scene");
        if (plant == null) { Finish(); yield break; }

        var peppers = Peppers();
        int ripe = peppers.Length;
        int modelled = plant.GetComponentsInChildren<Transform>(true)
            .Count(t => t.name.StartsWith("Pepper_") && t.GetComponent<Renderer>() != null);
        Check(ripe > 0 && ripe == modelled && plant.HangingCount == ripe && peppers.All(t => !t.enabled && !t.CanBeEaten()),
            $"Every pepper ({ripe} of {modelled}) hangs and cannot be eaten yet");

        yield return StandAtPlant(1.6f);
        yield return Click();
        yield return new WaitForSeconds(.2f);
        Check(plant.HangingCount == ripe, "Pecking far from the plant drops nothing");

        // First peck at the plant: a pepper falls toward the chick and can be eaten on the ground.
        yield return StandAtPlant(.62f);
        yield return Click();
        Check(plant.HangingCount == ripe - 1, $"One peck at the plant drops one pepper ({plant.HangingCount} left hanging)");
        yield return new WaitForSeconds(.7f);
        var dropped = Peppers().FirstOrDefault(t => t.enabled);
        Check(dropped != null && dropped.CanBeEaten(), "The fallen pepper is edible");
        if (dropped != null)
        {
            // The plant is sunk a little into the ground: stand on the terrain itself.
            float ground = Terrain.activeTerrain.SampleHeight(dropped.transform.position) + Terrain.activeTerrain.transform.position.y;
            Vector3 flat = dropped.transform.position - plant.transform.position; flat.y = 0f;
            Check(Mathf.Abs(AboveGround(dropped)) < .04f && flat.magnitude > .4f,
                $"It lands on the ground ({AboveGround(dropped):0.000} m over it) clear of the mound ({flat.magnitude:0.00} m)");
            Bounds lying = dropped.GetComponentInChildren<Renderer>().bounds;
            Check(lying.size.y < Mathf.Max(lying.size.x, lying.size.z) * .6f,
                $"It lies on its side, not on end ({lying.size.y:0.000} m tall, {Mathf.Max(lying.size.x, lying.size.z):0.000} m long)");
            yield return new WaitForSeconds(1f); // peck lock
            Vector3 approach = flat.normalized;
            Vector3 stand = dropped.BitePosition + approach * .14f;
            player.TeleportTo(new Vector3(stand.x, ground + .05f, stand.z), Quaternion.LookRotation(-approach));
            yield return new WaitForSeconds(.6f);
            int before = Eaten("pepper");
            yield return Click();
            float deadline = Time.time + 3f;
            while (!dropped.IsConsumed && Time.time < deadline) yield return null;
            yield return new WaitForSeconds(.4f);
            Check(dropped.IsConsumed && Eaten("pepper") == before + 1,
                $"Eating it counts one Biber ({before} -> {Eaten("pepper")})");
        }

        // Every pepper can be shaken down.
        yield return new WaitForSeconds(1f);
        int guard = 0;
        while (plant.HangingCount > 0 && guard++ < 40)
        {
            yield return StandAtPlant(.62f, guard * 37f);
            yield return Click();
            yield return new WaitForSeconds(1f);
        }
        Check(plant.HangingCount == 0, $"All peppers can be shaken down ({plant.HangingCount} left after {guard} pecks)");
        float highest = Peppers().Where(t => !t.IsConsumed).Select(AboveGround).DefaultIfEmpty(0f).Max();
        Check(highest < .04f, $"Every fallen pepper lies on the ground (highest {highest:0.000} m over it)");
        Check(EdibleObject.CaptureConsumedIds().Any(id => id.StartsWith("pepper-")), "Eaten peppers are saved by id");
        Finish();
    }

    private void Update() => mouse?.MakeCurrent();

    private void Finish()
    {
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("PEPPER VERIFICATION\n" + Result);
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
