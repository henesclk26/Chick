#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in Play Mode check. Never attached to the saved scene or included in builds.
// Checks independent wandering, following, no jump mirroring, obstacle avoidance and auto-eating.
[DefaultExecutionOrder(-100)]
public sealed class HelperChickPlayVerification : MonoBehaviour
{
    public static string Result = "Not run";

    private readonly StringBuilder report = new StringBuilder();
    private Keyboard testKeyboard;
    private InputSettings originalSettings, testSettings;
    private bool originalBackground;
    private GameObject ground;
    private ChickPlayerController player;
    private FreeOrbitThirdPersonCamera orbit;
    private HelperChickController helper;
    private Animator helperAnimator;
    private readonly List<GameObject> temporaries = new List<GameObject>();
    private readonly List<(float time, EdibleObject food, Vector3 helperPos, Vector3 playerPos)> eaten =
        new List<(float, EdibleObject, Vector3, Vector3)>();

    private static readonly string[] StateNames = { "idle", "move", "run", "crouch", "eat", "jump", "jump_descent" };

    private void Update()
    {
        if (testKeyboard != null && testKeyboard.added) testKeyboard.MakeCurrent();
    }

    private void OnFoodConsumed(EdibleObject food)
    {
        if (helper == null) return;
        eaten.Add((Time.time, food, helper.transform.position, player.transform.position));
    }

    private string State()
    {
        if (helperAnimator == null) return "no-animator";
        var info = helperAnimator.GetCurrentAnimatorStateInfo(0);
        foreach (string name in StateNames) if (info.IsName(name)) return name;
        if (helperAnimator.IsInTransition(0))
        {
            var next = helperAnimator.GetNextAnimatorStateInfo(0);
            foreach (string name in StateNames) if (next.IsName(name)) return name;
        }
        return "other";
    }

    private Vector3 LocalOffset() => player.transform.InverseTransformPoint(helper.transform.position);

