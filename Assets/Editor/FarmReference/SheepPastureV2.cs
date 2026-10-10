using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
 // Fourth map section: a fenced sheep pasture south of the crop fields, the same size as the fields
 // (the farm's wooden fence continues around it). The hill inside is lowered to gentle ground and the
 // fold stands where the hilltop was: timber shelter, trough with a hand pump and live water, covered
 // hay rack and bales, with 5 sheep + 2 lambs grazing. A gate in the fields' south fence leads in.
 // Reversible: the terrain is reshaped on a copy (FarmTerrain_SheepPasture_v2) of whatever terrain was
 // active; hidden objects and re-seated scenery are recorded on the section's MapSectionRecord and
 // restored by "Remove Sheep Pasture".
 public static class SheepPastureV2
 {
  const string Menu="Tools/Chick/Sheep Pasture/";
  public const string RootName="07 Sheep Pasture";
  const string SpawnName="04 Sheep Pasture Spawn";
  public const string TerrainPath="Assets/Art/Terrain/SheepPastureV2/FarmTerrain_SheepPasture_v2.asset";
  const string Models=SheepFoldImporter.ModelFolder;
  const string AnimFolder=SheepFoldImporter.Folder+"/Animation";
  const string TroughWaterPath=SheepFoldImporter.Folder+"/M_TroughWater.mat";
  const string PondWaterPath="Assets/Art/FarmReference/Pond/PondWater.mat";
  const string DropletPath="Assets/Art/FarmReference/Pond/PondDroplet.mat";

  // Pasture rectangle: the crop fields' width (the farm's west and east fence lines) and depth, south of them.
  const float XMin=59.3f,XMax=129.1f,ZMax=-31.94f,Depth=32.04f,ZMin=ZMax-Depth;
  const string FenceRoot="04 Fences and Gates/";
  const string GateSegment="Outer South 19";
  const float FenceMeshWidth=2.49f;
  const int SideSegments=11;
  // The hill keeps this share of its height above the fields; Base is the fields' ground level.
  const float Keep=.4f,Base=-11.7f,Falloff=14f;
  // The fold, where the hilltop was; it opens north towards the gate.
  static readonly Vector2 ShelterPos=new Vector2(114.5f,-58.3f),TroughPos=new Vector2(119.6f,-53.6f),
   RackPos=new Vector2(108.6f,-54.6f),StackPos=new Vector2(118.6f,-58.9f),BalePos=new Vector2(106.4f,-56.6f);
  static readonly Vector2 GateInside=new Vector2(111f,-33.4f),PathBend=new Vector2(111.8f,-46f),PathEnd=new Vector2(114f,-55.4f);
  const int SunlitGreen=3,TroddenEarth=4,Soil=5,Pebbles=6,WornMeadow=7;
  // Shade trees west of the track, where the flock lies in the midday heat.
  static readonly Vector2[] ShadeTrees={new Vector2(100.5f,-50.5f),new Vector2(103.2f,-47.6f)};
  // The noon sun shines from the south, so the shade lies just north of the trees.
  static readonly Vector2 ShadeSpot=new Vector2(101.8f,-46.8f);
  // Yard clutter around the shelter.
  static readonly Vector2 WheelbarrowPos=new Vector2(117.9f,-55.9f),BucketPos=new Vector2(118.2f,-54.6f),RakePos=new Vector2(111.75f,-58.8f);
  static readonly Vector2[] StrawPatches={new Vector2(113.4f,-55.4f),new Vector2(115.9f,-55.8f),new Vector2(108.4f,-53.3f)};
  // Sleeping places inside the shelter, relative to it (its opening faces +Z).
  static readonly Vector2[] RestSpots={new Vector2(-1.65f,.45f),new Vector2(-.65f,.65f),new Vector2(.35f,.45f),new Vector2(1.35f,.15f),new Vector2(.4f,-.5f)};

  // Trough measurements in Unity model space, from ArtSource/SheepFold/props.py (TROUGH_*).
  const float TroughSurface=.205f,TroughWading=.13f,TroughRim=.235f;
  static readonly Vector2 TroughWaterHalf=new Vector2(.84f,.195f);
  static readonly Vector3 TroughSpout=new Vector3(-.76f,.58f,0f);

  struct Pad{public Vector2 c,half;public float blend;}
  static readonly Pad[] Pads={
   new Pad{c=ShelterPos,half=new Vector2(2.9f,2.2f),blend=2.5f},
   new Pad{c=TroughPos+new Vector2(-.3f,0),half=new Vector2(1.6f,.8f),blend=1.5f},
   new Pad{c=RackPos,half=new Vector2(1.2f,.8f),blend=1.5f}};

  [MenuItem(Menu+"Build Sheep Pasture (V2)")]
  static void BuildMenu(){Debug.Log(Build());}
  [MenuItem(Menu+"Remove Sheep Pasture")]
  static void RemoveMenu(){Debug.Log(Remove());}

  internal static float S(float a,float b,float v){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));}
  static Vector2 Center=>new Vector2((XMin+XMax)/2,(ZMin+ZMax)/2);
  internal static Vector3 At(Vector2 p,float y){return new Vector3(p.x,y,p.y);}

  // Distance outside the pasture rectangle (0 inside).
  static float Outside(Vector2 p)
  {
   float dx=Mathf.Max(0,Mathf.Max(XMin-p.x,p.x-XMax)),dz=Mathf.Max(0,Mathf.Max(ZMin-p.y,p.y-ZMax));
   return Mathf.Sqrt(dx*dx+dz*dz);
  }

  // Distance to the new fence lines (west, south, east); the north line is the fields' existing fence.
  static float FenceDistance(Vector2 p)
  {
   float zc=Mathf.Clamp(p.y,ZMin,ZMax),xc=Mathf.Clamp(p.x,XMin,XMax);
   return Mathf.Min(Mathf.Min(new Vector2(p.x-XMin,p.y-zc).magnitude,new Vector2(p.x-XMax,p.y-zc).magnitude),
    new Vector2(p.x-xc,p.y-ZMin).magnitude);
  }

  static Vector2 PathPoint(float t)
  {
   float u=1-t;
   return u*u*GateInside+2*u*t*PathBend+t*t*PathEnd;
  }

  static float PathDistance(Vector2 p)
  {
   float best=float.PositiveInfinity;
   Vector2 prev=PathPoint(0);
   for(int i=1;i<=40;i++)
   {
    Vector2 next=PathPoint(i/40f),ab=next-prev;
    float t=Mathf.Clamp01(Vector2.Dot(p-prev,ab)/Mathf.Max(ab.sqrMagnitude,1e-6f));
    best=Mathf.Min(best,(prev+ab*t-p).magnitude);
    prev=next;
   }
   return Mathf.Min(best,(p-new Vector2(111f,ZMax)).magnitude);
  }

  // The lowered pasture ground for an original height: the hill keeps a share of its height, fading in from
  // the fields' fence and back to the untouched hills over the falloff outside the rectangle.
  static float Lowered(float h0,Vector2 p)
  {
   if(p.y>ZMax)return h0;
   float keep=Mathf.Lerp(1f,Keep,S(0,5,ZMax-p.y));
   float target=Base+(h0-Base)*keep;
   return Mathf.Lerp(h0,target,1-S(0,Falloff,Outside(p)));
  }

  public static string Build()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before building the sheep pasture.");
   var farm=GameObject.Find("Farm Reference Map");if(farm==null)throw new InvalidOperationException("Farm root missing.");
   var terrain=farm.GetComponentInChildren<Terrain>();if(terrain==null)throw new InvalidOperationException("Farm terrain missing.");
   foreach(var model in new[]{"Sheep","Lamb","FoldShelter","FoldWaterTrough","FoldHayRack","HayBale","HayBaleStack","PastureGate"})
    if(AssetDatabase.LoadAssetAtPath<GameObject>(Models+model+".fbx")==null)throw new InvalidOperationException("Missing model "+model+".fbx; export ArtSource/SheepFold first.");
   var fences=farm.transform.Find(FenceRoot.TrimEnd('/'));
   var southTemplate=farm.transform.Find(FenceRoot+"Outer South 18");
   var westTemplate=farm.transform.Find(FenceRoot+"Outer West 10");
   var eastTemplate=farm.transform.Find(FenceRoot+"Outer East 10");
   if(fences==null||southTemplate==null||westTemplate==null||eastTemplate==null)throw new InvalidOperationException("Farm fence segments missing.");

   Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Build sheep pasture");int undoGroup=Undo.GetCurrentGroup();
   var report=new System.Text.StringBuilder();
   // An earlier build is taken down first so its terrain source carries over.
   var old=farm.transform.Find(RootName);
   TerrainData source=null;
   if(old!=null)
   {
    source=old.TryGetComponent(out MapSectionRecord oldRecord)?oldRecord.sourceTerrain:null;
    Remove();
   }
   if(source==null)source=terrain.terrainData;
   if(AssetDatabase.GetAssetPath(source)==TerrainPath)throw new InvalidOperationException("Terrain is already the pasture copy but no record of its source was found.");

   if(!AssetDatabase.IsValidFolder("Assets/Art/Terrain/SheepPastureV2"))AssetDatabase.CreateFolder("Assets/Art/Terrain","SheepPastureV2");
   var data=AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
   if(data==null)
   {
    if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source),TerrainPath))throw new InvalidOperationException("Could not copy the terrain.");
    data=AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
   }
   else CopyWhole(source,data);

   Reshape(terrain,source,data,report);
   Undo.RecordObject(terrain,"Build sheep pasture");terrain.terrainData=data;
   var tc=terrain.GetComponent<TerrainCollider>();
   if(tc!=null){Undo.RecordObject(tc,"Build sheep pasture");tc.terrainData=data;}

   var root=new GameObject(RootName);Undo.RegisterCreatedObjectUndo(root,"Build sheep pasture");
   root.transform.SetParent(farm.transform,false);
   root.transform.position=At(Center,Base);
   var record=root.AddComponent<MapSectionRecord>();
   record.sourceTerrain=source;

   var hidden=new List<GameObject>();
   HideCovered(farm.transform,fences,hidden,report);
   record.hidden=hidden.Distinct().ToArray();
   Reseat(farm.transform,terrain,source,record,report);

   float groundOffset=southTemplate.position.y-Height(terrain,source,southTemplate.position);
   BuildFences(root.transform,terrain,southTemplate,westTemplate,eastTemplate,groundOffset,report);
   BuildGate(root.transform,terrain,fences.Find(GateSegment),report);
   var obstacles=new List<Vector3>();
   BuildFold(root.transform,terrain,obstacles,report);
   BuildShadeTrees(root.transform,terrain,obstacles);
   BuildWildflowers(root.transform,terrain,report);
   var pen=root.AddComponent<SheepPen>();
   AddSceneryObstacles(farm.transform,obstacles);
   pen.Configure(terrain,new Vector2((XMax-XMin)/2-.5f,Depth/2-.5f),obstacles.ToArray());
   Func<Vector2,Vector3> ground=v=>At(v,Ground(terrain,v));
   pen.ConfigurePlaces(RestSpots.Select(r=>ground(ShelterPos+r)).ToArray(),ground(ShelterPos+new Vector2(-.75f,2.4f)),ground(ShadeSpot),2.6f,
    ground(TroughPos+new Vector2(.1f,-.95f)),ground(TroughPos),ground(RackPos+new Vector2(0,1.15f)),ground(RackPos));
   BuildFlock(root.transform,pen,report);
   var spawn=SpawnPoint(farm.transform,terrain);
   Undo.CollapseUndoOperations(undoGroup);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(farm.scene);
   report.Insert(0,"Sheep pasture built: x "+XMin+".."+XMax+", z "+ZMin.ToString("F2")+".."+ZMax+" ("+(XMax-XMin).ToString("F1")+" x "+Depth.ToString("F1")+" m). Spawn: "+spawn.name+".\n");
   return report.ToString();
  }

  public static string Remove()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before removing the sheep pasture.");
   var farm=GameObject.Find("Farm Reference Map");if(farm==null)throw new InvalidOperationException("Farm root missing.");
   var root=farm.transform.Find(RootName);if(root==null)return "No sheep pasture to remove.";
   var record=root.GetComponent<MapSectionRecord>();
   var terrain=farm.GetComponentInChildren<Terrain>();
   int restored=0,reseated=0;
   if(record!=null)
   {
    if(record.sourceTerrain!=null&&terrain!=null)
    {
     Undo.RecordObject(terrain,"Remove sheep pasture");terrain.terrainData=record.sourceTerrain;
     var tc=terrain.GetComponent<TerrainCollider>();
     if(tc!=null){Undo.RecordObject(tc,"Remove sheep pasture");tc.terrainData=record.sourceTerrain;}
    }
    foreach(var go in record.hidden)if(go!=null){Undo.RecordObject(go,"Remove sheep pasture");go.SetActive(true);restored++;}
    for(int i=0;i<record.reseated.Length;i++)
     if(record.reseated[i]!=null){Undo.RecordObject(record.reseated[i],"Remove sheep pasture");record.reseated[i].position-=Vector3.up*record.reseatOffsets[i];reseated++;}
   }
   var spawn=farm.transform.Find("Developer Spawn Points/"+SpawnName);
   if(spawn!=null)Undo.DestroyObjectImmediate(spawn.gameObject);
   Undo.DestroyObjectImmediate(root.gameObject);
   EditorSceneManager.MarkSceneDirty(farm.scene);
   return "Sheep pasture removed: terrain restored, "+restored+" objects re-enabled, "+reseated+" re-seated objects put back. Assets are kept for a rebuild.";
  }

  internal static void CopyWhole(TerrainData src,TerrainData dst)
  {
   int res=src.heightmapResolution;
   dst.SetHeights(0,0,src.GetHeights(0,0,res,res));
   dst.SetAlphamaps(0,0,src.GetAlphamaps(0,0,src.alphamapWidth,src.alphamapHeight));
   for(int layer=0;layer<src.detailPrototypes.Length;layer++)
    dst.SetDetailLayer(0,0,layer,src.GetDetailLayer(0,0,src.detailWidth,src.detailHeight,layer));
  }

  internal static float Height(Terrain terrain,TerrainData d,Vector3 p)
  {
   Vector3 tp=terrain.transform.position;
   return d.GetInterpolatedHeight((p.x-tp.x)/d.size.x,(p.z-tp.z)/d.size.z)+tp.y;
  }

  internal static float Ground(Terrain terrain,Vector2 p){return terrain.SampleHeight(At(p,0))+terrain.transform.position.y;}

  static void Reshape(Terrain terrain,TerrainData src,TerrainData dst,System.Text.StringBuilder report)
  {
   Vector3 tp=terrain.transform.position,size=src.size;
   int res=src.heightmapResolution;
   float minX=XMin-Falloff,maxX=XMax+Falloff,minZ=ZMin-Falloff,maxZ=ZMax+.5f;
   int x0=Mathf.Max(0,Mathf.FloorToInt((minX-tp.x)/size.x*(res-1))),x1=Mathf.Min(res-1,Mathf.CeilToInt((maxX-tp.x)/size.x*(res-1)));
   int z0=Mathf.Max(0,Mathf.FloorToInt((minZ-tp.z)/size.z*(res-1))),z1=Mathf.Min(res-1,Mathf.CeilToInt((maxZ-tp.z)/size.z*(res-1)));
   var h=src.GetHeights(x0,z0,x1-x0+1,z1-z0+1);
   int rows=h.GetLength(0),cols=h.GetLength(1);
   var lowered=new float[rows,cols];
   float drop=0;
   for(int iz=0;iz<rows;iz++)for(int ix=0;ix<cols;ix++)
   {
    var p=new Vector2(tp.x+(x0+ix)/(float)(res-1)*size.x,tp.z+(z0+iz)/(float)(res-1)*size.z);
    float h0=h[iz,ix]*size.y+tp.y;
    lowered[iz,ix]=Lowered(h0,p);
    drop=Mathf.Max(drop,h0-lowered[iz,ix]);
   }
   // Level pads under the shelter, trough and hay rack, blended into the lowered ground.
   foreach(var pad in Pads)
   {
    float sum=0;int n=0;
    for(int iz=0;iz<rows;iz++)for(int ix=0;ix<cols;ix++)
    {
     var p=new Vector2(tp.x+(x0+ix)/(float)(res-1)*size.x,tp.z+(z0+iz)/(float)(res-1)*size.z);
     if(Mathf.Abs(p.x-pad.c.x)<=pad.half.x&&Mathf.Abs(p.y-pad.c.y)<=pad.half.y){sum+=lowered[iz,ix];n++;}
    }
    if(n==0)continue;
    float level=sum/n;
    for(int iz=0;iz<rows;iz++)for(int ix=0;ix<cols;ix++)
    {
     var p=new Vector2(tp.x+(x0+ix)/(float)(res-1)*size.x,tp.z+(z0+iz)/(float)(res-1)*size.z);
     float dx=Mathf.Max(0,Mathf.Abs(p.x-pad.c.x)-pad.half.x),dz=Mathf.Max(0,Mathf.Abs(p.y-pad.c.y)-pad.half.y);
     float w=1-S(0,pad.blend,Mathf.Sqrt(dx*dx+dz*dz));
     lowered[iz,ix]=Mathf.Lerp(lowered[iz,ix],level,w);
    }
   }
   for(int iz=0;iz<rows;iz++)for(int ix=0;ix<cols;ix++)h[iz,ix]=(lowered[iz,ix]-tp.y)/size.y;
   dst.SetHeights(x0,z0,h);
   report.AppendLine("Ground: the hill inside the pasture lowered by up to "+drop.ToString("F2")+" m (keeps "+(Keep*100).ToString("F0")+"% of its height); level pads under the shelter, trough and hay rack.");

   // Paint: a trodden sheep track from the gate to the shelter, worn ground at the shelter, trough and rack.
   int ares=src.alphamapResolution,layers=src.alphamapLayers;
   int ax0=Mathf.FloorToInt((XMin-tp.x)/size.x*ares),ax1=Mathf.CeilToInt((XMax-tp.x)/size.x*ares);
   int az0=Mathf.FloorToInt((ZMin-tp.z)/size.z*ares),az1=Mathf.CeilToInt((ZMax+1f-tp.z)/size.z*ares);
   var a=src.GetAlphamaps(ax0,az0,ax1-ax0+1,az1-az0+1);
   var target=new float[layers];
   for(int iz=0;iz<a.GetLength(0);iz++)for(int ix=0;ix<a.GetLength(1);ix++)
   {
    float x=tp.x+(ax0+ix+.5f)/ares*size.x,z=tp.z+(az0+iz+.5f)/ares*size.z;
    var p=new Vector2(x,z);
    float n=Mathf.PerlinNoise(x*1.3f+7,z*1.3f+3),fine=Mathf.PerlinNoise(x*4.1f,z*4.1f);
    float path=(1-S(.4f,1.05f,PathDistance(p)+(n-.5f)*.4f))*.85f;
    float worn=0,mud=0;
    worn=Mathf.Max(worn,1-S(0,2.2f,Box(p,ShelterPos+new Vector2(0,.6f),new Vector2(2.6f,2.2f))+(n-.5f)*1.2f));
    worn=Mathf.Max(worn,1-S(0,1.4f,Box(p,RackPos,new Vector2(1.1f,.7f))+(n-.5f)*.9f));
    mud=1-S(0,1.3f,Box(p,TroughPos,new Vector2(1.1f,.45f))+(n-.5f)*.8f);
    float grazed=Outside(p)>0?0:Grazed(x,z)*.45f;
    float weight=Mathf.Max(Mathf.Max(path,grazed),Mathf.Max(worn*.8f,mud*.9f));
    if(weight<=.001f)continue;
    Array.Clear(target,0,layers);
    if(grazed>=Mathf.Max(Mathf.Max(path,worn*.8f),mud*.9f)){target[SunlitGreen]=.55f+.15f*fine;target[WornMeadow]=.45f-.15f*fine;}
    else if(mud>=Mathf.Max(path,worn)){target[Soil]=.55f+.2f*fine;target[TroddenEarth]=.3f;target[WornMeadow]=.15f-.15f*fine;}
    else if(path>=worn){target[TroddenEarth]=.5f+.2f*fine;target[WornMeadow]=.38f-.2f*fine;target[Pebbles]=.12f;}
    else{target[WornMeadow]=.5f+.2f*fine;target[TroddenEarth]=.32f-.2f*fine;target[Soil]=.18f;}
    float sum=0;
    for(int k=0;k<layers;k++){a[iz,ix,k]=Mathf.Lerp(a[iz,ix,k],target[k],weight);sum+=a[iz,ix,k];}
    for(int k=0;k<layers;k++)a[iz,ix,k]/=Mathf.Max(sum,1e-5f);
   }
   dst.SetAlphamaps(ax0,az0,a);

   // Grass: none under the new fence lines, on the track or under the fold's buildings.
   int dres=src.detailWidth,cleared=0;
   int dx0=Mathf.Max(0,Mathf.FloorToInt((XMin-1-tp.x)/size.x*dres)),dx1=Mathf.Min(dres-1,Mathf.CeilToInt((XMax+1-tp.x)/size.x*dres));
   int dz0=Mathf.Max(0,Mathf.FloorToInt((ZMin-1-tp.z)/size.z*src.detailHeight)),dz1=Mathf.Min(src.detailHeight-1,Mathf.CeilToInt((ZMax+.5f-tp.z)/size.z*src.detailHeight));
   for(int layer=0;layer<src.detailPrototypes.Length;layer++)
   {
    var m=src.GetDetailLayer(dx0,dz0,dx1-dx0+1,dz1-dz0+1,layer);
    for(int iz=0;iz<m.GetLength(0);iz++)for(int ix=0;ix<m.GetLength(1);ix++)
    {
     if(m[iz,ix]==0)continue;
     float x=tp.x+(dx0+ix+.5f)/dres*size.x,z=tp.z+(dz0+iz+.5f)/src.detailHeight*size.z;
     var p=new Vector2(x,z);
     bool clear=FenceDistance(p)<.45f||PathDistance(p)<.7f||
                Box(p,ShelterPos,new Vector2(2.5f,1.8f))<=0||Box(p,TroughPos,new Vector2(1.5f,.6f))<.3f||Box(p,RackPos,new Vector2(.95f,.55f))<.2f||
                (Box(p,ShelterPos+new Vector2(0,1f),new Vector2(3.2f,3f))<=0&&Mathf.PerlinNoise(x*2.3f,z*2.3f)<.55f)||
                (Outside(p)<=0&&Grazed(x,z)>.5f&&Mathf.PerlinNoise(x*3.1f+5,z*3.1f)<.5f);
     if(clear){cleared+=m[iz,ix];m[iz,ix]=0;}
    }
    dst.SetDetailLayer(dx0,dz0,layer,m);
   }
   report.AppendLine("Grass instances cleared from fence lines, the track and the fold: "+cleared+".");
   EditorUtility.SetDirty(dst);
  }

  // 0..1: where the flock has grazed the meadow short, in broad irregular patches.
  static float Grazed(float x,float z){return S(.52f,.68f,Mathf.PerlinNoise(x*.09f+31,z*.09f+17));}

  // Distance from p to an axis-aligned box (0 inside).
  static float Box(Vector2 p,Vector2 c,Vector2 half)
  {
   float dx=Mathf.Max(0,Mathf.Abs(p.x-c.x)-half.x),dz=Mathf.Max(0,Mathf.Abs(p.y-c.y)-half.y);
   return Mathf.Sqrt(dx*dx+dz*dz);
  }

  internal static bool IsTuft(Transform t){return t.name.StartsWith("Meadow")||t.name.StartsWith("Exterior Grass")||t.name.StartsWith("grass_");}

  // Scenery items: the nature root's children, with grouping objects (e.g. "Exterior Grass Patches") opened up.
  internal static IEnumerable<Transform> Scenery(Transform nature)
  {
   foreach(Transform child in nature)
   {
    if(child.childCount>0&&child.GetComponent<Renderer>()==null&&child.GetComponent<LODGroup>()==null)
     foreach(Transform item in child)yield return item;
    else yield return child;
   }
  }

  static void HideCovered(Transform farm,Transform fences,List<GameObject> hidden,System.Text.StringBuilder report)
  {
   var gate=fences.Find(GateSegment);
   if(gate!=null&&gate.gameObject.activeSelf){Undo.RecordObject(gate.gameObject,"Build sheep pasture");gate.gameObject.SetActive(false);hidden.Add(gate.gameObject);}
   var nature=farm.Find("05 Nature and Scenery");
   int count=0;
   if(nature!=null)
    foreach(Transform child in Scenery(nature))
    {
     if(!child.gameObject.activeSelf)continue;
     var p=new Vector2(child.position.x,child.position.z);
     if(Outside(p)>1.5f)continue;
     bool tuft=IsTuft(child);
     bool covered=FenceDistance(p)<(tuft?.8f:1.3f)||PathDistance(p)<(tuft?.9f:1.2f)||(p-new Vector2(111f,ZMax)).magnitude<2.2f||
                  Box(p,ShelterPos,new Vector2(2.7f,2.1f))<(tuft?1.2f:1.6f)||Box(p,TroughPos,new Vector2(1.6f,.5f))<1.1f||
                  Box(p,RackPos,new Vector2(1.1f,.75f))<1f||Box(p,StackPos,new Vector2(1f,.5f))<.8f||Box(p,BalePos,new Vector2(.5f,.3f))<.7f||
                  ShadeTrees.Any(t=>(p-t).magnitude<2f)||(p-WheelbarrowPos).magnitude<1.1f||(p-BucketPos).magnitude<.6f||
                  (p-RakePos).magnitude<.6f||StrawPatches.Any(t=>(p-t).magnitude<.9f);
     if(!covered)continue;
     Undo.RecordObject(child.gameObject,"Build sheep pasture");child.gameObject.SetActive(false);hidden.Add(child.gameObject);count++;
    }
   report.AppendLine("Hidden (not deleted): fence segment "+GateSegment+" (now the gate) and "+count+" scenery objects on the fence lines, the track or the fold.");
  }

  // Scenery standing on the lowered ground is moved down with it (recorded for removal).
  static void Reseat(Transform farm,Terrain terrain,TerrainData source,MapSectionRecord record,System.Text.StringBuilder report)
  {
   var moved=new List<Transform>();var offsets=new List<float>();
   var nature=farm.Find("05 Nature and Scenery");
   if(nature!=null)
    foreach(Transform child in Scenery(nature))
    {
     if(!child.gameObject.activeSelf)continue;
     var p=child.position;
     if(Outside(new Vector2(p.x,p.z))>Falloff+1)continue;
     float before=Height(terrain,source,p);
     float after=Height(terrain,terrain.terrainData,p);
     if(Mathf.Abs(after-before)<.03f)continue;
     Undo.RecordObject(child,"Build sheep pasture");child.position+=Vector3.up*(after-before);
     moved.Add(child);offsets.Add(after-before);
    }
   record.reseated=moved.ToArray();record.reseatOffsets=offsets.ToArray();
   report.AppendLine("Re-seated on the lowered ground: "+moved.Count+" scenery objects.");
  }

  // The farm's wooden fence carried on around the pasture: west and east lines south from the fields' corners,
  // and a south line matching the fields' south fence. Segments follow the ground's slope.
  static void BuildFences(Transform root,Terrain terrain,Transform south,Transform west,Transform east,float groundOffset,System.Text.StringBuilder report)
  {
   var parent=new GameObject("Pasture Fence").transform;parent.SetParent(root,false);
   int made=0;
   float sideLength=Depth/SideSegments;
   for(int i=0;i<SideSegments;i++)
   {
    float z=ZMax-sideLength*(i+.5f);
    Segment(parent,west,new Vector2(XMin,z),sideLength,terrain,groundOffset,"West "+(i+1).ToString("00"));
    Segment(parent,east,new Vector2(XMax,z),sideLength,terrain,groundOffset,"East "+(i+1).ToString("00"));
    made+=2;
   }
   for(int i=0;i<25;i++)
   {
    float x=XMin+1.3f+i*2.8f;
    Segment(parent,south,new Vector2(x,ZMin),2.8f,terrain,groundOffset,"South "+(i+1).ToString("00"));
    made++;
   }
   report.AppendLine("Fence: "+made+" wooden fence segments around the pasture (same model as the farm fence).");
  }

  static void Segment(Transform parent,Transform template,Vector2 p,float length,Terrain terrain,float groundOffset,string name)
  {
   var source=PrefabUtility.GetCorrespondingObjectFromSource(template.gameObject);
   var go=source!=null?(GameObject)PrefabUtility.InstantiatePrefab(source,parent):UnityEngine.Object.Instantiate(template.gameObject,parent);
   go.name=name;
   if(go.GetComponent<MeshCollider>()==null&&template.GetComponent<MeshCollider>()!=null)go.AddComponent<MeshCollider>();
   var blocker=template.Find("Camera Blocker");
   if(blocker!=null&&go.transform.Find("Camera Blocker")==null)
   {
    var copy=UnityEngine.Object.Instantiate(blocker.gameObject,go.transform,false);copy.name="Camera Blocker";
   }
   Quaternion yaw=Quaternion.Euler(0,template.eulerAngles.y,0);
   Vector3 along=yaw*Vector3.right;
   Vector2 a=p-new Vector2(along.x,along.z)*length/2,b=p+new Vector2(along.x,along.z)*length/2;
   float ha=Ground(terrain,a),hb=Ground(terrain,b),hm=Ground(terrain,p);
   float tilt=Mathf.Atan2(hb-ha,length)*Mathf.Rad2Deg;
   go.transform.SetPositionAndRotation(At(p,Mathf.Min(hm,(ha+hb)/2)+groundOffset),yaw*Quaternion.Euler(0,0,tilt));
   var s=template.lossyScale;
   go.transform.localScale=new Vector3(length/FenceMeshWidth,s.y,s.z);
  }

  internal static GameObject Place(string model,Transform parent,Vector3 position,float yaw,string name)
  {
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Models+model+".fbx");
   var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
   go.name=name;
   go.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
   return go;
  }

  // A solid box, optionally doubled on the CameraBlocker layer so the orbit camera does not pass through it.
  internal static void AddBox(GameObject go,Vector3 center,Vector3 size,bool cameraBlocker,Vector3 euler=default)
  {
   GameObject holder=go;
   if(euler!=Vector3.zero)
   {
    holder=new GameObject("Collider");holder.transform.SetParent(go.transform,false);
    holder.transform.localPosition=center;holder.transform.localRotation=Quaternion.Euler(euler);
    center=Vector3.zero;
   }
   var box=holder.AddComponent<BoxCollider>();box.center=center;box.size=size;
   if(!cameraBlocker)return;
   var blocker=new GameObject("Camera Blocker");
   blocker.layer=LayerMask.NameToLayer("CameraBlocker");
   blocker.transform.SetParent(holder.transform,false);
   var b=blocker.AddComponent<BoxCollider>();b.center=center;b.size=size;
  }

  // Only the orbit camera collides with this (the roofs).
  static void AddCameraBlocker(GameObject go,Vector3 center,Vector3 size,Vector3 euler)
  {
   var blocker=new GameObject("Camera Blocker");
   blocker.layer=LayerMask.NameToLayer("CameraBlocker");
   blocker.transform.SetParent(go.transform,false);
   blocker.transform.localPosition=center;blocker.transform.localRotation=Quaternion.Euler(euler);
   blocker.AddComponent<BoxCollider>().size=size;
  }

  static Bounds LocalBounds(GameObject go)
  {
   var inv=go.transform.worldToLocalMatrix;bool any=false;var b=new Bounds();
   foreach(var mf in go.GetComponentsInChildren<MeshFilter>())
   {
    var m=inv*mf.transform.localToWorldMatrix;var mb=mf.sharedMesh.bounds;
    for(int i=0;i<8;i++)
    {
     var c=mb.center+Vector3.Scale(mb.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
     var p=m.MultiplyPoint3x4(c);
     if(!any){b=new Bounds(p,Vector3.zero);any=true;}else b.Encapsulate(p);
    }
   }
   return b;
  }

  // The gate replaces one bay of the fields' south fence; its leaf stands open into the pasture.
  static void BuildGate(Transform root,Terrain terrain,Transform segment,System.Text.StringBuilder report)
  {
   Vector3 p=segment!=null?segment.position:new Vector3(111f,-11.75f,ZMax);
   var gate=Place("PastureGate",root,At(new Vector2(p.x,p.z),Ground(terrain,new Vector2(p.x,p.z))),0,"Gate - Crop Fields to Sheep Pasture");
   foreach(float x in new[]{-1.4f,1.4f})AddBox(gate,new Vector3(x,.8f,0),new Vector3(.2f,1.6f,.2f),true);
   var leaf=gate.transform.Find("PastureGate_Leaf");
   if(leaf!=null)
   {
    leaf.localRotation=Quaternion.Euler(0,-100f,0);
    var lb=leaf.GetComponent<MeshFilter>().sharedMesh.bounds;
    var box=leaf.gameObject.AddComponent<BoxCollider>();box.center=lb.center;box.size=new Vector3(lb.size.x,lb.size.y,.08f);
   }
   report.AppendLine("Gate: in the fields' south fence (bay "+GateSegment+"), standing open into the pasture.");
  }

  static void BuildFold(Transform root,Terrain terrain,List<Vector3> obstacles,System.Text.StringBuilder report)
  {
   var fold=new GameObject("Fold").transform;fold.SetParent(root,false);
   Func<Vector2,Vector3> at=v=>At(v,Ground(terrain,v));

   var shelter=Place("FoldShelter",fold,at(ShelterPos),0,"Shelter");
   ShelterColliders(shelter);
   ShelterObstacles(obstacles);

   var trough=Place("FoldWaterTrough",fold,at(TroughPos),0,"Water Trough");
   TroughSetup(trough);
   foreach(float x in new[]{-.6f,0f,.6f})obstacles.Add(new Vector3(TroughPos.x+x,TroughPos.y,.3f));
   obstacles.Add(new Vector3(TroughPos.x-1.15f,TroughPos.y,.3f));

   var rack=Place("FoldHayRack",fold,at(RackPos),0,"Hay Rack");
   foreach(float x in new[]{-.5f,.5f})AddBox(rack,new Vector3(x*1.7f,.75f,0),new Vector3(.9f,1.5f,1f),false);
   AddCameraBlocker(rack,new Vector3(0,1.8f,0),new Vector3(2.2f,.35f,1.5f),Vector3.zero);
   obstacles.Add(new Vector3(RackPos.x-.5f,RackPos.y,.55f));
   obstacles.Add(new Vector3(RackPos.x+.5f,RackPos.y,.55f));

   var stack=Place("HayBaleStack",fold,at(StackPos),-12,"Hay Bale Stack");
   var sb=LocalBounds(stack);AddBox(stack,sb.center,sb.size,false);
   obstacles.Add(new Vector3(StackPos.x,StackPos.y,1f));
   var bale=Place("HayBale",fold,at(BalePos),24,"Hay Bale");
   var bb=LocalBounds(bale);AddBox(bale,bb.center,bb.size,false);
   obstacles.Add(new Vector3(BalePos.x,BalePos.y,.55f));
   BuildYard(fold,terrain,obstacles);
   report.AppendLine("Fold: timber shelter, trough with hand pump and live water, covered hay rack, bales, wheelbarrow, rake, bucket and straw (with colliders).");
  }

  // Shelter walls as a ring of small circles so sheep can walk in through the open front and sleep inside.
  static void ShelterObstacles(List<Vector3> obstacles)
  {
   Action<float,float,float> add=(x,z,r)=>obstacles.Add(new Vector3(ShelterPos.x+x,ShelterPos.y+z,r));
   for(float x=-2.3f;x<=2.31f;x+=.5f)add(x,-1.62f,.3f);
   for(float z=-1.6f;z<=1.51f;z+=.5f){add(-2.3f,z,.3f);add(2.3f,z,.3f);}
   for(float x=.75f;x<=2.21f;x+=.45f)add(x,1.42f,.25f);
   add(-1.2f,-.97f,.6f);
   foreach(float x in new[]{0f,.7f,1.4f})add(x,-1.25f,.3f);
   add(1.6f,1f,.25f);
  }

  // Wheelbarrow and bucket by the trough, a rake leaning on the shelter, trampled straw in front of it.
  static void BuildYard(Transform fold,Terrain terrain,List<Vector3> obstacles)
  {
   Func<Vector2,Vector3> at=v=>At(v,Ground(terrain,v));
   var barrow=Place("Wheelbarrow",fold,at(WheelbarrowPos),205,"Wheelbarrow");
   var wb=LocalBounds(barrow);AddBox(barrow,wb.center,wb.size,false);
   obstacles.Add(new Vector3(WheelbarrowPos.x,WheelbarrowPos.y,.65f));
   var bucket=Place("FarmBucket",fold,at(BucketPos),40,"Bucket");
   var bb=LocalBounds(bucket);AddBox(bucket,bb.center,bb.size,false);
   obstacles.Add(new Vector3(BucketPos.x,BucketPos.y,.25f));
   // Leaning against the west wall: the handle tips over towards the wall (+X).
   var rake=Place("Rake",fold,at(RakePos),0,"Rake");
   rake.transform.rotation=Quaternion.AngleAxis(-14f,Vector3.forward)*Quaternion.Euler(0,90,0);
   for(int i=0;i<StrawPatches.Length;i++)Place("StrawPatch",fold,at(StrawPatches[i])+Vector3.up*.01f,i*73f,"Straw "+(i+1));
  }

  static void BuildShadeTrees(Transform root,Terrain terrain,List<Vector3> obstacles)
  {
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BOKI/LowPolyNature/Prefabs/models/tree_normal.prefab");
   if(prefab==null)throw new InvalidOperationException("tree_normal prefab missing.");
   var parent=new GameObject("Shade Trees").transform;parent.SetParent(root,false);
   for(int i=0;i<ShadeTrees.Length;i++)
   {
    var tree=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
    tree.name="Shade Tree "+(i+1);
    tree.transform.SetPositionAndRotation(At(ShadeTrees[i],Ground(terrain,ShadeTrees[i])),Quaternion.Euler(0,i*137f+20f,0));
    tree.transform.localScale=Vector3.one*(i==0?.68f:.6f);
    obstacles.Add(new Vector3(ShadeTrees[i].x,ShadeTrees[i].y,.6f));
   }
  }

  // Clusters of daisies, poppies, buttercups, bellflowers and clover across the meadow, clear of paths and buildings.
  static void BuildWildflowers(Transform root,Terrain terrain,System.Text.StringBuilder report)
  {
   string[] kinds={"Daisy","Poppy","Buttercup","Bellflower","Clover"};
   var prefabs=kinds.Select(k=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/MeadowFlowers/Flower_"+k+".fbx")).ToArray();
   if(prefabs.Any(f=>f==null))throw new InvalidOperationException("Meadow flower models missing.");
   var parent=new GameObject("Wildflowers").transform;parent.SetParent(root,false);
   var rng=new System.Random(4242);
   Func<float> r01=()=>(float)rng.NextDouble();
   Func<Vector2,float,bool> blocked=(p,m)=>FenceDistance(p)<m+.5f||PathDistance(p)<m+.4f||
    Box(p,ShelterPos,new Vector2(2.7f,2.1f))<m+1f||Box(p,TroughPos,new Vector2(1.6f,.5f))<m+.8f||Box(p,RackPos,new Vector2(1.1f,.75f))<m+.8f||
    ShadeTrees.Any(t=>(p-t).magnitude<m+1.2f)||(p-WheelbarrowPos).magnitude<m+.8f||Box(p,StackPos,new Vector2(1f,.5f))<m+.5f;
   int clusters=0,made=0;
   for(int attempt=0;attempt<300&&clusters<20;attempt++)
   {
    var c=new Vector2(Mathf.Lerp(XMin+3,XMax-3,r01()),Mathf.Lerp(ZMin+3,ZMax-3,r01()));
    if(blocked(c,1.6f))continue;
    clusters++;
    int a=rng.Next(kinds.Length),b=rng.Next(kinds.Length),n=7+rng.Next(9);
    float radius=.6f+r01()*1f;
    for(int i=0;i<n;i++)
    {
     float ang=r01()*Mathf.PI*2,rr=radius*Mathf.Sqrt(r01());
     var p=c+new Vector2(Mathf.Cos(ang),Mathf.Sin(ang))*rr;
     if(blocked(p,.2f))continue;
     int kind=r01()<.65f?a:b;
     var flower=(GameObject)PrefabUtility.InstantiatePrefab(prefabs[kind],parent);
     flower.name=kinds[kind]+" "+made;
     flower.transform.SetPositionAndRotation(At(p,Ground(terrain,p)),Quaternion.Euler(0,r01()*360f,0)*prefabs[kind].transform.rotation);
     flower.transform.localScale=prefabs[kind].transform.localScale*(kinds[kind]=="Clover"?.7f+r01()*.25f:.62f+r01()*.22f);
     made++;
    }
   }
   report.AppendLine("Meadow: "+made+" wildflowers in "+clusters+" clusters, two shade trees, grazed patches.");
  }

  // Model space (opening towards +Z), from props.py: walls on a stone footing, open front with a half wall.
  static void ShelterColliders(GameObject shelter)
  {
   AddBox(shelter,new Vector3(0,1.1f,-1.62f),new Vector3(4.75f,2.2f,.34f),true);
   AddBox(shelter,new Vector3(-2.3f,1.1f,0),new Vector3(.34f,2.2f,3.3f),true);
   AddBox(shelter,new Vector3(2.3f,1.1f,0),new Vector3(.34f,2.2f,3.3f),true);
   AddBox(shelter,new Vector3(1.47f,.62f,1.42f),new Vector3(1.5f,1.25f,.14f),true);
   foreach(float x in new[]{-2.2f,.75f,2.2f})AddBox(shelter,new Vector3(x,1.05f,1.5f),new Vector3(.2f,2.1f,.2f),false);
   // Bales in the back corner, the hay manger on the back wall, the bucket by the front.
   AddBox(shelter,new Vector3(-1.2f,.38f,-.97f),new Vector3(1f,.76f,.95f),false);
   AddBox(shelter,new Vector3(.7f,.9f,-1.2f),new Vector3(2.15f,.8f,.5f),false);
   AddBox(shelter,new Vector3(1.6f,.15f,1f),new Vector3(.34f,.3f,.34f),false);
   // Roof slopes stop the orbit camera.
   AddCameraBlocker(shelter,new Vector3(0,2.47f,.96f),new Vector3(5.4f,.14f,2.3f),new Vector3(32.4f,0,0));
   AddCameraBlocker(shelter,new Vector3(0,2.47f,-.96f),new Vector3(5.4f,.14f,2.3f),new Vector3(-32.4f,0,0));
  }

  // Walls and rim the chick can hop onto, a wading floor inside, the pump, and the live water surface.
  static void TroughSetup(GameObject trough)
  {
   foreach(float s in new[]{-1f,1f})
   {
    AddBox(trough,new Vector3(0,TroughRim/2,s*.2225f),new Vector3(1.86f,TroughRim,.105f),false);
    AddBox(trough,new Vector3(s*.885f,TroughRim/2,0),new Vector3(.11f,TroughRim,.55f),false);
   }
   AddBox(trough,new Vector3(0,TroughWading/2,0),new Vector3(1.68f,TroughWading,.39f),false);
   AddBox(trough,new Vector3(-1.15f,.2f,0),new Vector3(.4f,.4f,.4f),true);
   AddBox(trough,new Vector3(-1.15f,.65f,0),new Vector3(.17f,.5f,.17f),false);
   AddBox(trough,new Vector3(.35f,.06f,.45f),new Vector3(.44f,.12f,.3f),false);

   var water=new GameObject("Water");
   water.transform.SetParent(trough.transform,false);
   water.transform.localPosition=new Vector3(0,TroughSurface,0);
   var surface=new GameObject("Surface");
   surface.transform.SetParent(water.transform,false);
   surface.transform.localRotation=Quaternion.Euler(90,0,0);
   surface.transform.localScale=new Vector3(TroughWaterHalf.x*2,TroughWaterHalf.y*2,1);
   surface.AddComponent<MeshFilter>().sharedMesh=Resources.GetBuiltinResource<Mesh>("Quad.fbx");
   var r=surface.AddComponent<MeshRenderer>();
   r.sharedMaterial=TroughWaterMaterial();
   r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
   r.receiveShadows=true;
   var spout=new GameObject("Pump Spout").transform;
   spout.SetParent(trough.transform,false);
   spout.localPosition=TroughSpout;
   var body=water.AddComponent<FarmWaterTrough>();
   body.Configure(TroughWaterHalf,TroughSurface-TroughWading,TroughRim-TroughSurface,spout,AssetDatabase.LoadAssetAtPath<Material>(DropletPath));
  }

  // The pond's water, tuned for a small deep box: the wooden floor shows through, smaller wind ripples.
  static Material TroughWaterMaterial()
  {
   var mat=AssetDatabase.LoadAssetAtPath<Material>(TroughWaterPath);
   if(mat!=null)return mat;
   var pond=AssetDatabase.LoadAssetAtPath<Material>(PondWaterPath);
   if(pond==null)throw new InvalidOperationException("Pond water material missing: "+PondWaterPath);
   mat=new Material(pond){name="M_TroughWater"};
   mat.SetFloat("_ColorDepth",.16f);
   mat.SetFloat("_Opacity",.6f);
   mat.SetFloat("_WaveScale",5.5f);
   mat.SetFloat("_WaveStrength",.22f);
   mat.SetFloat("_FoamWidth",.004f);
   AssetDatabase.CreateAsset(mat,TroughWaterPath);
   return mat;
  }

  // Trees, rocks and shrubs left standing inside the pasture are obstacles for the sheep too.
  static void AddSceneryObstacles(Transform farm,List<Vector3> obstacles)
  {
   var nature=farm.Find("05 Nature and Scenery");
   if(nature==null)return;
   foreach(Transform child in Scenery(nature))
   {
    if(!child.gameObject.activeSelf||IsTuft(child))continue;
    var p=new Vector2(child.position.x,child.position.z);
    if(Outside(p)>0)continue;
    float radius=.6f;
    if(!child.name.StartsWith("Scenery Tree"))
    {
     var rend=child.GetComponentInChildren<Renderer>();
     if(rend!=null)radius=Mathf.Clamp(Mathf.Max(rend.bounds.extents.x,rend.bounds.extents.z)*.8f,.3f,1.5f);
    }
    obstacles.Add(new Vector3(p.x,p.y,radius));
   }
  }

  static AnimatorController Controller(string name,string model)
  {
   if(!AssetDatabase.IsValidFolder(AnimFolder))AssetDatabase.CreateFolder(SheepFoldImporter.Folder,"Animation");
   string path=AnimFolder+"/"+name+".controller";
   AssetDatabase.DeleteAsset(path);
   var controller=AnimatorController.CreateAnimatorControllerAtPath(path);
   var machine=controller.layers[0].stateMachine;
   var clips=AssetDatabase.LoadAllAssetsAtPath(Models+model+".fbx").OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview")).ToDictionary(c=>c.name);
   foreach(var state in new[]{"Idle","Walk","Graze","Run","Lie"})
   {
    var s=machine.AddState(state);
    s.motion=clips[state];
    if(state=="Idle")machine.defaultState=s;
   }
   return controller;
  }

  static void BuildFlock(Transform root,SheepPen pen,System.Text.StringBuilder report)
  {
   var sheepController=Controller("SheepFlock","Sheep");
   var lambController=Controller("LambFlock","Lamb");
   var flock=new GameObject("Flock").transform;flock.SetParent(root,false);
   UnityEngine.Random.State saved=UnityEngine.Random.state;
   UnityEngine.Random.InitState(5531);
   var adults=new List<FarmSheep>();
   Vector3 grazing=At((ShelterPos+GateInside)/2+new Vector2(-1.5f,-3f),0);
   for(int i=0;i<7;i++)
   {
    bool lamb=i>=5;
    float body=lamb?.32f:.5f;
    Vector3 spot=lamb?pen.RandomPoint(body,adults[i==5?0:2].transform.position,1.3f):pen.RandomPoint(body,grazing,5f);
    spot.y=pen.GroundHeight(spot);
    var go=Place(lamb?"Lamb":"Sheep",flock,spot,UnityEngine.Random.Range(0f,360f),(lamb?"Lamb ":"Sheep ")+(lamb?i-4:i+1));
    var animator=go.GetComponent<Animator>();
    animator.runtimeAnimatorController=lamb?lambController:sheepController;
    animator.applyRootMotion=false;
    animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
    var rb=go.AddComponent<Rigidbody>();rb.isKinematic=true;rb.interpolation=RigidbodyInterpolation.Interpolate;
    var capsule=go.AddComponent<CapsuleCollider>();capsule.direction=2;
    capsule.center=lamb?new Vector3(0,.36f,.02f):new Vector3(0,.52f,.02f);
    capsule.radius=lamb?.2f:.32f;capsule.height=lamb?.78f:1.25f;
    AddContactShadow(go,lamb?new Vector2(.55f,.85f):new Vector2(.95f,1.45f));
    var sheep=go.AddComponent<FarmSheep>();
    if(lamb)sheep.Configure(pen,adults[i==5?0:2],animator,.45f,.5f,body,0);
    else{sheep.Configure(pen,null,animator,.55f,.65f,body,i);adults.Add(sheep);}
   }
   UnityEngine.Random.state=saved;
   report.AppendLine("Flock: 5 sheep and 2 lambs (lambs follow Sheep 1 and Sheep 3): graze by day, shade at noon, shelter at dusk.");
  }

  // A soft dark blob under the animal so it reads as standing on the ground even when the
  // low evening sun throws its real shadow far to the side.
  static void AddContactShadow(GameObject animal,Vector2 size)
  {
   const string path=SheepFoldImporter.Folder+"/M_ContactShadow.mat";
   var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(mat==null)
   {
    var shader=Shader.Find("Chick/ContactShadow");
    if(shader==null)throw new InvalidOperationException("Chick/ContactShadow shader not ready.");
    mat=new Material(shader){name="M_ContactShadow"};
    mat.SetFloat("_Strength",.5f);mat.SetFloat("_Softness",.65f);
    AssetDatabase.CreateAsset(mat,path);
   }
   var blob=new GameObject("Contact Shadow");
   blob.transform.SetParent(animal.transform,false);
   blob.transform.localPosition=new Vector3(0,.015f,.03f);
   blob.transform.localRotation=Quaternion.Euler(90,0,0);
   blob.transform.localScale=new Vector3(size.x,size.y,1);
   blob.AddComponent<MeshFilter>().sharedMesh=Resources.GetBuiltinResource<Mesh>("Quad.fbx");
   var r=blob.AddComponent<MeshRenderer>();
   r.sharedMaterial=mat;
   r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
   r.receiveShadows=false;
   r.lightProbeUsage=UnityEngine.Rendering.LightProbeUsage.Off;
   r.reflectionProbeUsage=UnityEngine.Rendering.ReflectionProbeUsage.Off;
  }

  static Transform SpawnPoint(Transform farm,Terrain terrain)
  {
   var points=farm.Find("Developer Spawn Points");
   var old=points!=null?points.Find(SpawnName):null;
   if(old!=null)Undo.DestroyObjectImmediate(old.gameObject);
   var spawn=new GameObject(SpawnName).transform;Undo.RegisterCreatedObjectUndo(spawn.gameObject,"Build sheep pasture");
   spawn.SetParent(points!=null?points:farm,false);
   Vector2 p=PathPoint(.18f);
   spawn.position=At(p,Ground(terrain,p)+.05f);
   Vector2 look=ShelterPos-p;
   spawn.rotation=Quaternion.Euler(0,Mathf.Atan2(look.x,look.y)*Mathf.Rad2Deg,0);
   var panel=UnityEngine.Object.FindFirstObjectByType<DeveloperPanelController>(FindObjectsInactive.Include);
   if(panel!=null)
   {
    var so=new SerializedObject(panel);var prop=so.FindProperty("foldSpawn");
    if(prop!=null){prop.objectReferenceValue=spawn;so.ApplyModifiedProperties();}
   }
   return spawn;
  }
 }
}
