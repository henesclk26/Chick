using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static FarmReferenceTools.SheepPastureV2;

namespace FarmReferenceTools
{
 // The farm's link to the outside world: a dirt road leaves the barn yard through a closed gate in the west fence
 // and climbs the hill westwards; the small hump south of the windmill is levelled. Reversible like the other map
 // sections: terrain edits go to a copy (FarmTerrain_WestRoad_v1) of the active terrain, hidden and re-seated
 // scenery is recorded on the section's MapSectionRecord.
 // shortcut: rebuilding the sheep pasture restores the pasture's own terrain copy and drops this road; remove the road first, rebuild the pasture, then build the road again.
 public static class FarmWestRoadV1
 {
  const string Menu="Tools/Chick/West Road/";
  public const string RootName="08 West Road";
  public const string TerrainPath="Assets/Art/Terrain/WestRoadV1/FarmTerrain_WestRoad_v1.asset";
  const string GateSegment="Outer West 09";
  // Road centre line from the end of the yard path, through the gate (x 59.3), up the hill to the west.
  static readonly Vector2[] Road={new Vector2(66.5f,12.9f),new Vector2(59.3f,12.2f),new Vector2(52f,12.6f),new Vector2(40f,13.4f),
   new Vector2(28f,14.6f),new Vector2(14f,15.5f),new Vector2(0f,16.2f)};
  const float HalfWidth=1.6f,Edge=1.2f;
  static readonly Vector2 HumpCenter=new Vector2(64.8f,7.6f);
  const float HumpRadius=5.5f,HumpBlend=3f,YardLevel=-11.75f;
  const int TroddenEarth=4,Pebbles=6,WornMeadow=7;

  [MenuItem(Menu+"Build West Road (V1)")]
  static void BuildMenu(){Debug.Log(Build());}
  [MenuItem(Menu+"Remove West Road")]
  static void RemoveMenu(){Debug.Log(Remove());}

  // Distance to the road centre line and the position along it (m from the yard end).
  static float RoadDistance(Vector2 p,out float along)
  {
   float best=float.PositiveInfinity,walked=0;along=0;
   for(int i=1;i<Road.Length;i++)
   {
    Vector2 a=Road[i-1],ab=Road[i]-a;
    float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude),d=(a+ab*t-p).magnitude;
    if(d<best){best=d;along=walked+ab.magnitude*t;}
    walked+=ab.magnitude;
   }
   return best;
  }

  static Vector2 RoadPoint(float along)
  {
   for(int i=1;i<Road.Length;i++)
   {
    float len=(Road[i]-Road[i-1]).magnitude;
    if(along<=len)return Vector2.Lerp(Road[i-1],Road[i],along/len);
    along-=len;
   }
   return Road[Road.Length-1];
  }

  public static string Build()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before building the road.");
   var farm=GameObject.Find("Farm Reference Map");if(farm==null)throw new InvalidOperationException("Farm root missing.");
   var terrain=farm.GetComponentInChildren<Terrain>();if(terrain==null)throw new InvalidOperationException("Farm terrain missing.");
   if(farm.transform.Find(RootName)!=null)Remove();
   var source=terrain.terrainData;
   if(AssetDatabase.GetAssetPath(source)==TerrainPath)throw new InvalidOperationException("Terrain is already the road copy but no record of its source was found.");
   Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Build west road");int undoGroup=Undo.GetCurrentGroup();
   var report=new System.Text.StringBuilder();

