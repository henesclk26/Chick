// The flat-coloured rocks (BluBlu's RockMoss materials: M_RocksCover_A/B, the waterfall's M_RocksCover_Wet) with the
// same colour and the same moss on their tops, lit so they keep their shape when the sun is low. In the morning the
// sun stands behind the rocks east of the yard, every face the player sees is in shade and, lit by the sky alone, they
// all come out the same flat tone. Here the shaded faces also get a soft fill from the sky opposite the sun (a little
// warmed by it), so each face keeps its own shade, and the rocks' edges catch the low sun from behind. Both fade out
// as the sun climbs, so at midday the rocks look as before. A slight change of tone across the slope and a broken
// moss edge keep large rock faces from looking flat. Same property names as RockMoss, so a material can switch to it
// and back without losing its settings.
Shader "Farm/Painted Rock"
{
    Properties
    {
        _BaseColor ("Rock Colour", Color) = (0.6, 0.58, 0.51, 1)
        _CoverTint ("Moss Colour", Color) = (0.29, 0.4, 0.21, 1)
        _CoverStr ("Moss Strength (0 = none)", Float) = 4
        _MossBreak ("Moss Edge Broken Up", Range(0, 1)) = 0.35
        _Variation ("Tone Change Across The Slope", Range(0, 0.3)) = 0.08
        _Fill ("Sky Fill When The Sun Is Low", Range(0, 1.5)) = 0.75
        _Rim ("Sunlit Edges When The Sun Is Behind", Range(0, 1)) = 0.35
        _ShadeLift ("Shade Lift", Range(0, 0.5)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _CoverTint;
            float _CoverStr, _MossBreak, _Variation, _Fill, _Rim, _ShadeLift;
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));

                // The rock's colour, its tone drifting a little across the slope.
                half3 albedo = _BaseColor.rgb;
                float tone = RockNoise(i.positionWS * 0.12) - 0.5;
                albedo *= 1 + tone * 2 * _Variation;
                albedo = lerp(albedo, albedo * half3(1.05, 1.0, 0.92), saturate(tone + 0.5) * _Variation * 2);

                // Moss on the faces that look up (as RockMoss: from about a quarter of the way up), its edge broken up.
                float patch = RockNoise(i.positionWS * 0.9) * 0.6 + RockNoise(i.positionWS * 2.7 + 5.1) * 0.4;
                float up = n.y + (patch - 0.5) * _MossBreak;
                float moss = saturate((up - 0.25) * 12) * saturate(_CoverStr);
                half3 mossColour = _CoverTint.rgb * (0.9 + 0.2 * patch);
                albedo = lerp(albedo, mossColour, moss);

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 l = sun.direction;
                half lit = saturate(dot(n, l)) * sun.shadowAttenuation;
                half3 ambient = SampleSH(n) * (1 + _ShadeLift);

                // When the sun is low: a fill from the sky on the side away from it, and its light round the edges.
                half low = smoothstep(0.8, 0.3, l.y);
                // The fill comes from the sky a little to one side of straight opposite the sun, so faces turned left and
                // right of it differ; the sky's own light is a touch weaker on faces that look down or out than on tops.
                float2 away = normalize(-l.xz + 1e-4);
                float2 turned = float2(away.x * 0.77 - away.y * 0.64, away.x * 0.64 + away.y * 0.77);
                float3 fillDir = normalize(float3(turned.x, 0.45, turned.y));
                half3 fillColour = SampleSH(float3(0, 1, 0));
                half fill = saturate(dot(n, fillDir)) * (1 - lit);
                ambient *= lerp(1, 0.4 + 0.3 * saturate(n.y * 0.5 + 0.5), low);
                half rim = pow(1 - saturate(dot(n, v)), 3) * saturate(dot(-v, l)) * (0.5 + 0.5 * sun.shadowAttenuation);

                half3 colour = albedo * (sun.color * lit + ambient + fillColour * fill * _Fill * low);
                colour += sun.color * rim * _Rim * low * (0.6 + 0.4 * albedo);
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
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
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes i) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(i); return TransformObjectToHClip(i.positionOS.xyz); }
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
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
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
