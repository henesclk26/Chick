using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static FarmReferenceTools.SheepPastureV2;

namespace FarmReferenceTools
{
 // Opens up the land west of the farm:
 //  - Beyond the west fence the ground carries on flat at the farm's own level for FlatWidth metres, from the map's
 //    south edge up to the farm's north fence (fading out north of it); only then does the hill rise. The hill is
 //    squeezed into the remaining width rather than cut off.
 //  - The west road (FarmWestRoadV1) is re-laid level across the flat and then as one straight, even slope, and it
 //    now carries on past its old end over the hill (RoadExtension), painted and cleared like the rest of it.
 //  - The map grows westwards by ExtensionWidth: the terrain itself is widened (one terrain, so no seam where a second
 //    tile would meet it) and the hill rolls on there, rising gently at the far edge, with the same ground, grass and a
 //    mirrored copy of the trees and rocks along the old west edge.
 // Reversible like the other map sections: terrain edits go to a copy (FarmTerrain_WestHill_v1) of the active terrain;
 // hidden and re-seated scenery and the terrain's old position are recorded on the section's MapSectionRecord, and
 // the scenery carried onto the new ground lives under the section's root.
 // Because it moves and resizes the terrain, remove this section first before removing or rebuilding an earlier one
 // (west road, pond, pasture...): their records hold terrain copies of the old size and position.
 public static class FarmWestHillV1
 {
  const string Menu="Tools/Chick/West Hill/";
  public const string RootName="09 West Hill";
  const string Folder="Assets/Art/Terrain/WestHillV1";
  const string TerrainPath=Folder+"/FarmTerrain_WestHill_v1.asset";
  const string OldExtensionPath=Folder+"/FarmTerrain_WestExtension_v1.asset";   // separate tile of an earlier build
  const float FenceX=59.3f;                   // west fence (world x; FarmWestRoadV1's gate bay)
  const float FlatWidth=50f;                  // flat farmland beyond the fence before the hill starts
  const float FarmMaxZ=36f,ZFade=20f;         // the farm's north fence (world z); the flat fades out north of it
  const float FarmProbe=1.5f;                 // farm ground is read this far inside the fence
  const float ExtensionWidth=40f;             // how far the map grows to the west
  const float EdgeSkip=8f;                    // the old west edge band (painted as a map border) is repainted too
  // The road carries on from its old end (0, 16.2) over the hill.
  static readonly Vector2[] RoadExtension={new Vector2(0f,16.2f),new Vector2(-12f,16.7f),new Vector2(-25f,17.5f)};
  const int TroddenEarth=4,Pebbles=6,WornMeadow=7;    // terrain layers, as FarmWestRoadV1 paints them

  [MenuItem(Menu+"Build West Hill (V1)")]
  static void BuildMenu(){Debug.Log(Build());}
  [MenuItem(Menu+"Remove West Hill")]
  static void RemoveMenu(){Debug.Log(Remove());}

