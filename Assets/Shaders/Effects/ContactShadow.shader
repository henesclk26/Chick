// A soft round contact shadow: a flat quad (local XZ, UV 0..1) drawn instanced by ContactShadows. Its strength rides
// in the instance matrix's Y scale (the quad is flat, so that scale is free): 0 = gone, 1 = full _Strength.
Shader "Chick/ContactShadow"
{
    Properties
    {
        _Color("Shadow Colour", Color) = (0.1, 0.075, 0.04, 1)
        _Strength("Strength", Range(0, 1)) = 0.5
        _Softness("Edge Softness", Range(0.05, 1)) = 0.75
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ContactShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Strength;
                half _Softness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fade : TEXCOORD1; half fog : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = v.uv;
                float4x4 m = GetObjectToWorldMatrix();
                o.fade = (half)saturate(length(float3(m._m01, m._m11, m._m21)));
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half d = length(i.uv*2.0 - 1.0);
                half a = saturate((1.0 - d)/_Softness);
                a = a*a*(3.0 - 2.0*a);
                half4 c = half4(_Color.rgb, a*_Strength*i.fade);
                c.rgb = MixFog(c.rgb, i.fog);
                return c;
            }
            ENDHLSL
        }
    }
}
