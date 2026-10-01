using System.Reflection;
using System.Text;
using UnityEngine;

public static class SmartRecenterKernelVerification
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string Run()
    {
        var go = new GameObject("RecenterKernel_TEMP");
        go.AddComponent<Camera>().enabled = false;
        var camera = go.AddComponent<FreeOrbitThirdPersonCamera>();
        var tick = typeof(FreeOrbitThirdPersonCamera).GetMethod("TickSmartRecenter", Flags);
        var reset = typeof(FreeOrbitThirdPersonCamera).GetMethod("ResetSmartRecenter", Flags);
        var yaw = typeof(FreeOrbitThirdPersonCamera).GetField("yaw", Flags);
        var pitch = typeof(FreeOrbitThirdPersonCamera).GetField("pitch", Flags);
        var zoom = typeof(FreeOrbitThirdPersonCamera).GetField("userDesiredDistance", Flags);
        var report = new StringBuilder(); int fails = 0;
        System.Action<bool, string> check = (ok, message) => { report.AppendLine((ok ? "PASS " : "FAIL ") + message); if (!ok) fails++; };
        System.Action<float> start = angle => { reset.Invoke(camera, null); yaw.SetValue(camera, angle); pitch.SetValue(camera, 23f); zoom.SetValue(camera, .61f); };
        System.Action<float, bool, Vector3, bool, bool> step = (dt, manual, velocity, available, eat) =>
            tick.Invoke(camera, new object[] { dt, manual, velocity, available, eat });
        try
        {
            foreach (int fps in new[] { 30, 60, 144 })
            foreach (float angle in new[] { 90f, 180f })
            {
                start(angle); float dt = 1f / fps; float maxStep = 0f; float previous = angle;
                step(dt, true, Vector3.forward, true, false);
                for (int i = 0; i < Mathf.FloorToInt(1.49f * fps); i++) step(dt, false, Vector3.forward, true, false);
                check((float)yaw.GetValue(camera) == angle, fps + "fps / " + angle + ": no yaw during manual cooldown");
                for (int i = 0; i < 6 * fps; i++)
                {
                    step(dt, false, Vector3.forward, true, false);
                    float now = (float)yaw.GetValue(camera); maxStep = Mathf.Max(maxStep, Mathf.Abs(Mathf.DeltaAngle(previous, now))); previous = now;
                }
                check(Mathf.Abs(Mathf.DeltaAngle((float)yaw.GetValue(camera), 0f)) < 6f, fps + "fps / " + angle + ": stable world travel converges behind");
                check(maxStep <= 90f / fps + .01f && (float)pitch.GetValue(camera) == 23f && (float)zoom.GetValue(camera) == .61f,
                    fps + "fps: bounded smooth yaw, selected pitch/zoom untouched");
            }
            start(180f);
            for (int i = 0; i < 360; i++) step(1f / 60f, false, Vector3.zero, true, false);
            check((float)yaw.GetValue(camera) == 180f, "Stationary front view remains exact for 6 seconds");
            foreach (float angle in new[] { 15f, 25f, 40f, 60f })
            {
                start(angle); for (int i = 0; i < 360; i++) step(1f / 60, false, Vector3.forward, true, false);
                check((float)yaw.GetValue(camera) == angle, "No recenter at " + angle + " degree offset");
            }
            start(90f);
            for (int i = 0; i < 240; i++) step(1f / 60f, false, i / 6 % 2 == 0 ? Vector3.left : Vector3.right, true, false);
            check((float)yaw.GetValue(camera) == 90f, "Rapid A/D world direction never becomes stable");
            start(90f); for (int i = 0; i < 125; i++) step(1f / 60f, false, Vector3.forward, true, false);
            float interrupted = (float)yaw.GetValue(camera);
            step(1f / 60f, true, Vector3.forward, true, false);
            check(interrupted < 90f && (float)yaw.GetValue(camera) == interrupted, "RMB interrupts active recenter with zero automatic delta");
            start(90f); for (int i = 0; i < 125; i++) step(1f / 60f, false, Vector3.forward, true, false);
            interrupted = (float)yaw.GetValue(camera);
            step(1f / 60f, false, Vector3.forward, true, true);
            check((float)yaw.GetValue(camera) == interrupted && camera.RecenterState == FreeOrbitThirdPersonCamera.SmartRecenterState.Eating, "Eat pauses immediately");
            step(1f / 60f, false, Vector3.forward, false, false);
            check((float)yaw.GetValue(camera) == interrupted, "Control lock produces no recenter yaw");
            start(179f); for (int i = 0; i < 500; i++) step(1f / 60f, false, Quaternion.Euler(0, -179f, 0) * Vector3.forward, true, false);
            check((float)yaw.GetValue(camera) == 179f, "179/-179 wrap uses 2 degree difference, no full spin");
            start(90f);
            for (int i = 0; i < 3600; i++)
            {
                // Simulated unchanged camera-relative side input: trajectory curves as camera yaw changes.
                float actualHeading = (float)yaw.GetValue(camera) - 90f;
                step(1f / 60f, false, Quaternion.Euler(0, actualHeading, 0) * Vector3.forward, true, false);
            }
            check(camera.RecenterState == FreeOrbitThirdPersonCamera.SmartRecenterState.FeedbackHold &&
                Mathf.Abs((float)yaw.GetValue(camera) - 90f) < 15f, "60s camera-relative feedback: one small correction then sustained quiet hold, no spiral");
            start(90f); camera.EnableSmartRecenter = false;
            for (int i = 0; i < 300; i++) step(1f / 60f, false, Vector3.forward, true, false);
            check((float)yaw.GetValue(camera) == 90f && camera.RecenterState == FreeOrbitThirdPersonCamera.SmartRecenterState.Disabled,
                "Master OFF has exactly zero yaw contribution");
            return (fails == 0 ? "PASS" : "FAIL " + fails) + "\n" + report;
        }
        finally { Object.DestroyImmediate(go); }
    }
}
