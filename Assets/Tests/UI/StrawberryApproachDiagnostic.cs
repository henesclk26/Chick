#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Play Mode check: walks the chick into fallen (solid) strawberries through its CharacterController and
// clicks; every click must eat one. Never attached to a saved scene; never writes the player's save.
public sealed class StrawberryApproachDiagnostic : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly StringBuilder report = new();
    private ChickPlayerController player;
    private ChickEatingController eater;
    private BerryPlant plant;
    private Mouse mouse;
    private Keyboard keyboard;
    private InputSettings originalInput, testInput;
    private float originalCapture;

    private T Get<T>(object o, string name) => (T)o.GetType().GetField(name, Private).GetValue(o);

    private IEnumerator Start()
    {
        originalCapture = Time.captureDeltaTime; Time.captureDeltaTime = 1f / 120f;
        Application.runInBackground = true;
        originalInput = InputSystem.settings; testInput = Instantiate(originalInput);
        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testInput;
        mouse = InputSystem.AddDevice<Mouse>("StrawberryDiagMouse");
        keyboard = InputSystem.AddDevice<Keyboard>("StrawberryDiagKeyboard");
        foreach (var clock in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { clock.StopAllCoroutines(); clock.enabled = false; }
        var menu = FindFirstObjectByType<MainMenuController>();
        if (menu != null) { menu.enabled = false; menu.GetComponent<UIDocument>().rootVisualElement.style.display = DisplayStyle.None; }
        typeof(MainMenuGameplayGate).GetField("released", Private)
            .SetValue(FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include), true);
        player = FindFirstObjectByType<ChickPlayerController>();
        eater = player.GetComponent<ChickEatingController>();
        player.enabled = true;
        Time.timeScale = 1f;
        player.GetComponent<PlayerGrowthController>().SetForm(PlayerGrowthController.Form.Chick);
        plant = FindObjectsByType<BerryPlant>(FindObjectsSortMode.None).First(p => p.FoodKey == "strawberry");

        // Knock 6 berries down from the front half, like a player pecking around the plant.
        for (int i = 0; i < 10; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 36f, 0f) * Vector3.back;
            BerryPlant.TryKnockNear(plant.transform.position + dir * .34f, -dir, 1f);
        }
        yield return new WaitForSeconds(.8f);

        int ok = 0, tries = 0;
        foreach (var berry in plant.GetComponentsInChildren<EdibleObject>(true).Where(b => b.enabled && !b.IsConsumed).ToArray())
        {
            if (berry.IsConsumed) continue;
            tries++;
            int hangingBefore = plant.HangingCount;
            Vector3 outward = berry.transform.position - plant.transform.position; outward.y = 0f; outward.Normalize();
            // Start outside the plant, facing inward at the berry.
            Vector3 start = berry.transform.position + outward * .45f; start.y = plant.transform.position.y + .05f;
            player.TeleportTo(start, Quaternion.LookRotation(-outward));
            yield return new WaitForSeconds(.4f);
            // Walk it straight at the berry through the real CharacterController (collisions included).
            var body = player.GetComponent<CharacterController>();
            Vector3 last = player.transform.position; float still = 0f, deadline = Time.time + 1.5f;
            while (Time.time < deadline && still < .15f)
            {
                Vector3 to = berry.transform.position - player.transform.position; to.y = 0f;
                if (to.sqrMagnitude > 1e-4f)
                {
                    player.transform.rotation = Quaternion.LookRotation(to);
                    body.Move(to.normalized * 1f * Time.deltaTime);
                }
                yield return null;
                still = (player.transform.position - last).sqrMagnitude < 1e-8f ? still + Time.deltaTime : 0f;
                last = player.transform.position;
            }
            yield return new WaitForSeconds(.15f);

            // Snapshot the targeting conditions just before the click.
            Vector3 bite = berry.BitePosition;
            Vector3 impact = player.transform.TransformPoint(eater.LocalImpactPoint);
            Vector3 detection = player.transform.position + player.transform.forward * Get<float>(eater, "eatForwardOffset") + Vector3.up * .015f;
            float radius = Get<float>(eater, "eatDetectionRadius") * berry.InteractionAssistMultiplier;
            float assist = Get<float>(eater, "maxContactAssistDistance") * berry.InteractionAssistMultiplier;
            Vector3 planar = bite - player.transform.position; planar.y = 0f;
            float angle = Vector3.Angle(planar, player.transform.forward);
            string snap = $"planar={planar.magnitude:0.000} angle={angle:0} dy={bite.y - impact.y:0.000} (max {berry.MaxVerticalInteractionOffset:0.000}) " +
                          $"toDetect={(bite - detection).magnitude:0.000}/{radius:0.000} toImpact={(bite - impact).magnitude:0.000}/{assist:0.000} " +
                          $"chickY={player.transform.position.y - plant.transform.position.y:0.000} berryY={berry.transform.position.y - plant.transform.position.y:0.000}";

            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return null;
            var chosen = eater.CurrentTarget;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return new WaitForSeconds(1.3f);
            // Pass: the click ate this berry or a touching neighbour in the pile, the chick stayed on the
            // ground (never climbed a berry), and the click did not fall back to shaking the plant.
            bool eaten = chosen != null && chosen.IsConsumed && hangingBefore == plant.HangingCount &&
                         player.transform.position.y - plant.transform.position.y < .03f;
            if (eaten) ok++;
            report.AppendLine($"{(eaten ? "PASS" : "FAIL")} {berry.name}: target={(chosen == berry ? "this" : chosen == null ? "none" : chosen.name)} " +
                              $"knocked={hangingBefore - plant.HangingCount} contactErr={eater.LastContactError:0.000} {snap}");
        }
        Result = $"{(ok == tries && tries > 0 ? "PASS" : "FAIL")} {ok}/{tries} clicks ate a berry\n{report}";
        Debug.Log("STRAWBERRY APPROACH DIAGNOSTIC\n" + Result);
        Cleanup();
    }

    private void Update() { mouse?.MakeCurrent(); keyboard?.MakeCurrent(); }

    private void Cleanup()
    {
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        mouse = null; keyboard = null;
        if (originalInput != null) InputSystem.settings = originalInput;
        if (testInput != null) Destroy(testInput);
        Time.captureDeltaTime = originalCapture;
    }

    private void OnDestroy() => Cleanup();
}
#endif
