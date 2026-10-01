using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FarmReferenceTools
{
    // Explicit editor authoring pass. Runtime receives only painted TerrainData and instanced details.
    // Starts from the preserved SouthGrass terrain, never regenerates heights or existing grass.
    public static class FarmGroundWearV1
    {
        public const string Source = "Assets/Art/Terrain/NaturalHillsV4/FarmTerrain_NaturalHills_v4_SouthGrass.asset";
        public const string Target = "Assets/Art/Terrain/GroundWearV1/FarmTerrain_GroundWear_v1.asset";
        static readonly Vector4[] Fences = {
            new Vector4(-35,-32,35,-32), new Vector4(-35,36,35,36),
            new Vector4(-35,-32,-35,36), new Vector4(35,-32,35,36),
            new Vector4(-35,0,-14.5f,0), new Vector4(-9.5f,0,17.5f,0),
            new Vector4(22.5f,0,35,0), new Vector4(5,0,5,14.5f), new Vector4(5,19.5f,5,36)
        };
        static readonly Vector2[] Gates = {new Vector2(-12,0),new Vector2(20,0),new Vector2(5,17)};
        static readonly float[] EdgeMix = {.03f,.14f,.05f,0,.05f,.05f,0,.68f};
        static readonly float[] TrampledMix = {0,0,0,0,.71f,.17f,0,.12f};
        static readonly float[] DustMix = {.01f,.04f,.02f,0,.44f,.33f,0,.16f};
        static readonly float[] TreeMix = {.02f,.50f,0,0,.04f,.33f,0,.11f};
        static readonly float[] LushMix = {.04f,.64f,.26f,0,0,0,0,.06f};

        static float Smooth(float a,float b,float v) { return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v)); }
        static float Noise(Vector2 p,float scale,float ox,float oz) { return Mathf.PerlinNoise(p.x*scale+ox,p.y*scale+oz); }
        static float SegmentDistance(Vector2 p,Vector4 s)
        {
            var a=new Vector2(s.x,s.y); var v=new Vector2(s.z-s.x,s.w-s.y);
            return Vector2.Distance(p,a+v*Mathf.Clamp01(Vector2.Dot(p-a,v)/Mathf.Max(.0001f,v.sqrMagnitude)));
        }
        static float FenceDistance(Vector2 p)
        {
            float d=100;
            foreach(var s in Fences)d=Mathf.Min(d,SegmentDistance(p,s));
            return d;
        }
        static float GateClearance(Vector2 p)
        {
            float d=100;
            foreach(var g in Gates)d=Mathf.Min(d,Vector2.Distance(g,p));
            return d;
        }
        static bool Crop(Vector2 p)
        {
            // Current mixed fields, including their irregular soil edges and wheat service lane.
            return p.y>=-25.8f && p.y<=-8.0f &&
                ((p.x>=-28.5f && p.x<=-2.2f) || (p.x>=.6f && p.x<=27.0f));
        }
        static bool Interior(Vector2 p) { return p.x>-34.95f && p.x<34.95f && p.y>-31.95f && p.y<35.95f; }
        static float Ellipse(Vector2 p,float x,float z,float rx,float rz,float seed)
        {
            var d=new Vector2((p.x-x)/rx,(p.y-z)/rz);
            float ragged=d.magnitude+(Noise(p,.66f,seed,seed*.37f)-.5f)*.29f;
            return 1-Smooth(.50f,1.12f,ragged);
        }
        static float BarnWear(Vector2 p)
        {
            float apron=Ellipse(p,-17.8f,13.55f,4.0f,2.65f,43);
            float side=Ellipse(p,-12.8f,21.55f,2.4f,4.7f,12);
            float approach=Mathf.Min(SegmentDistance(p,new Vector4(-12.6f,19.1f,-11.7f,17.3f)),
                                    SegmentDistance(p,new Vector4(-11.7f,17.3f,-9.2f,15.8f)));
            float trail=(1-Smooth(.48f,1.35f,approach+(Noise(p,1.2f,11,15)-.5f)*.22f))*.72f;
            return Mathf.Max(apron,Mathf.Max(side,trail));
        }
        static float CoopWear(Vector2 p)
        {
            float yard=Ellipse(p,21.7f,19.6f,5.4f,4.4f,61)*.74f;
            float ramp=Ellipse(p,21.9f,19.7f,1.9f,2.3f,71);
            float flock=Ellipse(p,16.9f,21.1f,2.7f,2.1f,83)*.62f;
            float feed=Ellipse(p,27,17,2.3f,1.9f,27)*.9f;
            float m=Mathf.Max(yard,Mathf.Max(ramp,Mathf.Max(flock,feed)));
            return Mathf.Clamp01(m*(.63f+.54f*Noise(p,1.0f,86,32)));
        }
        static bool Building(Vector2 p)
        {
            return (p.x>-22.9f && p.x<-13.0f && p.y>13.35f && p.y<31.0f) ||
                   (p.x>17.7f && p.x<24.8f && p.y>20.4f && p.y<26.7f) ||
                   (p.x>-8.9f && p.x<-5.1f && p.y>28.1f && p.y<31.9f) ||
                   (p.x>25.5f && p.x<28.5f && p.y>16.3f && p.y<17.7f);
        }
        static List<Vector3> TreeBases(Transform root)
        {
            var result=new List<Vector3>();
            foreach(var r in root.GetComponentsInChildren<Renderer>())
            {
                if(r.name.IndexOf("tree",StringComparison.OrdinalIgnoreCase)<0)continue;
                var p=root.InverseTransformPoint(r.transform.position);
                if(!Interior(new Vector2(p.x,p.z)))continue;
                float radius=Mathf.Clamp(Mathf.Max(r.bounds.size.x,r.bounds.size.z)*.29f,1.2f,2.3f);
                result.Add(new Vector3(p.x,p.z,radius));
            }
            return result;
        }
        static float TreeMask(Vector2 p,List<Vector3> trees)
        {
            float mask=0;
            foreach(var tr in trees)
            {
                float distance=Vector2.Distance(p,new Vector2(tr.x,tr.y));
                float edge=distance/tr.z+(Noise(p,1.4f,33,53)-.5f)*.23f;
                mask=Mathf.Max(mask,1-Smooth(.16f,1.28f,edge));
            }
            return mask;
        }
        static void Blend(float[,,] a,int z,int x,float amount,float[] mix)
        {
            amount=Mathf.Clamp01(amount);
            for(int k=0;k<8;k++)a[z,x,k]=Mathf.Lerp(a[z,x,k],mix[k],amount);
        }

        public static string Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode before authoring ground.");
            if(!Application.dataPath.Replace('\\','/').Equals("E:/Yeni klasör/Chick/Assets",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This pass belongs to E:/Yeni klasör/Chick.");
            var root=GameObject.Find("Farm Reference Map");
            if(root==null)throw new InvalidOperationException("Farm root missing.");
            var terrain=root.GetComponentInChildren<Terrain>();
            var original=AssetDatabase.LoadAssetAtPath<TerrainData>(Source);
            if(terrain==null || original==null || original.alphamapLayers!=8 || original.detailPrototypes.Length!=1)
                throw new InvalidOperationException("Unexpected source terrain; inspect before applying.");
            string current=AssetDatabase.GetAssetPath(terrain.terrainData);
            if(current!=Source && current!=Target)throw new InvalidOperationException("Current terrain has changed; inspect before applying.");
            if(!AssetDatabase.IsValidFolder("Assets/Art/Terrain/GroundWearV1"))
                AssetDatabase.CreateFolder("Assets/Art/Terrain","GroundWearV1");
            var data=AssetDatabase.LoadAssetAtPath<TerrainData>(Target);
            if(data==null)
            {
                if(!AssetDatabase.CopyAsset(Source,Target))throw new InvalidOperationException("Could not preserve and copy source terrain.");
                data=AssetDatabase.LoadAssetAtPath<TerrainData>(Target);
            }
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Ground wear and fence vegetation");
            Undo.RegisterCompleteObjectUndo(data,"Paint ground use");
            var trees=TreeBases(root.transform);
            int w=original.alphamapWidth,h=original.alphamapHeight;
            var alpha=original.GetAlphamaps(0,0,w,h);
            int painted=0;
            for(int z=0;z<h;z++)for(int x=0;x<w;x++)
            {
                var wp=terrain.transform.position+new Vector3(x/(float)(w-1)*data.size.x,0,z/(float)(h-1)*data.size.z);
                var lp=root.transform.InverseTransformPoint(wp);var p=new Vector2(lp.x,lp.z);
                if(!Interior(p)||Crop(p))continue;
                float core,shoulder;OrganicFarmPaths.Evaluate(p,out core,out shoulder);
                float n=Noise(p,.83f,17,94),band=0;
                // Sample adjacent path core for a wider broken transition on otherwise hard edges.
                if(core<.98f)
                {
                    float c,s;
                    OrganicFarmPaths.Evaluate(p+new Vector2(.72f,0),out c,out s);band+=c;
                    OrganicFarmPaths.Evaluate(p-new Vector2(.72f,0),out c,out s);band+=c;
                    OrganicFarmPaths.Evaluate(p+new Vector2(0,.72f),out c,out s);band+=c;
                    OrganicFarmPaths.Evaluate(p-new Vector2(0,.72f),out c,out s);band+=c;
                }
                float edge=Mathf.Max(shoulder*.76f,band*.29f)*(1-core)*(.63f+.52f*n);
                float tree=TreeMask(p,trees)*(1-core*.91f);
                float fence=(1-Smooth(.28f,1.4f,FenceDistance(p)))*Smooth(2.8f,3.5f,GateClearance(p));
                float barn=BarnWear(p),coop=CoopWear(p);
                if(edge+tree+fence+barn+coop+core<.004f)continue;
                Blend(alpha,z,x,edge,EdgeMix);
                Blend(alpha,z,x,core*.23f,TrampledMix);
                Blend(alpha,z,x,tree*.9f,TreeMix);
                Blend(alpha,z,x,fence*(.24f+.30f*Noise(p,.42f,77,32))*(1-core),LushMix);
                Blend(alpha,z,x,barn*(.77f+.17f*n),TrampledMix);
                Blend(alpha,z,x,Mathf.Pow(coop,.78f)*.96f*(1-core*.35f),DustMix);
                float total=0;for(int k=0;k<8;k++)total+=alpha[z,x,k];
                for(int k=0;k<8;k++)alpha[z,x,k]/=total;
                painted++;
            }
            data.SetAlphamaps(0,0,alpha);data.SetBaseMapDirty();

            // A second, smaller prototype uses the same mesh/material and leaves the original south layer intact.
            var tuft=new DetailPrototype(original.detailPrototypes[0]);
            tuft.minWidth=.65f;tuft.maxWidth=1.04f;tuft.minHeight=.50f;tuft.maxHeight=.83f;
            tuft.noiseSeed=7483;tuft.useInstancing=true;tuft.positionJitter=.76f;tuft.alignToGround=.12f;
            data.detailPrototypes=new[]{new DetailPrototype(original.detailPrototypes[0]),tuft};
            int res=original.detailResolution;
            data.SetDetailLayer(0,0,0,original.GetDetailLayer(0,0,res,res,0));
            var detail=new int[res,res];var rng=new System.Random(42923);int fenceTufts=0,coopTufts=0;
            for(int z=0;z<res;z++)for(int x=0;x<res;x++)
            {
                var wp=terrain.transform.position+new Vector3((x+.5f)/res*data.size.x,0,(z+.5f)/res*data.size.z);
                var lp=root.transform.InverseTransformPoint(wp);var p=new Vector2(lp.x,lp.z);
                if(!Interior(p)||Crop(p)||Building(p)||GateClearance(p)<3.15f)continue;
                float core,shoulder;OrganicFarmPaths.Evaluate(p,out core,out shoulder);
                if(core>.025f||shoulder>.38f||BarnWear(p)>.18f)continue;
                float d=FenceDistance(p),patch=Noise(p,.51f,61,19);
                float fence=(1-Smooth(.22f,.95f,d))*(.30f+.70f*Smooth(.29f,.66f,patch));
                int count=0;
                if(fence>.03f)
                {
                    if(rng.NextDouble()<fence*.89f)count++;
                    if(rng.NextDouble()<fence*.47f)count++;
                    fenceTufts+=count;
                }
                else if(p.x>13.5f && p.x<29 && p.y>15.3f && p.y<27.5f && TreeMask(p,trees)<.68f)
                {
                    float wear=CoopWear(p);
                    float sparse=.13f*Smooth(.4f,.66f,Noise(p,.75f,11,74))*(1-wear*.90f);
                    if(rng.NextDouble()<sparse){count=1;coopTufts++;}
                }
                detail[z,x]=count;
            }
            data.SetDetailLayer(0,0,1,detail);
            EditorUtility.SetDirty(data);AssetDatabase.SaveAssetIfDirty(data);
            Undo.RecordObject(terrain,"Use detailed ground terrain");terrain.terrainData=data;
            var collider=terrain.GetComponent<TerrainCollider>();
            if(collider!=null){Undo.RecordObject(collider,"Match ground collider");collider.terrainData=data;}
            terrain.Flush();EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            return "Ground wear applied: "+painted+" painted samples; "+trees.Count+" tree bases; "+
                fenceTufts+" fence tufts; "+coopTufts+" sparse coop tufts. Original terrain preserved at "+Source;
        }
    }
}
