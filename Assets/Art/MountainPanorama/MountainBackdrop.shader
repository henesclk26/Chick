Shader "Chick/MountainBackdrop"
{
 // The painted mountain panorama on a far arc around the farm. Unlit art, tinted by the day's light so it follows
 // the day cycle: the painting shows its own colours at noon (_NoonLight), warms at sunset and darkens at dusk.
 Properties {
  _MainTex("Panorama",2D)="white"{}
  // Tuned in play at 12:30 until the painting showed its own colours (M_MountainBackdrop: 1.44, 1.31, 1.19).
  _NoonLight("Light at noon (sky + sun, linear)",Vector)=(1,1,1,1)
  _HazeColor("Distant air tint",Color)=(0.74,0.84,0.93,1)
  _Haze("Atmospheric depth",Range(0,1))=0.12
  _EdgeFade("Fade at the arc ends (UV)",Range(0.001,0.2))=0.05
 }
 SubShader {
  Tags{"RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True"}
  Blend SrcAlpha OneMinusSrcAlpha
  ZWrite Off
  Cull Off
  Pass {
   Name "Unlit"
   Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float4 _NoonLight;half4 _HazeColor;
    half _Haze,_EdgeFade;
   CBUFFER_END
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half fog:TEXCOORD1;};
   V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
   // The same light the old 3D mountain received on its sunny faces: sky from above plus the sun.
   half3 DayLight()
   {
    Light sun=GetMainLight();
    return max(SampleSH(half3(0,1,0)),half3(.03,.035,.04))+sun.color*saturate(sun.direction.y+.2)*.6;
   }
   half4 frag(V i):SV_Target
   {
    half4 tex=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
    half3 tint=min(DayLight()/max(_NoonLight.rgb,half3(.01,.01,.01)),half3(1.2,1.2,1.2));
    half3 color=lerp(tex.rgb,_HazeColor.rgb,_Haze)*tint;
    half alpha=tex.a*smoothstep(0,_EdgeFade,i.uv.x)*smoothstep(0,_EdgeFade,1-i.uv.x);
    return half4(MixFog(color,i.fog),alpha);
   }
   ENDHLSL
  }
 }
}
