#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Temporary MCP Play Mode run: eat one complete face, walk around, then eat the other.
public sealed class TwoSidedFoodPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private readonly StringBuilder report = new StringBuilder();
    private readonly Vector3 origin = TestAreaLayout.Origin + new Vector3(20f, 0f, 20f);
    private readonly List<GameObject> temporary = new List<GameObject>();
    private ChickPlayerController player;
    private ChickEatingController eater;
    private CharacterController character;
    private Mouse mouse;
    private InputSettings originalSettings, testSettings;
    private bool oldBackground;
    private int failures, pecks;

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
        mouse = InputSystem.AddDevice<Mouse>("TwoSidedFoodMouse");
        foreach (var time in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { time.StopAllCoroutines(); time.enabled = false; }
        player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        eater = player.GetComponent<ChickEatingController>();
        character = player.GetComponent<CharacterController>();
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        temporary.Add(ground);
        ground.name = "TwoSidedFoodGround_TEMP";
        ground.transform.position = origin - Vector3.up * .05f;
        ground.transform.localScale = new Vector3(4f, .1f, 4f);
        player.enabled = eater.enabled = player.GetComponent<Animator>().enabled = true;
        Time.timeScale = 1f;
        yield return new WaitForSeconds(.2f);

        foreach (string name in new[] { "Watermelon_Slice_1", "Watermelon_Slice_2", "Tomato_Piece" })
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Assets/EdibleFoods/" + name + ".prefab");
            var food = Instantiate(prefab, origin, Quaternion.identity);
            temporary.Add(food);
            var all = food.transform.Find("EdiblePieces").GetComponentsInChildren<EdibleObject>();
            var front = new List<EdibleObject>();
            var back = new List<EdibleObject>();
            foreach (var piece in all)
                (piece.transform.localPosition.y > .008f ? front : back).Add(piece);
            foreach (bool frontSide in new[] { true, false })
            {
                var activeSide = frontSide ? front : back;
                var opposite = frontSide ? back : front;
                for (int attempt = 0; attempt < activeSide.Count; attempt++)
                {
                    EdibleObject chosen = null;
                    foreach (var candidate in activeSide)
                        if (!candidate.IsConsumed) { chosen = candidate; break; }
                    if (!chosen) break;
                    Vector3 location = new Vector3(chosen.BitePosition.x, .001f,
                        origin.z + (frontSide ? .17f : -.17f));
                    character.enabled = false;
                    player.transform.SetPositionAndRotation(location,
                        Quaternion.Euler(0f, frontSide ? 180f : 0f, 0f));
                    character.enabled = true;
                    Physics.SyncTransforms();
                    yield return new WaitForSeconds(.18f);
                    int sideBefore = Consumed(activeSide);
                    int otherBefore = Consumed(opposite);
                    yield return Click();
                    float timeout = Time.realtimeSinceStartup + 2f;
                    while (eater.IsBusy && Time.realtimeSinceStartup < timeout) yield return null;
                    int sideAfter = Consumed(activeSide);
                    int otherAfter = Consumed(opposite);
                    bool ok = sideAfter == sideBefore + 1 && otherAfter == otherBefore &&
                        !eater.IsBusy && food.transform.Find("Body").gameObject.activeSelf;
                    pecks++;
                    if (!ok) failures++;
                    report.AppendLine((ok ? "PASS " : "FAIL ") + name +
                        (frontSide ? " front" : " back") + " " + sideAfter + "/" + activeSide.Count +
                        " opposite=" + otherAfter + " grounded=" + character.isGrounded +
                        " contact=" + eater.LastContactError.ToString("F4"));
                    Result = "Running " + pecks + "/48 failures=" + failures + "\n" + report;
                    yield return new WaitForSeconds(.12f);
                }
                if (Consumed(activeSide) != activeSide.Count) failures++;
                if (frontSide && Consumed(back) != 0) failures++;
            }
            if (!food.transform.Find("Body").gameObject.activeSelf) failures++;
            food.SetActive(false);
            Destroy(food);
        }
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("TWO-SIDED FOOD VERIFICATION\n" + Result);
        Cleanup();
    }

    private static int Consumed(List<EdibleObject> pieces)
    {
        int count = 0;
        foreach (var piece in pieces) if (piece.IsConsumed) count++;
        return count;
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
        foreach (var go in temporary) if (go) Destroy(go);
        temporary.Clear();
        Application.runInBackground = oldBackground;
    }
}
#endif
