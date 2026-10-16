using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts the hand-painted pipe (Assets/Art/FarmerPipe, made by ArtSource/FarmerPipe/build_pipe.py) in the old farmer's
/// mouth with a thin wisp of smoke rising from the bowl. The pipe is a child of his head bone, so it follows him as he
/// rocks; the mouth is found from his own mesh (the front of the face below the hat brim), and the pipe can be nudged
/// by hand afterwards. Running the menu again replaces the pipe.
/// </summary>
public static class FarmerPipeSetup
{
    private const string FarmerName = "SM_Chr_Farmer_Male_Old_01";
    private const string PipeModelPath = "Assets/Art/FarmerPipe/FarmerPipe.fbx";
    private const string PipeMaterialPath = "Assets/Art/FarmerPipe/M_FarmerPipe.mat";
    private const string SmokeMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";
    private const string PipeName = "Farmer Pipe";

    [MenuItem("Tools/Chick/Farmer/Give Old Farmer A Pipe")]
    private static void GivePipe()
    {
        GameObject farmer = null;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.name.StartsWith(FarmerName) && (t.parent == null || !t.parent.name.StartsWith(FarmerName))) { farmer = t.gameObject; break; }
        Animator animator = farmer != null ? farmer.GetComponentInChildren<Animator>() : null;
        Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(PipeModelPath);
        if (head == null || model == null)
        {
            EditorUtility.DisplayDialog("Farmer pipe", head == null
                ? "No " + FarmerName + " with a Humanoid head bone in the open scene."
                : "Missing " + PipeModelPath + ".", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Give farmer a pipe");
        int group = Undo.GetCurrentGroup();
        Transform old = head.Find(PipeName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        Transform root = animator.transform;
        Vector3 mouth = FindMouth(animator, head, out bool measured);
        var pipe = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(pipe, "Give farmer a pipe");
        pipe.name = PipeName;
        pipe.transform.SetParent(head, false);
        // Held in the corner of the mouth, pointing forward and a little out to his left, the stem dipping slightly.
        pipe.transform.SetPositionAndRotation(root.TransformPoint(mouth + new Vector3(-0.018f, 0f, 0f)),
            root.rotation * Quaternion.Euler(6f, -14f, 0f));
        var material = AssetDatabase.LoadAssetAtPath<Material>(PipeMaterialPath);
        foreach (var renderer in pipe.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Transform smokePoint = null;
        foreach (Transform t in pipe.GetComponentsInChildren<Transform>(true))
            if (t.name == "SmokePoint") smokePoint = t;
        AddSmoke(smokePoint != null ? smokePoint : pipe.transform);

        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = pipe;
        EditorSceneManager.MarkSceneDirty(pipe.scene);
        Debug.Log($"Pipe placed in {FarmerName}'s mouth at {pipe.transform.position}" +
                  (measured ? " (mouth found from his mesh)." : " (mouth guessed from the head bone).") +
                  " It is a child of his head bone: move it there if it needs a nudge, then save the scene.");
    }

    /// <summary>
    /// Mouth position in the character's root space (+Z forward). Looks at the middle of the face in profile: the hat
    /// brim is the furthest-forward point near the top, the nose the furthest-forward point a little below it, and the
    /// mouth sits under the nose on the front of the beard.
    /// </summary>
    private static Vector3 FindMouth(Animator animator, Transform head, out bool measured)
    {
        Transform root = animator.transform;
        Vector3 headInRoot = root.InverseTransformPoint(head.position);
        Vector3 guess = headInRoot + new Vector3(0f, 0.08f, 0.11f);
        measured = false;
        SkinnedMeshRenderer skin = null;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            if (smr.enabled && smr.gameObject.activeInHierarchy) { skin = smr; break; }
        if (skin == null) return guess;

        var baked = new Mesh();
        skin.BakeMesh(baked, true);
        const float bin = 0.01f;
        var front = new System.Collections.Generic.Dictionary<int, float>();
        foreach (Vector3 v in baked.vertices)
        {
            Vector3 p = root.InverseTransformPoint(skin.transform.position + skin.transform.rotation * v);
            if (Mathf.Abs(p.x) > 0.03f || p.y < headInRoot.y - 0.08f || p.y > headInRoot.y + 0.45f || p.z < headInRoot.z) continue;
            int key = Mathf.RoundToInt(p.y / bin);
            front[key] = front.TryGetValue(key, out float z) ? Mathf.Max(z, p.z) : p.z;
        }
        Object.DestroyImmediate(baked);
        if (front.Count == 0) return guess;

        int brim = int.MinValue; float brimZ = float.MinValue;
        foreach (var kv in front)
            if (kv.Key * bin > headInRoot.y + 0.05f && kv.Value > brimZ) { brimZ = kv.Value; brim = kv.Key; }
        if (brim == int.MinValue) return guess;
        int nose = int.MinValue; float noseZ = float.MinValue;
        foreach (var kv in front)
            if (kv.Key <= brim - 4 && kv.Key >= brim - 13 && kv.Value > noseZ) { noseZ = kv.Value; nose = kv.Key; }
        if (nose == int.MinValue) return guess;
        int mouthKey = nose - 4;
        float mouthZ = front.TryGetValue(mouthKey, out float mz) ? mz : noseZ - 0.01f;
        measured = true;
        return new Vector3(0f, mouthKey * bin, mouthZ - 0.006f);
    }

    // A thin, slow wisp with an occasional bigger puff, drifting up and fading out.
    private static void AddSmoke(Transform at)
    {
        var go = new GameObject("Pipe Smoke");
        Undo.RegisterCreatedObjectUndo(go, "Give farmer a pipe");
        go.transform.SetParent(at, false);
        go.transform.rotation = Quaternion.LookRotation(Vector3.up);      // cone points up

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        main.gravityModifier = -0.012f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;      // trails behind as he rocks
        main.maxParticles = 50;

        var emission = ps.emission;
        emission.rateOverTime = 2.5f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(1.2f, 3, 5, 0, 4.5f) });   // a puff every few seconds

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 7f;
        shape.radius = 0.004f;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 3f)));

        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.78f, 0.78f, 0.8f), 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.42f, 0.15f), new GradientAlphaKey(0f, 1f) });
        colour.color = fade;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.05f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.3f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath);
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }
}
