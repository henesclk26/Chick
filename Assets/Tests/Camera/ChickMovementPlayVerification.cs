#if UNITY_EDITOR
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in Play Mode check. Never attached to the saved scene or included in builds.
[DefaultExecutionOrder(-100)]
public sealed class ChickMovementPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private readonly StringBuilder report = new StringBuilder();
    private int failures;
    private Mouse testMouse;
    private Keyboard testKeyboard;
    private InputSettings originalSettings, testSettings;
    private bool originalBackground;
    private GameObject ground, ledge;

    private void Check(bool passed, string description)
    {
        report.AppendLine((passed ? "PASS " : "FAIL ") + description);
        if (!passed) failures++;
    }

    private void Update()
    {
        if (testMouse != null && testMouse.added) testMouse.MakeCurrent();
        if (testKeyboard != null && testKeyboard.added) testKeyboard.MakeCurrent();
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
        testMouse = InputSystem.AddDevice<Mouse>("ChickMovementTestMouse");
        testKeyboard = InputSystem.AddDevice<Keyboard>("ChickMovementTestKeyboard");

        foreach (var clock in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            clock.StopAllCoroutines();
            clock.enabled = false;
        }

        var player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        var capsule = player.GetComponent<CharacterController>();
        var orbit = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        player.enabled = true;
        player.GetComponent<Animator>().enabled = true;
        orbit.enabled = true;
        Time.timeScale = 1f;

        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "MovementTestGround_TEMP";
        ground.transform.position = TestAreaLayout.Origin + new Vector3(20f, -.05f, 20f);
        ground.transform.localScale = new Vector3(4f, .1f, 4f);
        capsule.enabled = false;
        player.transform.SetPositionAndRotation(TestAreaLayout.Origin + new Vector3(20f, .002f, 20f), Quaternion.identity);
        capsule.enabled = true;
        orbit.SetOrbitAngles(0f, 8f);
        orbit.SnapToTarget();
        Physics.SyncTransforms();
        yield return new WaitForSeconds(.2f);

        Vector3 stillPosition = player.transform.position;
        float chickYaw = player.transform.eulerAngles.y;
        float cameraYaw = orbit.OrbitYaw;
        InputSystem.QueueStateEvent(testMouse,
            new MouseState { delta = new Vector2(200f, 0f) }.WithButton(MouseButton.Right));
        yield return new WaitForSeconds(.3f);
        float cameraTurn = Mathf.DeltaAngle(cameraYaw, orbit.OrbitYaw);
        float chickTurn = Mathf.DeltaAngle(chickYaw, player.transform.eulerAngles.y);
        Check(Mathf.Abs(cameraTurn - 24f) < 1f && Mathf.Abs(cameraTurn - chickTurn) < 1f &&
            Vector3.Distance(stillPosition, player.transform.position) < .02f,
            "RMB orbits camera and turns chick on its axis without travel");

        InputSystem.QueueStateEvent(testMouse, new MouseState());
        yield return new WaitForSeconds(.1f);
        chickYaw = player.transform.eulerAngles.y;
        cameraYaw = orbit.OrbitYaw;
        InputSystem.QueueStateEvent(testMouse, new MouseState { delta = new Vector2(100f, 0f) });
        yield return new WaitForSeconds(.2f);
        Check(Mathf.Abs(Mathf.DeltaAngle(cameraYaw, orbit.OrbitYaw) - 12f) < 1f &&
            Mathf.Abs(Mathf.DeltaAngle(chickYaw, player.transform.eulerAngles.y)) < 1f,
            "Normal mouse orbit leaves chick facing unchanged");

        ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ledge.name = "MovementTestLedge_TEMP";
        ledge.transform.position = TestAreaLayout.Origin + new Vector3(20f, .08f, 20.55f);
        ledge.transform.localScale = new Vector3(1f, .16f, .9f);
        capsule.enabled = false;
        player.transform.SetPositionAndRotation(TestAreaLayout.Origin + new Vector3(20f, .002f, 20f), Quaternion.identity);
        capsule.enabled = true;
        orbit.SetOrbitAngles(0f, 8f);
        orbit.SnapToTarget();
        Physics.SyncTransforms();
        yield return new WaitForSeconds(.2f);
        Check(Mathf.Abs(capsule.stepOffset) < .0001f, "Automatic step height is disabled");

        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.W));
        yield return new WaitForSeconds(.35f);
        Check(player.transform.position.y < .06f && player.transform.position.z < TestAreaLayout.Origin.z + 20.13f,
            "W alone cannot climb the 0.16-high ledge");
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
        yield return new WaitForSeconds(.1f);

        capsule.enabled = false;
        player.transform.SetPositionAndRotation(TestAreaLayout.Origin + new Vector3(20f, .002f, 20f), Quaternion.identity);
        capsule.enabled = true;
        Physics.SyncTransforms();
        yield return new WaitForSeconds(.15f);
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.W, Key.Space));
        yield return null;
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.W));
        yield return new WaitForSeconds(.22f);
        Check(player.transform.position.y > .16f && player.transform.position.z > TestAreaLayout.Origin.z + 20.15f,
            "W+Space carries the chick upward and forward");
        yield return new WaitForSeconds(.35f);
        Check(player.transform.position.y > .13f && player.transform.position.z > TestAreaLayout.Origin.z + 20.3f,
            "Forward jump reaches and lands on the ledge");

        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("CHICK MOVEMENT VERIFICATION\n" + Result);
        Cleanup();
    }

    private void Cleanup()
    {
        if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
        if (testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
        if (originalSettings != null) InputSystem.settings = originalSettings;
        if (testSettings != null) Destroy(testSettings);
        if (ground != null) Destroy(ground);
        if (ledge != null) Destroy(ledge);
        Application.runInBackground = originalBackground;
    }

    private void OnDestroy() => Cleanup();
}
#endif