    private IEnumerator Hold(Key[] keys, float seconds, System.Action<float> tick = null)
    {
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(keys));
        float t = 0f;
        while (t < seconds)
        {
            yield return null;
            t += Time.deltaTime;
            tick?.Invoke(t);
        }
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
    }

    private void Place(Vector3 position, float yaw)
    {
        player.TeleportTo(position, Quaternion.Euler(0f, yaw, 0f));
        orbit.SetOrbitAngles(yaw, orbit.OrbitPitch);
        orbit.SnapToTarget();
        Physics.SyncTransforms();
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
        testKeyboard = InputSystem.AddDevice<Keyboard>("HelperChickTestKeyboard");
        EdibleObject.Consumed += OnFoodConsumed;
        HelperChickTracer.Log.Clear();
        gameObject.AddComponent<HelperChickTracer>();

        foreach (var clock in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            clock.StopAllCoroutines();
            clock.enabled = false;
        }
        // Release the gate without starting the day clock or writing any save.
        var gate = FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include);
        typeof(MainMenuGameplayGate).GetField("released", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(gate, true);

        player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        var growth = player.GetComponent<PlayerGrowthController>();
        var upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        orbit = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        player.enabled = true;
        player.GetComponent<Animator>().enabled = true;
        orbit.enabled = true;
        Time.timeScale = 1f;

        growth.SetForm(PlayerGrowthController.Form.Chicken);
        yield return new WaitForSeconds(.5f);
        report.AppendLine("helper before purchase: " + (FindFirstObjectByType<HelperChickController>() == null ? "none (ok)" : "EXISTS (bad)"));
        bool granted = upgrades.TryGrantDebugEggs(60);
        bool bought = upgrades.TryPurchase(PlayerUpgrades.HelperChick);
        report.AppendLine("granted=" + granted + " bought=" + bought + " helperCount=" + upgrades.HelperChickCount +
            " eggsLeft=" + upgrades.AvailableEggs + " roamRadius=" + upgrades.HelperRoamRadius);

        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "HelperTestGround_TEMP";
        ground.transform.position = new Vector3(500f, 300f - .05f, 500f);
        ground.transform.localScale = new Vector3(60f, .1f, 60f);
        temporaries.Add(ground);
        Vector3 start = new Vector3(500f, 300f + .002f, 480f);
        Place(start, 0f);
        yield return new WaitForSeconds(1.2f);

        helper = FindFirstObjectByType<HelperChickController>();
        report.AppendLine("helper spawned=" + (helper != null) + " count=" +
            FindObjectsByType<HelperChickController>(FindObjectsSortMode.None).Length);
        if (helper == null) { Finish(); yield break; }
        helperAnimator = helper.GetComponent<Animator>();
        yield return new WaitForSeconds(1f);

        Vector3 idleOffset = LocalOffset();
        report.AppendLine("A idle: local offset right=" + idleOffset.x.ToString("F2") + " fwd=" + idleOffset.z.ToString("F2") +
            " state=" + State());

        // Isolate turning from random wandering: owner yaw alone must not drag the helper around.
        var helperFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(HelperChickController).GetField("roamTimer", helperFlags).SetValue(helper, 100f);
        typeof(HelperChickController).GetField("slotWorld", helperFlags).SetValue(helper, helper.transform.position);
        yield return new WaitForSeconds(.4f);
        Vector3 beforeTurn = helper.transform.position;
        float beforeYaw = helper.transform.eulerAngles.y;
        Place(player.transform.position, 180f);
        yield return new WaitForSeconds(.8f);
        float turnDisplacement = Vector3.Distance(beforeTurn, helper.transform.position);
        float copiedYaw = Mathf.Abs(Mathf.DeltaAngle(beforeYaw, helper.transform.eulerAngles.y));
        report.AppendLine("A owner turn: displacement=" + turnDisplacement.ToString("F3") + " copiedYaw=" + copiedYaw.ToString("F1"));
        Debug.Assert(turnDisplacement < .06f && copiedYaw < 5f, "Helper should not orbit or turn with stationary owner");
        Place(start, 0f);
        typeof(HelperChickController).GetField("roamTimer", helperFlags).SetValue(helper, 0f);

        // B: walk straight
        float maxDist = 0f; float sumX = 0f; float sumZ = 0f; int n = 0; string walkState = "";
        yield return Hold(new[] { Key.W }, 3f, t =>
        {
            maxDist = Mathf.Max(maxDist, Vector3.Distance(helper.transform.position, player.transform.position));
            if (t > 1f) { var o = LocalOffset(); sumX += o.x; sumZ += o.z; n++; walkState = State(); }
        });
        report.AppendLine("B walk 3s: maxDistance=" + maxDist.ToString("F2") + " avgOffset right=" + (sumX / Mathf.Max(1, n)).ToString("F2") +
            " fwd=" + (sumZ / Mathf.Max(1, n)).ToString("F2") + " state=" + walkState);
        Debug.Assert(maxDist < 1.2f, "Helper should promptly keep up with walking owner");
        yield return new WaitForSeconds(.8f);

        // C: run
        maxDist = 0f; string runState = ""; var runStates = new HashSet<string>();
        yield return Hold(new[] { Key.W, Key.LeftShift }, 1.6f, t =>
        {
            maxDist = Mathf.Max(maxDist, Vector3.Distance(helper.transform.position, player.transform.position));
            if (t > .7f) { runState = State(); runStates.Add(runState); }
        });
        report.AppendLine("C run 1.6s: maxDistance=" + maxDist.ToString("F2") + " states=" + string.Join("/", new List<string>(runStates).ToArray()));
        yield return new WaitForSeconds(1f);

        // D: jump on the spot
        Place(start, 0f);
        yield return new WaitForSeconds(1.2f);
        float groundY = helper.transform.position.y; float peak = 0f; bool sawJump = false;
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.Space));
        yield return null;
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
        for (float t = 0f; t < .9f; t += Time.deltaTime)
        {
            yield return null;
            peak = Mathf.Max(peak, helper.transform.position.y - groundY);
            string s = State();
            if (s == "jump" || s == "jump_descent") sawJump = true;
        }
        report.AppendLine("D jump: helper peak=" + peak.ToString("F2") + "m sawJumpAnimation=" + sawJump);
        Debug.Assert(peak < .05f && !sawJump, "Helper must not mirror owner's stationary jump");

        // E: obstacle on the helper's side of the path
        Place(start, 0f);
        yield return new WaitForSeconds(1.2f);
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = "HelperTestObstacle_TEMP";
        obstacle.transform.localScale = new Vector3(.4f, .4f, .4f);
        obstacle.transform.position = start + new Vector3(.4f, .2f, 1.5f);
        temporaries.Add(obstacle);
        Physics.SyncTransforms();
        var obstacleCollider = obstacle.GetComponent<Collider>();
        float minGap = float.MaxValue; float minX = float.MaxValue; float maxX = float.MinValue; int teleports = 0;
        Vector3 lastPos = helper.transform.position; bool inside = false;
        yield return Hold(new[] { Key.W }, 3.6f, t =>
        {
            Vector3 p = helper.transform.position;
            if ((p - lastPos).magnitude > .5f) teleports++;
            lastPos = p;
            Vector3 flat = obstacleCollider.ClosestPoint(p + Vector3.up * .1f);
            float gap = Vector2.Distance(new Vector2(flat.x, flat.z), new Vector2(p.x, p.z));
            minGap = Mathf.Min(minGap, gap);
            if (gap < .001f) inside = true;
            var o = LocalOffset(); minX = Mathf.Min(minX, o.x); maxX = Mathf.Max(maxX, o.x);
        });
        report.AppendLine("E obstacle: minGapToCube=" + minGap.ToString("F3") + " insideCube=" + inside +
            " helperRightOffset range=[" + minX.ToString("F2") + "," + maxX.ToString("F2") + "] teleports=" + teleports);
        yield return new WaitForSeconds(1.5f);
        Vector3 after = LocalOffset();
        report.AppendLine("E after passing: offset right=" + after.x.ToString("F2") + " fwd=" + after.z.ToString("F2"));

        // F: auto-eating next to real food, chicken standing still
        EdibleObject bestFood = null; int bestNeighbours = 0;
        var all = FindObjectsByType<EdibleObject>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i += 7)
        {
            var e = all[i];
            if (e.gameObject.layer != 9 || e.Category != EdibleCategory.Seed || !e.CanBeEaten()) continue;
            int count = 0;
            foreach (var other in all)
                if (other.gameObject.layer == 9 && other.CanBeEaten() && (other.BitePosition - e.BitePosition).sqrMagnitude < .8f * .8f) count++;
            if (count > bestNeighbours) { bestNeighbours = count; bestFood = e; }
        }
        if (bestFood != null)
        {
            Vector3 spot = bestFood.BitePosition + new Vector3(0f, 0f, -.5f);
            if (Physics.Raycast(spot + Vector3.up * 1f, Vector3.down, out var hit, 3f, ~(1 << 9), QueryTriggerInteraction.Ignore))
                spot = hit.point + Vector3.up * .01f;
            eaten.Clear();
            Place(spot, 0f);
            yield return new WaitForSeconds(.5f);
            float t0 = Time.time; float eatSpeedParam = 0f; bool sawEat = false; float maxFromPlayer = 0f;
            EdibleObject turningTarget = null;
            float turnForTarget = 0f, maxTurnForTarget = 0f, lastYaw = helper.transform.eulerAngles.y;
            for (float t = 0f; t < 14f; t += Time.deltaTime)
            {
                yield return null;
                var target = typeof(HelperChickController).GetField("foodTarget", helperFlags).GetValue(helper) as EdibleObject;
                if (target != turningTarget) { turningTarget = target; turnForTarget = 0f; }
                if (target != null)
                {
                    turnForTarget += Mathf.Abs(Mathf.DeltaAngle(lastYaw, helper.transform.eulerAngles.y));
                    maxTurnForTarget = Mathf.Max(maxTurnForTarget, turnForTarget);
                }
                lastYaw = helper.transform.eulerAngles.y;
                if (State() == "eat") { sawEat = true; eatSpeedParam = helperAnimator.GetFloat("EatPlaybackSpeed"); }
                maxFromPlayer = Mathf.Max(maxFromPlayer, Vector3.Distance(
                    new Vector3(helper.transform.position.x, 0f, helper.transform.position.z),
                    new Vector3(player.transform.position.x, 0f, player.transform.position.z)));
            }
            report.AppendLine("F eating: foodsNear=" + bestNeighbours + " consumedByEvent=" + eaten.Count + " sawEatAnimation=" + sawEat +
                " EatPlaybackSpeed=" + eatSpeedParam.ToString("F2") + " maxDistFromPlayer=" + maxFromPlayer.ToString("F2") +
                " maxTurnPerFood=" + maxTurnForTarget.ToString("F1"));
            Debug.Assert(eaten.Count > 0 && sawEat, "Helper must still eat food after approach changes");
            Debug.Assert(maxTurnForTarget < 220f, "Helper should use a short turn, not spin around food");
            for (int i = 0; i < eaten.Count; i++)
                report.AppendLine("   #" + i + " at +" + (eaten[i].time - t0).ToString("F1") + "s food=" + eaten[i].food.StatisticKey +
                    " distFoodToPlayer=" + Vector3.Distance(eaten[i].food.transform.position, eaten[i].playerPos).ToString("F2") + "m");
            Vector3 away = player.transform.position - helper.transform.position;
            Place(player.transform.position, Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg);
            float followAt = -1f;
            yield return Hold(new[] { Key.W }, 1f, t =>
            {
                bool follows = (bool)typeof(HelperChickController).GetField("following", helperFlags).GetValue(helper);
                if (follows && followAt < 0f) followAt = t;
            });
            report.AppendLine("F departure while eating: follow after=" + followAt.ToString("F2") + "s");
            Debug.Assert(followAt >= 0f && followAt < .4f, "Helper must promptly leave food when owner walks away");
        }
        else report.AppendLine("F eating: no seed cluster found");

        Finish();
    }

    private void Finish()
    {
        Result = "DONE\n" + report;
        Debug.Log("HELPER CHICK VERIFICATION\n" + Result);
        Cleanup();
    }

    private void Cleanup()
    {
        EdibleObject.Consumed -= OnFoodConsumed;
        if (testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
        if (originalSettings != null) InputSystem.settings = originalSettings;
        Application.runInBackground = originalBackground;
        if (testSettings != null) Destroy(testSettings);
        foreach (var t in temporaries) if (t != null) Destroy(t);
        temporaries.Clear();
    }

    private void OnDestroy() => Cleanup();
}