   if(!AssetDatabase.IsValidFolder("Assets/Art/Terrain/WestRoadV1"))AssetDatabase.CreateFolder("Assets/Art/Terrain","WestRoadV1");
   var data=AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
   if(data==null)
   {
    if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source),TerrainPath))throw new InvalidOperationException("Could not copy the terrain.");
    data=AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
   }
   else CopyWhole(source,data);
   Reshape(terrain,source,data,report);
   Undo.RecordObject(terrain,"Build west road");terrain.terrainData=data;
   var tc=terrain.GetComponent<TerrainCollider>();
   if(tc!=null){Undo.RecordObject(tc,"Build west road");tc.terrainData=data;}

   var root=new GameObject(RootName);Undo.RegisterCreatedObjectUndo(root,"Build west road");
   root.transform.SetParent(farm.transform,false);
   var record=root.AddComponent<MapSectionRecord>();
   record.sourceTerrain=source;
   record.hidden=HideCovered(farm.transform,report).ToArray();
   Reseat(farm.transform,terrain,source,record,report);
   BuildGate(root.transform,terrain,farm.transform.Find("04 Fences and Gates/"+GateSegment),report);
   Undo.CollapseUndoOperations(undoGroup);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(farm.scene);
   return "West road built.\n"+report;
  }

  public static string Remove()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before removing the road.");
   var farm=GameObject.Find("Farm Reference Map");if(farm==null)throw new InvalidOperationException("Farm root missing.");
   var root=farm.transform.Find(RootName);if(root==null)return "No west road to remove.";
   var record=root.GetComponent<MapSectionRecord>();
   var terrain=farm.GetComponentInChildren<Terrain>();
   if(record!=null)
   {
    if(record.sourceTerrain!=null&&terrain!=null)
    {
     Undo.RecordObject(terrain,"Remove west road");terrain.terrainData=record.sourceTerrain;
     var tc=terrain.GetComponent<TerrainCollider>();
     if(tc!=null){Undo.RecordObject(tc,"Remove west road");tc.terrainData=record.sourceTerrain;}
    }
    foreach(var go in record.hidden)if(go!=null){Undo.RecordObject(go,"Remove west road");go.SetActive(true);}
    for(int i=0;i<record.reseated.Length;i++)
     if(record.reseated[i]!=null){Undo.RecordObject(record.reseated[i],"Remove west road");record.reseated[i].position-=Vector3.up*record.reseatOffsets[i];}
   }
   Undo.DestroyObjectImmediate(root.gameObject);
   EditorSceneManager.MarkSceneDirty(farm.scene);
   return "West road removed: terrain, hidden scenery and the fence bay restored.";
  }

  static void Reshape(Terrain terrain,TerrainData src,TerrainData dst,System.Text.StringBuilder report)
  {
   Vector3 tp=terrain.transform.position,size=src.size;
   int res=src.heightmapResolution;
   float minX=-3,maxX=72,minZ=0,maxZ=22;
   // The road follows the hill's own rise, smoothed along its length, and is level across its width.
   float length=0;for(int i=1;i<Road.Length;i++)length+=(Road[i]-Road[i-1]).magnitude;
   var profile=new float[Mathf.CeilToInt(length)+1];
   for(int i=0;i<profile.Length;i++)profile[i]=Height(terrain,src,At(RoadPoint(i),0));
   var smooth=new float[profile.Length];
   for(int i=0;i<profile.Length;i++){float sum=0;int n=0;for(int k=Mathf.Max(0,i-4);k<=Mathf.Min(profile.Length-1,i+4);k++){sum+=profile[k];n++;}smooth[i]=sum/n;}

   int x0=Mathf.FloorToInt((minX-tp.x)/size.x*(res-1)),x1=Mathf.CeilToInt((maxX-tp.x)/size.x*(res-1));
   int z0=Mathf.FloorToInt((minZ-tp.z)/size.z*(res-1)),z1=Mathf.CeilToInt((maxZ-tp.z)/size.z*(res-1));
   var h=src.GetHeights(x0,z0,x1-x0+1,z1-z0+1);
   float hump=0;
   for(int iz=0;iz<h.GetLength(0);iz++)for(int ix=0;ix<h.GetLength(1);ix++)
   {
    var p=new Vector2(tp.x+(x0+ix)/(float)(res-1)*size.x,tp.z+(z0+iz)/(float)(res-1)*size.z);
    float h0=h[iz,ix]*size.y+tp.y,y=h0;
    float d=RoadDistance(p,out float along);
    float centre=smooth[Mathf.Clamp(Mathf.RoundToInt(along),0,smooth.Length-1)];
    y=Mathf.Lerp(y,centre,1-S(HalfWidth,HalfWidth+Edge,d));
    // Level the hump south of the windmill down to the yard; never raise ground.
    float w=1-S(HumpRadius,HumpRadius+HumpBlend,(p-HumpCenter).magnitude);
    if(y>YardLevel){float lowered=Mathf.Lerp(y,YardLevel,w);hump=Mathf.Max(hump,y-lowered);y=lowered;}
    h[iz,ix]=(y-tp.y)/size.y;
   }
   dst.SetHeights(x0,z0,h);
   report.AppendLine("Ground: road levelled across its width; hump south of the windmill lowered by up to "+hump.ToString("F2")+" m.");

   int ares=src.alphamapResolution,layers=src.alphamapLayers;
   int ax0=Mathf.FloorToInt((minX-tp.x)/size.x*ares),ax1=Mathf.CeilToInt((maxX-tp.x)/size.x*ares);
   int az0=Mathf.FloorToInt((minZ-tp.z)/size.z*ares),az1=Mathf.CeilToInt((maxZ-tp.z)/size.z*ares);
   var a=src.GetAlphamaps(ax0,az0,ax1-ax0+1,az1-az0+1);
   var target=new float[layers];
   for(int iz=0;iz<a.GetLength(0);iz++)for(int ix=0;ix<a.GetLength(1);ix++)
   {
    float x=tp.x+(ax0+ix+.5f)/ares*size.x,z=tp.z+(az0+iz+.5f)/ares*size.z;
    float n=Mathf.PerlinNoise(x*1.3f+11,z*1.3f+5),fine=Mathf.PerlinNoise(x*4.1f,z*4.1f);
    float d=RoadDistance(new Vector2(x,z),out _);
    float weight=1-S(HalfWidth-.4f,HalfWidth+.5f,d+(n-.5f)*.6f);
    if(weight<=.001f)continue;
    Array.Clear(target,0,layers);
    target[TroddenEarth]=.62f+.2f*fine;target[Pebbles]=.14f;target[WornMeadow]=.24f-.2f*fine;
    float sum=0;
    for(int k=0;k<layers;k++){a[iz,ix,k]=Mathf.Lerp(a[iz,ix,k],target[k],weight);sum+=a[iz,ix,k];}
    for(int k=0;k<layers;k++)a[iz,ix,k]/=Mathf.Max(sum,1e-5f);
   }
   dst.SetAlphamaps(ax0,az0,a);

   int dres=src.detailWidth,cleared=0;
   int dx0=Mathf.Max(0,Mathf.FloorToInt((minX-tp.x)/size.x*dres)),dx1=Mathf.Min(dres-1,Mathf.CeilToInt((maxX-tp.x)/size.x*dres));
   int dz0=Mathf.Max(0,Mathf.FloorToInt((minZ-tp.z)/size.z*src.detailHeight)),dz1=Mathf.Min(src.detailHeight-1,Mathf.CeilToInt((maxZ-tp.z)/size.z*src.detailHeight));
   for(int layer=0;layer<src.detailPrototypes.Length;layer++)
   {
    var m=src.GetDetailLayer(dx0,dz0,dx1-dx0+1,dz1-dz0+1,layer);
    for(int iz=0;iz<m.GetLength(0);iz++)for(int ix=0;ix<m.GetLength(1);ix++)
    {
     if(m[iz,ix]==0)continue;
     float x=tp.x+(dx0+ix+.5f)/dres*size.x,z=tp.z+(dz0+iz+.5f)/src.detailHeight*size.z;
     if(RoadDistance(new Vector2(x,z),out _)<HalfWidth+.2f){cleared+=m[iz,ix];m[iz,ix]=0;}
    }
    dst.SetDetailLayer(dx0,dz0,layer,m);
   }
   report.AppendLine("Grass instances cleared from the road: "+cleared+".");
   EditorUtility.SetDirty(dst);
  }

  static List<GameObject> HideCovered(Transform farm,System.Text.StringBuilder report)
  {
   var hidden=new List<GameObject>();
   var fence=farm.Find("04 Fences and Gates/"+GateSegment);
   if(fence!=null&&fence.gameObject.activeSelf){Undo.RecordObject(fence.gameObject,"Build west road");fence.gameObject.SetActive(false);hidden.Add(fence.gameObject);}
   var nature=farm.Find("05 Nature and Scenery");
   if(nature!=null)
    foreach(Transform item in Scenery(nature))
    {
     if(!item.gameObject.activeSelf)continue;
     float d=RoadDistance(new Vector2(item.position.x,item.position.z),out _);
     if(d>=HalfWidth+(IsTuft(item)?.5f:1.6f))continue;
     Undo.RecordObject(item.gameObject,"Build west road");item.gameObject.SetActive(false);hidden.Add(item.gameObject);
    }
   report.AppendLine("Hidden (not deleted): fence bay "+GateSegment+" (now the gate) and "+(hidden.Count-1)+" scenery objects on the road.");
   return hidden;
  }

  // Scenery on the changed ground (the hump, the road's banks) follows it, so nothing floats or sinks.
  static void Reseat(Transform farm,Terrain terrain,TerrainData source,MapSectionRecord record,System.Text.StringBuilder report)
  {
   var moved=new List<Transform>();var offsets=new List<float>();
   var nature=farm.Find("05 Nature and Scenery");
   if(nature!=null)
    foreach(Transform item in Scenery(nature))
    {
     if(!item.gameObject.activeSelf)continue;
     var p=item.position;
     if(p.x<-3||p.x>72||p.z<0||p.z>22)continue;
     float offset=Height(terrain,terrain.terrainData,p)-Height(terrain,source,p);
     if(Mathf.Abs(offset)<.03f)continue;
     Undo.RecordObject(item,"Build west road");item.position+=Vector3.up*offset;
     moved.Add(item);offsets.Add(offset);
    }
   record.reseated=moved.ToArray();record.reseatOffsets=offsets.ToArray();
   report.AppendLine("Re-seated on the changed ground: "+moved.Count+" scenery objects.");
  }

  // The pasture's gate model fills the fence bay, its leaf shut.
  static void BuildGate(Transform root,Terrain terrain,Transform bay,System.Text.StringBuilder report)
  {
   Vector3 p=bay!=null?bay.position:new Vector3(59.3f,0,11.9f);
   var gate=Place("PastureGate",root,At(new Vector2(p.x,p.z),Ground(terrain,new Vector2(p.x,p.z))),90,"Gate - Farm to Outside (closed)");
   foreach(float x in new[]{-1.4f,1.4f})AddBox(gate,new Vector3(x,.8f,0),new Vector3(.2f,1.6f,.2f),true);
   var leaf=gate.transform.Find("PastureGate_Leaf");
   if(leaf!=null)
   {
    var lb=leaf.GetComponent<MeshFilter>().sharedMesh.bounds;
    var box=leaf.gameObject.AddComponent<BoxCollider>();box.center=lb.center;box.size=new Vector3(lb.size.x,lb.size.y,.12f);
   }
   report.AppendLine("Gate: in the west fence (bay "+GateSegment+"), closed.");
  }
 }
}
