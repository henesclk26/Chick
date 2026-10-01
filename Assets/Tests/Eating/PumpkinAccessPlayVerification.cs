#if UNITY_EDITOR
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Temporary Play Mode verification of actual Pumpkin_Quarter body collision and peck events.
public sealed class PumpkinAccessPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private readonly StringBuilder report = new StringBuilder();
    private readonly Vector3 origin = TestAreaLayout.Origin + new Vector3(20f, 0f, 20f);
    private ChickPlayerController player;
    private ChickEatingController eater;
    private CharacterController character;
    private GameObject ground;
    private Mouse mouse;
    private InputSettings originalSettings, testSettings;
    private bool oldBackground;
    private int failures, attempts;

    private void Update()
    {
        if (mouse != null && mouse.added) mouse.MakeCurrent();
    }

    private IEnumerator Start()
    {
        Result = "Running";
        oldBackground = Application.runInBackground;
        Application.runInBackground = true;
        originalSettings = InputSystem.settings;
        testSettings = Instantiate(originalSettings);
        testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testSettings;
        mouse = InputSystem.AddDevice<Mouse>("PumpkinAccessMouse");
        foreach (var time in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { time.StopAllCoroutines(); time.enabled = false; }
        player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        eater = player.GetComponent<ChickEatingController>();
        character = player.GetComponent<CharacterController>();
        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "PumpkinAccessGround_TEMP";
        ground.transform.position = origin - Vector3.up * .05f;
        ground.transform.localScale = new Vector3(4f, .1f, 4f);
        player.enabled = eater.enabled = player.GetComponent<Animator>().enabled = true;
        Time.timeScale = 1f;
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Assets/EdibleFoods/Pumpkin_Quarter.prefab");
        yield return new WaitForSeconds(.2f);
        for (int index = 0; index < 8; index++)
        for (int surface = 0; surface < 2; surface++)
        {
            var food = Instantiate(prefab, origin, Quaternion.identity);
            var body = food.transform.Find("Body").GetComponent<MeshCollider>();
            var pieces = food.transform.Find("EdiblePieces").GetComponentsInChildren<EdibleObject>();
            for (int i = 0; i < pieces.Length; i++)
                if (i != index) pieces[i].gameObject.SetActive(false);
            var piece = pieces[index];
            bool xFace = index >= 4;
            bool onTop = surface == 1;
            Vector3 spot = onTop ? origin + new Vector3(.08f, .201f, -.075f)
                : xFace ? origin + new Vector3(-.18f, .001f, -.07f)
                : origin + new Vector3(0f, .001f, .18f);
            float yaw = onTop ? (xFace ? 270f : 0f) : (xFace ? 90f : 180f);
            character.enabled = false;
            player.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));
            character.enabled = true;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.35f);
            float beforeY = player.transform.position.y;
            bool grounded = character.isGrounded;
            var validMethod = typeof(ChickEatingController).GetMethod("IsTargetValid",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            bool validBeforeClick = (bool)validMethod.Invoke(eater, new object[] { piece });
            var queryMethod = typeof(ChickEatingController).GetMethod("FindTarget",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            bool foundBeforeClick = (EdibleObject)queryMethod.Invoke(eater, null) == piece;
            yield return Click();
            bool selected = eater.CurrentTarget == piece;
            float timeout = Time.realtimeSinceStartup + 2f;
            while (eater.IsBusy && Time.realtimeSinceStartup < timeout) yield return null;
            bool passed = selected && piece.IsConsumed && !piece.gameObject.activeSelf &&
                food.transform.Find("Body").gameObject.activeSelf && body.enabled &&
                (onTop ? beforeY > .16f : beforeY < .05f);
            attempts++;
            if (!passed) failures++;
            report.AppendLine((passed ? "PASS " : "FAIL ") + piece.name +
                (onTop ? " from top" : " from side") + " playerY=" + beforeY.ToString("F3") +
                " grounded=" + grounded + " valid=" + validBeforeClick +
                " queried=" + foundBeforeClick + " player=" + player.transform.position.ToString("F3") +
                " seed=" + piece.BitePosition.ToString("F3") +
                " selected=" + selected + " consumed=" + piece.IsConsumed +
                " beakError=" + eater.LastContactError.ToString("F4"));
            food.SetActive(false);
            Destroy(food);
            Result = "Running " + attempts + "/16 failures=" + failures + "\n" + report;
            yield return new WaitForSeconds(.12f);
        }
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("PUMPKIN ACCESS VERIFICATION\n" + Result);
        Cleanup();
    }

    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
    }

    private void OnDestroy() => Cleanup();
    private void Cleanup()
    {
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (originalSettings != null)
        {
            InputSystem.settings = originalSettings;
            originalSettings = null;
            if (testSettings != null) Destroy(testSettings);
        }
        if (ground != null) Destroy(ground);
        Application.runInBackground = oldBackground;
    }
}
#endif
