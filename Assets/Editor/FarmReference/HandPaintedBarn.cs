using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Hand-painted version of Synty's SM_Bld_Barn_01 (Assets/Art/SyntyBarn, made by ArtSource/SyntyBarn/handpaint_barn.py):
/// the same shape and parts with a painted texture, bare-wood backs on the inside of its one-sided walls and roof plus a
/// floor (SM_Bld_Barn_01_Interior, own texture). At the front end the big doors are slid open and the loft shutter
/// swung out; the back end stays shut. The side room is empty (its posts and stall rails removed) for the chick's
/// upgrade stand. Synty's own files stay untouched: the collider is the model's SM_Bld_Barn_01_Collision, Synty's
/// collision mesh without those posts and rails. SM_Bld_Barn_01_ShadowRoof, an unbroken sheet under the shingles, is
/// drawn into the shadow map only: Unity's shadow bias opens slits between Synty's separate shingle rows and the sun
/// would otherwise fall through them onto the floor in stripes.
/// The menu builds the prefab and swaps it in for every SM_Bld_Barn_01 in the open scene, keeping where each one stands.
/// </summary>
public sealed class HandPaintedBarn : AssetPostprocessor
{
    private const string Folder = "Assets/Art/SyntyBarn/";
    private const string ModelPath = Folder + "SM_Bld_Barn_01_HandPainted.fbx";
    private const string MaterialPath = Folder + "M_Barn_HandPainted.mat";
    private const string InteriorMaterialPath = Folder + "M_Barn_Interior.mat";
    private const string PrefabPath = Folder + "SM_Bld_Barn_01_HandPainted.prefab";
    private const string SyntyPrefabPath = "Assets/Synty/PolygonFarm/Prefabs/Buildings/SM_Bld_Barn_01.prefab";
    private const string SyntyModelPath = "Assets/Synty/PolygonFarm/Models/SM_Bld_Barn_01.fbx";
    private const string BarnName = "SM_Bld_Barn_01";
    private const string CollisionName = BarnName + "_Collision";
    private const string ShadowRoofName = BarnName + "_ShadowRoof";

    // Sliding barn doors run along the rail over the doorway: each leaf moves away from the middle, a little proud of
    // the trim. The loft shutters swing out on their hinges (their pivots).
    private const float DoorSlide = 1.25f;
    private const float DoorProud = 0.12f;
    private const float ShutterAngle = 100f;
    // The front is the barn's local +X end: in the farm it faces east, towards the barn yard and the rest of the farm,
    // while the -X end looks at the outer fence a few metres away.
    private const float FrontSide = 1f;

    public override uint GetVersion() => 4;