// Timeline logger for diagnosing helper eating; attach at runtime, read the console.
public sealed class HelperChickTracer : MonoBehaviour
{
    public static readonly StringBuilder Log = new StringBuilder();
    private HelperChickController helper;
    private float next;
    private static readonly string[] StateNames = { "idle", "move", "run", "crouch", "eat", "jump", "jump_descent" };
    private static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    private object Get(string name) => typeof(HelperChickController).GetField(name, Flags).GetValue(helper);

    private void Update()
    {
        if (helper == null) helper = FindFirstObjectByType<HelperChickController>();
        if (helper == null || Time.time < next) return;
        next = Time.time + .25f;
        var animator = helper.GetComponent<Animator>();
        var info = animator.GetCurrentAnimatorStateInfo(0);
        string state = "other";
        foreach (string s in StateNames) if (info.IsName(s)) { state = s; break; }
        var food = Get("foodTarget") as EdibleObject;
        Vector3 stand = (Vector3)Get("standPoint");
        float toStand = new Vector2(stand.x - helper.transform.position.x, stand.z - helper.transform.position.z).magnitude;
        Log.AppendLine("t=" + Time.time.ToString("F2") + " phase=" + Get("eatPhase") + " target=" + (food != null) +
            " toStand=" + (food != null ? toStand.ToString("F3") : "-") + " speed=" + ((float)Get("smoothedSpeed")).ToString("F2") +
            " state=" + state + " ignored=" + ((System.Collections.IDictionary)Get("ignoredUntil")).Count +
            " foodTimer=" + ((float)Get("foodTimer")).ToString("F2"));
    }
}
#endif
