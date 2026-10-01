// Two-sided crops (wheat, corn) with GPU wind. UV0.x = sway weight (0 at the root, ~1 at the top), UV0.y = per-plant phase.
// Vertex colour tints individual parts. Rolling gusts travel across the field along _WindDirection;
// each plant adds its own small flutter.
Shader "Chick/CropWind"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 0.82, 0.3, 1)
        _WindDirection("Wind Direction (XZ)", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Wind Strength", Float) = 0.07
        _WindSpeed("Wind Speed", Float) = 1.4
        _GustScale("Gust Wave Scale", Float) = 0.35
        _Flutter("Flutter", Float) = 0.018
        _Wrap("Light Wrap", Range(0, 1)) = 0.35
        _RootShade("Root Shade (1 = off)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _WindDirection;
            float _WindStrength, _WindSpeed, _GustScale, _Flutter;
            half _Wrap, _RootShade;
        CBUFFER_END

        float3 Sway(float3 positionWS, float2 sway)
        {
            float bend = sway.x * sway.x;                  // stiff near the root, loose at the ear
            float2 dir = normalize(_WindDirection.xz);
            float t = _Time.y * _WindSpeed;
            float gust = sin(dot(positionWS.xz, dir) * _GustScale - t) * 0.5 + 0.5;
            float2 offset = dir * (_WindStrength * (0.3 + gust) + _Flutter * sin(t * 1.9 + sway.y * 6.2832));
            offset += float2(-dir.y, dir.x) * _Flutter * 0.6 * sin(t * 2.6 + sway.y * 11.0);
            offset *= bend;
            positionWS.xz += offset;
            positionWS.y -= dot(offset, offset) * 0.5;     // keep the stalk length roughly constant
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half fog : TEXCOORD2; half3 color : TEXCOORD3; half height : TEXCOORD4; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.color = v.color.rgb;
                o.height = v.uv.x;
                o.positionWS = Sway(TransformObjectToWorld(v.positionOS.xyz), v.uv);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 n = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1, -1);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse = saturate((dot(n, light.direction) + _Wrap) / (1 + _Wrap));   // thin blades pass light
                half3 color = _BaseColor.rgb * i.color * (light.color * diffuse * light.shadowAttenuation + SampleSH(n));
                color *= lerp(_RootShade, 1, smoothstep(0, 0.7, i.height));   // cheap self-shadow toward the roots
                return half4(MixFog(color, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            float4 vert(Attributes v) : SV_POSITION
            {
                float3 positionWS = Sway(TransformObjectToWorld(v.positionOS.xyz), v.uv);   // shadows sway too
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            float4 vert(Attributes v) : SV_POSITION { return TransformWorldToHClip(Sway(TransformObjectToWorld(v.positionOS.xyz), v.uv)); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(Sway(TransformObjectToWorld(v.positionOS.xyz), v.uv));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                return half4(normalize(i.normalWS) * IS_FRONT_VFACE(face, 1, -1), 0);
            }
            ENDHLSL
        }
    }
}
