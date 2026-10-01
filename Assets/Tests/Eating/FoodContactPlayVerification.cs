#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Explicit MCP Play-mode runner. Never attached to an authored scene or included in builds.
public sealed class FoodContactPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private readonly StringBuilder report = new StringBuilder();
    private readonly List<GameObject> temporary = new List<GameObject>();
    private ChickPlayerController player;
    private ChickEatingController eater;
    private Animator animator;
    private Mouse mouse;
    private Keyboard keyboard;
    private InputSettings oldSettings, testSettings;
    private bool oldBackground;
    private int failures, attempts;
    private readonly Vector3 origin = TestAreaLayout.Origin + new Vector3(20, 0, 20);

    private void Update()
    {
        if (mouse != null && mouse.added) mouse.MakeCurrent();
        if (keyboard != null && keyboard.added) keyboard.MakeCurrent();
    }

    private IEnumerator Start()
    {
        Result = "Running";
        oldBackground = Application.runInBackground;
        Application.runInBackground = true;
        oldSettings = InputSystem.settings;
        testSettings = Instantiate(oldSettings);
        testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testSettings;
        mouse = InputSystem.AddDevice<Mouse>("FoodContactMouse");
        keyboard = InputSystem.AddDevice<Keyboard>("FoodContactKeyboard");
        // Bypass menu only in this temporary run. Never start day/save routines.
        foreach (var time in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { time.StopAllCoroutines(); time.enabled = false; }
        player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        eater = player.GetComponent<ChickEatingController>();
        animator = player.GetComponent<Animator>();
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        temporary.Add(ground);
        ground.name = "FoodContactTestGround";
        ground.transform.position = origin - Vector3.up * .05f;
        ground.transform.localScale = new Vector3(4, .1f, 4);
        var cc = player.GetComponent<CharacterController>();
        cc.enabled = false;
        player.transform.SetPositionAndRotation(origin + Vector3.up * .001f, Quaternion.identity);
        cc.enabled = true;
        player.enabled = eater.enabled = animator.enabled = true;
        Time.timeScale = 1;
        yield return new WaitForSeconds(.4f);

        string[] names = { "Watermelon_002", "Watermelon_003", "Watermelon_Slice_2", "Tomato_Piece",
            "Pumpkin_Quarter", "Cookie_1", "Apple_Red_Half_2", "Watermelon_Slice_1", "Cookie_2", "Croissant", "Donut", "Bread_1" };
        // Same valid approach at different fruit heights and front-side angles.
        Vector3[] offsets = { new Vector3(0, .011f, .10f), new Vector3(-.12f, .035f, .065f), new Vector3(.12f, .048f, .065f) };
        foreach (string name in names)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/EdibleFoods/" + name + ".prefab");
            foreach (Vector3 offset in offsets)
            {
                var food = Instantiate(prefab);
                temporary.Add(food);
                // Isolate the contact calculation from body pushing/standing physics.
                food.transform.Find("Body").GetComponent<Collider>().enabled = false;
                var pieces = food.transform.Find("EdiblePieces").GetComponentsInChildren<EdibleObject>();
                // This generic setup places food ahead of the chick (+Z). Tomato's
                // first piece is on its opposite face after the slice is stood up.
                int selectedIndex = name == "Tomato_Piece" ? 8 : 0;
                for (int i = 0; i < pieces.Length; i++)
                    if (i != selectedIndex) pieces[i].gameObject.SetActive(false);
                var selected = pieces[selectedIndex];
                food.transform.position += player.transform.position + offset - selected.BitePosition;
                Physics.SyncTransforms();
                yield return Click();
                bool selectedCorrectly = eater.CurrentTarget == selected;
                float deadline = Time.realtimeSinceStartup + 2;
                while (eater.IsBusy && Time.realtimeSinceStartup < deadline) yield return null;
                bool passed = selectedCorrectly && selected.IsConsumed && !selected.gameObject.activeSelf &&
                    !eater.IsBusy && food.transform.Find("Body").gameObject.activeSelf;
                attempts++;
                if (!passed) failures++;
                report.AppendLine((passed ? "PASS " : "FAIL ") + name + " offset=" + offset.ToString("F3") +
                    " selected=" + selectedCorrectly + " consumed=" + selected.IsConsumed +
                    " contactError=" + eater.LastContactError.ToString("F5"));
                food.SetActive(false);
                Destroy(food);
                Result = "Running " + attempts + "/36 failures=" + failures + "\n" + report;
                yield return new WaitForSeconds(.12f);
            }
        }
        // Ordinary loose grains use the very same input/event route.
        foreach (string name in new[] { "CornSeed", "SunflowerSeed", "RiceGrain", "WheatGrain" })
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/" + name + ".prefab");
            var grain = Instantiate(prefab);
            temporary.Add(grain);
            var edible = grain.GetComponent<EdibleObject>();
            grain.transform.position += player.transform.position + new Vector3(.045f, .011f, .11f) - edible.BitePosition;
            Physics.SyncTransforms();
            yield return Click();
            float deadline = Time.realtimeSinceStartup + 2;
            while (eater.IsBusy && Time.realtimeSinceStartup < deadline) yield return null;
            if (!edible.IsConsumed) failures++;
            report.AppendLine((edible.IsConsumed ? "PASS " : "FAIL ") + "Loose " + name);
            grain.SetActive(false);
            yield return new WaitForSeconds(.12f);
        }
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("FOOD CONTACT VERIFICATION\n" + Result);
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
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (oldSettings != null)
        {
            InputSystem.settings = oldSettings;
            oldSettings = null;
            if (testSettings != null) Destroy(testSettings);
        }
        foreach (var go in temporary) if (go) Destroy(go);
        temporary.Clear();
        Application.runInBackground = oldBackground;
    }
}
#endif