    private void OnPreprocessModel()
    {
        if (!assetPath.Equals(ModelPath, StringComparison.OrdinalIgnoreCase)) return;
        var importer = (ModelImporter)assetImporter;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importNormals = ModelImporterNormals.Import;   // Synty's own normals
        importer.globalScale = 1f;
        importer.addCollider = false;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.Equals(ModelPath, StringComparison.OrdinalIgnoreCase)) return;
        context.DependsOnArtifact(MaterialPath);
        context.DependsOnArtifact(InteriorMaterialPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        var interior = AssetDatabase.LoadAssetAtPath<Material>(InteriorMaterialPath);
        if (material == null || interior == null)
        {
            context.LogImportWarning("Missing " + MaterialPath + " or " + InteriorMaterialPath + "; barn imported without them.");
            return;
        }
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            // The collision mesh is only there to be picked up by the prefab's MeshCollider; never drawn.
            if (renderer.name == CollisionName) UnityEngine.Object.DestroyImmediate(renderer);
            else if (renderer.name == ShadowRoofName) SetUpShadowRoof(renderer, material);
            else renderer.sharedMaterial = renderer.name.EndsWith("_Interior") ? interior : material;
        }
    }

    [MenuItem("Tools/Chick/Barn/Replace SM_Bld_Barn_01 With Hand-Painted (Doors Open)")]
    private static void ReplaceInScene()
    {
        GameObject prefab = BuildPrefab();
        if (prefab == null) return;

        var syntyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyPrefabPath);
        var syntyModel = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyModelPath);
        var targets = new List<GameObject>();
        foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            GameObject go = t.gameObject;
            if (!go.scene.IsValid() || PrefabUtility.GetCorrespondingObjectFromSource(go) == prefab) continue;
            bool fromSynty = PrefabUtility.IsOutermostPrefabInstanceRoot(go) &&
                             (PrefabUtility.GetCorrespondingObjectFromOriginalSource(go) == syntyPrefab ||
                              PrefabUtility.GetCorrespondingObjectFromOriginalSource(go) == syntyModel);
            // An unpacked copy still has Synty's part names.
            bool unpacked = go.name.StartsWith(BarnName) && t.Find(BarnName + "_Door_01") != null &&
                            !PrefabUtility.IsPartOfPrefabInstance(go);
            if (fromSynty || unpacked) targets.Add(go);
        }
        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog("Hand-painted barn",
                "The hand-painted barn prefab (" + PrefabPath + ") is built and up to date; barns already using it have " +
                "been updated. No Synty SM_Bld_Barn_01 left in the open scene to replace.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Hand-painted barn");
        int group = Undo.GetCurrentGroup();
        foreach (GameObject old in targets)
        {
            Transform o = old.transform;
            var barn = (GameObject)PrefabUtility.InstantiatePrefab(prefab, old.scene);
            Undo.RegisterCreatedObjectUndo(barn, "Hand-painted barn");
            barn.transform.SetParent(o.parent, false);
            barn.transform.SetSiblingIndex(o.GetSiblingIndex());
            barn.transform.SetLocalPositionAndRotation(o.localPosition, o.localRotation);
            barn.transform.localScale = o.localScale;
            barn.name = old.name;
            barn.SetActive(old.activeSelf);
            foreach (Transform part in barn.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.layer = old.layer;
                GameObjectUtility.SetStaticEditorFlags(part.gameObject, GameObjectUtility.GetStaticEditorFlags(old));
            }
            Undo.DestroyObjectImmediate(old);
            EditorSceneManager.MarkSceneDirty(barn.scene);
        }
        Undo.CollapseUndoOperations(group);
        Debug.Log($"Replaced {targets.Count} SM_Bld_Barn_01 with the hand-painted barn (doors open). " +
                  "Save the scene to keep it (Ctrl+Z undoes it).");
    }

    // Builds (or rebuilds) the prefab from the painted model, Synty's collision mesh and door colliders.
    private static GameObject BuildPrefab()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Transform collisionPart = model != null ? model.transform.Find(CollisionName) : null;
        Mesh collision = collisionPart != null && collisionPart.TryGetComponent(out MeshFilter collisionFilter)
            ? collisionFilter.sharedMesh : null;
        if (model == null || collision == null)
        {
            EditorUtility.DisplayDialog("Hand-painted barn", "Missing " + ModelPath + " or its " + CollisionName + ".", "OK");
            return null;
        }

        // An existing prefab is refreshed in place, so barns already placed keep their link to it.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var modelRenderers = new Dictionary<string, Material>();
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) modelRenderers[r.name] = r.sharedMaterial;
                foreach (var r in contents.GetComponentsInChildren<Renderer>(true))
                    if (modelRenderers.TryGetValue(r.name, out Material m)) r.sharedMaterial = m;
                SetOpenings(contents, model);
                SetHull(contents, collision);
                EnsureShadowRoof(contents, model);
                return PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        GameObject barn = UnityEngine.Object.Instantiate(model);
        barn.name = BarnName + "_HandPainted";
        try
        {
            SetHull(barn, collision);
            EnsureShadowRoof(barn, model);
            foreach (Transform part in barn.GetComponentsInChildren<Transform>(true))
                if (part.name.Contains("_Door_") || part.name.Contains("_Shutter_"))
                    part.gameObject.AddComponent<BoxCollider>();
            SetOpenings(barn, model);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(barn, PrefabPath, out bool ok);
            if (!ok) Debug.LogError("Could not save " + PrefabPath, barn);
            return ok ? saved : null;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(barn);
        }
    }

    private static void SetUpShadowRoof(Renderer renderer, Material material)
    {
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        renderer.receiveShadows = false;
    }

    // A prefab built before the shadow roof existed gets it added; one that has it is brought in line with the model.
    private static void EnsureShadowRoof(GameObject barn, GameObject model)
    {
        Transform source = model.transform.Find(ShadowRoofName);
        if (source == null || !source.TryGetComponent(out MeshFilter sourceFilter)) return;
        Transform part = barn.transform.Find(ShadowRoofName);
        if (part == null)
        {
            part = new GameObject(ShadowRoofName).transform;
            part.SetParent(barn.transform, false);
        }
        part.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
        part.localScale = source.localScale;
        if (!part.TryGetComponent(out MeshFilter filter)) filter = part.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = sourceFilter.sharedMesh;
        if (!part.TryGetComponent(out MeshRenderer renderer)) renderer = part.gameObject.AddComponent<MeshRenderer>();
        SetUpShadowRoof(renderer, AssetDatabase.LoadAssetAtPath<Material>(MaterialPath));
    }

    // Hull: hollow with open doorways, so the doors can be walked through; the helper child holding the mesh goes.
    private static void SetHull(GameObject barn, Mesh collision)
    {
        Transform part = barn.transform.Find(CollisionName);
        if (part != null) UnityEngine.Object.DestroyImmediate(part.gameObject);
        if (!barn.TryGetComponent(out MeshCollider hull)) hull = barn.AddComponent<MeshCollider>();
        hull.sharedMesh = collision;
        hull.convex = false;
    }

    // Doors and shutters back to where the model has them (shut), then the front ones opened.
    private static void SetOpenings(GameObject barn, GameObject model)
    {
        var rest = new Dictionary<string, Transform>();
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) rest[t.name] = t;
        foreach (Transform part in barn.GetComponentsInChildren<Transform>(true))
        {
            bool door = part.name.Contains("_Door_"), shutter = part.name.Contains("_Shutter_");
            if ((!door && !shutter) || !rest.TryGetValue(part.name, out Transform shut)) continue;
            part.SetLocalPositionAndRotation(shut.localPosition, shut.localRotation);
            if (shut.localPosition.x * FrontSide <= 0f) continue;
            if (door)
            {
                Vector3 p = shut.localPosition;
                part.localPosition = new Vector3(p.x + Mathf.Sign(p.x) * DoorProud, p.y, p.z + Mathf.Sign(p.z) * DoorSlide);
            }
            else part.localRotation = OutwardSwing(part);
        }
    }

    // The shutter's pivot is its hinge; swing it whichever way takes it away from the barn's middle.
    private static Quaternion OutwardSwing(Transform shutter)
    {
        var filter = shutter.GetComponent<MeshFilter>();
        Vector3 centre = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.center : Vector3.zero;
        Quaternion best = shutter.localRotation;
        float bestDistance = float.MinValue;
        foreach (float sign in new[] { 1f, -1f })
        {
            Quaternion r = Quaternion.AngleAxis(sign * ShutterAngle, Vector3.up) * shutter.localRotation;
            Vector3 p = shutter.localPosition + r * Vector3.Scale(centre, shutter.localScale);
            if (Mathf.Abs(p.x) > bestDistance) { bestDistance = Mathf.Abs(p.x); best = r; }
        }
        return best;
    }
}
