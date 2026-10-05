using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
 // Switches only the ridge LOD renderers between the original vertex-colour palette (V1)
 // and the procedural natural surface (V2). Meshes, V1 shader and V1 material are never modified,
 // and the foothill tree silhouettes keep the V1 material either way.
 public static class SouthMountainLookV2
 {
  public const string OriginalMaterial=SouthMountainV1.Folder+"/MountainPalette.mat";
  public const string NaturalMaterial=SouthMountainV1.Folder+"/MountainNaturalV2.mat";
  const string Menu="Tools/Chick/Mountain/";

  [MenuItem(Menu+"Apply Natural Look (V2)")]
  public static void ApplyMenu(){Debug.Log(Apply());}
  [MenuItem(Menu+"Restore Original Look (V1)")]
  public static void RestoreMenu(){Debug.Log(Restore());}

  public static string Apply()
  {
   var mat=AssetDatabase.LoadAssetAtPath<Material>(NaturalMaterial);
   if(mat==null)
   {
    var shader=Shader.Find("Chick/DistantMountainV2");
    if(shader==null||!shader.isSupported)throw new InvalidOperationException("Chick/DistantMountainV2 shader not ready.");
    mat=new Material(shader){name="MountainNaturalV2"};
    var original=AssetDatabase.LoadAssetAtPath<Material>(OriginalMaterial);
    if(original!=null){mat.SetColor("_HazeColor",original.GetColor("_HazeColor"));mat.SetFloat("_HazeStrength",original.GetFloat("_HazeStrength"));}
    AssetDatabase.CreateAsset(mat,NaturalMaterial);AssetDatabase.SaveAssets();
   }
   return Assign(mat,"Apply natural mountain look");
  }

  public static string Restore()
  {
   var mat=AssetDatabase.LoadAssetAtPath<Material>(OriginalMaterial);
   if(mat==null)throw new InvalidOperationException("Original material missing: "+OriginalMaterial);
   return Assign(mat,"Restore original mountain look");
  }

  static string Assign(Material mat,string undoName)
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before switching the mountain look.");
   var root=GameObject.Find("Farm Reference Map");if(root==null)throw new InvalidOperationException("Farm root missing.");
   var group=root.transform.Find(SouthMountainV1.GroupName);if(group==null)throw new InvalidOperationException("Mountain group missing.");
   int count=0;
   for(int i=0;i<3;i++)
   {
    var lod=group.Find("Mountain LOD "+i);if(lod==null)continue;
    var r=lod.GetComponent<MeshRenderer>();if(r==null)continue;
    Undo.RecordObject(r,undoName);r.sharedMaterial=mat;count++;
   }
   if(count==0)throw new InvalidOperationException("No mountain LOD renderers found.");
   EditorSceneManager.MarkSceneDirty(root.scene);
   return mat.name+" assigned to "+count+" mountain LOD renderers.";
  }
 }
}
