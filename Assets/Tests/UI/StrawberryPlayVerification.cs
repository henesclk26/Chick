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

// Opt-in Play Mode check for edible strawberry plants. Never attached to a saved scene; never writes the player's save.
public sealed class StrawberryPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly StringBuilder report = new();
    private int failures;
    private ChickPlayerController player;
    private ChickEatingController eater;
    private FoodStatisticsTracker statistics;
    private BerryPlant plant;
    private Mouse mouse;
    private Keyboard keyboard;
    private InputSettings originalInput, testInput;
    private bool originalBackground;
    private float originalCapture;

    private void Check(bool ok, string message)
    {
        if (!ok) failures++;
        report.AppendLine((ok ? "PASS " : "FAIL ") + message);
        Result = "Running\n" + report;
    }
    private EdibleObject[] Berries() => plant.GetComponentsInChildren<EdibleObject>(true);
    private int StrawberriesEaten() =>
        statistics.GetSnapshot(false).Where(entry => entry.key == "strawberry").Select(entry => entry.count).FirstOrDefault();

    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
    }

    // Stands the player `distance` from the plant's centre, facing it.
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
        mouse = InputSystem.AddDevice<Mouse>("StrawberryTestMouse");
        keyboard = InputSystem.AddDevice<Keyboard>("StrawberryTestKeyboard");

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
        var growth = player.GetComponent<PlayerGrowthController>();
        growth.SetForm(PlayerGrowthController.Form.Chick);

        var modelPlant = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/StrawberryPlant/StrawberryPlant.fbx");
        var prefabPlant = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/StrawberryPlant/StrawberryPlant.prefab");
        Check(modelPlant.GetComponent<BerryPlant>() != null && prefabPlant.GetComponent<BerryPlant>() != null,
            "Model and prefab both carry BerryPlant, so every placed plant is edible");
        var plants = FindObjectsByType<BerryPlant>(FindObjectsSortMode.None).Where(p => p.FoodKey == "strawberry").ToArray();
        plant = plants.FirstOrDefault();
        Check(plant != null, $"Scene plants with edible berries: {plants.Length}");
        if (plant == null) { Finish(); yield break; }

        var berries = Berries();
        Check(berries.Length == 10 && plant.HangingCount == 10 && berries.All(b => !b.enabled && !b.CanBeEaten()),
            $"10 ripe berries hang and cannot be eaten yet (found {berries.Length})");
        int unripe = plant.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Strawberry_Unripe"));
        Check(unripe == 5 && plant.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Strawberry_Unripe"))
            .All(t => t.GetComponentInParent<EdibleObject>() == null), "5 unripe berries stay decoration");
        Check(berries.Select(b => b.PersistentId).Distinct().Count() == 10, "Each berry has its own save id");

        // The pot is solid: walking straight into it (W walks along the camera's view) stops at its side.
        var pot = plant.transform.Find("Pot");
        Check(pot != null && pot.TryGetComponent(out MeshCollider potCollider) && potCollider.convex && !potCollider.isTrigger,
            "The pot has a solid collider");
        var leaves = plant.transform.Find("Leaves");
        Check(leaves != null && leaves.TryGetComponent(out MeshCollider leafCollider) && leafCollider.convex && !leafCollider.isTrigger,
            "The leaves have a solid collider");
        var berryMeshes = plant.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Strawberry_") && t.GetComponent<Renderer>() != null).ToArray();
        Check(berryMeshes.Length == 15 && berryMeshes.All(t => t.TryGetComponent(out SphereCollider c) && !c.isTrigger && c.gameObject.layer != 9),
            $"All 15 berries (ripe and unripe) are solid, off the Edible layer ({berryMeshes.Count(t => t.GetComponent<SphereCollider>() != null)} solid)");
        yield return StandAtPlant(.6f);
        Vector3 view = Camera.main.transform.forward; view.y = 0f; view.Normalize();
        player.TeleportTo(plant.transform.position - view * .6f + Vector3.up * .05f, Quaternion.LookRotation(view));
        yield return new WaitForSeconds(.5f);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        yield return new WaitForSeconds(1.5f);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return new WaitForSeconds(.3f);
        Vector3 fromPot = player.transform.position - plant.transform.position; fromPot.y = 0f;
        // Tapered pot: ~0.11 m wide at chick height + 0.09 m chick radius.
        Check(fromPot.magnitude > .17f && Vector3.Dot(fromPot, view) < 0f && player.transform.position.y - plant.transform.position.y < .1f,
            $"Walking into the pot stops the chick at its side ({fromPot.magnitude:0.00} m from its centre)");

        // Too far away: nothing falls.
        yield return StandAtPlant(1.2f);
        yield return Click();
        yield return new WaitForSeconds(.2f);
        Check(plant.HangingCount == 10, "Pecking far from the plant drops nothing");

        // At the plant: one berry falls toward the chick and becomes edible on the ground.
        yield return StandAtPlant(.34f);
        yield return Click();
        var anim = player.ActiveAnimator;
        bool pecked = anim.GetBool("peck");
        Check(plant.HangingCount == 9 && pecked, "A peck at the plant shakes one berry loose, with the peck animation");
        yield return new WaitForSeconds(.7f);
        var dropped = Berries().FirstOrDefault(b => b.enabled);
        Check(dropped != null && dropped.CanBeEaten(), "The fallen berry is edible");
        if (dropped != null)
        {
            float ground = plant.transform.position.y;
            Vector3 flat = dropped.transform.position - plant.transform.position; flat.y = 0f;
            Vector3 toPlayer = player.transform.position - plant.transform.position; toPlayer.y = 0f;
            var box = dropped.GetComponent<BoxCollider>();
            var sphere = dropped.GetComponentsInChildren<SphereCollider>().FirstOrDefault(c => !c.isTrigger);
            Check(box != null && box.enabled && !box.isTrigger && sphere != null && !sphere.enabled,
                "On the ground the berry's solid sphere becomes an upright box (no riding up onto it)");
            Check(dropped.BitePosition.y - ground < .07f && flat.magnitude > .2f && Vector3.Angle(flat, toPlayer) < 50f,
                $"It lands on the ground ({dropped.BitePosition.y - ground:0.000} m) clear of the pot ({flat.magnitude:0.00} m), toward the chick");

            // Walk up to it and eat it with a real click.
            yield return new WaitForSeconds(1f); // peck lock
            Vector3 approach = flat.normalized;
            Vector3 stand = dropped.BitePosition + approach * .12f;
            player.TeleportTo(new Vector3(stand.x, ground + .05f, stand.z), Quaternion.LookRotation(-approach));
            yield return new WaitForSeconds(.6f);
            int before = StrawberriesEaten();
            yield return Click();
            float deadline = Time.time + 3f;
            while (!dropped.IsConsumed && Time.time < deadline) yield return null;
            yield return new WaitForSeconds(.4f);
            Check(dropped.IsConsumed && StrawberriesEaten() == before + 1,
                $"Eating it counts as Çilek in the statistics ({before} -> {StrawberriesEaten()})");
        }

        // The chick cannot shake the plant from 0.55 m; the larger chicken can.
        yield return new WaitForSeconds(1f);
        int hanging = plant.HangingCount;
        yield return StandAtPlant(.55f);
        yield return Click();
        Check(plant.HangingCount == hanging, "The chick is too far at 0.55 m");
        growth.SetForm(PlayerGrowthController.Form.Chicken);
        yield return StandAtPlant(.55f);
        yield return Click();
        Check(plant.HangingCount == hanging - 1, "The chicken shakes a berry loose from 0.55 m");
        yield return new WaitForSeconds(1f);
        growth.SetForm(PlayerGrowthController.Form.Chick);

        // Knock the rest down; then the plant is empty.
        int guard = 0;
        while (plant.HangingCount > 0 && guard++ < 20)
        {
            yield return StandAtPlant(.34f, guard * 37f);
            yield return Click();
            yield return new WaitForSeconds(1f);
        }
        Check(plant.HangingCount == 0 && Berries().Count(b => b.enabled || b.IsConsumed) == 10, "All 10 berries can be shaken down");
        float highest = Berries().Where(b => !b.IsConsumed).Select(b => b.BitePosition.y - plant.transform.position.y).DefaultIfEmpty(0f).Max();
        Check(highest < .07f, $"Every fallen berry lies on the ground, never on the chick (highest bite {highest:0.000} m)");
        yield return StandAtPlant(.34f);
        yield return Click();
        Check(!player.ActiveAnimator.GetBool("peck"), "An empty plant no longer reacts to pecks");

        // Save/load: the eaten berry stays gone.
        string[] consumed = EdibleObject.CaptureConsumedIds();
        Check(consumed.Any(id => id.StartsWith("strawberry-")), "Eaten berries are saved by id");
        Finish();
    }

    private void Update() { mouse?.MakeCurrent(); keyboard?.MakeCurrent(); }

    private void Finish()
    {
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("STRAWBERRY VERIFICATION\n" + Result);
        Cleanup();
    }

    private void Cleanup()
    {
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        mouse = null;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        if (originalInput != null) InputSystem.settings = originalInput;
        if (testInput != null) Destroy(testInput);
        Application.runInBackground = originalBackground; Time.captureDeltaTime = originalCapture;
    }

    private void OnDestroy() => Cleanup();
}
#endif
