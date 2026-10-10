using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
 // Replaces the 3D south mountain with a painted panorama (ArtSource/Mountains/mountain_panorama.py) on a far arc
 // around the farm. Reversible: the 3D mountain objects are only hidden (recorded on the backdrop's MapSectionRecord)
 // and "Remove" shows them again.
 public static class MountainBackdropV1
 {
  const string Menu="Tools/Chick/Mountain Backdrop/";
  const string Folder="Assets/Art/MountainPanorama/";
  const string TexturePath=Folder+"MountainPanorama.png",MaterialPath=Folder+"M_MountainBackdrop.mat",MeshPath=Folder+"MountainBackdropArc.asset";
  public const string ObjectName="South Mountain - painted backdrop";
  // The arc: centred on the farm, from 100 degrees west of south to 100 east; the painting is 2792.5 x 240 m.
  static readonly Vector3 Centre=new Vector3(94f,-50f,4f);
  const float Radius=800f,Span=200f,Height=240f;
  const int Segments=96;

  [MenuItem(Menu+"Build Painted Backdrop (V1)")]
  static void BuildMenu(){Debug.Log(Build());}
  [MenuItem(Menu+"Remove Painted Backdrop (show 3D mountain)")]
  static void RemoveMenu(){Debug.Log(Remove());}

  public static string Build()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode first.");
   var farm=GameObject.Find("Farm Reference Map");if(farm==null)throw new InvalidOperationException("Farm root missing.");
   var mountain=farm.transform.Find("South Mountain - distant scenery");
   if(mountain==null)throw new InvalidOperationException("South mountain group missing.");
   if(mountain.Find(ObjectName)!=null)Remove();
   var texture=ImportTexture();
   Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Build mountain backdrop");int undoGroup=Undo.GetCurrentGroup();

   var go=new GameObject(ObjectName);Undo.RegisterCreatedObjectUndo(go,"Build mountain backdrop");
   go.transform.SetParent(mountain,false);
   go.transform.position=Centre;
   go.AddComponent<MeshFilter>().sharedMesh=ArcMesh();
   var renderer=go.AddComponent<MeshRenderer>();
   renderer.sharedMaterial=Material(texture);
   renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
   renderer.receiveShadows=false;
   renderer.lightProbeUsage=UnityEngine.Rendering.LightProbeUsage.Off;
   renderer.reflectionProbeUsage=UnityEngine.Rendering.ReflectionProbeUsage.Off;

   // Hide the 3D ridge (its LODs) and the woodland silhouettes that stood on its slopes.
   var record=go.AddComponent<MapSectionRecord>();
   var hidden=new System.Collections.Generic.List<GameObject>();
   foreach(Transform child in mountain)
    if(child.name!=ObjectName&&child.gameObject.activeSelf)
    {Undo.RecordObject(child.gameObject,"Build mountain backdrop");child.gameObject.SetActive(false);hidden.Add(child.gameObject);}
   record.hidden=hidden.ToArray();
   Undo.CollapseUndoOperations(undoGroup);
   EditorSceneManager.MarkSceneDirty(farm.scene);
   return "Painted mountain backdrop built ("+Span+" degree arc, radius "+Radius+" m); hidden "+hidden.Count+" 3D mountain objects.";
  }

  public static string Remove()
  {
   var farm=GameObject.Find("Farm Reference Map");if(farm==null)throw new InvalidOperationException("Farm root missing.");
   var backdrop=farm.transform.Find("South Mountain - distant scenery/"+ObjectName);
   if(backdrop==null)return "No painted backdrop to remove.";
   var record=backdrop.GetComponent<MapSectionRecord>();
   if(record!=null)foreach(var go in record.hidden)if(go!=null){Undo.RecordObject(go,"Remove mountain backdrop");go.SetActive(true);}
   Undo.DestroyObjectImmediate(backdrop.gameObject);
   EditorSceneManager.MarkSceneDirty(farm.scene);
   return "Painted backdrop removed; the 3D mountain is shown again.";
  }

  static Texture2D ImportTexture()
  {
   var importer=(TextureImporter)AssetImporter.GetAtPath(TexturePath);
   if(importer==null)throw new InvalidOperationException("Render the panorama first: "+TexturePath);
   importer.textureType=TextureImporterType.Default;
   importer.sRGBTexture=true;
   importer.alphaIsTransparency=true;
   importer.wrapMode=TextureWrapMode.Clamp;
   importer.mipmapEnabled=true;
   importer.maxTextureSize=8192;
   importer.textureCompression=TextureImporterCompression.CompressedHQ;
   importer.SaveAndReimport();
   return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
  }

  static Material Material(Texture2D texture)
  {
   var mat=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
   if(mat==null)
   {
    mat=new Material(Shader.Find("Chick/MountainBackdrop")){name="M_MountainBackdrop"};
    AssetDatabase.CreateAsset(mat,MaterialPath);
   }
   mat.SetTexture("_MainTex",texture);
   EditorUtility.SetDirty(mat);
   return mat;
  }

  // A vertical band on the inside of a circle. Seen from the farm looking south, east is on the left, so u runs
  // east -> west to show the painting unmirrored; v runs bottom -> top.
  static Mesh ArcMesh()
  {
   var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
   bool created=mesh==null;
   if(created)mesh=new Mesh{name="MountainBackdropArc"};
   var vertices=new Vector3[(Segments+1)*2];var uv=new Vector2[vertices.Length];var normals=new Vector3[vertices.Length];
   var triangles=new int[Segments*6];
   for(int i=0;i<=Segments;i++)
   {
    float u=i/(float)Segments,angle=(-Span/2+Span*u)*Mathf.Deg2Rad;
    // Angle 0 is due south (-Z); positive angles turn east (+X).
    var dir=new Vector3(Mathf.Sin(angle),0,-Mathf.Cos(angle));
    vertices[i*2]=dir*Radius;vertices[i*2+1]=dir*Radius+Vector3.up*Height;
    uv[i*2]=new Vector2(1-u,0);uv[i*2+1]=new Vector2(1-u,1);
    normals[i*2]=normals[i*2+1]=-dir;
    if(i==Segments)continue;
    int t=i*6,v=i*2;
    triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;triangles[t+3]=v+2;triangles[t+4]=v+1;triangles[t+5]=v+3;
   }
   mesh.Clear();mesh.vertices=vertices;mesh.uv=uv;mesh.normals=normals;mesh.triangles=triangles;mesh.RecalculateBounds();
   if(created)AssetDatabase.CreateAsset(mesh,MeshPath);else EditorUtility.SetDirty(mesh);
   AssetDatabase.SaveAssets();
   return mesh;
  }
 }
}
