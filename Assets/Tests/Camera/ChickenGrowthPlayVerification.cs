#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[DefaultExecutionOrder(-100)]
public sealed class ChickenGrowthPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private readonly StringBuilder report = new StringBuilder();
    private int failures;
    private Keyboard keyboard;
    private Mouse mouse;
    private InputSettings originalSettings, testSettings;
    private bool background;
    private GameObject ground, seed;
    private ChickPlayerController player;
    private PlayerGrowthController growth;
    private void Update() { keyboard?.MakeCurrent(); mouse?.MakeCurrent(); }
    private void Keys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    private void Check(bool ok, string text)
    {
        if (!ok) failures++;
        report.AppendLine((ok ? "PASS " : "FAIL ") + text);
        Result = "Running\n" + report;
    }
    private bool State(string name, int layer = 0)
    {
        var a = player.ActiveAnimator;
        return a.GetCurrentAnimatorStateInfo(layer).IsName(name) ||
            (a.IsInTransition(layer) && a.GetNextAnimatorStateInfo(layer).IsName(name));
    }
    private IEnumerator Action(Key key, string state, float waitAfter = 1.1f)
    {
        Keys(key); yield return new WaitForSeconds(.22f);
        Check(State(state), key + " uses chicken " + state);
        Keys(); yield return new WaitForSeconds(waitAfter);
    }
    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return new WaitForSeconds(.8f);
    }
    private IEnumerator Start()
    {
        Result = "Running";
        background = Application.runInBackground; Application.runInBackground = true;
        originalSettings = InputSystem.settings; testSettings = Instantiate(originalSettings);
        testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testSettings;
        keyboard = InputSystem.AddDevice<Keyboard>("GrowthTestKeyboard");
        mouse = InputSystem.AddDevice<Mouse>("GrowthTestMouse");
        foreach (var clock in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { clock.StopAllCoroutines(); clock.enabled = false; }
        player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        growth = player.GetComponent<PlayerGrowthController>();
        var eater = player.GetComponent<ChickEatingController>();
        var cc = player.GetComponent<CharacterController>();
        var orbit = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        player.enabled = eater.enabled = orbit.enabled = true;
        player.ActiveAnimator.enabled = true;
        Time.timeScale = 1f;
        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "GrowthTestGround_TEMP";
        ground.transform.position = TestAreaLayout.Origin + new Vector3(20, -.05f, 20);
        ground.transform.localScale = new Vector3(10, .1f, 10);
        cc.enabled = false;
        player.transform.SetPositionAndRotation(TestAreaLayout.Origin + new Vector3(20, .002f, 20), Quaternion.identity);
        cc.enabled = true;
        orbit.SetOrbitAngles(0, 8); orbit.SnapToTarget();
        Physics.SyncTransforms(); yield return new WaitForSeconds(.25f);
        var chickSkins = player.GetComponentsInChildren<SkinnedMeshRenderer>();
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/WheatGrain.prefab");
        seed = Instantiate(prefab, player.transform.position + Vector3.forward * .45f, Quaternion.identity);
        var edible = seed.GetComponent<EdibleObject>();
        yield return Click();
        Check(!edible.IsConsumed && !eater.IsBusy, "Chick cannot eat the distant seed at 0.45");
        float chickStartY = player.transform.position.y;
        float chickApex = chickStartY;
        Keys(Key.Space); yield return null; Keys();
        for (int i = 0; i < 35; i++)
        {
            yield return null;
            chickApex = Mathf.Max(chickApex, player.transform.position.y);
        }
        Check(chickApex - chickStartY > .21f && chickApex - chickStartY < .31f,
            "Chick jump reaches roughly half its former height");
        yield return new WaitForSeconds(.3f);
        Vector3 originalPosition = player.transform.position;
        float cameraY = orbit.transform.position.y;
        Keys(Key.RightBracket); yield return null; Keys();
        yield return new WaitForSeconds(.8f);
        Check(growth.CurrentForm == PlayerGrowthController.Form.Chicken, "Ü position switches to chicken");
        Check(chickSkins.All(s => !s.enabled) && player.ActiveAnimator != player.GetComponent<Animator>() &&
            player.ActiveAnimator.GetComponentsInChildren<SkinnedMeshRenderer>().Any(s => s.enabled),
            "Only chicken is visible and its Animator is active");
        Check(Vector3.Distance(originalPosition, player.transform.position) < .02f && Mathf.Abs(cc.height - .51f) < .001f && cc.stepOffset == 0,
            "Position preserved; chicken capsule and no auto-step active");
        Check(Mathf.Abs(orbit.UserDesiredDistance - 1.6f) < .01f && orbit.transform.position.y > cameraY + .1f &&
            Mathf.Abs(orbit.OrbitYaw) < .01f && Mathf.Abs(orbit.OrbitPitch - 8) < .01f,
            "Camera moves farther/higher and preserves viewing angles");
        Check(State("idle") && State("wing_idle", 1), "Chicken idle body and wings");
        yield return Click();
        Check(edible.IsConsumed && eater.LastImpactSucceeded && eater.LastContactError <= .012f,
            "Chicken eats distant seed through real animation events; error=" + eater.LastContactError);
        Check(!eater.IsBusy, "Eating releases action lock");
        Keys(Key.W); yield return new WaitForSeconds(.25f); Check(State("move"), "W chicken walk");
        Keys(Key.W, Key.LeftShift); yield return new WaitForSeconds(.25f); Check(State("run"), "Shift chicken run");
        Keys(); yield return new WaitForSeconds(.2f);
        yield return Action(Key.C, "crouch", .25f);
        yield return Action(Key.E, "peck");
        yield return Action(Key.P, "peep");
        yield return Action(Key.F, "flapping");
        yield return Action(Key.Q, "damage");
        yield return Action(Key.X, "down");
        var beforeJump = player.transform.position;
        Keys(Key.W, Key.Space); yield return new WaitForSeconds(.12f);
        Check(State("jump") && player.transform.position.y > beforeJump.y + .1f &&
            player.transform.position.z > beforeJump.z + .05f, "Chicken W+Space forward jump");
        Keys(); yield return new WaitForSeconds(.7f);
        Check(State("wing_idle", 1), "Chicken landing ends wing flapping");
        var controller = player.ActiveAnimator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        Check(controller != null && controller.layers.SelectMany(l => l.stateMachine.states).All(s =>
            Mathf.Abs(s.state.speed - (s.state.name == "move" ? 1.7f : s.state.name == "run" ? 2.3f : 1f)) < .001f),
            "Chicken playback rates match current form settings");
        var save = JsonUtility.FromJson<FarmSaveData>(JsonUtility.ToJson(new FarmSaveData { playerForm = (int)growth.CurrentForm, zoom = orbit.UserDesiredDistance }));
        growth.SetForm(PlayerGrowthController.Form.Chick);
        Check(chickSkins.All(s => s.enabled) && cc.height == .22f && orbit.UserDesiredDistance < .84f,
            "Form reset restores original chick settings");
        growth.SetForm((PlayerGrowthController.Form)save.playerForm); orbit.SetZoomDistance(save.zoom); orbit.SnapToTarget();
        Check(growth.CurrentForm == PlayerGrowthController.Form.Chicken && Mathf.Abs(orbit.UserDesiredDistance - save.zoom) < .001f &&
            JsonUtility.FromJson<FarmSaveData>("{\"version\":1}").playerForm == 0,
            "Form and zoom round trip; older saves default to chick");
        Keys(Key.RightBracket); yield return null; Keys(); yield return new WaitForSeconds(.1f);
        Check(player.transform.GetComponentsInChildren<ChickenEatEventRelay>(true).Length == 1,
            "Repeated Ü does not duplicate the chicken");
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("CHICKEN GROWTH VERIFICATION\n" + Result);
        Cleanup();
    }
    private void OnDestroy() => Cleanup();
    private void Cleanup()
    {
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        keyboard = null; mouse = null;
        if (originalSettings != null) InputSystem.settings = originalSettings;
        if (testSettings != null) Destroy(testSettings);
        if (seed != null) Destroy(seed);
        // Leave the platform until Play Mode ends so the review camera can inspect the chicken.
        Application.runInBackground = background;
    }
}
#endif
