// The spray at the foot of the waterfall by the chicken yard: soft round puffs for its particles, tinted and faded by
// the particle colour, lit by the sky, fogged like the rest of the scene. Texture-free, transparent, no depth write.
Shader "Farm/Mist"
{
    Properties
    {
        _Color ("Mist", Color) = (0.92, 0.97, 1, 0.5)
        _Softness ("Edge Softness", Range(0.05, 1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Softness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float fogFactor : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.color = i.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv * 2 - 1);
                float puff = 1 - smoothstep(1 - _Softness, 1, r);
                half3 colour = _Color.rgb * i.color.rgb * (GetMainLight().color * 0.35 + SampleSH(float3(0, 1, 0)) + 0.2);
                colour = MixFog(colour, i.fogFactor);
                return half4(colour, puff * _Color.a * i.color.a);
            }
            ENDHLSL
        }
    }
}
