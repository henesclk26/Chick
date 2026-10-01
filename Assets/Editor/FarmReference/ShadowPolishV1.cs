using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FarmReferenceTools
{
    // Editor-only, reversible shadow polish. Apply stores every value it changes in Backups/ShadowPolishV1/original.json;
    // Revert restores them. Covers tighter/higher-resolution sun shadows and real swaying shadows for corn and wheat
    // (with mild root shading). Revert also removes the V1 chick contact shadow if an older scene still has it.
    public static class ShadowPolishV1
    {
        const string StateFolder = "Backups/ShadowPolishV1";
        const string StatePath = StateFolder + "/original.json";
        const string ContactName = "Contact Shadow";
        const string Fields = "Assets/Art/FarmReference/Fields/";

        [Serializable]
        sealed class Snapshot
        {
            public float shadowDistance; public int cascadeCount; public Vector3 cascade4Split; public int resolution;
            public int lightSoftQuality;
            public float strawRootShade = 1, earRootShade = 1, cornRootShade = 1;
        }

        public static bool IsApplied { get { return File.Exists(StatePath); } }

        [MenuItem("Tools/Farm Reference/Shadows/Apply Shadow Polish V1")]
        static void ApplyMenu() { Debug.Log(Apply()); }

        [MenuItem("Tools/Farm Reference/Shadows/Revert Shadow Polish V1")]
        static void RevertMenu() { Debug.Log(Revert()); }

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var urp = Pipeline(); var light = Sun();
            // Only the first Apply records originals, so re-applying never overwrites them with polished values.
            if (!IsApplied)
            {
                var s = new Snapshot
                {
                    shadowDistance = urp.shadowDistance, cascadeCount = urp.shadowCascadeCount,
                    cascade4Split = urp.cascade4Split, resolution = urp.mainLightShadowmapResolution,
                    lightSoftQuality = (int)light.softShadowQuality,
                    strawRootShade = RootShade("WheatStraw"), earRootShade = RootShade("WheatEar"), cornRootShade = RootShade("CornPlant")
                };
                Directory.CreateDirectory(StateFolder);
                File.WriteAllText(StatePath, JsonUtility.ToJson(s, true));
            }
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Apply shadow polish V1");

            // 1. Sun shadows: shorter range, a tight first cascade around the small player, 4096 map, High soft filter.
            Undo.RecordObject(urp, "Shadow settings");
            urp.shadowDistance = 90f; urp.shadowCascadeCount = 4;
            urp.cascade4Split = new Vector3(.065f, .2f, .45f);
            urp.mainLightShadowmapResolution = 4096;
            EditorUtility.SetDirty(urp); AssetDatabase.SaveAssetIfDirty(urp);
            Undo.RecordObject(light, "Sun soft shadows");
            light.softShadowQuality = SoftShadowQuality.High; EditorUtility.SetDirty(light);

            // 2. Crops: corn and wheat cast real (swaying) shadows and get slightly darker roots.
            ApplyCropShadows(true);
            RemoveContactShadow();
            EditorSceneManager.MarkSceneDirty(light.gameObject.scene);
            return "Shadow polish applied; originals in " + StatePath;
        }

        public static string Revert()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            if (!IsApplied) return "Nothing to revert (no " + StatePath + ").";
            var s = JsonUtility.FromJson<Snapshot>(File.ReadAllText(StatePath));
            var urp = Pipeline(); var light = Sun();
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Revert shadow polish V1");
            Undo.RecordObject(urp, "Shadow settings");
            urp.shadowDistance = s.shadowDistance; urp.shadowCascadeCount = s.cascadeCount;
            urp.cascade4Split = s.cascade4Split; urp.mainLightShadowmapResolution = s.resolution;
            EditorUtility.SetDirty(urp); AssetDatabase.SaveAssetIfDirty(urp);
            Undo.RecordObject(light, "Sun soft shadows");
            light.softShadowQuality = (SoftShadowQuality)s.lightSoftQuality; EditorUtility.SetDirty(light);
            SetRootShade("WheatStraw", s.strawRootShade); SetRootShade("WheatEar", s.earRootShade); SetRootShade("CornPlant", s.cornRootShade);
            SetCropShadows(false);
            RemoveContactShadow();
            EditorSceneManager.MarkSceneDirty(light.gameObject.scene);
            File.Delete(StatePath);
            return "Shadow polish reverted.";
        }

        // Also called by MixedCropFieldsV1 after it rebuilds the fields, so the polish survives a rebuild.
        public static void ApplyCropShadows(bool on)
        {
            SetCropShadows(on);
            SetRootShade("WheatStraw", on ? .7f : 1f); SetRootShade("WheatEar", 1f); SetRootShade("CornPlant", on ? .7f : 1f);
        }

        static void SetCropShadows(bool on)
        {
            foreach (var field in new[]{ "Corn Field", "Wheat Field" })
            {
                var group = GameObject.Find("Farm Reference Map/02 Crop Fields/Mixed Crop Fields/" + field);
                if (group == null) continue;
                foreach (var r in group.GetComponentsInChildren<MeshRenderer>())
                { Undo.RecordObject(r, "Crop shadows"); r.shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.Off; }
            }
        }

        static void RemoveContactShadow()
        {
            var player = GameObject.Find("ChickPlayer");
            var blob = player != null ? player.transform.Find(ContactName) : null;
            if (blob != null) Undo.DestroyObjectImmediate(blob.gameObject);
        }

        static float RootShade(string material)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Fields + material + ".mat");
            return m != null && m.HasProperty("_RootShade") ? m.GetFloat("_RootShade") : 1f;
        }

        static void SetRootShade(string material, float value)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Fields + material + ".mat");
            if (m == null) return;
            Undo.RecordObject(m, "Root shade"); m.SetFloat("_RootShade", value); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
        }

        static UniversalRenderPipelineAsset Pipeline()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) throw new InvalidOperationException("Active pipeline is not URP.");
            return urp;
        }

        static UniversalAdditionalLightData Sun()
        {
            var sun = GameObject.Find("Directional Light");
            var data = sun != null ? sun.GetComponent<UniversalAdditionalLightData>() : null;
            if (data == null) throw new InvalidOperationException("Directional Light with URP data missing.");
            return data;
        }
    }
}
