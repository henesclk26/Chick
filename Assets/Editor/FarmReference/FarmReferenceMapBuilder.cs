using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    public static class FarmReferenceMapBuilder
    {
        public const string RootName = "Farm Reference Map";
        const string Dir = "Assets/Art/FarmReference";
        const string Nature = "Assets/BOKI/LowPolyNature/Prefabs/models/";
        const float CenterX = 115f;
        static readonly Vector2[] Routes = {
            new Vector2(-18,12),new Vector2(-12,0), new Vector2(-12,0),new Vector2(-12,-4),
            new Vector2(-28,-4),new Vector2(28,-4), new Vector2(20,-4),new Vector2(20,17),
            new Vector2(-12,20),new Vector2(5,17), new Vector2(5,17),new Vector2(20,17),
            new Vector2(-28,-4),new Vector2(-28,-28), new Vector2(28,-4),new Vector2(28,-28),
            new Vector2(-28,-28),new Vector2(28,-28), new Vector2(0,-28),new Vector2(0,-32),
            new Vector2(-12,14),new Vector2(-27,13)
        };
        static Transform Root { get { return GameObject.Find(RootName).transform; } }
        static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/'); Folder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b-a; return Vector2.Distance(p, a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude));
        }
        public static float PathDistance(float x, float z)
        {
            float d=1000; var p=new Vector2(x,z);
            for(int i=0;i<Routes.Length;i+=2) d=Mathf.Min(d,SegmentDistance(p,Routes[i],Routes[i+1]));
            return d;
        }
        public static float Height(float x,float z)
        {
            float outside=Mathf.Max(Mathf.Abs(x)-35, z-36, -32-z);
            if(outside>0)
            {
                float ramp=Mathf.SmoothStep(0,1,Mathf.Clamp01(outside/23));
                float back=Mathf.Lerp(5f,17f,Mathf.InverseLerp(-45,45,z));
                float noise=Mathf.PerlinNoise((x+132)*.042f,(z+98)*.042f);
                return .25f+ramp*(back*(.45f+noise)+3*Mathf.Sin(x*.08f)*Mathf.Sin(z*.08f));
            }
            float h=.25f+.28f*Mathf.Sin(x*.16f)*Mathf.Sin(z*.19f)+.14f*Mathf.Sin((x+z)*.3f);
            h+=.6f*Mathf.Exp(-((x+28)*(x+28)+(z-7)*(z-7))/35f);
            h-=.38f*Mathf.Exp(-((x-28)*(x-28)+(z-26)*(z-26))/22f);
            float barnPad=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,3,Mathf.Max(Mathf.Abs(x+18)-7,Mathf.Abs(z-22)-10)));
            float coopPad=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(5,8,Vector2.Distance(new Vector2(x,z),new Vector2(20,20))));
            float fieldPad=(x>-27&&x<27&&z>-28&&z<-6)?1:0;
            float path=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.2f,3,PathDistance(x,z)));
            float fence=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,2,Mathf.Abs(outside)));
            return Mathf.Lerp(h,.25f,Mathf.Max(barnPad,coopPad,fieldPad,path,fence));
        }
        static Vector3 At(float x,float z) { return new Vector3(CenterX+x,Height(x,z),z); }
        static Material Mat(string name,Color color)
        {
            string path=Dir+"/Generated/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path); if(m) return m;
            m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=name;m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.12f);m.enableInstancing=true;
            AssetDatabase.CreateAsset(m,path);return m;
        }
        static Bounds BoundsOf(GameObject o)
        {
            var rs=o.GetComponentsInChildren<Renderer>(); var b=rs[0].bounds;
            for(int i=1;i<rs.Length;i++)b.Encapsulate(rs[i].bounds);return b;
        }
        static GameObject Place(string asset,Transform parent,string name,float x,float z,float longest,float yaw=0)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(asset);if(!source)throw new Exception("Missing "+asset);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(source,parent);go.name=name;
            go.transform.rotation=Quaternion.Euler(0,yaw,0)*source.transform.rotation;go.transform.position=Vector3.zero;
            var b=BoundsOf(go);go.transform.localScale*=longest/Mathf.Max(b.size.x,b.size.y,b.size.z);b=BoundsOf(go);
            go.transform.position+=At(x,z)-new Vector3(b.center.x,b.min.y,b.center.z);return go;
        }
        static GameObject FarmPart(string node,Transform parent,string name,float x,float z,float longest,float yaw=0,float yStretch=1)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/low_poly_farm_v2.glb");MeshRenderer renderer=null;
            foreach(var r in source.GetComponentsInChildren<MeshRenderer>(true))if(r.name==node){renderer=r;break;}
            if(!renderer)throw new Exception("Missing farm part "+node);
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=renderer.GetComponent<MeshFilter>().sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials;
            go.transform.rotation=Quaternion.Euler(0,yaw,0)*renderer.transform.rotation;
            go.transform.localScale=renderer.transform.lossyScale;go.transform.position=Vector3.zero;
            var b=BoundsOf(go);go.transform.localScale*=longest/Mathf.Max(b.size.x,b.size.y,b.size.z);
            var s=go.transform.localScale;s.y*=yStretch;go.transform.localScale=s;b=BoundsOf(go);
            go.transform.position+=At(x,z)-new Vector3(b.center.x,b.min.y,b.center.z);return go;
        }
        static GameObject Box(string name,Transform parent,Vector3 local,Vector3 size,Material mat,bool collision=true)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=local;g.transform.localScale=size;
            g.GetComponent<Renderer>().sharedMaterial=mat;if(!collision)UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());return g;
        }
        public static string Prepare()
        {
            if(GameObject.Find(RootName))throw new Exception("Farm root already exists; no duplicate generated.");
            Folder(Dir+"/Generated");Folder(Dir+"/Terrain");
            var root=new GameObject(RootName);root.transform.position=new Vector3(CenterX,0,0);Undo.RegisterCreatedObjectUndo(root,"Create farm reference map");
            Group("01 Barn Yard",root.transform);Group("02 Crop Fields",root.transform);Group("03 Chicken Yard",root.transform);
            Group("04 Fences and Gates",root.transform);Group("05 Nature and Scenery",root.transform);Group("06 Exterior Boundary",root.transform);
            var data=new TerrainData();data.name="Farm Sculpted Terrain";data.heightmapResolution=513;data.alphamapResolution=512;data.baseMapResolution=1024;data.size=new Vector3(170,28,160);
            var heights=new float[513,513];for(int iz=0;iz<513;iz++)for(int ix=0;ix<513;ix++)heights[iz,ix]=(Height(ix*170f/512-85,iz*160f/512-80)+2)/28;
            data.SetHeights(0,0,heights);
            string lp="Assets/BOKI/LowPolyNature/Terrain/layers/";
            var grass=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<TerrainLayer>(lp+"Ground_Grass_Layer.terrainlayer"));grass.name="Farm Grass";grass.tileSize=new Vector2(7,7);grass.normalScale=.25f;
            var dirt=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<TerrainLayer>(lp+"Ground_Dirt_Layer.terrainlayer"));dirt.name="Farm Paths";dirt.tileSize=new Vector2(5,5);dirt.normalScale=.25f;
            var rocky=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<TerrainLayer>(lp+"Ground_Grass_Rocks_Layer.terrainlayer"));rocky.name="Farm Hills";rocky.tileSize=new Vector2(10,10);rocky.normalScale=.3f;
            AssetDatabase.CreateAsset(grass,Dir+"/Terrain/Grass.terrainlayer");AssetDatabase.CreateAsset(dirt,Dir+"/Terrain/Paths.terrainlayer");AssetDatabase.CreateAsset(rocky,Dir+"/Terrain/Hills.terrainlayer");data.terrainLayers=new[]{grass,dirt,rocky};
            var alpha=new float[512,512,3];for(int iz=0;iz<512;iz++)for(int ix=0;ix<512;ix++){
                float x=ix*170f/511-85,z=iz*160f/511-80;float edge=Mathf.Max(Mathf.Abs(x)-35,z-36,-32-z);
                float d=PathDistance(x,z);float noise=(Mathf.PerlinNoise(x*1.1f+57,z*1.1f+84)-.5f)*.4f;
                float path=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.15f,1.85f,d+noise)))*(edge<0?1:0);
                float rock=Mathf.Clamp01((edge-12)/30)*.38f;alpha[iz,ix,0]=(1-path)*(1-rock);alpha[iz,ix,1]=path;alpha[iz,ix,2]=(1-path)*rock;
            }AssetDatabase.CreateAsset(data,Dir+"/Terrain/FarmTerrain.asset");data.SetAlphamaps(0,0,alpha);EditorUtility.SetDirty(data);
            grass.diffuseRemapMax=new Vector4(.5f,.65f,.55f,1);dirt.diffuseRemapMax=new Vector4(1.15f,.95f,.7f,1);rocky.diffuseRemapMax=new Vector4(.5f,.65f,.55f,1);
            EditorUtility.SetDirty(grass);EditorUtility.SetDirty(dirt);EditorUtility.SetDirty(rocky);
            var terrainGo=Terrain.CreateTerrainGameObject(data);terrainGo.name="Terrain - gentle interior and perimeter hills";terrainGo.transform.SetParent(root.transform);terrainGo.transform.position=new Vector3(CenterX-85,-2,-80);
            var terrain=terrainGo.GetComponent<Terrain>();terrain.heightmapPixelError=3;terrain.basemapDistance=220;terrain.drawInstanced=true;terrain.materialTemplate=new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));AssetDatabase.CreateAsset(terrain.materialTemplate,Dir+"/Terrain/TerrainMaterial.mat");
            var barn=Place("Assets/Art/Barn/LowPolyFarmBarn.fbx",root.transform.Find("01 Barn Yard"),"Barn - separated animated door leaves",-18,22,17,180);
            foreach(var f in barn.GetComponentsInChildren<MeshFilter>())if(!f.GetComponent<Collider>())f.gameObject.AddComponent<MeshCollider>().sharedMesh=f.sharedMesh;
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);return "Terrain and imported barn created at X=115; existing scene area preserved.";
        }
        static void FenceLine(Vector2 a,Vector2 b,Transform parent,string name)
        {
            float len=Vector2.Distance(a,b);int count=Mathf.CeilToInt(len/2.9f);float yaw=-Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Nature+"wooden_fence_1.prefab");
            for(int i=0;i<count;i++){
                Vector2 p=Vector2.Lerp(a,b,(i+.5f)/count);var g=(GameObject)PrefabUtility.InstantiatePrefab(source,parent);g.name=name+" "+(i+1).ToString("00");
                g.transform.rotation=Quaternion.Euler(0,yaw,0);g.transform.localScale=new Vector3(len/count/2.49f,1.3f,1.3f);g.transform.position=Vector3.zero;
                var bounds=BoundsOf(g);g.transform.position+=At(p.x,p.y)-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            }
            // Each fence prefab owns its collider so manual edits cannot leave an invisible wall behind.
        }
        static void Gate(string name,float x,float z,float yaw)
        {
            var g=Group(name,Root.Find("04 Fences and Gates"));g.position=At(x,z);g.rotation=Quaternion.Euler(0,yaw,0);
            Material wood=Mat("Warm Gate Timber",new Color(.38f,.20f,.075f)),dark=Mat("Gate Iron",new Color(.12f,.14f,.13f));
            for(int side=-1;side<=1;side+=2)Box("Gate Post",g,new Vector3(side*2.5f,.85f,0),new Vector3(.25f,1.7f,.25f),wood);
            var hinge=Group("Hinge - open 65 degrees",g);hinge.localPosition=new Vector3(-2.35f,0,0);hinge.localRotation=Quaternion.Euler(0,-65,0);
            Box("Top Rail",hinge,new Vector3(2.25f,1.3f,0),new Vector3(4.5f,.16f,.15f),wood,false);
            Box("Bottom Rail",hinge,new Vector3(2.25f,.4f,0),new Vector3(4.5f,.16f,.15f),wood,false);
            for(int side=0;side<=1;side++)Box("Leaf Upright",hinge,new Vector3(side*4.5f,.85f,0),new Vector3(.16f,1.15f,.16f),wood,false);
            var brace=Box("Diagonal Brace",hinge,new Vector3(2.25f,.85f,0),new Vector3(4.56f,.13f,.13f),wood,false);brace.transform.localRotation=Quaternion.Euler(0,0,11.3f);
            Box("Iron Hinge",hinge,new Vector3(.08f,1.2f,-.1f),new Vector3(.35f,.1f,.1f),dark,false);
            var bc=hinge.gameObject.AddComponent<BoxCollider>();bc.center=new Vector3(2.25f,.85f,0);bc.size=new Vector3(4.6f,1.4f,.2f);
        }
        public static string BuildZones()
        {
            var fences=Root.Find("04 Fences and Gates");if(fences.childCount>0)throw new Exception("Zones already built");
            FenceLine(new Vector2(-35,-32),new Vector2(35,-32),fences,"Outer South");FenceLine(new Vector2(35,-32),new Vector2(35,36),fences,"Outer East");FenceLine(new Vector2(35,36),new Vector2(-35,36),fences,"Outer North");FenceLine(new Vector2(-35,36),new Vector2(-35,-32),fences,"Outer West");
            FenceLine(new Vector2(-35,0),new Vector2(-14.5f,0),fences,"Barn to field");FenceLine(new Vector2(-9.5f,0),new Vector2(17.5f,0),fences,"Field divider");FenceLine(new Vector2(22.5f,0),new Vector2(35,0),fences,"Coop to field");
            FenceLine(new Vector2(5,0),new Vector2(5,14.5f),fences,"Barn to coop");FenceLine(new Vector2(5,19.5f),new Vector2(5,36),fences,"Barn to coop north");
            Gate("Gate 1 - Barn to Field",-12,0,0);Gate("Gate 2 - Field to Chicken Yard",20,0,0);Gate("Gate 3 - Barn to Chicken Yard",5,17,-90);
            // No artificial exterior boundary; retain only collisions belonging to visible scenery.
            var yard=Root.Find("01 Barn Yard");FarmPart("Cube.011_0",yard,"Tractor - farm asset",-27,10,4.6f,125).AddComponent<MeshCollider>();
            FarmPart("Plane.000_0",yard,"Windmill - farm asset",-29,27,11,0);
            for(int i=0;i<8;i++){var bale=FarmPart("Cube.001_0",yard,"Hay Bale "+(i+1),-8+(i%3)*1.3f,25+(i/3)*1.4f,1.3f,90);bale.AddComponent<BoxCollider>();}
            var field=Root.Find("02 Crop Fields");for(int row=0;row<2;row++)for(int col=0;col<6;col++){
                float x=-23+col*9.2f,z=-11.5f-row*10.5f;string node=col<3?"Cube.009_0":"Cube.010_0";
                FarmPart(node,field,(col<3?"Pumpkin":"Corn")+" Bed "+row+"-"+col,x,z,9,0,.6f);
            }
            FarmPart("Plane_0",field,"Scarecrow - farm asset",1,-6,2.5f,180);
            for(int i=0;i<10;i++){float angle=i*2.39996f;float radius=2.6f+(i%3)*1.25f;float x=20+Mathf.Cos(angle)*radius,z=18+Mathf.Sin(angle)*radius;FarmPart(i%2==0?"Cylinder.000_0":"Cylinder.001_0",Root.Find("03 Chicken Yard"),"Chicken "+(i+1),x,z,.8f,i*47);}
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(Root.gameObject.scene);return "Three fenced zones, three open hinged gates, crops, tractor, windmill, hay and chickens created.";
        }
        public static string Landscape()
        {
            var scenery=Root.Find("05 Nature and Scenery");if(scenery.childCount>0)throw new Exception("Landscape already populated");
            var rng=new System.Random(9216);Func<float,float,float> random=(a,b)=>a+(float)rng.NextDouble()*(b-a);
            for(int i=0;i<125;i++){
                float x=random(-69,69),z=random(-61,66);float outside=Mathf.Max(Mathf.Abs(x)-35,z-36,-32-z);
                if(outside<5){i--;continue;}if(z<-38&&Mathf.Abs(x)<26&&rng.NextDouble()<.8){i--;continue;}
                bool pine=i%3!=0;Place(Nature+(pine?"tree_pine":"tree_normal")+".prefab",scenery,"Scenery Tree "+i,x,z,random(pine?5.5f:7,pine?10:12),random(0,360));
            }
            Vector2[] insideTrees={new Vector2(-30,-25),new Vector2(-31,-8),new Vector2(31,-22),new Vector2(31,-9),new Vector2(-30,3),new Vector2(-4,29),new Vector2(1,32),new Vector2(10,30),new Vector2(30,28),new Vector2(30,10),new Vector2(10,8),new Vector2(-30,33)};
            for(int i=0;i<insideTrees.Length;i++){var p=insideTrees[i];Place(Nature+(i%3==0?"tree_birch":"tree_normal")+".prefab",scenery,"Yard Tree "+i,p.x,p.y,i%3==0?5.5f:7.5f,random(0,360));}
            for(int i=0;i<85;i++){
                float x=random(-72,72),z=random(-63,67);float outside=Mathf.Max(Mathf.Abs(x)-35,z-36,-32-z);if(outside<3){i--;continue;}
                Place(Nature+"rock_"+(1+i%10)+".prefab",scenery,"Scenery Rock "+i,x,z,random(1.5f,4),random(0,360));
            }
            for(int i=0;i<330;i++){
                float x=random(-40,40),z=random(-37,41);if(PathDistance(x,z)<2.2f){i--;continue;}
                bool field=x>-27&&x<27&&z>-28&&z<-6;bool barn=Mathf.Abs(x+18)<7&&Mathf.Abs(z-22)<11;bool coop=Vector2.Distance(new Vector2(x,z),new Vector2(20,20))<6;
                if(field||barn||coop){i--;continue;}
                string part=i%6==0?"flower_"+(1+i%5):"grass_"+(1+i%8);Place(Nature+part+".prefab",scenery,"Meadow "+i,x,z,random(.35f,.85f),random(0,360));
            }
            for(int i=0;i<30;i++){
                float x=random(-52,52),z=random(-48,50);float edge=Mathf.Max(Mathf.Abs(x)-35,z-36,-32-z);if(edge<2||edge>14){i--;continue;}
                Place(Nature+"bush_1.prefab",scenery,"Boundary Shrub "+i,x,z,random(1.4f,2.6f),random(0,360));
            }
            var camGo=new GameObject("Farm Overview Camera - preview only");camGo.transform.SetParent(Root);camGo.transform.position=new Vector3(CenterX+49,75,-91);camGo.transform.LookAt(new Vector3(CenterX,1,2));
            var cam=camGo.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.orthographicSize=47;cam.nearClipPlane=.3f;cam.farClipPlane=350;cam.clearFlags=CameraClearFlags.Skybox;
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(Root.gameObject.scene);Frame();return "Landscaping complete; preview camera is disabled to preserve the gameplay camera.";
        }
        [MenuItem("Tools/Farm Reference/Frame Map")]
        public static void Frame()
        {
            Selection.activeGameObject=Root.gameObject;
            var sv=SceneView.lastActiveSceneView;if(sv){sv.sceneLighting=true;sv.orthographic=true;sv.LookAtDirect(new Vector3(CenterX,1,2),Quaternion.Euler(39,-28,0),39);sv.Repaint();}
        }
    }
}
