using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FarmReferenceTools
{
 // Builds (and removes) the shallow pond in the fenced field south-west of the barn.
 // Reversible: the terrain is carved on a copy (FarmTerrain_Pond_v1) while FarmTerrain_GroundWear_v1
 // stays untouched; covered meadow tufts are only disabled; the player only gains two components.
 public static class FarmPondV1
 {
  const string Menu="Tools/Chick/Pond/";
  public const string Folder="Assets/Art/FarmReference/Pond";
  public const string OriginalTerrain="Assets/Art/Terrain/GroundWearV1/FarmTerrain_GroundWear_v1.asset";
  public const string PondTerrain="Assets/Art/Terrain/PondV1/FarmTerrain_Pond_v1.asset";
  public const string RootName="Farm Pond";
  public const float WaterLevel=-11.90f;
  const float MaxDepth=.075f,ShoreMargin=.3f,Freeboard=.035f;
  // Shoreline traced from the area marked in the field (world XZ), kept clear of both fences.
  static readonly Vector2[] Control={
   new Vector2(90.9f,8.2f),new Vector2(93.5f,9.0f),new Vector2(95.2f,7.6f),new Vector2(96.8f,5.9f),
   new Vector2(97.5f,4.7f),new Vector2(97.4f,3.1f),new Vector2(96.5f,2.0f),new Vector2(95.0f,1.85f),
   new Vector2(93.1f,2.2f),new Vector2(91.4f,2.15f),new Vector2(89.5f,1.85f),new Vector2(87.9f,1.85f),
   new Vector2(86.5f,2.4f),new Vector2(86.0f,3.3f),new Vector2(86.8f,4.5f),new Vector2(88.8f,5.2f),
   new Vector2(90.0f,5.9f),new Vector2(90.5f,6.9f)};
  // Layer indices of FarmTerrain_GroundWear_v1.
  const int TroddenEarth=4,Soil=5,Pebbles=6,WornMeadow=7;

  [MenuItem(Menu+"Build Pond (V1)")]
  static void BuildMenu(){Debug.Log(Build());}
  [MenuItem(Menu+"Remove Pond (restore field)")]
  static void RemoveMenu(){Debug.Log(Remove());}

  static float S(float a,float b,float v){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));}

  public static Vector2[] Shoreline()
  {
   var pts=new List<Vector2>();int n=Control.Length;
   for(int i=0;i<n;i++)
   {
    Vector2 p0=Control[(i-1+n)%n],p1=Control[i],p2=Control[(i+1)%n],p3=Control[(i+2)%n];
    for(int k=0;k<10;k++)
    {
     float t=k/10f,t2=t*t,t3=t2*t;
     pts.Add(.5f*(2*p1+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t2+(-p0+3*p1-3*p2+p3)*t3));
    }
   }
   return pts.ToArray();
  }

  // Negative inside the shoreline.
  static float Signed(Vector2[] poly,float x,float z)
  {
   bool inside=false;float best=float.PositiveInfinity;var p=new Vector2(x,z);
   for(int i=0,j=poly.Length-1;i<poly.Length;j=i++)
   {
    Vector2 a=poly[i],b=poly[j];
    if((a.y>z)!=(b.y>z) && x<(b.x-a.x)*(z-a.y)/(b.y-a.y)+a.x)inside=!inside;
    Vector2 ab=b-a;float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/Mathf.Max(ab.sqrMagnitude,1e-6f));
    best=Mathf.Min(best,(a+ab*t-p).sqrMagnitude);
   }
   float d=Mathf.Sqrt(best);return inside?-d:d;
  }

  // New ground height: a level basin below the water line, a bank that meets the water exactly at the
  // shore, carved down where the field is higher and built up into a low berm where it is lower.
  static float Ground(float h0,float sd,float x,float z)
  {
   float target;
   if(sd<0)
   {
    float inside=-sd;
    float bowl=MaxDepth*S(0,1.5f,inside);
    float wobble=(Mathf.PerlinNoise(x*.9f+11,z*.9f+3)-.5f)*.018f*S(.3f,1.2f,inside);
    target=WaterLevel-.006f*S(0,.2f,inside)-bowl+wobble;
   }
   else
   {
    float bank=WaterLevel+Freeboard*S(0,.45f,sd);
    if(h0>=bank){float w=Mathf.Clamp((h0-WaterLevel)*4.5f,.6f,1.5f);target=Mathf.Lerp(bank,h0,S(0,w,sd));}
    else{float w=Mathf.Clamp((bank-h0)*5f,.6f,1.6f);target=Mathf.Lerp(bank,h0,S(.55f,.55f+w,sd));}
   }
   // Leave the ground under both fence lines where it is.
   float keep=Mathf.Max(S(98.45f,98.95f,x),1-S(.3f,.8f,z));
   return Mathf.Lerp(target,h0,keep);
  }

  static Material EnsureMaterial(string name,string shaderName,Action<Material> setup)
  {
   string path=Folder+"/"+name+".mat";
   var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(mat!=null)return mat;
   var shader=Shader.Find(shaderName);
   if(shader==null||!shader.isSupported)throw new InvalidOperationException(shaderName+" not ready.");
   mat=new Material(shader){name=name};setup?.Invoke(mat);
   AssetDatabase.CreateAsset(mat,path);
   return mat;
  }

  static Mesh SaveMesh(Mesh mesh)
  {
   string path=Folder+"/"+mesh.name+".asset";
   var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(existing==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
   existing.Clear();existing.indexFormat=mesh.indexFormat;existing.vertices=mesh.vertices;existing.normals=mesh.normals;
   existing.colors=mesh.colors;existing.uv=mesh.uv;existing.triangles=mesh.triangles;existing.RecalculateBounds();
   UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;
  }

  public static string Build()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before building the pond.");
   var root=GameObject.Find("Farm Reference Map");if(root==null)throw new InvalidOperationException("Farm root missing.");
   var terrain=root.GetComponentInChildren<Terrain>();if(terrain==null)throw new InvalidOperationException("Farm terrain missing.");
   var player=UnityEngine.Object.FindFirstObjectByType<ChickPlayerController>(FindObjectsInactive.Include);
   if(player==null)throw new InvalidOperationException("Player missing.");
   var original=AssetDatabase.LoadAssetAtPath<TerrainData>(OriginalTerrain);
   string current=AssetDatabase.GetAssetPath(terrain.terrainData);
   if(original==null||(current!=OriginalTerrain&&current!=PondTerrain))throw new InvalidOperationException("Unexpected terrain ("+current+"); inspect before building.");
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Art/FarmReference","Pond");
   if(!AssetDatabase.IsValidFolder("Assets/Art/Terrain/PondV1"))AssetDatabase.CreateFolder("Assets/Art/Terrain","PondV1");
   var data=AssetDatabase.LoadAssetAtPath<TerrainData>(PondTerrain);
   if(data==null)
   {
    if(!AssetDatabase.CopyAsset(OriginalTerrain,PondTerrain))throw new InvalidOperationException("Could not copy the field terrain.");
    data=AssetDatabase.LoadAssetAtPath<TerrainData>(PondTerrain);
   }
   var shore=Shoreline();
   Vector2 min=shore[0],max=shore[0];foreach(var p in shore){min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
   var report=new System.Text.StringBuilder();
   Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Build farm pond");int undoGroup=Undo.GetCurrentGroup();
   CarveTerrain(terrain,original,data,shore,min-Vector2.one*2.4f,max+Vector2.one*2.4f,report);

   Undo.RecordObject(terrain,"Build farm pond");terrain.terrainData=data;
   var collider=terrain.GetComponent<TerrainCollider>();
   if(collider!=null){Undo.RecordObject(collider,"Build farm pond");collider.terrainData=data;}

   var old=root.transform.Find(RootName);
   var previouslyHidden=old!=null&&old.TryGetComponent(out FarmPond oldPond)?oldPond.HiddenForPond:new GameObject[0];
   if(old!=null)Undo.DestroyObjectImmediate(old.gameObject);
   var pondGo=new GameObject(RootName);Undo.RegisterCreatedObjectUndo(pondGo,"Build farm pond");
   pondGo.transform.SetParent(root.transform,false);
   Vector2 center=(min+max)*.5f;
   pondGo.transform.position=new Vector3(center.x,WaterLevel,center.y);

   var water=EnsureMaterial("PondWater","Chick/PondWater",null);
   var droplet=EnsureMaterial("PondDroplet","Chick/PondDroplet",null);
   var reedMat=EnsureMaterial("PondReeds","Chick/DistantMountain",m=>{m.SetColor("_BaseColor",Color.white);m.SetFloat("_HazeStrength",0);m.SetFloat("_FoliageBend",1);m.EnableKeyword("_FOLIAGE_BEND");});

   var surface=new GameObject("Water Surface");surface.transform.SetParent(pondGo.transform,false);
   surface.AddComponent<MeshFilter>().sharedMesh=SaveMesh(WaterMesh(shore,min,max,pondGo.transform.position));
   var surfaceRenderer=surface.AddComponent<MeshRenderer>();surfaceRenderer.sharedMaterial=water;
   surfaceRenderer.shadowCastingMode=ShadowCastingMode.Off;surfaceRenderer.receiveShadows=true;
   surfaceRenderer.lightProbeUsage=LightProbeUsage.Off;surfaceRenderer.reflectionProbeUsage=ReflectionProbeUsage.BlendProbes;

   var hidden=HideCoveredMeadow(root.transform,terrain,original,shore,previouslyHidden,report);
   Decorate(pondGo.transform,terrain,shore,reedMat,root.transform,report);

   var pond=pondGo.AddComponent<FarmPond>();
   pond.Configure(terrain,shore,ShoreMargin,droplet,hidden);
   if(player.GetComponent<ChickWaterWading>()==null)Undo.AddComponent<ChickWaterWading>(player.gameObject);
   if(player.GetComponent<ChickDrinkingController>()==null)Undo.AddComponent<ChickDrinkingController>(player.gameObject);
   Undo.CollapseUndoOperations(undoGroup);

   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);
   report.Insert(0,"Pond built: water level "+WaterLevel+" m, max depth "+MaxDepth+" m, shoreline "+shore.Length+" points.\n");
   return report.ToString();
  }

  public static string Remove()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before removing the pond.");
   var root=GameObject.Find("Farm Reference Map");if(root==null)throw new InvalidOperationException("Farm root missing.");
   var terrain=root.GetComponentInChildren<Terrain>();
   var original=AssetDatabase.LoadAssetAtPath<TerrainData>(OriginalTerrain);
   if(original==null)throw new InvalidOperationException("Original terrain missing: "+OriginalTerrain);
   Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Remove farm pond");int undoGroup=Undo.GetCurrentGroup();
   Undo.RecordObject(terrain,"Remove farm pond");terrain.terrainData=original;
   var collider=terrain.GetComponent<TerrainCollider>();
   if(collider!=null){Undo.RecordObject(collider,"Remove farm pond");collider.terrainData=original;}
   int restored=0;
   var pondRoot=root.transform.Find(RootName);
   if(pondRoot!=null)
   {
    if(pondRoot.TryGetComponent(out FarmPond pond))
     foreach(var go in pond.HiddenForPond)if(go!=null){Undo.RecordObject(go,"Remove farm pond");go.SetActive(true);restored++;}
    Undo.DestroyObjectImmediate(pondRoot.gameObject);
   }
   var player=UnityEngine.Object.FindFirstObjectByType<ChickPlayerController>(FindObjectsInactive.Include);
   if(player!=null)
   {
    var drink=player.GetComponent<ChickDrinkingController>();if(drink!=null)Undo.DestroyObjectImmediate(drink);
    var wade=player.GetComponent<ChickWaterWading>();if(wade!=null)Undo.DestroyObjectImmediate(wade);
   }
   Undo.CollapseUndoOperations(undoGroup);
   EditorSceneManager.MarkSceneDirty(root.scene);
   return "Pond removed: original terrain restored, "+restored+" meadow tufts re-enabled. Pond assets are kept for a rebuild.";
  }

  static void CarveTerrain(Terrain terrain,TerrainData src,TerrainData dst,Vector2[] shore,Vector2 min,Vector2 max,System.Text.StringBuilder report)
  {
   Vector3 tp=terrain.transform.position,size=src.size;
   // Heights, always rebuilt from the untouched field so the tool can be re-run.
   int res=src.heightmapResolution;
   int x0=Mathf.Max(0,Mathf.FloorToInt((min.x-tp.x)/size.x*(res-1))),x1=Mathf.Min(res-1,Mathf.CeilToInt((max.x-tp.x)/size.x*(res-1)));
   int z0=Mathf.Max(0,Mathf.FloorToInt((min.y-tp.z)/size.z*(res-1))),z1=Mathf.Min(res-1,Mathf.CeilToInt((max.y-tp.z)/size.z*(res-1)));
   var h=src.GetHeights(x0,z0,x1-x0+1,z1-z0+1);
   float lowest=float.PositiveInfinity,raised=0,carved=0;
   for(int iz=0;iz<h.GetLength(0);iz++)for(int ix=0;ix<h.GetLength(1);ix++)
   {
    float x=tp.x+(x0+ix)/(float)(res-1)*size.x,z=tp.z+(z0+iz)/(float)(res-1)*size.z;
    float h0=h[iz,ix]*size.y+tp.y;
    float sd=Signed(shore,x,z),y=Ground(h0,sd,x,z);
    if(sd<0)lowest=Mathf.Min(lowest,y);
    raised=Mathf.Max(raised,y-h0);carved=Mathf.Max(carved,h0-y);
    h[iz,ix]=(y-tp.y)/size.y;
   }
   dst.SetHeights(x0,z0,h);
   report.AppendLine("Terrain: deepest bed "+(WaterLevel-lowest).ToString("F3")+" m below water, bank carved up to "+carved.ToString("F2")+" m, berm raised up to "+raised.ToString("F2")+" m.");

   // Wet earth and pebbles on the bed and shore, blending back into the meadow.
   int ares=src.alphamapResolution,layers=src.alphamapLayers;
   int ax0=Mathf.Max(0,Mathf.FloorToInt((min.x-tp.x)/size.x*ares)),ax1=Mathf.Min(ares-1,Mathf.CeilToInt((max.x-tp.x)/size.x*ares));
   int az0=Mathf.Max(0,Mathf.FloorToInt((min.y-tp.z)/size.z*ares)),az1=Mathf.Min(ares-1,Mathf.CeilToInt((max.y-tp.z)/size.z*ares));
   var a=src.GetAlphamaps(ax0,az0,ax1-ax0+1,az1-az0+1);
   var target=new float[layers];
   for(int iz=0;iz<a.GetLength(0);iz++)for(int ix=0;ix<a.GetLength(1);ix++)
   {
    float x=tp.x+(ax0+ix+.5f)/ares*size.x,z=tp.z+(az0+iz+.5f)/ares*size.z;
    float sd=Signed(shore,x,z),n=Mathf.PerlinNoise(x*1.7f+5,z*1.7f+9);
    Array.Clear(target,0,layers);
    if(sd<-.25f){target[Soil]=.4f;target[WornMeadow]=.22f;target[Pebbles]=.13f+.25f*n;target[TroddenEarth]=.25f-.25f*n;}
    else{target[Soil]=.48f;target[TroddenEarth]=.3f;target[Pebbles]=.08f+.1f*n;target[WornMeadow]=.14f;}
    float blend=1-S(-.05f,.55f,sd+(n-.5f)*.3f);
    float sum=0;
    for(int k=0;k<layers;k++){a[iz,ix,k]=Mathf.Lerp(a[iz,ix,k],target[k],blend);sum+=a[iz,ix,k];}
    for(int k=0;k<layers;k++)a[iz,ix,k]/=Mathf.Max(sum,1e-5f);
   }
   dst.SetAlphamaps(ax0,az0,a);

   // No grass growing through the water; thinner on the wet bank.
   int dres=src.detailWidth,cleared=0;
   int dx0=Mathf.Max(0,Mathf.FloorToInt((min.x-tp.x)/size.x*dres)),dx1=Mathf.Min(dres-1,Mathf.CeilToInt((max.x-tp.x)/size.x*dres));
   int dz0=Mathf.Max(0,Mathf.FloorToInt((min.y-tp.z)/size.z*src.detailHeight)),dz1=Mathf.Min(src.detailHeight-1,Mathf.CeilToInt((max.y-tp.z)/size.z*src.detailHeight));
   for(int layer=0;layer<src.detailPrototypes.Length;layer++)
   {
    var m=src.GetDetailLayer(dx0,dz0,dx1-dx0+1,dz1-dz0+1,layer);
    for(int iz=0;iz<m.GetLength(0);iz++)for(int ix=0;ix<m.GetLength(1);ix++)
    {
     if(m[iz,ix]==0)continue;
     float x=tp.x+(dx0+ix+.5f)/dres*size.x,z=tp.z+(dz0+iz+.5f)/src.detailHeight*size.z;
     float sd=Signed(shore,x,z);
     bool clear=sd<.35f||(sd<.9f&&Mathf.PerlinNoise(x*3.1f,z*3.1f)<.55f);
     if(clear){cleared+=m[iz,ix];m[iz,ix]=0;}
    }
    dst.SetDetailLayer(dx0,dz0,layer,m);
   }
   report.AppendLine("Detail grass instances cleared from water and wet bank: "+cleared+".");
   EditorUtility.SetDirty(dst);
  }

  static Mesh WaterMesh(Vector2[] shore,Vector2 min,Vector2 max,Vector3 origin)
  {
   const float step=.2f;
   int nx=Mathf.CeilToInt((max.x-min.x+2*ShoreMargin)/step),nz=Mathf.CeilToInt((max.y-min.y+2*ShoreMargin)/step);
   var index=new Dictionary<int,int>();var verts=new List<Vector3>();var uvs=new List<Vector2>();var tris=new List<int>();
   Func<int,int,int> vertex=(ix,iz)=>{
    int key=iz*(nx+1)+ix;
    if(index.TryGetValue(key,out int found))return found;
    float x=min.x-ShoreMargin+ix*step,z=min.y-ShoreMargin+iz*step;
    index[key]=verts.Count;verts.Add(new Vector3(x-origin.x,0,z-origin.z));uvs.Add(new Vector2(x,z));
    return verts.Count-1;
   };
   for(int iz=0;iz<nz;iz++)for(int ix=0;ix<nx;ix++)
   {
    float cx=min.x-ShoreMargin+(ix+.5f)*step,cz=min.y-ShoreMargin+(iz+.5f)*step;
    if(Signed(shore,cx,cz)>ShoreMargin)continue;
    int a=vertex(ix,iz),b=vertex(ix+1,iz),c=vertex(ix,iz+1),d=vertex(ix+1,iz+1);
    tris.Add(a);tris.Add(c);tris.Add(b);tris.Add(b);tris.Add(c);tris.Add(d);
   }
   var mesh=new Mesh{name="PondWaterSurface"};
   mesh.SetVertices(verts);mesh.SetUVs(0,uvs);mesh.SetTriangles(tris,0);
   var normals=new Vector3[verts.Count];for(int i=0;i<normals.Length;i++)normals[i]=Vector3.up;mesh.normals=normals;
   mesh.RecalculateBounds();
   return mesh;
  }

  static GameObject[] HideCoveredMeadow(Transform farm,Terrain terrain,TerrainData original,Vector2[] shore,GameObject[] previouslyHidden,System.Text.StringBuilder report)
  {
   var hidden=new List<GameObject>();
   foreach(var go in previouslyHidden)if(go!=null&&!hidden.Contains(go))hidden.Add(go);
   var nature=farm.Find("05 Nature and Scenery");
   if(nature!=null)
    foreach(Transform child in nature)
    {
     if(!child.gameObject.activeSelf||!child.name.StartsWith("Meadow"))continue;
     var p=child.position;
     // Covered by water or the wet bank, or standing where the ground was raised or lowered.
     float sd=Signed(shore,p.x,p.z);
     Vector3 tp=terrain.transform.position;
     float h0=original.GetInterpolatedHeight((p.x-tp.x)/original.size.x,(p.z-tp.z)/original.size.z)+tp.y;
     if(sd>.45f&&Mathf.Abs(Ground(h0,sd,p.x,p.z)-h0)<.03f)continue;
     Undo.RecordObject(child.gameObject,"Build farm pond");child.gameObject.SetActive(false);hidden.Add(child.gameObject);
    }
   report.AppendLine("Meadow tufts covered by the pond and its wet bank (disabled, not deleted): "+hidden.Count+".");
   return hidden.ToArray();
  }

  static Vector2 ShorePoint(Vector2[] shore,float t,float outward)
  {
   float f=Mathf.Repeat(t,1)*shore.Length;int i=(int)f;
   Vector2 a=shore[i%shore.Length],b=shore[(i+1)%shore.Length];
   Vector2 p=Vector2.Lerp(a,b,f-i),tangent=(b-a).normalized;
   Vector2 normal=new Vector2(tangent.y,-tangent.x);
   // Pick the side that points out of the water.
   if(Signed(shore,p.x+normal.x*.05f,p.y+normal.y*.05f)<0)normal=-normal;
   return p+normal*outward;
  }

  static float GroundAt(Terrain terrain,Vector2 p){return terrain.SampleHeight(new Vector3(p.x,0,p.y))+terrain.transform.position.y;}

  static bool NearFence(Vector2 p){return p.x>98.5f||p.y<.65f;}

  static void Decorate(Transform pondRoot,Terrain terrain,Vector2[] shore,Material reedMat,Transform farm,System.Text.StringBuilder report)
  {
   var rng=new System.Random(4127);
   Func<float,float,float> range=(a,b)=>a+(float)rng.NextDouble()*(b-a);

   // Reeds and cattails standing in the shallows.
   var reeds=new GameObject("Reeds").transform;reeds.SetParent(pondRoot,false);
   var clumps=new Mesh[3];for(int i=0;i<clumps.Length;i++)clumps[i]=SaveMesh(ReedClump(7031+i*97,"PondReedClump_"+i));
   float[] reedSpots={.03f,.09f,.21f,.27f,.46f,.6f,.66f,.79f,.85f};
   int reedCount=0;
   foreach(float t in reedSpots)
   {
    int tufts=rng.Next(1,3);
    for(int k=0;k<tufts;k++)
    {
     var p=ShorePoint(shore,t+range(-.012f,.012f),range(-.32f,.05f));
     if(NearFence(p))continue;
     var go=new GameObject("Reed clump "+(++reedCount));go.transform.SetParent(reeds,false);
     go.transform.position=new Vector3(p.x,GroundAt(terrain,p)-.01f,p.y);
     go.transform.rotation=Quaternion.Euler(0,range(0,360),0);
     go.transform.localScale=Vector3.one*range(.85f,1.2f);
     go.AddComponent<MeshFilter>().sharedMesh=clumps[rng.Next(clumps.Length)];
     var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=reedMat;r.shadowCastingMode=ShadowCastingMode.On;
     r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
    }
   }

   var nature=farm.Find("05 Nature and Scenery");
   var rocks=new List<GameObject>();var plants=new List<GameObject>();
   if(nature!=null)
    foreach(Transform child in nature)
    {
     if(child.name.StartsWith("Scenery Rock"))rocks.Add(child.gameObject);
     else if(child.name.StartsWith("Meadow")&&child.gameObject.activeSelf)plants.Add(child.gameObject);
    }

   // A few weathered stones half sunk into the bank.
   int stoneCount=0;
   if(rocks.Count>0)
   {
    var stones=new GameObject("Shore Stones").transform;stones.SetParent(pondRoot,false);
    float[] spots={.015f,.05f,.24f,.31f,.5f,.55f,.72f,.9f,.94f};
    foreach(float t in spots)
    {
     var p=ShorePoint(shore,t,range(.0f,.35f));
     if(NearFence(p))continue;
     float size=range(.1f,.26f);
     var go=Copy(rocks[rng.Next(rocks.Count)],stones,"Shore stone "+(++stoneCount),size);
     go.transform.position=new Vector3(p.x,GroundAt(terrain,p)-size*.3f,p.y);
     go.transform.rotation=Quaternion.Euler(range(-8,8),range(0,360),range(-8,8));
     // Small enough to hop around, so they never trap the chick against the water.
     foreach(var c in go.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(c);
    }
   }

   // Grass tufts and flowers on the damp bank.
   int plantCount=0;
   if(plants.Count>0)
   {
    var bank=new GameObject("Bank Plants").transform;bank.SetParent(pondRoot,false);
    for(int i=0;i<26;i++)
    {
     var p=ShorePoint(shore,i/26f+range(-.01f,.01f),range(.25f,1.1f));
     if(NearFence(p))continue;
     var source=plants[rng.Next(plants.Count)];
     var go=Copy(source,bank,"Bank plant "+(++plantCount),0);
     go.transform.position=new Vector3(p.x,GroundAt(terrain,p),p.y);
     go.transform.rotation=Quaternion.Euler(0,range(0,360),0);
     go.transform.localScale=source.transform.localScale*range(.85f,1.25f);
     // Copy() re-instantiates the prefab, which drops the meadow tuft's scene setup; restore it so bank plants
     // bend away from the player like the meadow (interactive material, not static batched) and stay walk-through.
     var sourceRenderer=source.GetComponent<Renderer>();var copyRenderer=go.GetComponent<Renderer>();
     if(sourceRenderer!=null&&copyRenderer!=null)copyRenderer.sharedMaterials=sourceRenderer.sharedMaterials;
     GameObjectUtility.SetStaticEditorFlags(go,GameObjectUtility.GetStaticEditorFlags(source));
     foreach(var c in go.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(c);
    }
   }
   report.AppendLine("Decoration: "+reedCount+" reed clumps, "+stoneCount+" shore stones, "+plantCount+" bank plants.");
  }

  // Copies a scene prop, keeping its prefab link when it has one; size>0 rescales to that largest extent.
  static GameObject Copy(GameObject source,Transform parent,string name,float size)
  {
   GameObject go;
   var prefab=PrefabUtility.GetCorrespondingObjectFromSource(source);
   if(prefab!=null)go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
   else go=UnityEngine.Object.Instantiate(source,parent);
   go.name=name;go.SetActive(true);
   go.transform.localScale=source.transform.lossyScale;
   if(size>0)
   {
    var renderer=source.GetComponentInChildren<Renderer>();
    float extent=renderer!=null?Mathf.Max(renderer.bounds.size.x,renderer.bounds.size.y,renderer.bounds.size.z):1f;
    go.transform.localScale=source.transform.lossyScale*(size/Mathf.Max(extent,1e-3f));
   }
   return go;
  }

  // A clump of tapered reed blades with a couple of cattails; double-sided, vertex coloured.
  static Mesh ReedClump(int seed,string name)
  {
   var rng=new System.Random(seed);
   Func<float,float,float> range=(a,b)=>a+(float)rng.NextDouble()*(b-a);
   var v=new List<Vector3>();var n=new List<Vector3>();var c=new List<Color>();var t=new List<int>();
   Action<Vector3[],Vector3[],Color[]> strip=(left,right,colors)=>{
    for(int side=0;side<2;side++)
    {
     int start=v.Count;
     for(int i=0;i<left.Length;i++)
     {
      Vector3 up=i<left.Length-1?left[i+1]-left[i]:left[i]-left[i-1];
      Vector3 normal=Vector3.Cross(right[i]-left[i],up).normalized;
      if(side==1)normal=-normal;
      normal=(normal+Vector3.up*.35f).normalized;
      v.Add(left[i]);v.Add(right[i]);n.Add(normal);n.Add(normal);c.Add(colors[i]);c.Add(colors[i]);
     }
     for(int i=0;i<left.Length-1;i++)
     {
      int a=start+i*2,b=a+1,cc=a+2,d=a+3;
      // Front faces wind clockwise around their own normal: side 0 faces cross(across, up).
      if(side==0){t.Add(a);t.Add(b);t.Add(cc);t.Add(b);t.Add(d);t.Add(cc);}
      else{t.Add(a);t.Add(cc);t.Add(b);t.Add(b);t.Add(cc);t.Add(d);}
     }
    }
   };
   int blades=rng.Next(11,16);
   for(int k=0;k<blades;k++)
   {
    float yaw=range(0,Mathf.PI*2),r=range(0,.06f);
    var root=new Vector3(Mathf.Cos(yaw)*r,0,Mathf.Sin(yaw)*r);
    float height=range(.3f,.58f),width=range(.011f,.019f),lean=range(.04f,.16f);
    float face=range(0,Mathf.PI*2);
    var leanDir=new Vector3(Mathf.Cos(yaw),0,Mathf.Sin(yaw));
    var across=new Vector3(Mathf.Cos(face),0,Mathf.Sin(face));
    const int rows=5;var left=new Vector3[rows];var right=new Vector3[rows];var colors=new Color[rows];
    bool dry=rng.NextDouble()<.18;
    for(int i=0;i<rows;i++)
    {
     float f=i/(float)(rows-1);
     var spine=root+Vector3.up*height*f+leanDir*lean*f*f;
     float w=width*(1-f*.92f);
     left[i]=spine-across*w;right[i]=spine+across*w;
     var low=new Color(.2f,.31f,.14f);var high=dry?new Color(.62f,.58f,.32f):new Color(.46f,.6f,.27f);
     var col=Color.Lerp(low,high,f).linear;col.a=Mathf.Lerp(.82f,1,f);colors[i]=col;
    }
    strip(left,right,colors);
   }
   int cattails=rng.Next(1,4);
   for(int k=0;k<cattails;k++)
   {
    float yaw=range(0,Mathf.PI*2),r=range(0,.04f),height=range(.44f,.62f);
    var root=new Vector3(Mathf.Cos(yaw)*r,0,Mathf.Sin(yaw)*r);
    var tip=root+Vector3.up*height+new Vector3(Mathf.Cos(yaw),0,Mathf.Sin(yaw))*.03f;
    var across=new Vector3(-Mathf.Sin(yaw),0,Mathf.Cos(yaw));
    var stem=new Color(.3f,.4f,.18f).linear;stem.a=1;
    strip(new[]{root-across*.003f,tip-across*.002f},new[]{root+across*.003f,tip+across*.002f},new[]{stem,stem});
    // Velvety brown head: a short hexagonal tube.
    var head=new Color(.34f,.2f,.1f).linear;head.a=1;
    var axis=(tip-root).normalized;var headBase=root+axis*(height*.72f);
    for(int s=0;s<6;s++)
    {
     float a0=s*Mathf.PI/3,a1=(s+1)*Mathf.PI/3;
     var o0=new Vector3(Mathf.Cos(a0),0,Mathf.Sin(a0))*.011f;var o1=new Vector3(Mathf.Cos(a1),0,Mathf.Sin(a1))*.011f;
     int start=v.Count;var normal=((o0+o1)*.5f).normalized;
     v.Add(headBase+o0);v.Add(headBase+o1);v.Add(headBase+o0+axis*.075f);v.Add(headBase+o1+axis*.075f);
     for(int q=0;q<4;q++){n.Add(normal);c.Add(head);}
     t.Add(start);t.Add(start+2);t.Add(start+1);t.Add(start+1);t.Add(start+2);t.Add(start+3);
    }
   }
   var mesh=new Mesh{name=name};
   mesh.SetVertices(v);mesh.SetNormals(n);mesh.SetColors(c);mesh.SetTriangles(t,0);mesh.RecalculateBounds();
   return mesh;
  }
 }
}
