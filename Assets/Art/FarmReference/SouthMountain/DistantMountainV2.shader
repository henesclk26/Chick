Shader "Chick/DistantMountainV2"
{
 // Procedural, texture-free surface for the distant south ridge: meadow tones, forest stands,
 // lowland field patchwork, stratified rock on steep faces and a fine detail normal.
 // V1 (Chick/DistantMountain) is kept untouched so the original look can be restored.
 Properties {
  _MeadowLight("Meadow light",Color)=(0.43,0.58,0.24,1)
  _MeadowDark("Meadow dark",Color)=(0.26,0.41,0.17,1)
  _AlpineGrass("High grass",Color)=(0.46,0.53,0.31,1)
  _DryGrass("Sunny dry grass",Color)=(0.63,0.61,0.34,1)
  _ForestColor("Forest canopy",Color)=(0.08,0.17,0.10,1)
  _RockLight("Rock light",Color)=(0.62,0.60,0.54,1)
  _RockDark("Rock dark",Color)=(0.35,0.34,0.32,1)
  _ForestAmount("Forest stands",Range(0,1))=1
  _RockAmount("Exposed rock",Range(0,1))=1
  _Patchwork("Lowland field patchwork",Range(0,1))=0.8
  _DetailStrength("Surface detail",Range(0,2))=1
  _VertexColorBlend("Original palette blend",Range(0,1))=0.12
  _HazeColor("Distant air tint",Color)=(0.53,0.65,0.72,1)
  _HazeStrength("Atmospheric depth",Range(0,0.6))=0.40
 }
 SubShader {
  Tags{"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
  Cull Back ZWrite On
  HLSLINCLUDE
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  CBUFFER_START(UnityPerMaterial)
   half4 _MeadowLight,_MeadowDark,_AlpineGrass,_DryGrass,_ForestColor,_RockLight,_RockDark,_HazeColor;
   half _ForestAmount,_RockAmount,_Patchwork,_DetailStrength,_VertexColorBlend,_HazeStrength;
  CBUFFER_END
  ENDHLSL
  Pass {
   Name "ForwardLit"
   Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   struct A {float4 p:POSITION;float3 n:NORMAL;half4 c:COLOR;};
   struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;half3 n:TEXCOORD1;float3 obj:TEXCOORD2;half4 c:COLOR;half fog:TEXCOORD3;};
   V vert(A a){V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(a.n);o.obj=a.p.xyz;o.c=a.c;o.fog=ComputeFogFactor(o.p.z);return o;}

   float Hash(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
   float2 Hash2(float2 p){return float2(Hash(p),Hash(p+19.19));}
   float Noise(float2 p)
   {
    float2 i=floor(p),f=frac(p),u=f*f*(3-2*f);
    return lerp(lerp(Hash(i),Hash(i+float2(1,0)),u.x),lerp(Hash(i+float2(0,1)),Hash(i+1),u.x),u.y);
   }
   // Octaves fade out once they get smaller than a pixel, so the far ridge does not shimmer.
   float Fbm(float2 p,float footprint)
   {
    float sum=0,amp=.5,freq=1;
    const float2x2 rot=float2x2(.8,-.6,.6,.8);
    [unroll] for(int k=0;k<5;k++)
    {
     sum+=amp*saturate(1.5-footprint*freq*2)*(Noise(p)-.5);
     p=mul(rot,p)*2.03+17.1;freq*=2.03;amp*=.5;
    }
    return .5+sum;
   }
   // x: random value of the nearest cell, y: distance to the cell border.
   float2 Cells(float2 p)
   {
    float2 n=floor(p),f=frac(p);float d1=8,d2=8,id=0;
    [unroll] for(int j=-1;j<=1;j++)[unroll] for(int i=-1;i<=1;i++)
    {
     float2 g=float2(i,j),r=g+Hash2(n+g)*.85-f;float d=dot(r,r);
     if(d<d1){d2=d1;d1=d;id=Hash(n+g+3.7);}else if(d<d2)d2=d;
    }
    return float2(id,sqrt(d2)-sqrt(d1));
   }

   half4 frag(V i):SV_Target {
    float2 xz=i.obj.xz;float h=i.obj.y;
    float dist=distance(_WorldSpaceCameraPos,i.world);
    float fp=max(fwidth(xz.x),fwidth(xz.y));
    half3 geo=normalize(i.n);
    half slope=1-geo.y;
    // Vertex alpha darkens concave gullies (V1 bakes the ridge laplacian there).
    half concave=saturate((1-i.c.a)/.21);

    float macro=Fbm(xz*.006,fp*.006);
    float mid=Fbm(xz*.03+31,fp*.03);
    float fine=Fbm(xz*.16+7,fp*.16);

    // Meadow: broad light/dark swathes, cooler grass higher up, dry sunny grass on open ground.
    half3 grass=lerp(_MeadowDark.rgb,_MeadowLight.rgb,saturate(macro*1.35-.18+(mid-.5)*.6));
    grass=lerp(grass,_AlpineGrass.rgb,smoothstep(45,110,h+(mid-.5)*25));
    grass=lerp(grass,_DryGrass.rgb,smoothstep(.58,.8,mid+(macro-.5)*.4)*.35*(1-concave));
    // Steeper grass reads as rougher, shrubby ground.
    grass=lerp(grass,_MeadowDark.rgb*.82,smoothstep(.1,.26,slope)*.45);
    half3 albedo=grass;

    // Field patchwork with darker hedgerow edges on low, gentle foothills.
    half lowland=(1-smoothstep(14,36,h+(macro-.5)*16))*(1-smoothstep(.05,.2,slope))*_Patchwork;
    float2 cell=Cells(xz*.03+float2(mid,macro)*1.6);
    half3 field=grass*lerp(.76,1.14,cell.x);
    field=lerp(field,_DryGrass.rgb*.95,step(.86,cell.x)*.4);
    half hedge=1-smoothstep(.02,.07+fp*.06,cell.y);
    field=lerp(field,_ForestColor.rgb*1.15,hedge*.3*saturate(1.6-fp*.9));
    albedo=lerp(albedo,field,lowland);

    // Forest stands in mid altitudes, thicker in gullies, clumped canopy.
    half forest=smoothstep(.45,.5,Fbm(xz*.011+73,fp*.011)+concave*.12-slope*.25+(fine-.5)*.12);
    forest*=(1-smoothstep(52,76,h+(mid-.5)*20))*smoothstep(6,16,h)*_ForestAmount;
    // Clumped canopy: individual crowns read as light/dark speckles up close.
    float crowns=Fbm(xz*.45+11,fp*.45);
    albedo=lerp(albedo,_ForestColor.rgb*lerp(.6,1.35,saturate(fine*.5+crowns*.8-.15)),forest);

    // Exposed, layered rock on steep faces and the highest crests.
    half rock=smoothstep(.17,.32,slope+(mid-.5)*.22+(fine-.5)*.1)*smoothstep(25,55,h);
    rock=saturate(max(rock,smoothstep(70,105,h+(mid-.5)*30)*.85)*_RockAmount);
    half strata=sin(h*.55+mid*7+xz.x*.012)*.5+.5;
    half3 stone=lerp(_RockDark.rgb,_RockLight.rgb,saturate(strata*.55+fine*.65-.12))*lerp(1,.74,concave);
    albedo=lerp(albedo,stone,rock);

    albedo*=.9+.2*fine;
    albedo=lerp(albedo,i.c.rgb,_VertexColorBlend);

    // Fine surface detail as a bumped normal: strong on rock, gentle on grass, gone in the far distance.
    float bf=.12,e=.7,bfp=fp*bf;
    float b0=Fbm(xz*bf,bfp),bx=Fbm((xz+float2(e,0))*bf,bfp),bz=Fbm((xz+float2(0,e))*bf,bfp);
    half bump=lerp(2.6,5.5,rock)*_DetailStrength*(1-smoothstep(260,520,dist));
    half3 n=normalize(geo-half3(bx-b0,0,bz-b0)/e*bump);

    Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
    half wrap=saturate((dot(n,sun.direction)+.1)/1.1);
    half ao=lerp(.7,1,1-concave);
    half3 ambient=max(SampleSH(n),half3(.028,.033,.04))*ao;
    half3 color=albedo*(ambient+sun.color*wrap*sun.shadowAttenuation);

    // Atmospheric perspective, a little thicker in the low valleys.
    half aerial=smoothstep(100,580,dist)*_HazeStrength*lerp(1.15,.85,saturate(h/120));
    half3 air=_HazeColor.rgb*(max(SampleSH(half3(0,1,0)),half3(.03,.035,.04))*1.15+sun.color*.32);
    color=lerp(color,air,aerial);
    return half4(MixFog(color,i.fog),1);
   }
   ENDHLSL
  }
  Pass {
   Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"}
   ColorMask 0 ZWrite On ZTest LEqual
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
   float3 _LightDirection,_LightPosition;
   struct A {float4 p:POSITION;float3 n:NORMAL;};
   float4 vert(A a):SV_POSITION {
    float3 w=TransformObjectToWorld(a.p.xyz),n=TransformObjectToWorldNormal(a.n);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
     float3 l=normalize(_LightPosition-w);
    #else
     float3 l=_LightDirection;
    #endif
    float4 p=TransformWorldToHClip(ApplyShadowBias(w,n,l));
    #if UNITY_REVERSED_Z
     p.z=min(p.z,UNITY_NEAR_CLIP_VALUE);
    #else
     p.z=max(p.z,UNITY_NEAR_CLIP_VALUE);
    #endif
    return p;
   }
   half4 frag():SV_Target{return 0;}
   ENDHLSL
  }
  Pass {
   Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask R
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   float4 vert(float4 p:POSITION):SV_POSITION{return TransformObjectToHClip(p.xyz);}
   half frag():SV_Target{return 0;}
   ENDHLSL
  }
  Pass {
   Name "DepthNormals" Tags{"LightMode"="DepthNormals"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   struct A {float4 p:POSITION;float3 n:NORMAL;};
   struct V {float4 p:SV_POSITION;half3 n:TEXCOORD0;};
   V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.n=TransformObjectToWorldNormal(a.n);return o;}
   half4 frag(V i):SV_Target{return half4(normalize(i.n),0);}
   ENDHLSL
  }
 }
}