  public static string Build()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before reshaping the hill.");
   var farm=GameObject.Find("Farm Reference Map");if(farm==null)throw new InvalidOperationException("Farm root missing.");
   if(farm.transform.Find(RootName)!=null)Remove();
   var terrain=MainTerrain(farm.transform);if(terrain==null)throw new InvalidOperationException("Farm terrain missing.");
   var source=terrain.terrainData;
   if(AssetDatabase.GetAssetPath(source)==TerrainPath)throw new InvalidOperationException("Terrain is already the hill copy but no record of its source was found.");
   Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Build west hill");int undoGroup=Undo.GetCurrentGroup();
   var report=new System.Text.StringBuilder();

   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Art/Terrain","WestHillV1");
   var data=AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
   if(data==null)
   {
    if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source),TerrainPath))throw new InvalidOperationException("Could not copy the terrain.");
    data=AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
   }
   else CopyWhole(source,data);
   Reshape(terrain,source,data,report);
   bool road=farm.transform.Find(FarmWestRoadV1.RootName)!=null;
   if(road)LayRoad(terrain,data,report);
   EditorUtility.SetDirty(data);
   Undo.RecordObject(terrain,"Build west hill");terrain.terrainData=data;
   var tc=terrain.GetComponent<TerrainCollider>();
   if(tc!=null){Undo.RecordObject(tc,"Build west hill");tc.terrainData=data;}

   var root=new GameObject(RootName);Undo.RegisterCreatedObjectUndo(root,"Build west hill");
   root.transform.SetParent(farm.transform,false);
   var record=root.AddComponent<MapSectionRecord>();
   record.sourceTerrain=source;
   record.hidden=road?HideOnExtension(farm.transform,report).ToArray():new GameObject[0];
   Reseat(farm.transform,terrain,source,record,report);
   float oldEdge=terrain.transform.position.x;
   record.movedTerrain=true;record.terrainPosition=terrain.transform.position;
   Widen(terrain,data,report);
   // Painted after widening, so the new ground's mirrored paint never copies the road extension.
   if(road)PaintExtensionRoad(terrain,data,report);
   MirrorScenery(farm.transform,terrain,oldEdge,root.transform,report);
   if(AssetDatabase.LoadAssetAtPath<TerrainData>(OldExtensionPath)!=null)AssetDatabase.DeleteAsset(OldExtensionPath);
   Undo.CollapseUndoOperations(undoGroup);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(farm.scene);
   return "West of the farm opened up.\n"+report;
  }

  public static string Remove()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before removing the hill change.");
   var farm=GameObject.Find("Farm Reference Map");if(farm==null)throw new InvalidOperationException("Farm root missing.");
   var root=farm.transform.Find(RootName);if(root==null)return "No west hill change to remove.";
   var record=root.GetComponent<MapSectionRecord>();
   var terrain=MainTerrain(farm.transform);
   if(record!=null)
   {
    if(record.sourceTerrain!=null&&terrain!=null)
    {
     Undo.RecordObject(terrain,"Remove west hill");terrain.terrainData=record.sourceTerrain;
     if(record.movedTerrain){Undo.RecordObject(terrain.transform,"Remove west hill");terrain.transform.position=record.terrainPosition;}
     var tc=terrain.GetComponent<TerrainCollider>();
     if(tc!=null){Undo.RecordObject(tc,"Remove west hill");tc.terrainData=record.sourceTerrain;}
    }
    foreach(var go in record.hidden)if(go!=null){Undo.RecordObject(go,"Remove west hill");go.SetActive(true);}
    for(int i=0;i<record.reseated.Length;i++)
     if(record.reseated[i]!=null){Undo.RecordObject(record.reseated[i],"Remove west hill");record.reseated[i].position-=Vector3.up*record.reseatOffsets[i];}
   }
   Undo.DestroyObjectImmediate(root.gameObject);   // the scenery carried onto the new ground with it
   EditorSceneManager.MarkSceneDirty(farm.scene);
   return "West hill change removed: terrain (size and position), road extension and scenery restored.";
  }

  // The farm's terrain (an earlier build of this section added a separate tile under its root; skip that one).
  static Terrain MainTerrain(Transform farm)
  {
   var section=farm.Find(RootName);
   foreach(var t in farm.GetComponentsInChildren<Terrain>())if(section==null||!t.transform.IsChildOf(section))return t;
   return null;
  }

  // How strongly a row at world z is reshaped: fully from the map's south edge to the farm's north fence, fading out
  // north of it so the north hills stay as they are.
  static float Weight(float z)=>1-S(0,ZFade,z-FarmMaxZ);

  static void Reshape(Terrain terrain,TerrainData src,TerrainData dst,System.Text.StringBuilder report)
  {
   Vector3 tp=terrain.transform.position,size=src.size;
   int res=src.heightmapResolution;
   float edge=tp.x,hillStart=FenceX-FlatWidth;
   int x1=Mathf.Min(res-1,Mathf.CeilToInt((FenceX-tp.x)/size.x*(res-1)));
   int z1=Mathf.Min(res-1,Mathf.CeilToInt((FarmMaxZ+ZFade-tp.z)/size.z*(res-1)));
   var h=src.GetHeights(0,0,x1+1,z1+1);
   float lowered=0,raised=0;
   for(int iz=0;iz<h.GetLength(0);iz++)
   {
    float z=tp.z+iz/(float)(res-1)*size.z,w=Weight(z);
    if(w<=0)continue;
    float farmY=Height(terrain,src,new Vector3(FenceX+FarmProbe,0,z));
    float baseY=Height(terrain,src,new Vector3(FenceX,0,z));
    for(int ix=0;ix<h.GetLength(1);ix++)
    {
     float x=tp.x+ix/(float)(res-1)*size.x;
     float old=h[iz,ix]*size.y+tp.y,target;
     if(x>=hillStart)target=farmY;
     else
     {
      // Squeeze the old hill (fence .. west edge) into the space left (hill start .. west edge).
      float s=(hillStart-x)/Mathf.Max(1e-3f,hillStart-edge);
      float from=FenceX-s*(FenceX-edge);
      target=Height(terrain,src,new Vector3(from,0,z))-baseY+farmY;
     }
     float y=Mathf.Lerp(old,target,w);
     lowered=Mathf.Max(lowered,old-y);raised=Mathf.Max(raised,y-old);
     h[iz,ix]=Mathf.Clamp01((y-tp.y)/size.y);
    }
   }
   dst.SetHeights(0,0,h);
   report.AppendLine("Ground: flat for "+FlatWidth+" m beyond the west fence (x "+hillStart.ToString("F1")+" to "+FenceX+
                     ") from the map's south edge to the farm's north fence, hill squeezed in west of that; lowered by up to "+
                     lowered.ToString("F1")+" m, raised by up to "+raised.ToString("F1")+" m.");
  }

  // ---------------------------------------------------------------- road

  static Vector2[] FullRoad()
  {
   var all=new List<Vector2>(FarmWestRoadV1.Road);
   for(int i=1;i<RoadExtension.Length;i++)all.Add(RoadExtension[i]);
   return all.ToArray();
  }

  static float Distance(Vector2[] line,Vector2 p,out float along)
  {
   float best=float.PositiveInfinity,walked=0;along=0;
   for(int i=1;i<line.Length;i++)
   {
    Vector2 a=line[i-1],ab=line[i]-a;
    float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude),d=(a+ab*t-p).magnitude;
    if(d<best){best=d;along=walked+ab.magnitude*t;}
    walked+=ab.magnitude;
   }
   return best;
  }

  static Vector2 PointAt(Vector2[] line,float along)
  {
   for(int i=1;i<line.Length;i++)
   {
    float len=(line[i]-line[i-1]).magnitude;
    if(along<=len)return Vector2.Lerp(line[i-1],line[i],along/len);
    along-=len;
   }
   return line[line.Length-1];
  }

  // The whole road on the new ground: level across its width; along it, the farm's level over the flat and then one
  // straight slope from the foot of the hill to the end of the extension.
  static void LayRoad(Terrain terrain,TerrainData data,System.Text.StringBuilder report)
  {
   Vector3 tp=terrain.transform.position,size=data.size;
   int res=data.heightmapResolution;
   var road=FullRoad();
   float length=0;for(int i=1;i<road.Length;i++)length+=(road[i]-road[i-1]).magnitude;
   float foot=0;                                           // distance along the road where the hill starts
   for(float a=0;a<=length;a+=.25f)if(PointAt(road,a).x>=FenceX-FlatWidth)foot=a;
   float footY=Height(terrain,data,At(PointAt(road,foot),0)),endY=Height(terrain,data,At(road[road.Length-1],0));
   // Along the flat the centre follows the (now level) ground; sampled before the heights change.
   var flat=new float[Mathf.CeilToInt(foot)+2];
   for(int i=0;i<flat.Length;i++)flat[i]=Height(terrain,data,At(PointAt(road,Mathf.Min(i,foot)),0));

   float reach=FarmWestRoadV1.HalfWidth+FarmWestRoadV1.Edge;
   float minX=road[road.Length-1].x-reach-1,maxX=FenceX+8,minZ=0,maxZ=22;
   int x0=Mathf.Max(0,Mathf.FloorToInt((minX-tp.x)/size.x*(res-1))),x1=Mathf.Min(res-1,Mathf.CeilToInt((maxX-tp.x)/size.x*(res-1)));
   int z0=Mathf.Max(0,Mathf.FloorToInt((minZ-tp.z)/size.z*(res-1))),z1=Mathf.Min(res-1,Mathf.CeilToInt((maxZ-tp.z)/size.z*(res-1)));
   var h=data.GetHeights(x0,z0,x1-x0+1,z1-z0+1);
   for(int iz=0;iz<h.GetLength(0);iz++)for(int ix=0;ix<h.GetLength(1);ix++)
   {
    var p=new Vector2(tp.x+(x0+ix)/(float)(res-1)*size.x,tp.z+(z0+iz)/(float)(res-1)*size.z);
    float d=Distance(road,p,out float along);
    if(d>=reach)continue;
    float centre;
    if(along<=foot){float f=Mathf.Clamp(along,0,flat.Length-1);int i0=Mathf.Min((int)f,flat.Length-2);centre=Mathf.Lerp(flat[i0],flat[i0+1],f-i0);}
    else centre=Mathf.Lerp(footY,endY,(along-foot)/Mathf.Max(1e-3f,length-foot));
    float y=h[iz,ix]*size.y+tp.y;
    y=Mathf.Lerp(y,centre,1-S(FarmWestRoadV1.HalfWidth,reach,d));
    h[iz,ix]=Mathf.Clamp01((y-tp.y)/size.y);
   }
   data.SetHeights(x0,z0,h);
   report.AppendLine("Road: level to the foot of the hill ("+foot.ToString("F0")+" m), then one even slope of "+
                     (Mathf.Atan2(endY-footY,length-foot)*Mathf.Rad2Deg).ToString("F1")+"° over "+(length-foot).ToString("F0")+" m up the hill.");
  }

  // The new stretch gets the same trodden earth and loses its grass, as FarmWestRoadV1 does for the rest.
  static void PaintExtensionRoad(Terrain terrain,TerrainData data,System.Text.StringBuilder report)
  {
   Vector3 tp=terrain.transform.position,size=data.size;
   float minX=RoadExtension[RoadExtension.Length-1].x-4,maxX=RoadExtension[0].x+1,minZ=12,maxZ=22,half=FarmWestRoadV1.HalfWidth;
   int ares=data.alphamapResolution,layers=data.alphamapLayers;
   int ax0=Mathf.Max(0,Mathf.FloorToInt((minX-tp.x)/size.x*ares)),ax1=Mathf.Min(ares-1,Mathf.CeilToInt((maxX-tp.x)/size.x*ares));
   int az0=Mathf.Max(0,Mathf.FloorToInt((minZ-tp.z)/size.z*ares)),az1=Mathf.Min(ares-1,Mathf.CeilToInt((maxZ-tp.z)/size.z*ares));
   var a=data.GetAlphamaps(ax0,az0,ax1-ax0+1,az1-az0+1);
   var target=new float[layers];
   for(int iz=0;iz<a.GetLength(0);iz++)for(int ix=0;ix<a.GetLength(1);ix++)
   {
    float x=tp.x+(ax0+ix+.5f)/ares*size.x,z=tp.z+(az0+iz+.5f)/ares*size.z;
    float n=Mathf.PerlinNoise(x*1.3f+11,z*1.3f+5),fine=Mathf.PerlinNoise(x*4.1f,z*4.1f);
    float d=Distance(RoadExtension,new Vector2(x,z),out _);
    float weight=1-S(half-.4f,half+.5f,d+(n-.5f)*.6f);
    if(weight<=.001f||layers<=WornMeadow)continue;
    Array.Clear(target,0,layers);
    target[TroddenEarth]=.62f+.2f*fine;target[Pebbles]=.14f;target[WornMeadow]=.24f-.2f*fine;
    float sum=0;
    for(int k=0;k<layers;k++){a[iz,ix,k]=Mathf.Lerp(a[iz,ix,k],target[k],weight);sum+=a[iz,ix,k];}
    for(int k=0;k<layers;k++)a[iz,ix,k]/=Mathf.Max(sum,1e-5f);
   }
   data.SetAlphamaps(ax0,az0,a);

   int dres=data.detailWidth,cleared=0;
   int dx0=Mathf.Max(0,Mathf.FloorToInt((minX-tp.x)/size.x*dres)),dx1=Mathf.Min(dres-1,Mathf.CeilToInt((maxX-tp.x)/size.x*dres));
   int dz0=Mathf.Max(0,Mathf.FloorToInt((minZ-tp.z)/size.z*data.detailHeight)),dz1=Mathf.Min(data.detailHeight-1,Mathf.CeilToInt((maxZ-tp.z)/size.z*data.detailHeight));
   for(int layer=0;layer<data.detailPrototypes.Length;layer++)
   {
    var m=data.GetDetailLayer(dx0,dz0,dx1-dx0+1,dz1-dz0+1,layer);
    for(int iz=0;iz<m.GetLength(0);iz++)for(int ix=0;ix<m.GetLength(1);ix++)
    {
     if(m[iz,ix]==0)continue;
     float x=tp.x+(dx0+ix+.5f)/dres*size.x,z=tp.z+(dz0+iz+.5f)/data.detailHeight*size.z;
     if(Distance(RoadExtension,new Vector2(x,z),out _)<half+.2f){cleared+=m[iz,ix];m[iz,ix]=0;}
    }
    data.SetDetailLayer(dx0,dz0,layer,m);
   }
   report.AppendLine("Road extension painted over the hill; grass cleared from it: "+cleared+".");
  }

  static List<GameObject> HideOnExtension(Transform farm,System.Text.StringBuilder report)
  {
   var hidden=new List<GameObject>();
   var nature=farm.Find("05 Nature and Scenery");
   if(nature!=null)
    foreach(Transform item in Scenery(nature))
    {
     if(!item.gameObject.activeSelf)continue;
     float d=Distance(RoadExtension,new Vector2(item.position.x,item.position.z),out _);
     if(d>=FarmWestRoadV1.HalfWidth+(IsTuft(item)?.5f:1.6f))continue;
     Undo.RecordObject(item.gameObject,"Build west hill");item.gameObject.SetActive(false);hidden.Add(item.gameObject);
    }
   report.AppendLine("Hidden (not deleted): "+hidden.Count+" scenery objects on the road extension.");
   return hidden;
  }

  // Scenery on the changed ground follows it, so nothing floats or sinks.
  static void Reseat(Transform farm,Terrain terrain,TerrainData source,MapSectionRecord record,System.Text.StringBuilder report)
  {
   var moved=new List<Transform>();var offsets=new List<float>();
   var nature=farm.Find("05 Nature and Scenery");
   if(nature!=null)
    foreach(Transform item in Scenery(nature))
    {
     var p=item.position;
     if(p.x>FenceX||Weight(p.z)<=0)continue;
     float offset=Height(terrain,terrain.terrainData,p)-Height(terrain,source,p);
     if(Mathf.Abs(offset)<.03f)continue;
     Undo.RecordObject(item,"Build west hill");item.position+=Vector3.up*offset;
     moved.Add(item);offsets.Add(offset);
    }
   record.reseated=moved.ToArray();record.reseatOffsets=offsets.ToArray();
   report.AppendLine("Re-seated on the changed ground: "+moved.Count+" scenery objects.");
  }

  // ---------------------------------------------------------------- map extension

  // Widens the terrain westwards by ExtensionWidth: the existing ground is resampled onto the wider grid (same
  // resolution, so slightly coarser), and the new strip carries the hill on from the old west edge with its own slope,
  // levelling off, rolling a little and rising softly towards the far side. Ground paint and grass there mirror the
  // ground just inside the old edge, skipping the old edge band (EdgeSkip), which was painted as a map border and would
  // otherwise show as a strip of different colour. Moves the terrain so everything already on it stays where it is.
  static void Widen(Terrain terrain,TerrainData data,System.Text.StringBuilder report)
  {
   Vector3 tp=terrain.transform.position,size=data.size;
   int res=data.heightmapResolution,ares=data.alphamapResolution,layers=data.alphamapLayers,dres=data.detailWidth;
   var oldH=data.GetHeights(0,0,res,res);
   var oldA=data.GetAlphamaps(0,0,ares,ares);
   var oldD=new int[data.detailPrototypes.Length][,];
   for(int l=0;l<oldD.Length;l++)oldD[l]=data.GetDetailLayer(0,0,dres,dres,l);
   float width=size.x+ExtensionWidth,mainStep=size.x/(res-1),origin=tp.x-ExtensionWidth;
   data.size=new Vector3(width,size.y,size.z);

   var h=new float[res,res];
   for(int iz=0;iz<res;iz++)
   {
    float z=tp.z+iz/(float)(res-1)*size.z;
    float e=oldH[iz,0]*size.y,rise=(oldH[iz,0]-oldH[iz,1])*size.y/mainStep;     // metres up per metre going west
    for(int ix=0;ix<res;ix++)
    {
     float x=origin+ix/(float)(res-1)*width;
     if(x>=tp.x)
     {
      float f=(x-tp.x)/size.x*(res-1);int i0=Mathf.Min((int)f,res-2);
      h[iz,ix]=Mathf.Lerp(oldH[iz,i0],oldH[iz,i0+1],f-i0);
      continue;
     }
     float u=tp.x-x;                                                             // metres west of the old edge
     float carry=rise*8f*(1-Mathf.Exp(-u/8f));
     float roll=(Mathf.PerlinNoise(x*.035f+3f,z*.035f+7f)-.5f)*5f*S(0,15,u);
     float rim=3.5f*S(ExtensionWidth*.45f,ExtensionWidth,u);
     h[iz,ix]=Mathf.Clamp01((e+carry+roll+rim)/size.y);
    }
   }
   data.SetHeights(0,0,h);

   // Where each new column's ground paint and grass come from: itself east of the mirror line, its mirror image west of it.
   float mirror=tp.x+EdgeSkip;
   int Source(float x,int cells)=>Mathf.Clamp((int)(((x>=mirror?x:2*mirror-x)-tp.x)/size.x*cells),0,cells-1);
   var a=new float[ares,ares,layers];
   for(int iz=0;iz<ares;iz++)for(int ix=0;ix<ares;ix++)
   {
    int from=Source(origin+(ix+.5f)/ares*width,ares);
    for(int k=0;k<layers;k++)a[iz,ix,k]=oldA[iz,from,k];
   }
   data.SetAlphamaps(0,0,a);
   for(int l=0;l<oldD.Length;l++)
   {
    var m=new int[dres,dres];
    for(int iz=0;iz<dres;iz++)for(int ix=0;ix<dres;ix++)
    {
     m[iz,ix]=oldD[l][iz,Source(origin+(ix+.5f)/dres*width,dres)];
    }
    data.SetDetailLayer(0,0,l,m);
   }
   EditorUtility.SetDirty(data);
   Undo.RecordObject(terrain.transform,"Build west hill");
   terrain.transform.position=new Vector3(origin,tp.y,tp.z);
   report.AppendLine("Map widened "+ExtensionWidth+" m to the west (terrain now from x "+origin.ToString("F1")+").");
  }

  // Trees and rocks for the new ground: the ones along the old west edge, mirrored across it and nudged a little, so
  // the woods carry on at the same density. Grass tufts are left to the terrain grass.
  static void MirrorScenery(Transform farm,Terrain terrain,float edge,Transform root,System.Text.StringBuilder report)
  {
   var nature=farm.Find("05 Nature and Scenery");if(nature==null)return;
   var holder=new GameObject("West Extension Scenery");
   Undo.RegisterCreatedObjectUndo(holder,"Build west hill");
   holder.transform.SetParent(root,false);
   int count=0;
   var random=new System.Random(41);
   foreach(Transform item in new List<Transform>(Scenery(nature)))
   {
    var p=item.position;
    if(!item.gameObject.activeInHierarchy||IsTuft(item)||p.x<edge||p.x>edge+ExtensionWidth)continue;
    float ground=Height(terrain,terrain.terrainData,p);
    var q=new Vector3(edge-(p.x-edge)-(float)random.NextDouble()*2f,0,p.z+((float)random.NextDouble()-.5f)*3f);
    if(q.x<=edge-ExtensionWidth+1f)continue;
    q.y=Height(terrain,terrain.terrainData,q)+(p.y-ground);
    var source=PrefabUtility.IsOutermostPrefabInstanceRoot(item.gameObject)?PrefabUtility.GetCorrespondingObjectFromSource(item.gameObject):null;
    GameObject copy=source!=null?(GameObject)PrefabUtility.InstantiatePrefab(source,holder.transform):UnityEngine.Object.Instantiate(item.gameObject,holder.transform);
    Undo.RegisterCreatedObjectUndo(copy,"Build west hill");
    copy.name=item.name;
    copy.transform.SetPositionAndRotation(q,Quaternion.Euler(0,(float)random.NextDouble()*360f,0)*item.rotation);
    copy.transform.localScale=item.lossyScale;
    count++;
   }
   report.AppendLine("Trees and rocks carried onto the new ground: "+count+".");
  }
 }
}
