// A painted ground for plain meshes (the test Plane): up to six ground layers from texture arrays, placed by two
// world-space control maps, mixed by their painted height (grass over earth with a ragged edge), with normal,
// cavity and occlusion maps and the same world-space macro variation as Chick/Terrain Rich.
// Set up by Tools/Chick/Ground/Reference Style Test Plane (V1).
Shader "Chick/Ground Rich Mesh"
{
    Properties
    {
        _AlbedoArray("Layer Colours (array)", 2DArray) = "" {}
        _NormalArray("Layer Normals (array)", 2DArray) = "" {}
        _MaskArray("Layer Masks (array: cavity, AO, height, smoothness)", 2DArray) = "" {}
        [NoScaleOffset] _Ctrl0("Control 0-3", 2D) = "red" {}
        [NoScaleOffset] _Ctrl1("Control 4-5", 2D) = "black" {}
        _CtrlRect("Control Rect (min x, min z, 1/size x, 1/size z)", Vector) = (0, 0, 0.02, 0.02)
        _Tile0123("Tile Size m (layers 0-3)", Vector) = (4, 4, 3.5, 3.5)
        _Tile45("Tile Size m (layers 4-5)", Vector) = (4.5, 4.5, 0, 0)
        _Normal0123("Normal Strength (layers 0-3)", Vector) = (1, 1, 1, 1)
        _Normal45("Normal Strength (layers 4-5)", Vector) = (1, 1, 0, 0)

        _HeightTransition("Height Transition", Range(0.01, 1)) = 0.12
        _NormalStrength("Normal Strength", Range(0, 3)) = 1.2
        _CavityStrength("Cavity Strength", Range(0, 1)) = 0.8
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 0.8

        [NoScaleOffset] _MacroTex("Macro Variation", 2D) = "grey" {}
        _MacroScaleLarge("Macro Large Patch Size (m)", Float) = 60
        _MacroScaleMedium("Macro Medium Patch Size (m)", Float) = 18
        _MacroScaleFine("Macro Fine Mottle Size (m)", Float) = 4
        _MacroBrightness("Macro Brightness", Range(0, 0.5)) = 0.1
        _MacroFine("Macro Fine Mottle", Range(0, 0.3)) = 0.04
        _MacroWarm("Sunny Patches", Range(0, 1)) = 0.45
        _MacroLush("Lush Patches", Range(0, 1)) = 0.35
        _MacroWarmColor("Sunny Tint", Color) = (1.12, 1.06, 0.78, 1)
        _MacroLushColor("Lush Tint", Color) = (0.8, 0.92, 0.8, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _CtrlRect;
            float4 _Tile0123, _Tile45;
            float4 _Normal0123, _Normal45;
            float _HeightTransition, _NormalStrength, _CavityStrength, _OcclusionStrength;
            float _MacroScaleLarge, _MacroScaleMedium, _MacroScaleFine;
            float _MacroBrightness, _MacroFine, _MacroWarm, _MacroLush;
            float4 _MacroWarmColor, _MacroLushColor;
        CBUFFER_END

        TEXTURE2D_ARRAY(_AlbedoArray); SAMPLER(sampler_AlbedoArray);
        TEXTURE2D_ARRAY(_NormalArray); SAMPLER(sampler_NormalArray);
        TEXTURE2D_ARRAY(_MaskArray);   SAMPLER(sampler_MaskArray);
        TEXTURE2D(_Ctrl0); SAMPLER(sampler_Ctrl0);
        TEXTURE2D(_Ctrl1);
        TEXTURE2D(_MacroTex); SAMPLER(sampler_MacroTex);

        struct GroundSurface { half3 albedo; half3 normalTS; half occlusion; half smoothness; };

        GroundSurface ComputeGround(float3 positionWS)
        {
            float2 wp = positionWS.xz;
            float2 cuv = (wp - _CtrlRect.xy)*_CtrlRect.zw;
            float4 c0 = SAMPLE_TEXTURE2D(_Ctrl0, sampler_Ctrl0, cuv);
            float4 c1 = SAMPLE_TEXTURE2D(_Ctrl1, sampler_Ctrl0, cuv);
            float w[6] = { c0.r, c0.g, c0.b, c0.a, c1.r, c1.g };
            float tile[6] = { _Tile0123.x, _Tile0123.y, _Tile0123.z, _Tile0123.w, _Tile45.x, _Tile45.y };
            float nstr[6] = { _Normal0123.x, _Normal0123.y, _Normal0123.z, _Normal0123.w, _Normal45.x, _Normal45.y };
            float wsum = 0;
            [unroll] for (int a = 0; a < 6; a++) wsum += w[a];

            float2 dx = ddx(wp), dy = ddy(wp);
            half3 alb[6]; half3 nrm[6]; half4 msk[6]; float h[6]; float hmax = 0;
            [unroll] for (int i = 0; i < 6; i++)
            {
                w[i] /= max(wsum, 1e-5);
                alb[i] = 0; nrm[i] = half3(0, 0, 1); msk[i] = half4(1, 1, 0, 0); h[i] = 0;
                [branch] if (w[i] > 0.002)
                {
                    float2 uv = wp/tile[i];
                    float2 ldx = dx/tile[i], ldy = dy/tile[i];
                    alb[i] = SAMPLE_TEXTURE2D_ARRAY_GRAD(_AlbedoArray, sampler_AlbedoArray, uv, i, ldx, ldy).rgb;
                    half3 n = SAMPLE_TEXTURE2D_ARRAY_GRAD(_NormalArray, sampler_NormalArray, uv, i, ldx, ldy).rgb*2.0 - 1.0;
                    n.xy *= nstr[i]*_NormalStrength;
                    nrm[i] = normalize(n);
                    msk[i] = SAMPLE_TEXTURE2D_ARRAY_GRAD(_MaskArray, sampler_MaskArray, uv, i, ldx, ldy);
                    h[i] = msk[i].b*w[i];
                }
                hmax = max(hmax, h[i]);
            }
            float hw[6]; float hsum = 0;
            [unroll] for (int k = 0; k < 6; k++) { hw[k] = (max(h[k] + _HeightTransition - hmax, 0.0) + 1e-5)*w[k]; hsum += hw[k]; }

            GroundSurface o;
            half3 albedo = 0; half3 nn = 0; half4 m = 0;
            [unroll] for (int j = 0; j < 6; j++) { float f = hw[j]/max(hsum, 1e-6); albedo += alb[j]*f; nn += nrm[j]*f; m += msk[j]*f; }
            nn.z += 1e-3;
            o.normalTS = normalize(nn);
            o.albedo = albedo*lerp(1.0, m.r, _CavityStrength);
            o.occlusion = lerp(1.0, m.g, _OcclusionStrength);
            o.smoothness = m.a;

            half m1 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp/_MacroScaleLarge).r;
            half m2 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp/_MacroScaleMedium + 0.37).g;
            half m3 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp/_MacroScaleFine + 0.71).b;
            half patch = m1*0.55 + m2*0.45;
            o.albedo *= 1.0 + (m1 - 0.5)*2.0*_MacroBrightness + (m3 - 0.5)*2.0*_MacroFine;
            o.albedo = lerp(o.albedo, o.albedo*_MacroWarmColor.rgb, smoothstep(0.54, 0.70, patch)*_MacroWarm);
            o.albedo = lerp(o.albedo, o.albedo*_MacroLushColor.rgb, smoothstep(0.46, 0.30, patch)*_MacroLush);
            return o;
        }

        // world-space tangent frame of the ground: tangent along +x (u), bitangent along +z (v)
        half3 GroundNormalWS(half3 normalTS, half3 normalWS)
        {
            half3 t = normalize(half3(1, 0, 0) - normalWS*normalWS.x);
            half3 b = cross(normalWS, t);
            return normalize(t*normalTS.x + b*normalTS.y + normalWS*normalTS.z);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half fogFactor : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                GroundSurface s = ComputeGround(i.positionWS);
                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = GroundNormalWS(s.normalTS, normalize(i.normalWS));
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fogFactor;
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
                half4 c = UniversalFragmentPBR(d, s.albedo, 0, half3(0, 0, 0), s.smoothness, s.occlusion, half3(0, 0, 0), 1);
                c.rgb = MixFog(c.rgb, d.fogCoord);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 ShadowVert(A v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 l = normalize(_LightPosition - pw);
            #else
                float3 l = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pw, nw, l));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }
            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 DepthVert(A v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(v.positionOS.xyz); }
            half DepthFrag(float4 p : SV_POSITION) : SV_Target { return p.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; };
            V DNVert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 DNFrag(V i) : SV_Target
            {
                GroundSurface s = ComputeGround(i.positionWS);
                return half4(GroundNormalWS(s.normalTS, normalize(i.normalWS)), 0);
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
