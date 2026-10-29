// Small glowing specks for particles (the fireflies at dusk): a soft bright core with a faint halo, added on top of
// the scene, bright enough (HDR) for the bloom to catch. Tinted and faded by the particle colour. Texture-free.
Shader "Farm/Glow"
{
    Properties
    {
        [HDR] _Color ("Glow", Color) = (2.2, 2.6, 0.9, 1)
        _Core ("Core Size", Range(0.05, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Core;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv * 2 - 1);
                float glow = (1 - smoothstep(0, _Core, r)) + pow(saturate(1 - r), 3) * 0.35;
                return half4(_Color.rgb * i.color.rgb, glow * i.color.a);
            }
            ENDHLSL
        }
    }
}
