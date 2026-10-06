Shader "Chick/DistantMountain"
{
 Properties {
  _BaseColor("Tint",Color)=(1,1,1,1)
  _HazeColor("Distant air tint",Color)=(0.53,0.65,0.72,1)
  _HazeStrength("Atmospheric depth",Range(0,0.6))=0.24
  // Pond reeds lean away from the player like the meadow foliage (FoliageBend.hlsl); off for the mountains.
  [Toggle(_FOLIAGE_BEND)] _FoliageBend("Bend away from the player",Float)=0
 }
 SubShader {
  Tags{"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
  Cull Back ZWrite On
  HLSLINCLUDE
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor,_HazeColor;
   half _HazeStrength;
  CBUFFER_END
  #include "Assets/Art/FarmReference/Foliage/FoliageBend.hlsl"
  float3 BendOS(float3 p)
  {
   #if _FOLIAGE_BEND
    p=FoliageBendOS(p);
   #endif
   return p;
  }
  ENDHLSL
  Pass {
   Name "ForwardLit"
   Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #pragma multi_compile_fog
   #pragma shader_feature_local_vertex _FOLIAGE_BEND
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   struct A {float4 p:POSITION;float3 n:NORMAL;half4 c:COLOR;};
   struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;half3 n:TEXCOORD1;half4 c:COLOR;half fog:TEXCOORD2;};
   V vert(A a){V o;o.world=TransformObjectToWorld(BendOS(a.p.xyz));o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(a.n);o.c=a.c;o.fog=ComputeFogFactor(o.p.z);return o;}
   half4 frag(V i):SV_Target {
    half3 n=normalize(i.n);
    Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
    half diffuse=saturate(dot(n,sun.direction));
    half3 ambient=max(SampleSH(n),half3(.028,.033,.04));
    half3 color=i.c.rgb*_BaseColor.rgb*(ambient*i.c.a+sun.color*diffuse*sun.shadowAttenuation);
    half aerial=smoothstep(100,580,distance(_WorldSpaceCameraPos,i.world))*_HazeStrength;
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
   #pragma shader_feature_local_vertex _FOLIAGE_BEND
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
   float3 _LightDirection,_LightPosition;
   struct A {float4 p:POSITION;float3 n:NORMAL;};
   float4 vert(A a):SV_POSITION {
    float3 w=TransformObjectToWorld(BendOS(a.p.xyz)),n=TransformObjectToWorldNormal(a.n);
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
   #pragma shader_feature_local_vertex _FOLIAGE_BEND
   float4 vert(float4 p:POSITION):SV_POSITION{return TransformObjectToHClip(BendOS(p.xyz));}
   half frag():SV_Target{return 0;}
   ENDHLSL
  }
  Pass {
   Name "DepthNormals" Tags{"LightMode"="DepthNormals"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma shader_feature_local_vertex _FOLIAGE_BEND
   struct A {float4 p:POSITION;float3 n:NORMAL;};
   struct V {float4 p:SV_POSITION;half3 n:TEXCOORD0;};
   V vert(A a){V o;o.p=TransformObjectToHClip(BendOS(a.p.xyz));o.n=TransformObjectToWorldNormal(a.n);return o;}
   half4 frag(V i):SV_Target{return half4(normalize(i.n),0);}
   ENDHLSL
  }
 }
}