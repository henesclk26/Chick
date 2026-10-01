#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in real Input System verification; not part of the saved scene or player build.
[DefaultExecutionOrder(-100)]
public sealed class ReversedOrbitPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private FreeOrbitThirdPersonCamera orbit;
    private Mouse mouse;
    private Keyboard keyboard;
    private InputSettings original, settings;
    private bool background;
    private GameObject wall;
    private readonly StringBuilder report = new StringBuilder();
    private int failures;
    private float Read(string field) => (float)typeof(FreeOrbitThirdPersonCamera).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(orbit);
    private void Check(bool ok, string label) { report.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
    private void Update()
    {
        if (mouse != null && mouse.added) mouse.MakeCurrent();
        if (keyboard != null && keyboard.added) keyboard.MakeCurrent();
    }
    private void Input(bool locked, float x = 0, float y = 0, float scroll = 0)
    {
        var state = new MouseState { delta = new Vector2(x, y), scroll = new Vector2(0, scroll) };
        if (locked) state = state.WithButton(MouseButton.Right);
        InputSystem.QueueStateEvent(mouse, state);
    }
    private IEnumerator Start()
    {
        Result = "Running"; background = Application.runInBackground; Application.runInBackground = true;
        original = InputSystem.settings; settings = Instantiate(original);
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = settings;
        var player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        float timeout = Time.realtimeSinceStartup + 20;
        while (!player.enabled && Time.realtimeSinceStartup < timeout) yield return null;
        if (!player.enabled) { Result = "FAIL startup"; Cleanup(); yield break; }
        orbit = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        mouse = InputSystem.AddDevice<Mouse>("ReverseOrbitMouse"); keyboard = InputSystem.AddDevice<Keyboard>("ReverseOrbitKeyboard");
        yield return new WaitForSeconds(.2f);
        float yaw = Read("yaw"), pitch = Read("pitch");
        // Settle the pre-existing orbit filter before recording the exact lock baseline.
        // Coroutine resumes before LateUpdate; sampling an unsettled tail would measure
        // one legitimate pre-press camera frame as if it happened after pressing RMB.
        Input(false, 1500, 40); yield return new WaitForSeconds(.8f);
        Check(Mathf.Abs(Read("yaw") - yaw - 180) < .2f && Mathf.Abs(Read("pitch") - pitch + 4) < .1f, "1 Normal mouse orbits to front with unchanged .12/.10 sensitivity and smoothing");
        // RMB now keeps orbit available and turns the chick through the same yaw.
        yaw = Read("yaw"); pitch = Read("pitch"); float zoom = orbit.UserDesiredDistance;
        float chickYaw = player.transform.eulerAngles.y;
        Input(true, 300, 200); yield return new WaitForSeconds(.3f);
        Check(Mathf.Abs(Read("yaw") - yaw - 36f) < .2f &&
            Mathf.Abs(Read("pitch") - pitch + 20f) < .2f &&
            Mathf.Abs(Mathf.DeltaAngle(chickYaw, player.transform.eulerAngles.y) - 36f) < .2f &&
            orbit.UserDesiredDistance == zoom, "2 RMB orbits camera and turns chick without changing zoom");
        yaw = Read("yaw"); pitch = Read("pitch");
        Vector3 playerBefore = player.transform.position, cameraBefore = orbit.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        Input(true, -500, -100); yield return new WaitForSeconds(.8f);
        Vector3 playerMove = player.transform.position - playerBefore, cameraMove = orbit.transform.position - cameraBefore;
        playerMove.y = cameraMove.y = 0;
        Check(playerMove.magnitude > .5f && (playerMove - cameraMove).magnitude < .01f &&
            Mathf.Abs(Read("yaw") - yaw + 60f) < .2f && Mathf.Abs(Read("pitch") - pitch - 10f) < .2f,
            "3 WASD movement and camera follow continue while RMB turns");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yaw = Read("yaw"); pitch = Read("pitch");
        Input(false); yield return new WaitForSeconds(.15f);
        Check(Read("yaw") == yaw && Read("pitch") == pitch, "4 RMB release without delta has no snap/reset");
        Input(false, 100, -20); yield return new WaitForSeconds(.25f);
        Check(Mathf.Abs(Read("yaw") - yaw - 12) < .1f && Mathf.Abs(Read("pitch") - pitch - 2) < .1f, "4 Release immediately restores mouse orbit from existing angle");
        float wheel = original.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms ? 1 : 120;
        foreach (bool locked in new[] { false, true })
        {
            orbit.SetZoomDistance(.61f); Input(locked, 0, 0, wheel); yield return new WaitForSeconds(.2f);
            Check(Mathf.Abs(orbit.UserDesiredDistance - .57f) < .001f, "5 Wheel unchanged in " + (locked ? "RMB turning" : "normal orbit") + " mode");
        }
        yaw = Read("yaw"); pitch = Read("pitch");
        foreach (bool locked in new[] { true, false })
        {
            Input(locked); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            yield return new WaitForSeconds(2.2f);
            Check(Read("yaw") == yaw && Read("pitch") == pitch && !orbit.EnableSmartRecenter, "6 No auto recenter during side travel, RMB=" + locked);
        }
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Input(true);
        orbit.SetZoomDistance(.83f); yield return new WaitForSeconds(.3f);
        Vector3 direction = Quaternion.Euler(Read("pitch"), Read("yaw"), 0) * Vector3.back;
        var target = GameObject.Find("CameraTarget").transform;
        wall = new GameObject("ReverseOrbitWall_TEMP"); wall.layer = LayerMask.NameToLayer("CameraBlocker");
        wall.transform.position = target.position + direction * .4f;
        wall.AddComponent<BoxCollider>().size = Vector3.one * .12f;
        Physics.SyncTransforms(); yield return new WaitForSeconds(.2f);
        Check(Read("currentDistance") < .5f && orbit.UserDesiredDistance == .83f, "Collision remains authoritative while RMB turns");
        wall.SetActive(false); yield return new WaitForSeconds(.7f);
        Check(Mathf.Abs(Read("currentDistance") - .83f) < .01f, "Collision recovery returns to selected zoom");
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Cleanup();
    }
    private void Cleanup()
    {
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (original != null) InputSystem.settings = original;
        if (settings != null) Destroy(settings);
        if (wall != null) Destroy(wall);
        Application.runInBackground = background;
    }
    private void OnDestroy() => Cleanup();
}
#endif
