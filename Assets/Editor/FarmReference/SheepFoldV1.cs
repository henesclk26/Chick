using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
 // Fourth map section: the sheep fold on the hill south of the crop fields (zone A).
 // Builds a terraced dry stone fold with shelter, trough and hay rack, a gate in the south farm fence with a
 // dirt path up to the fold, and a flock of 5 sheep + 2 lambs. Reversible: the terrain is reshaped on a copy
 // (FarmTerrain_SheepFold_v1) of whatever terrain was active; hidden props and re-seated scenery are recorded
 // on the section's MapSectionRecord and restored by "Remove Sheep Fold".
 public static class SheepFoldV1
 {
  const string Menu="Tools/Chick/Sheep Fold/";
  public const string RootName="07 Sheep Fold";
  public const string TerrainPath="Assets/Art/Terrain/SheepFoldV1/FarmTerrain_SheepFold_v1.asset";
  const string Models=SheepFoldImporter.ModelFolder;
  const string AnimFolder=SheepFoldImporter.Folder+"/Animation";
  public static readonly Vector2 Center=new Vector2(113.5f,-57.5f);
  public const float WallRadius=7f;
  const int Segments=22;
  const string FenceSegment="Outer South 19";
  // Crop-field path just inside the south fence, and the dirt path's bend that keeps it clear of the big oak.
  static readonly Vector2 PathStart=new Vector2(111f,-29.8f),PathBend=new Vector2(109.4f,-41f);
  const int TroddenEarth=4,Soil=5,Pebbles=6,WornMeadow=7;

  [MenuItem(Menu+"Build Sheep Fold (V1)")]
  static void BuildMenu(){Debug.Log(Build());}
  [MenuItem(Menu+"Remove Sheep Fold")]
  static void RemoveMenu(){Debug.Log(Remove());}

  static float S(float a,float b,float v){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));}
  static Vector2 Dir(float degrees){float r=degrees*Mathf.Deg2Rad;return new Vector2(Mathf.Cos(r),Mathf.Sin(r));}
  // Yaw (degrees) that turns a model's local +X onto the given XZ direction.
  static float YawAlongX(Vector2 d){return -Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg;}
  // Yaw (degrees) that turns a model's local +Z onto the given XZ direction.
  static float YawAlongZ(Vector2 d){return Mathf.Atan2(d.x,d.y)*Mathf.Rad2Deg;}

  static float GateAngle(Transform farm)
  {
   var fence=farm.Find("04 Fences and Gates/"+FenceSegment);
   Vector2 gate=fence!=null?new Vector2(fence.position.x,fence.position.z):new Vector2(111f,-31.94f);
   var d=gate-Center;
   return Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg;
  }

  static Vector2 FoldGatePoint(float gateAngle){return Center+Dir(gateAngle)*(WallRadius+.6f);}

  static Vector2 PathPoint(float t,Vector2 end)
  {
   float u=1-t;
   return u*u*PathStart+2*u*t*PathBend+t*t*end;
  }

  static float PathDistance(Vector2 p,Vector2 end)
  {
   float best=float.PositiveInfinity;
   Vector2 prev=PathPoint(0,end);
   for(int i=1;i<=40;i++)
   {
    Vector2 next=PathPoint(i/40f,end),ab=next-prev;
    float t=Mathf.Clamp01(Vector2.Dot(p-prev,ab)/Mathf.Max(ab.sqrMagnitude,1e-6f));
    best=Mathf.Min(best,(prev+ab*t-p).magnitude);
    prev=next;
   }
   return best;
  }

  public static string Build()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before building the sheep fold.");
   var farm=GameObject.Find("Farm Reference Map");if(farm==null)throw new InvalidOperationException("Farm root missing.");
   var terrain=farm.GetComponentInChildren<Terrain>();if(terrain==null)throw new InvalidOperationException("Farm terrain missing.");
   foreach(var path in new[]{"Sheep","Lamb","FoldWall_A","FoldWall_B","FoldShelter","FoldWaterTrough","FoldHayRack","HayBale","HayBaleStack","FoldGate"})
    if(AssetDatabase.LoadAssetAtPath<GameObject>(Models+path+".fbx")==null)throw new InvalidOperationException("Missing model "+path+".fbx; export ArtSource/SheepFold first.");

   Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Build sheep fold");int undoGroup=Undo.GetCurrentGroup();
   var report=new System.Text.StringBuilder();
   // An earlier build is taken down first so its terrain source and hidden objects carry over.
   var old=farm.transform.Find(RootName);
   TerrainData source=null;var keepHidden=new List<GameObject>();
   if(old!=null&&old.TryGetComponent(out MapSectionRecord oldRecord))
   {
    source=oldRecord.sourceTerrain;
    for(int i=0;i<oldRecord.reseated.Length;i++)
     if(oldRecord.reseated[i]!=null){Undo.RecordObject(oldRecord.reseated[i],"Build sheep fold");oldRecord.reseated[i].position-=Vector3.up*oldRecord.reseatOffsets[i];}
    keepHidden.AddRange(oldRecord.hidden.Where(g=>g!=null));
   }
   if(old!=null)Undo.DestroyObjectImmediate(old.gameObject);
   if(source==null)source=terrain.terrainData;
   if(AssetDatabase.GetAssetPath(source)==TerrainPath)throw new InvalidOperationException("Terrain is already the fold copy but no record of its source was found.");

   if(!AssetDatabase.IsValidFolder("Assets/Art/Terrain/SheepFoldV1"))AssetDatabase.CreateFolder("Assets/Art/Terrain","SheepFoldV1");
   var data=AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
   if(data==null)
   {
    if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source),TerrainPath))throw new InvalidOperationException("Could not copy the terrain.");
    data=AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
   }
   else CopyWhole(source,data);

   float gateAngle=GateAngle(farm.transform);
   Vector2 foldGate=FoldGatePoint(gateAngle);
   float level=TerraceLevel(terrain,source);
   Reshape(terrain,source,data,level,gateAngle,foldGate,report);
   Undo.RecordObject(terrain,"Build sheep fold");terrain.terrainData=data;
   var tc=terrain.GetComponent<TerrainCollider>();
   if(tc!=null){Undo.RecordObject(tc,"Build sheep fold");tc.terrainData=data;}

   var root=new GameObject(RootName);Undo.RegisterCreatedObjectUndo(root,"Build sheep fold");
   root.transform.SetParent(farm.transform,false);
   root.transform.position=new Vector3(Center.x,level,Center.y);
   var record=root.AddComponent<MapSectionRecord>();
   record.sourceTerrain=source;

   var hidden=new List<GameObject>(keepHidden);
   HideCovered(farm.transform,gateAngle,foldGate,hidden,report);
   record.hidden=hidden.Distinct().ToArray();
   Reseat(farm.transform,root.transform,terrain,source,record,report);

   var pen=BuildFold(root.transform,terrain,level,gateAngle,report);
   BuildFieldGate(farm.transform,root.transform,terrain,report);
   BuildFlock(root.transform,pen,report);
   var spawn=SpawnPoint(farm.transform,terrain,foldGate,gateAngle);
   Undo.CollapseUndoOperations(undoGroup);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(farm.scene);
   report.Insert(0,"Sheep fold built at "+Center+" (terrace level "+level.ToString("F2")+" m, wall radius "+WallRadius+" m). Spawn: "+spawn.name+".\n");
   return report.ToString();
  }

  public static string Remove()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before removing the sheep fold.");
   var farm=GameObject.Find("Farm Reference Map");if(farm==null)throw new InvalidOperationException("Farm root missing.");
   var root=farm.transform.Find(RootName);if(root==null)return "No sheep fold to remove.";
   var record=root.GetComponent<MapSectionRecord>();
   Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Remove sheep fold");int undoGroup=Undo.GetCurrentGroup();
   var terrain=farm.GetComponentInChildren<Terrain>();
   int restored=0,reseated=0;
   if(record!=null)
   {
    if(record.sourceTerrain!=null&&terrain!=null)
    {
     Undo.RecordObject(terrain,"Remove sheep fold");terrain.terrainData=record.sourceTerrain;
     var tc=terrain.GetComponent<TerrainCollider>();
     if(tc!=null){Undo.RecordObject(tc,"Remove sheep fold");tc.terrainData=record.sourceTerrain;}
    }
    foreach(var go in record.hidden)if(go!=null){Undo.RecordObject(go,"Remove sheep fold");go.SetActive(true);restored++;}
    for(int i=0;i<record.reseated.Length;i++)
     if(record.reseated[i]!=null){Undo.RecordObject(record.reseated[i],"Remove sheep fold");record.reseated[i].position-=Vector3.up*record.reseatOffsets[i];reseated++;}
   }
   var spawn=farm.transform.Find("Developer Spawn Points/04 Sheep Fold Spawn");
   if(spawn!=null)Undo.DestroyObjectImmediate(spawn.gameObject);
   Undo.DestroyObjectImmediate(root.gameObject);
   Undo.CollapseUndoOperations(undoGroup);
   EditorSceneManager.MarkSceneDirty(farm.scene);
   return "Sheep fold removed: terrain restored, "+restored+" objects re-enabled, "+reseated+" re-seated objects put back. Assets are kept for a rebuild.";
  }

  static void CopyWhole(TerrainData src,TerrainData dst)
  {
   int res=src.heightmapResolution;
   dst.SetHeights(0,0,src.GetHeights(0,0,res,res));
   dst.SetAlphamaps(0,0,src.GetAlphamaps(0,0,src.alphamapWidth,src.alphamapHeight));
   for(int layer=0;layer<src.detailPrototypes.Length;layer++)
    dst.SetDetailLayer(0,0,layer,src.GetDetailLayer(0,0,src.detailWidth,src.detailHeight,layer));
  }

  static float TerraceLevel(Terrain terrain,TerrainData source)
  {
   Vector3 tp=terrain.transform.position;float sum=0;int n=0;
   for(float x=-WallRadius;x<=WallRadius;x+=.5f)for(float z=-WallRadius;z<=WallRadius;z+=.5f)
   {
    if(x*x+z*z>WallRadius*WallRadius)continue;
    sum+=source.GetInterpolatedHeight((Center.x+x-tp.x)/source.size.x,(Center.y+z-tp.z)/source.size.z)+tp.y;n++;
   }
   return sum/n;
  }

  // Flat terrace for the fold, banked smoothly back into the hill.
  static float Ground(float h0,float r,float level)
  {
   float flat=WallRadius+.7f;
   if(r<=flat)return level;
   float w=Mathf.Clamp(Mathf.Abs(h0-level)*3f,1.5f,5f);
   return Mathf.Lerp(level,h0,S(flat,flat+w,r));
  }

  static void Reshape(Terrain terrain,TerrainData src,TerrainData dst,float level,float gateAngle,Vector2 foldGate,System.Text.StringBuilder report)
  {
   Vector3 tp=terrain.transform.position,size=src.size;
   int res=src.heightmapResolution;
   float reach=WallRadius+6.5f;
   int x0=Mathf.FloorToInt((Center.x-reach-tp.x)/size.x*(res-1)),x1=Mathf.CeilToInt((Center.x+reach-tp.x)/size.x*(res-1));
   int z0=Mathf.FloorToInt((Center.y-reach-tp.z)/size.z*(res-1)),z1=Mathf.CeilToInt((Center.y+reach-tp.z)/size.z*(res-1));
   var h=src.GetHeights(x0,z0,x1-x0+1,z1-z0+1);
   float cut=0,fill=0;
   for(int iz=0;iz<h.GetLength(0);iz++)for(int ix=0;ix<h.GetLength(1);ix++)
   {
    float x=tp.x+(x0+ix)/(float)(res-1)*size.x,z=tp.z+(z0+iz)/(float)(res-1)*size.z;
    float h0=h[iz,ix]*size.y+tp.y,r=(new Vector2(x,z)-Center).magnitude;
    float y=Ground(h0,r,level);
    cut=Mathf.Max(cut,h0-y);fill=Mathf.Max(fill,y-h0);
    h[iz,ix]=(y-tp.y)/size.y;
   }
   dst.SetHeights(x0,z0,h);
   report.AppendLine("Terrace: cut up to "+cut.ToString("F2")+" m, filled up to "+fill.ToString("F2")+" m.");

   // Paint: trodden fold floor, worn ring under the wall, and the dirt path from the field gate.
   int ares=src.alphamapResolution,layers=src.alphamapLayers;
   float minX=Mathf.Min(Center.x-reach,PathBend.x-3),maxX=Mathf.Max(Center.x+reach,PathStart.x+3);
   float minZ=Center.y-reach,maxZ=PathStart.y+1.5f;
   int ax0=Mathf.FloorToInt((minX-tp.x)/size.x*ares),ax1=Mathf.CeilToInt((maxX-tp.x)/size.x*ares);
   int az0=Mathf.FloorToInt((minZ-tp.z)/size.z*ares),az1=Mathf.CeilToInt((maxZ-tp.z)/size.z*ares);
   var a=src.GetAlphamaps(ax0,az0,ax1-ax0+1,az1-az0+1);
   var target=new float[layers];
   for(int iz=0;iz<a.GetLength(0);iz++)for(int ix=0;ix<a.GetLength(1);ix++)
   {
    float x=tp.x+(ax0+ix+.5f)/ares*size.x,z=tp.z+(az0+iz+.5f)/ares*size.z;
    var p=new Vector2(x,z);
    float r=(p-Center).magnitude,n=Mathf.PerlinNoise(x*1.3f+7,z*1.3f+3),fine=Mathf.PerlinNoise(x*4.1f,z*4.1f);
    float path=1-S(.55f,1.15f,PathDistance(p,foldGate)+(n-.5f)*.35f);
    float floor=1-S(WallRadius-.4f,WallRadius+1.2f,r+(n-.5f)*.8f);
    float weight=Mathf.Max(path,floor*(.55f+.35f*n));
    if(weight<=.001f)continue;
    Array.Clear(target,0,layers);
    if(path>=floor*(.55f+.35f*n)){target[TroddenEarth]=.62f+.2f*fine;target[Pebbles]=.18f;target[WornMeadow]=.2f-.2f*fine;}
    else{target[WornMeadow]=.5f+.2f*fine;target[TroddenEarth]=.3f-.2f*fine;target[Soil]=.2f;}
    float sum=0;
    for(int k=0;k<layers;k++){a[iz,ix,k]=Mathf.Lerp(a[iz,ix,k],target[k],weight);sum+=a[iz,ix,k];}
    for(int k=0;k<layers;k++)a[iz,ix,k]/=Mathf.Max(sum,1e-5f);
   }
   dst.SetAlphamaps(ax0,az0,a);

   // Grass: none on the path or under the wall, grazed short inside the fold.
   int dres=src.detailWidth,cleared=0;
   int dx0=Mathf.FloorToInt((minX-tp.x)/size.x*dres),dx1=Mathf.CeilToInt((maxX-tp.x)/size.x*dres);
   int dz0=Mathf.FloorToInt((minZ-tp.z)/size.z*src.detailHeight),dz1=Mathf.CeilToInt((maxZ-tp.z)/size.z*src.detailHeight);
   for(int layer=0;layer<src.detailPrototypes.Length;layer++)
   {
    var m=src.GetDetailLayer(dx0,dz0,dx1-dx0+1,dz1-dz0+1,layer);
    for(int iz=0;iz<m.GetLength(0);iz++)for(int ix=0;ix<m.GetLength(1);ix++)
    {
     if(m[iz,ix]==0)continue;
     float x=tp.x+(dx0+ix+.5f)/dres*size.x,z=tp.z+(dz0+iz+.5f)/src.detailHeight*size.z;
     var p=new Vector2(x,z);float r=(p-Center).magnitude;
     bool clear=PathDistance(p,foldGate)<.9f||Mathf.Abs(r-WallRadius)<.7f||
                (r<WallRadius&&Mathf.PerlinNoise(x*2.3f,z*2.3f)<.62f);
     if(clear){cleared+=m[iz,ix];m[iz,ix]=0;}
    }
    dst.SetDetailLayer(dx0,dz0,layer,m);
   }
   report.AppendLine("Grass instances cleared from path, wall line and grazed fold floor: "+cleared+".");
   EditorUtility.SetDirty(dst);
  }

  static void HideCovered(Transform farm,float gateAngle,Vector2 foldGate,List<GameObject> hidden,System.Text.StringBuilder report)
  {
   int before=hidden.Count;
   var fence=farm.Find("04 Fences and Gates/"+FenceSegment);
   if(fence!=null&&fence.gameObject.activeSelf){Undo.RecordObject(fence.gameObject,"Build sheep fold");fence.gameObject.SetActive(false);hidden.Add(fence.gameObject);}
   var nature=farm.Find("05 Nature and Scenery");
   if(nature!=null)
    foreach(Transform child in nature)
    {
     if(!child.gameObject.activeSelf)continue;
     var p=new Vector2(child.position.x,child.position.z);
     float r=(p-Center).magnitude;
     bool tuft=child.name.StartsWith("Meadow")||child.name.StartsWith("Exterior Grass");
     // Trunks just outside the wall stay (their canopies shade the fold); tufts clear a little wider.
     bool covered=r<WallRadius+(tuft?1.2f:1f)||PathDistance(p,foldGate)<(tuft?1.1f:1.4f);
     if(!covered)continue;
     Undo.RecordObject(child.gameObject,"Build sheep fold");child.gameObject.SetActive(false);hidden.Add(child.gameObject);
    }
   report.AppendLine("Hidden (not deleted): "+(hidden.Count-before)+" objects in the fold or on the path, including fence segment "+FenceSegment+".");
  }

  // Scenery standing on the reshaped bank is moved to the new ground height (recorded for removal).
  static void Reseat(Transform farm,Transform root,Terrain terrain,TerrainData source,MapSectionRecord record,System.Text.StringBuilder report)
  {
   var moved=new List<Transform>();var offsets=new List<float>();
   Vector3 tp=terrain.transform.position;
   var nature=farm.Find("05 Nature and Scenery");
   if(nature!=null)
    foreach(Transform child in nature)
    {
     if(!child.gameObject.activeSelf)continue;
     var p=child.position;
     if((new Vector2(p.x,p.z)-Center).magnitude>WallRadius+7f)continue;
     float before=source.GetInterpolatedHeight((p.x-tp.x)/source.size.x,(p.z-tp.z)/source.size.z)+tp.y;
     float after=terrain.SampleHeight(p)+tp.y;
     if(Mathf.Abs(after-before)<.03f)continue;
     Undo.RecordObject(child,"Build sheep fold");child.position+=Vector3.up*(after-before);
     moved.Add(child);offsets.Add(after-before);
    }
   record.reseated=moved.ToArray();record.reseatOffsets=offsets.ToArray();
   report.AppendLine("Re-seated on the new bank: "+moved.Count+" scenery objects.");
  }

  static GameObject Place(string model,Transform parent,Vector3 position,float yaw,string name)
  {
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Models+model+".fbx");
   var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
   go.name=name;
   go.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
   return go;
  }

  static void AddBox(GameObject go,Vector3 center,Vector3 size,bool cameraBlocker)
  {
   var box=go.AddComponent<BoxCollider>();box.center=center;box.size=size;
   if(!cameraBlocker)return;
   var blocker=new GameObject("Camera Blocker");
   blocker.layer=LayerMask.NameToLayer("CameraBlocker");
   blocker.transform.SetParent(go.transform,false);
   var b=blocker.AddComponent<BoxCollider>();b.center=center;b.size=size;
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

  // The gate model: two posts with the leaf as a child whose origin is the hinge.
  static void GateColliders(GameObject gate,float openDegrees)
  {
   var leaf=gate.transform.Find("FoldGate_Leaf");
   foreach(var mf in gate.GetComponentsInChildren<MeshFilter>())
   {
    if(mf.transform==gate.transform)
    {
     var b=mf.sharedMesh.bounds;
     // Two posts at the ends of the posts mesh.
     float postW=.18f;
     AddBox(gate,new Vector3(b.min.x+postW/2,b.center.y,0),new Vector3(postW,b.size.y,postW),true);
     AddBox(gate,new Vector3(b.max.x-postW/2,b.center.y,0),new Vector3(postW,b.size.y,postW),true);
    }
   }
   if(leaf!=null)
   {
    leaf.localRotation=Quaternion.Euler(0,openDegrees,0);
    var lb=leaf.GetComponent<MeshFilter>().sharedMesh.bounds;
    var box=leaf.gameObject.AddComponent<BoxCollider>();box.center=lb.center;box.size=new Vector3(lb.size.x,lb.size.y,.1f);
   }
  }

  static SheepPen BuildFold(Transform root,Terrain terrain,float level,float gateAngle,System.Text.StringBuilder report)
  {
   var walls=new GameObject("Dry Stone Wall").transform;walls.SetParent(root,false);
   var rng=new System.Random(907);
   for(int i=1;i<Segments;i++)
   {
    float angle=gateAngle+i*360f/Segments;
    var d=Dir(angle);var p=Center+d*WallRadius;
    var tangent=new Vector2(-d.y,d.x);
    float yaw=YawAlongX(tangent)+(rng.NextDouble()<.5?180f:0f);
    var seg=Place(i%2==0?"FoldWall_A":"FoldWall_B",walls,new Vector3(p.x,level,p.y),yaw,"Wall "+i.ToString("00"));
    AddBox(seg,new Vector3(0,.42f,0),new Vector3(2.02f,.88f,.5f),true);
   }
   var g=Dir(gateAngle);var gp=Center+g*WallRadius;
   var gate=Place("FoldGate",root,new Vector3(gp.x,level,gp.y),YawAlongX(new Vector2(-g.y,g.x)),"Fold Gate");
   var gb=LocalBounds(gate);
   gate.transform.position-=gate.transform.rotation*new Vector3(gb.center.x,0,gb.center.z);
   GateColliders(gate,-105f);

   // Inside: shelter across from the gate, opening towards it; trough and hay rack on either side.
   var fwd=g;var right=new Vector2(fwd.y,-fwd.x);
   float yawF=YawAlongZ(fwd);
   var inside=new GameObject("Fold Furniture").transform;inside.SetParent(root,false);
   Func<Vector2,Vector3> at=v=>new Vector3(v.x,level,v.y);
   var shelterPos=Center-fwd*3.75f;
   var shelter=Place("FoldShelter",inside,at(shelterPos),yawF,"Shelter");
   ShelterColliders(shelter);
   var troughPos=Center+right*4.25f+fwd*.4f;
   var trough=Place("FoldWaterTrough",inside,at(troughPos),yawF+90,"Water Trough");
   var tb=LocalBounds(trough);AddBox(trough,tb.center,tb.size,false);
   var rackPos=Center-right*4.2f+fwd*.3f;
   var rack=Place("FoldHayRack",inside,at(rackPos),yawF+90,"Hay Rack");
   var rb=LocalBounds(rack);AddBox(rack,new Vector3(rb.center.x,.55f,rb.center.z),new Vector3(rb.size.x,1.1f,rb.size.z*.85f),false);
   var stackPos=Center-fwd*2.3f+right*3.05f;
   var stack=Place("HayBaleStack",inside,at(stackPos),yawF-28,"Hay Bale Stack");
   var sb=LocalBounds(stack);AddBox(stack,sb.center,sb.size,false);
   var balePos=Center-fwd*2.6f-right*3.0f;
   var bale=Place("HayBale",inside,at(balePos),yawF+18,"Hay Bale");
   var bb=LocalBounds(bale);AddBox(bale,bb.center,bb.size,false);

   var pen=root.gameObject.AddComponent<SheepPen>();
   var obstacles=new List<Vector3>{
    new Vector3(shelterPos.x,shelterPos.y,1.3f),
    new Vector3((shelterPos+right*1.25f).x,(shelterPos+right*1.25f).y,1.15f),
    new Vector3((shelterPos-right*1.25f).x,(shelterPos-right*1.25f).y,1.15f),
    new Vector3((troughPos+fwd*.45f).x,(troughPos+fwd*.45f).y,.5f),new Vector3((troughPos-fwd*.45f).x,(troughPos-fwd*.45f).y,.5f),
    new Vector3((rackPos+fwd*.45f).x,(rackPos+fwd*.45f).y,.6f),new Vector3((rackPos-fwd*.45f).x,(rackPos-fwd*.45f).y,.6f),
    new Vector3(stackPos.x,stackPos.y,1.0f),new Vector3(balePos.x,balePos.y,.55f)};
   pen.Configure(terrain,WallRadius-.35f,obstacles.ToArray());
   report.AppendLine("Fold: "+(Segments-1)+" wall segments, gate, shelter, trough, hay rack and bales with colliders.");
   return pen;
  }

  static void ShelterColliders(GameObject shelter)
  {
   // Model space (opening towards +Z): back wall at z = -1.1, plank sides on the back half, five posts.
   AddBox(shelter,new Vector3(0,.72f,-1.12f),new Vector3(3.25f,1.45f,.1f),true);
   AddBox(shelter,new Vector3(-1.62f,.6f,-.5f),new Vector3(.1f,1.2f,1.25f),true);
   AddBox(shelter,new Vector3(1.62f,.6f,-.5f),new Vector3(.1f,1.2f,1.25f),true);
   AddBox(shelter,new Vector3(-1.6f,1f,1.1f),new Vector3(.14f,2f,.14f),false);
   AddBox(shelter,new Vector3(1.6f,1f,1.1f),new Vector3(.14f,2f,.14f),false);
   // The two hay bales stacked in the back corner.
   AddBox(shelter,new Vector3(.88f,.38f,-.56f),new Vector3(.95f,.76f,.5f),false);
  }

  static void BuildFieldGate(Transform farm,Transform root,Terrain terrain,System.Text.StringBuilder report)
  {
   var fence=farm.Find("04 Fences and Gates/"+FenceSegment);
   Vector3 p=fence!=null?fence.position:new Vector3(111f,-11.75f,-31.94f);
   var gate=Place("FoldGate",root,p,0,"Field Gate - Crop Fields to Sheep Fold");
   var b=LocalBounds(gate);
   // The fence segment is 2.8 m; the gate is stretched to fill it.
   float stretch=2.8f/b.size.x;
   gate.transform.localScale=new Vector3(stretch,1,1);
   b=LocalBounds(gate);
   gate.transform.position-=new Vector3(b.center.x*stretch,0,b.center.z);
   gate.transform.position=new Vector3(gate.transform.position.x,terrain.SampleHeight(p)+terrain.transform.position.y,gate.transform.position.z);
   // Only the posts are stretched: the leaf leaves the scaled parent (a rotated child of a
   // non-uniformly scaled parent would shear) and swings open towards the pasture.
   var leaf=gate.transform.Find("FoldGate_Leaf");
   GateColliders(gate,0f);
   if(leaf!=null)
   {
    PrefabUtility.UnpackPrefabInstance(gate,PrefabUnpackMode.OutermostRoot,InteractionMode.AutomatedAction);
    Vector3 hinge=gate.transform.TransformPoint(leaf.localPosition);
    leaf.SetParent(root,false);
    leaf.name="Field Gate Leaf";
    leaf.SetPositionAndRotation(hinge,gate.transform.rotation*Quaternion.Euler(0,-100f,0));
    leaf.localScale=Vector3.one;
   }
   report.AppendLine("Field gate placed in the south fence (segment "+FenceSegment+" hidden), leaf open towards the pasture.");
  }

  static AnimatorController Controller(string name,string model)
  {
   if(!AssetDatabase.IsValidFolder(AnimFolder))AssetDatabase.CreateFolder(SheepFoldImporter.Folder,"Animation");
   string path=AnimFolder+"/"+name+".controller";
   AssetDatabase.DeleteAsset(path);
   var controller=AnimatorController.CreateAnimatorControllerAtPath(path);
   var machine=controller.layers[0].stateMachine;
   var clips=AssetDatabase.LoadAllAssetsAtPath(Models+model+".fbx").OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview")).ToDictionary(c=>c.name);
   foreach(var state in new[]{"Idle","Walk","Graze","Run"})
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
   for(int i=0;i<7;i++)
   {
    bool lamb=i>=5;
    float body=lamb?.32f:.5f;
    Vector3 spot=lamb?pen.RandomPoint(body,adults[i==5?0:2].transform.position,1.3f):pen.RandomPoint(body);
    spot.y=pen.GroundHeight(spot);
    var go=Place(lamb?"Lamb":"Sheep",flock,spot,UnityEngine.Random.Range(0f,360f),(lamb?"Lamb ":"Sheep ")+(lamb?i-4:i+1));
    var animator=go.GetComponent<Animator>();
    animator.runtimeAnimatorController=lamb?lambController:sheepController;
    animator.applyRootMotion=false;
    animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
    var body3=go.AddComponent<Rigidbody>();body3.isKinematic=true;body3.interpolation=RigidbodyInterpolation.Interpolate;
    var capsule=go.AddComponent<CapsuleCollider>();capsule.direction=2;
    capsule.center=lamb?new Vector3(0,.36f,.02f):new Vector3(0,.52f,.02f);
    capsule.radius=lamb?.2f:.32f;capsule.height=lamb?.78f:1.25f;
    AddContactShadow(go,lamb?new Vector2(.55f,.85f):new Vector2(.95f,1.45f));
    var sheep=go.AddComponent<FarmSheep>();
    if(lamb)sheep.Configure(pen,adults[i==5?0:2],animator,.45f,2f,.5f,1.1f,body);
    else{sheep.Configure(pen,null,animator,.55f,2.3f,.65f,1.45f,body);adults.Add(sheep);}
   }
   UnityEngine.Random.state=saved;
   report.AppendLine("Flock: 5 sheep and 2 lambs (lambs follow Sheep 1 and Sheep 3).");
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

  static Transform SpawnPoint(Transform farm,Terrain terrain,Vector2 foldGate,float gateAngle)
  {
   var points=farm.Find("Developer Spawn Points");
   var old=points!=null?points.Find("04 Sheep Fold Spawn"):null;
   if(old!=null)Undo.DestroyObjectImmediate(old.gameObject);
   var spawn=new GameObject("04 Sheep Fold Spawn").transform;Undo.RegisterCreatedObjectUndo(spawn.gameObject,"Build sheep fold");
   spawn.SetParent(points!=null?points:farm,false);
   Vector2 p=PathPoint(.86f,foldGate);
   spawn.position=new Vector3(p.x,terrain.SampleHeight(new Vector3(p.x,0,p.y))+terrain.transform.position.y+.05f,p.y);
   spawn.rotation=Quaternion.Euler(0,YawAlongZ(Center-p),0);
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
