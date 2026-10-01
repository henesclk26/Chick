using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FarmReferenceTools
{
 public static class SouthMountainV1
 {
  public const string Folder="Assets/Art/FarmReference/SouthMountain";
  public const int Revision=4;
  public const string GroupName="South Mountain - distant scenery";
  static Terrain terrain;
  static Transform farm;
  // x, z, height, x-radius, z-radius: a broken, asymmetric ridge rather than a single cone.
  static readonly float[,] Peaks={
   {-205,-330,82,155,170},{-65,-355,132,175,185},{47,-325,113,155,170},
   {159,-387,123,165,190},{278,-395,78,153,180},{-315,-405,59,150,175}
  };
  static float S(float a,float b,float v){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));}
  static float N(float x,float z,float s,float seed){return Mathf.PerlinNoise(x*s+seed,z*s+seed*.43f);}
  static float Height(float x,float z)
  {
   float side=1-S(325,430,Mathf.Abs(x)),back=1-S(550,620,-z);
   float sum=0;
   for(int i=0;i<Peaks.GetLength(0);i++)
   {
    float u=(x-Peaks[i,0])/Peaks[i,3],v=(z-Peaks[i,1])/Peaks[i,4];
    float theta=Mathf.Atan2(v,u),radius=Mathf.Sqrt(u*u+v*v);
    float eroded=radius*(1+.105f*Mathf.Sin(theta*5+i*1.7f+radius*2.4f)+.045f*Mathf.Sin(theta*11+i));
    float height=Peaks[i,2]*Mathf.Pow(Mathf.Max(0,1-eroded),1.23f);
    sum+=Mathf.Pow(height,4);
   }
   float massif=Mathf.Pow(sum,.25f);
   float detail=(N(x,z,.025f,17)-.5f)*16+(N(x,z,.066f,93)-.5f)*5;
   massif=Mathf.Max(0,massif+detail*S(8,60,massif));
   float rolling=5+16*N(x,z,.010f,54)+7*N(x,z,.028f,73);
   float foothills=rolling*S(75,155,-z)*(1-S(230,345,-z));
   float y=-3+(massif+foothills)*side*back;
   float sampleX=Mathf.Clamp(x,-134.8f,134.8f);
   var wp=farm.TransformPoint(new Vector3(sampleX,0,-79.7f));
   float seam=terrain.SampleHeight(wp)+terrain.transform.position.y-farm.position.y-.32f;
   // The first rows tuck under the existing terrain. No terrain heights are changed.
   return Mathf.Lerp(seam,y,S(77,137,-z));
  }
  static Vector3 Normal(float x,float z)
  {
   const float d=2.5f;
   return new Vector3(Height(x-d,z)-Height(x+d,z),2*d,Height(x,z-d)-Height(x,z+d)).normalized;
  }
  static Color Tint(Vector3 p,Vector3 n)
  {
   float rock=S(.27f,.69f,1-n.y)*.86f+S(47,109,p.y)*.66f;
   rock=Mathf.Clamp01(rock+(N(p.x,p.z,.036f,25)-.5f)*.36f);
   var meadow=new Color(.37f,.46f,.205f);
   var forest=new Color(.225f,.345f,.205f);
   var grass=Color.Lerp(meadow,forest,S(7,58,p.y)*.67f+N(p.x,p.z,.037f,94)*.18f);
   var stone=Color.Lerp(new Color(.38f,.405f,.345f),new Color(.52f,.52f,.445f),N(p.x,p.z,.043f,64));
   var color=Color.Lerp(grass,stone,rock);
   color*=.91f+.17f*N(p.x,p.z,.09f,42);
   float ridge=Height(p.x-6,p.z)+Height(p.x+6,p.z)+Height(p.x,p.z-6)+Height(p.x,p.z+6)-4*p.y;
   color=color.linear;color.a=Mathf.Lerp(1,.79f,Mathf.Clamp01(ridge*.06f));
   return color;
  }
  static Mesh BuildMesh(int nx,int nz,string name)
  {
   var pts=new Vector3[(nx+1)*(nz+1)];var norms=new Vector3[pts.Length];var colors=new Color[pts.Length];
   var rng=new System.Random(6161);
   for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
   {
    float px=Mathf.Lerp(-430,430,x/(float)nx),pz=Mathf.Lerp(-620,-76,z/(float)nz);
    if(x>0 && x<nx)px+=((float)rng.NextDouble()-.5f)*860/nx*.45f;
    if(z>0 && z<nz)pz+=((float)rng.NextDouble()-.5f)*544/nz*.45f;
    int i=z*(nx+1)+x;
    pts[i]=new Vector3(px,Height(px,pz),pz);norms[i]=Normal(px,pz);colors[i]=Tint(pts[i],norms[i]);
   }
   var verts=new List<Vector3>(nx*nz*6);var normals=new List<Vector3>(nx*nz*6);
   var cs=new List<Color>(nx*nz*6);var indices=new List<int>(nx*nz*6);
   Action<int,int,int> tri=(a,b,c)=>{
    Vector3 face=Vector3.Cross(pts[b]-pts[a],pts[c]-pts[a]).normalized;
    if(face.y<0){int temp=b;b=c;c=temp;face=-face;}
    foreach(int i in new[]{a,b,c}){indices.Add(verts.Count);verts.Add(pts[i]);normals.Add(Vector3.Slerp(norms[i],face,.38f).normalized);cs.Add(colors[i]);}
   };
   for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
   {
    int a=z*(nx+1)+x,b=a+1,c=a+nx+1,d=c+1;
    if((x+z)%2==0){tri(a,c,b);tri(b,c,d);}else{tri(a,c,d);tri(a,d,b);}
   }
   var mesh=new Mesh{name=name,indexFormat=verts.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
   mesh.SetVertices(verts);mesh.SetNormals(normals);mesh.SetColors(cs);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
   string path=Folder+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(existing==null)AssetDatabase.CreateAsset(mesh,path);
   else{existing.Clear();existing.indexFormat=mesh.indexFormat;existing.vertices=mesh.vertices;existing.normals=mesh.normals;existing.colors=mesh.colors;existing.triangles=mesh.triangles;existing.RecalculateBounds();UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);}
   return mesh;
  }
  static void Woodland(GameObject parent,Material mat)
  {
   var points=new List<Vector4>();var rng=new System.Random(8317);
   for(int attempt=0;attempt<10000 && points.Count<680;attempt++)
   {
    float x=Mathf.Lerp(-295,295,(float)rng.NextDouble()),z=Mathf.Lerp(-278,-138,(float)rng.NextDouble());
    float y=Height(x,z);
    if(y>61 || Normal(x,z).y<.79f || N(x,z,.021f,183)<.55f)continue;
    bool near=false;foreach(var p in points)if(new Vector2(p.x-x,p.z-z).sqrMagnitude<7){near=true;break;}
    if(near)continue;
    points.Add(new Vector4(x,y-.08f,z,Mathf.Lerp(3.7f,6.8f,(float)rng.NextDouble())));
   }
   var group=new GameObject("Foothill woodland - combined silhouettes");group.transform.SetParent(parent.transform,false);
   var lods=new LOD[2];
   for(int level=0;level<2;level++)
   {
    var v=new List<Vector3>();var ns=new List<Vector3>();var colors=new List<Color>();var ts=new List<int>();
    Action<Vector3,Vector3,Vector3,Color> tri=(a,b,c,color)=>{
     var n=Vector3.Cross(b-a,c-a).normalized;
     foreach(var p in new[]{a,b,c}){ts.Add(v.Count);v.Add(p);ns.Add(n);colors.Add(color);}
    };
    int number=0;
    foreach(var p in points)
    {
     float h=p.w,phase=number++*2.39996f,r=h*.235f;
     var center=new Vector3(p.x,p.y,p.z);
     var leaf=Color.Lerp(new Color(.29f,.41f,.24f),new Color(.40f,.50f,.29f),N(p.x,p.z,.2f,99)).linear;
     leaf.a=.95f;
     int tiers=level==0?3:2,sides=level==0?5:4;
     for(int k=0;k<tiers;k++)
     {
      float f=k/(float)tiers,baseY=h*(.20f+.54f*f),topY=h*(.78f+.22f*(k/(float)(tiers-1))),radius=r*(1-.6f*f);
      for(int j=0;j<sides;j++)
      {
       float a=phase+j*2*Mathf.PI/sides,b=phase+(j+1)*2*Mathf.PI/sides;
       var a1=center+new Vector3(Mathf.Cos(a)*radius,baseY,Mathf.Sin(a)*radius);
       var b1=center+new Vector3(Mathf.Cos(b)*radius,baseY,Mathf.Sin(b)*radius);
       tri(a1,center+Vector3.up*topY,b1,leaf);
      }
     }
     var bark=new Color(.255f,.205f,.145f).linear;bark.a=1;
     for(int j=0;j<4;j++)
     {
      float a=phase+j*Mathf.PI*.5f,b=phase+(j+1)*Mathf.PI*.5f;
      var a1=center+new Vector3(Mathf.Cos(a)*r*.10f,0,Mathf.Sin(a)*r*.10f);
      var b1=center+new Vector3(Mathf.Cos(b)*r*.10f,0,Mathf.Sin(b)*r*.10f);
      tri(a1,a1+Vector3.up*h*.55f,b1,bark);tri(b1,a1+Vector3.up*h*.55f,b1+Vector3.up*h*.55f,bark);
     }
    }
    var mesh=new Mesh{name="FoothillWoodland_LOD"+level};mesh.SetVertices(v);mesh.SetNormals(ns);mesh.SetColors(colors);mesh.SetTriangles(ts,0);mesh.RecalculateBounds();
    string path=Folder+"/"+mesh.name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if(existing==null)AssetDatabase.CreateAsset(mesh,path);else{existing.Clear();existing.indexFormat=mesh.indexFormat;existing.vertices=mesh.vertices;existing.normals=mesh.normals;existing.colors=mesh.colors;existing.triangles=mesh.triangles;existing.RecalculateBounds();UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);}
    var go=new GameObject("Woodland LOD "+level);go.transform.SetParent(group.transform,false);
    go.AddComponent<MeshFilter>().sharedMesh=mesh;var render=go.AddComponent<MeshRenderer>();render.sharedMaterial=mat;
    render.shadowCastingMode=ShadowCastingMode.Off;render.receiveShadows=true;render.lightProbeUsage=LightProbeUsage.Off;render.reflectionProbeUsage=ReflectionProbeUsage.Off;
    lods[level]=new LOD(level==0?.52f:.035f,new Renderer[]{render});
   }
   var lod=group.AddComponent<LODGroup>();lod.SetLODs(lods);lod.RecalculateBounds();
  }
  public static string Apply()
  {
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Author in edit mode.");
   if(Application.dataPath!="E:/Yeni klasör/Chick/Assets")throw new InvalidOperationException("Wrong project.");
   var root=GameObject.Find("Farm Reference Map");farm=root.transform;terrain=root.GetComponentInChildren<Terrain>();
   var shader=Shader.Find("Chick/DistantMountain");if(shader==null||!shader.isSupported)throw new InvalidOperationException("Mountain shader not ready.");
   var mat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/MountainPalette.mat");
   if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Folder+"/MountainPalette.mat");}
   mat.shader=shader;mat.SetColor("_BaseColor",Color.white);mat.SetColor("_HazeColor",new Color(.53f,.65f,.72f));mat.SetFloat("_HazeStrength",.40f);EditorUtility.SetDirty(mat);
   Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Add distant south mountain");
   var old=farm.Find(GroupName);if(old!=null)Undo.DestroyObjectImmediate(old.gameObject);
   var group=new GameObject(GroupName);Undo.RegisterCreatedObjectUndo(group,"Create mountain");group.transform.SetParent(farm,false);
   var lods=new LOD[3];int[] xs={128,64,32},zs={80,40,20};float[] thresholds={.62f,.26f,.015f};
   var report=new System.Text.StringBuilder();
   for(int i=0;i<3;i++)
   {
    var mesh=BuildMesh(xs[i],zs[i],"SouthRidge_LOD"+i);
    var go=new GameObject("Mountain LOD "+i);go.transform.SetParent(group.transform,false);
    go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;
    r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
    GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.OccludeeStatic);
    lods[i]=new LOD(thresholds[i],new Renderer[]{r});
    report.AppendLine("LOD"+i+": "+mesh.vertexCount+" vertices / "+mesh.triangles.Length/3+" triangles");
   }
   var lod=group.AddComponent<LODGroup>();lod.fadeMode=LODFadeMode.None;lod.SetLODs(lods);lod.RecalculateBounds();
   Woodland(group,mat);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);
   report.AppendLine("One material per visible LOD; zero textures, zero colliders, zero runtime behaviours. Bounds: "+group.GetComponentInChildren<MeshRenderer>().bounds);
   return report.ToString();
  }
 }
}