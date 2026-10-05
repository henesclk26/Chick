Shader "Chick/PondDroplet"
{
 // Round, softly lit water droplet for the pond's splash particles; no texture needed.
 Properties {}
 SubShader {
  Tags{"RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True"}
  Blend SrcAlpha OneMinusSrcAlpha
  ZWrite Off
  Cull Off
  Pass {
   Name "ForwardLit"
   Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   struct A {float4 p:POSITION;half4 c:COLOR;float2 uv:TEXCOORD0;};
   struct V {float4 p:SV_POSITION;half4 c:COLOR;float2 uv:TEXCOORD0;half fog:TEXCOORD1;};
   V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.c=a.c;o.uv=a.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
   half4 frag(V i):SV_Target
   {
    float2 d=i.uv*2-1;
    float r=dot(d,d);
    clip(1-r);
    half3 light=max(SampleSH(half3(0,1,0)),half3(.03,.035,.04))+GetMainLight().color*.45;
    half rim=smoothstep(.35,1,r);
    half spark=smoothstep(.12,0,dot(d-float2(-.3,.32),d-float2(-.3,.32)));
    half3 color=i.c.rgb*light*(.75+.35*rim)+spark*.8;
    half alpha=i.c.a*(.55+.45*rim)*smoothstep(1,.8,r);
    return half4(MixFog(color,i.fog),alpha);
   }
   ENDHLSL
  }
 }
}
