Shader "Chick/FoodOutline"
{
 // Inverted-hull outline for the food the next peck would take (FoodTargetHighlight). Back faces are pushed out
 // along the normal in screen space, so the rim stays a fixed pixel width however small or far the food is.
 Properties {
  _Color("Color",Color)=(1,1,1,1)
  _Width("Width (pixels at 1080p)",Float)=2.5
 }
 SubShader {
  Tags{"RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline"}
  Pass {
   Name "Outline"
   Tags{"LightMode"="SRPDefaultUnlit"}
   Cull Front
   ZWrite On
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

   CBUFFER_START(UnityPerMaterial)
   half4 _Color;
   float _Width;
   CBUFFER_END

   struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
   struct Varyings { float4 positionCS : SV_POSITION; };

   Varyings vert(Attributes input)
   {
    Varyings output;
    float4 clip = TransformObjectToHClip(input.positionOS.xyz);
    float2 normalCS = mul((float3x3)UNITY_MATRIX_VP, TransformObjectToWorldNormal(input.normalOS)).xy;
    float2 direction = normalCS / max(length(normalCS), 1e-5);
    // Pixels to clip units: 2 / screen size, scaled by w so the width does not shrink with distance.
    float pixels = _Width * _ScreenParams.y / 1080.0;
    clip.xy += direction * pixels * 2.0 / _ScreenParams.xy * clip.w;
    output.positionCS = clip;
    return output;
   }

   half4 frag(Varyings input) : SV_Target { return _Color; }
   ENDHLSL
  }
 }
}
