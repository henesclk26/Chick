using MicahW.PointGrass;
using UnityEngine;

// The Point Grass shader reads the pushing objects from a buffer that only PointGrassDisplacementManager provides, and
// that manager only runs in play mode. Without the buffer, D3D12 skips every grass draw ("requires a buffer
// _DisplacementObjects ... Skipping draw calls"), so the grass is invisible in the Scene view, and in play mode wherever
// no manager is present. This binds an empty buffer (zero pushing objects) globally; a renderer's own material
// property block still overrides it whenever the manager is there.
public static class PointGrassFallbackBuffers
{
    private static readonly int ObjectsId = Shader.PropertyToID("_DisplacementObjects");
    private static readonly int CountId = Shader.PropertyToID("_DisplacementCount");
    private static ComputeBuffer empty;

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bind()
    {
        if (empty == null || !empty.IsValid())
        {
            empty = new ComputeBuffer(1, PointGrassCommon.ObjectData.stride);
            empty.SetData(new[] { new PointGrassCommon.ObjectData(Vector3.zero, 0f, 0f) });
#if UNITY_EDITOR
            // Not Application.quitting: in the Editor that also fires when play mode ends, and edit mode needs the buffer.
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Release;
#else
            Application.quitting += Release;
#endif
        }
        Shader.SetGlobalBuffer(ObjectsId, empty);
        Shader.SetGlobalInteger(CountId, 0);
    }

    private static void Release()
    {
        if (empty != null) empty.Release();
        empty = null;
    }
}
