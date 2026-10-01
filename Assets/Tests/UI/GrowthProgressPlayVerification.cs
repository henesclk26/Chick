#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Explicit, disposable Play Mode verification. Never attached to the saved scene.
public sealed class GrowthProgressPlayVerification : MonoBehaviour
{
    public bool ContinuePhase;
    public static string Result = "Not run";
    private const string SessionKey = "Chick.GrowthHUD.VerificationFile";
    private readonly StringBuilder report = new();
    private readonly List<GameObject> seeds = new();
    private FarmSaveSystem saves;
    private string originalFile;
    private int failures, completions;
    private GrowthProgressController growth;
    private UIDocument document;
    private GrowthProgressBarElement bar;
    private GameObject prefab, ground;
    private Mouse mouse;
    private Keyboard keyboard;
    private InputSettings originalInput, testInput;
    private bool originalBackground;

    private void Check(bool ok, string message)
    {
        if (!ok) failures++;
        report.AppendLine((ok ? "PASS " : "FAIL ") + message);
        Result = "Running\n" + report;
    }
    private void Update() { mouse?.MakeCurrent(); keyboard?.MakeCurrent(); }
    private EdibleObject Seed(int number, Vector3 position)
    {
        var go = Instantiate(prefab, position, Quaternion.identity);
        go.name = "GrowthSeed_Test_" + number.ToString("D3");
        seeds.Add(go);
        return go.GetComponent<EdibleObject>();
    }
    private void EatTo(int target)
    {
        int needed = target - growth.EatenSeedCount;
        for (int i = 0; i < needed; i++)
        {
            var seed = Seed(seeds.Count + 1, TestAreaLayout.Origin + new Vector3(20, 0, 21));
            if (!seed.Consume()) throw new InvalidOperationException("Test seed was not consumable.");
        }
    }
    private IEnumerator Start()
    {
        Result = "Running";
        originalBackground = Application.runInBackground;
        Application.runInBackground = true;
        originalInput = InputSystem.settings;
        testInput = Instantiate(originalInput);
        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testInput;
        mouse = InputSystem.AddDevice<Mouse>("GrowthHUDTestMouse");
        keyboard = InputSystem.AddDevice<Keyboard>("GrowthHUDTestKeyboard");
        saves = FindFirstObjectByType<FarmSaveSystem>();
        var field = typeof(FarmSaveSystem).GetField("fileName", BindingFlags.Instance | BindingFlags.NonPublic);
        originalFile = (string)field.GetValue(saves);
        if (!ContinuePhase && string.IsNullOrEmpty(SessionState.GetString(SessionKey, "")))
            SessionState.SetString(SessionKey, "growth-hud-test-" + Guid.NewGuid().ToString("N") + ".json");
        string file = SessionState.GetString(SessionKey, "");
        if (string.IsNullOrEmpty(file)) throw new InvalidOperationException("Run the first phase before Continue verification.");
        field.SetValue(saves, file);
        growth = FindFirstObjectByType<GrowthProgressController>();
        document = growth.GetComponent<UIDocument>();
        bar = document.rootVisualElement.Q<GrowthProgressBarElement>("GrowthBarContainer");
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/WheatGrain.prefab");
        var menu = FindFirstObjectByType<MainMenuController>();
        var gate = FindFirstObjectByType<MainMenuGameplayGate>();
        var time = FindFirstObjectByType<GameTimeManager>();
        growth.OnGrowthProgressComplete += Completed;
        yield return null;
        Check(document.rootVisualElement.Q("GrowthProgressHUD").resolvedStyle.display == DisplayStyle.None,
            "HUD stays hidden on the main menu");

        if (ContinuePhase)
        {
            for (int i = 1; i <= 17; i++) Seed(i, TestAreaLayout.Origin + new Vector3(20, 0, 21));
            menu.RefreshSave();
            menu.Continue();
        }
        else
        {
            // A previous 17/50 save must not leak into New Game's fresh snapshot.
            var old = saves.SaveAsync(new FarmSaveData { eatenSeedCount = 17 });
            while (!old.IsCompleted) yield return null;
            if (old.IsFaulted) throw old.Exception;
            menu.RefreshSave();
            Check(growth.EatenSeedCount == 17, "Existing save preview restores 17/50");
            menu.ConfirmNew();
        }
        float deadline = Time.realtimeSinceStartup + 20f;
        while ((!gate.IsReleased || time.IsTransitioning) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(gate.IsReleased && !time.IsTransitioning, "Existing menu starts gameplay successfully");
        time.enabled = false; // Hold the existing light/time state during screenshots.
        yield return new WaitForSecondsRealtime(.1f);
        Check(document.rootVisualElement.Q("GrowthProgressHUD").resolvedStyle.display == DisplayStyle.Flex,
            "HUD appears only after gameplay starts");

        if (ContinuePhase)
        {
            Check(growth.EatenSeedCount == 17 && Mathf.Abs(bar.Progress - .34f) < .001f,
                "Actual Continue restores 17/50 and 34 percent");
            Check(seeds.All(x => x.GetComponent<EdibleObject>().IsConsumed && !x.activeSelf),
                "Continue hides saved consumed seeds without counting them again");
            Check(completions == 0, "Loading does not fire the completion reward event");
            yield return VerifyLayoutAndRender();
            // Delete only this test's unique files; the user's save path was never written.
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                if (File.Exists(saves.SavePath + suffix)) File.Delete(saves.SavePath + suffix);
            SessionState.EraseString(SessionKey);
        }
        else
        {
            Check(growth.EatenSeedCount == 0 && bar.Progress == 0f, "New Game resets a prior 17/50 save to empty red 0/50");
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "GrowthHUDTestGround_TEMP";
            ground.transform.position = TestAreaLayout.Origin + new Vector3(20, -.05f, 20);
            ground.transform.localScale = new Vector3(10, .1f, 10);
            var player = FindFirstObjectByType<ChickPlayerController>();
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.SetPositionAndRotation(TestAreaLayout.Origin + new Vector3(20, .002f, 20), Quaternion.identity);
            cc.enabled = true;
            Camera.main.GetComponent<FreeOrbitThirdPersonCamera>().SnapToTarget();
            var seed = Seed(1, player.transform.position + Vector3.forward * .09f);
            Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(.15f);
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            Check(growth.EatenSeedCount == 0, "Click/eat start does not advance progress before impact");
            yield return new WaitForSecondsRealtime(.8f);
            Check(seed.IsConsumed && growth.EatenSeedCount == 1 && Mathf.Abs(bar.Progress - .02f) < .001f,
                "Real beak impact consumes seed and advances to 1/50 (2 percent)");
            Check(!seed.Consume() && growth.EatenSeedCount == 1, "The same consumed seed cannot count twice");
            var other = new GameObject("GrowthOther_Test");
            seeds.Add(other);
            other.AddComponent<EdibleObject>().Consume();
            Check(growth.EatenSeedCount == 1, "Non-seed food does not advance progress");
            // Keep deterministic seed names for the independent Continue phase.
            seeds.Remove(other); Destroy(other);
            EatTo(10);
            yield return new WaitForSecondsRealtime(.09f);
            Check(bar.Progress > .02f && bar.Progress < .2f, "Fill interpolates instead of jumping to its target");
            yield return new WaitForSecondsRealtime(.3f);
            Check(growth.EatenSeedCount == 10 && Mathf.Abs(bar.Progress - .2f) < .001f, "10 seeds = 20 percent");
            EatTo(17);
            var snapshot = new FarmSaveData { playerPosition = player.transform.position,
                playerRotation = player.transform.rotation, consumedEdibleIds = EdibleObject.CaptureConsumedIds() };
            var save = saves.SaveAsync(snapshot);
            while (!save.IsCompleted) yield return null;
            if (save.IsFaulted) throw save.Exception;
            Check(snapshot.eatenSeedCount == 17, "Existing SaveAsync captures progress automatically");
            EatTo(18);
            var loaded = saves.Load();
            Check(loaded.eatenSeedCount == 17 && growth.EatenSeedCount == 17, "Disk save/load restores the saved count");
            seed.ResetForReuse(); EdibleObject.RestoreConsumedIds(loaded.consumedEdibleIds);
            Check(seed.IsConsumed && growth.EatenSeedCount == 17, "Consumed-food restore does not increment the counter");
            EatTo(25); yield return new WaitForSecondsRealtime(.35f);
            Check(Mathf.Abs(bar.Progress - .5f) < .001f, "25 seeds = 50 percent");
            Vector2 edge = GrowthProgressBarElement.FillEdge(594, 42, bar.Progress);
            Check(Mathf.Abs(edge.x - 297f) < .001f && Mathf.Abs(edge.y - 297f) < .001f &&
                Mathf.Abs(bar.FillWidth - bar.TrackWidth * .5f) < .2f &&
                Mathf.Abs(bar.CursorPosition - bar.TrackWidth * .5f) < .2f,
                "50 percent fills half the continuous oval track and places the cursor at its midpoint");
            EatTo(49); yield return new WaitForSecondsRealtime(.35f);
            Check(Mathf.Abs(bar.Progress - .98f) < .001f, "49 seeds = 98 percent");
            EatTo(50); yield return new WaitForSecondsRealtime(.35f);
            Check(bar.Progress == 1f && GrowthProgressBarElement.FillEdge(594, 42, 1f) == new Vector2(594, 594) &&
                Mathf.Abs(bar.FillWidth - bar.TrackWidth) < .2f && Mathf.Abs(bar.CursorPosition - bar.TrackWidth) < .2f,
                "50 seeds = 100 percent; gold fill and cursor reach the track end");
            Seed(seeds.Count + 1, TestAreaLayout.Origin + new Vector3(20, 0, 21)).Consume();
            Check(growth.EatenSeedCount == 50 && completions == 1, "51st seed stays clamped; completion fires once");
            Check(player.GetComponent<PlayerGrowthController>().CurrentForm == PlayerGrowthController.Form.Chick,
                "Completing the bar does not transform the player");
            Check(JsonUtility.FromJson<FarmSaveData>("{\"version\":1}").eatenSeedCount == 0,
                "Older saves default to zero growth progress");
        }
        Check(growth.GetComponentsInChildren<Component>(true).All(c => c.GetType().Name != "Canvas" &&
            c.GetType().Namespace != "UnityEngine.UI"),
            "Growth HUD has no Canvas or legacy UI components");
        Check(document.visualTreeAsset != null && document.rootVisualElement.Q<GrowthProgressBarElement>() != null,
            "HUD uses UIDocument, authored UXML/USS and the custom VisualElement");
        Result = (failures == 0 ? "PASS" : "FAIL " + failures) + "\n" + report;
        Debug.Log("GROWTH HUD VERIFICATION\n" + Result);
        CleanupInput();
    }

    private IEnumerator VerifyLayoutAndRender()
    {
        var panel = document.panelSettings;
        var original = panel.targetTexture;
        Directory.CreateDirectory("Temp/GrowthHUDReview");
        foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(1440, 1080), new Vector2Int(2560, 1080) })
        {
            var target = new RenderTexture(size.x, size.y, 24);
            target.Create(); panel.targetTexture = target;
            yield return null; yield return new WaitForEndOfFrame();
            var root = document.rootVisualElement;
            Rect track = bar.worldBound;
            Rect l = root.Q<Image>("LeftIcon").worldBound;
            Rect r = root.Q<Image>("RightIcon").worldBound;
            Rect frame = bar.Q("growth-frame").worldBound;
            Rect cursor = bar.Q("growth-cursor").worldBound;
            Check(Mathf.Abs(track.center.x - root.worldBound.center.x) < 1f &&
                l.xMax <= frame.xMin && r.xMin >= frame.xMax && cursor.yMin >= frame.yMax &&
                cursor.xMin >= frame.xMin && cursor.xMax <= frame.xMax,
                size.x + "x" + size.y + ": centered pixel bar; icons outside frame and cursor underneath");
            var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
            File.WriteAllBytes("Temp/GrowthHUDReview/HUD-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
            RenderTexture.active = previous;
            panel.targetTexture = original;
            target.Release(); Destroy(target); Destroy(image);
        }
    }
    private void Completed() => completions++;
    private void CleanupInput()
    {
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        mouse = null; keyboard = null;
        if (originalInput != null) InputSystem.settings = originalInput;
        if (testInput != null) Destroy(testInput);
        Application.runInBackground = originalBackground;
    }
    private void OnDestroy()
    {
        CleanupInput();
        if (growth != null) growth.OnGrowthProgressComplete -= Completed;
        if (saves != null && originalFile != null)
            typeof(FarmSaveSystem).GetField("fileName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(saves, originalFile);
    }
}
#endif
