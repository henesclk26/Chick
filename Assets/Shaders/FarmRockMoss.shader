// Hand-painted rocks (Assets/Art/RockyFalls) as part of the meadow: their own painted colour and normal map, a soft
// coat of moss on the faces that look up (its edge broken up by noise, so it settles in patches rather than a line),
// a slight change of tone from rock to rock, and shade that is lifted a little so a face away from the low morning sun
// stays readable instead of going flat grey. Lit by the sun (with shadows) and the sky, fogged like the scene; casts
// shadows. Made by RockKit.
Shader "Farm/Rock Moss"
{
    Properties
    {
        [MainTexture] _BaseMap ("Painted Colour", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _MossColor ("Moss", Color) = (0.24, 0.34, 0.16, 1)
        _MossDry ("Dry Moss", Color) = (0.38, 0.42, 0.22, 1)
        _Moss ("Moss Amount", Range(0, 1)) = 0.6
        _MossFrom ("Moss From (how upward a face)", Range(-1, 1)) = 0.55
        _MossScale ("Moss Patch Size (m)", Float) = 1.6
        _Variation ("Tone Change Rock To Rock", Range(0, 0.3)) = 0.1
        _ShadowLift ("Shade Lift", Range(0, 0.6)) = 0.22
        _Wrap ("Light Wrap", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _Tint, _MossColor, _MossDry;
            float _BumpScale, _Moss, _MossFrom, _MossScale, _Variation, _ShadowLift, _Wrap;
        CBUFFER_END

        float RockHash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }
        float RockNoise(float3 p)
        {
            float3 i = floor(p), f = frac(p);
            f = f * f * (3 - 2 * f);
            float n000 = RockHash(i), n100 = RockHash(i + float3(1, 0, 0)), n010 = RockHash(i + float3(0, 1, 0)), n110 = RockHash(i + float3(1, 1, 0));
            float n001 = RockHash(i + float3(0, 0, 1)), n101 = RockHash(i + float3(1, 0, 1)), n011 = RockHash(i + float3(0, 1, 1)), n111 = RockHash(i + float3(1, 1, 1));
            return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS, i.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, i.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _Tint.rgb;
                // A little change of tone from rock to rock, by where the rock stands.
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                float h = RockHash(floor(origin * 0.5));
                albedo *= 1 + (h - 0.5) * 2 * _Variation;
                albedo = lerp(albedo, albedo * float3(1.04, 1.0, 0.94), h * _Variation * 3);

                float3 nT = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float3 bitangent = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                float3 n = normalize(TransformTangentToWorld(nT, half3x3(i.tangentWS.xyz, bitangent, i.normalWS)));

                // Moss on the faces that look up, in patches.
                float patch = RockNoise(i.positionWS / _MossScale) * 0.65 + RockNoise(i.positionWS / (_MossScale * 0.3) + 7.3) * 0.35;
                float up = normalize(i.normalWS).y * 0.6 + n.y * 0.4;
                float moss = smoothstep(_MossFrom, _MossFrom + 0.25, up + (patch - 0.5) * 0.55) * _Moss;
                half3 mossColour = lerp(_MossColor.rgb, _MossDry.rgb, RockNoise(i.positionWS * 0.35 + 3.1));
                albedo = lerp(albedo, mossColour * (0.85 + 0.3 * patch), moss);

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse = saturate((dot(n, sun.direction) + _Wrap) / (1 + _Wrap));
                half lit = diffuse * sun.shadowAttenuation;
                half3 ambient = SampleSH(n);
                half3 colour = albedo * (sun.color * lit + ambient * (1 + _ShadowLift) + _ShadowLift * 0.15);
                colour = MixFog(colour, i.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(Attributes i) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(i.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 toLight = normalize(_LightPosition - positionWS);
            #else
                float3 toLight = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, toLight));
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
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
