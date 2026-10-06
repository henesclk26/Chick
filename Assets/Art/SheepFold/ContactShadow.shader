Shader "Chick/ContactShadow"
{
 // Soft, texture-free blob that darkens the ground under an animal so it reads as standing on it,
 // whatever the sun angle. Used on a flat quad (UV 0..1) lying just above the ground.
 Properties {
  _Strength("Darkness",Range(0,1))=0.5
  _Softness("Edge softness",Range(0.05,1))=0.65
 }
 SubShader {
  Tags{"RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True"}
  Blend DstColor Zero
  ZWrite Off
  Offset -1, -1
  Cull Off
  Pass {
   Name "ForwardLit"
   Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
    half _Strength,_Softness;
   CBUFFER_END
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
   V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;return o;}
   half4 frag(V i):SV_Target
   {
    float r=length(i.uv*2-1);
    half shade=(1-smoothstep(1-_Softness,1,r))*_Strength;
    return half4(1-shade.xxx,1);
   }
   ENDHLSL
  }
 }
}
