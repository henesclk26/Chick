// The ground of the scenery around the map (Map/Backdrop Forest), drawn from eight of the map's terrain layers with the
// same height blend, cavity, world-space macro patches, grass grade and URP lighting as Chick/Terrain Rich
// (ChickTerrainRich.hlsl). The mesh carries each point's weights: at the map's edge the map's own four (so the two meet
// unseen), further out the forest's four (olive and dry grass, grass with earth, bare earth). The vertex colour shades
// it a little, and it hazes into the horizon colour the further out it lies (on top of the game's distance fog), so the
// map itself stays clear. Its textures and settings are
// copied from the map's terrain material by Tools/Chick/Map/Add Backdrop Forest.
Shader "Farm/Backdrop Ground"
{
    Properties
    {
        _AlbedoArray("Layer Colours (array)", 2DArray) = "" {}
        _NormalArray("Layer Normals (array, RG)", 2DArray) = "" {}
        _MaskArray("Layer Masks (array: cavity, AO, height, smoothness)", 2DArray) = "" {}
        [NoScaleOffset] _LayerData("Layer Data", 2D) = "black" {}
        _LayerIds("Edge Layers (ids)", Vector) = (0, 1, 2, 3)
        _LayerIds2("Forest Layers (ids)", Vector) = (4, 5, 6, 7)

        _RichHeightTransition("Height Transition", Range(0.01, 1)) = 0.16
        _NormalStrength("Normal Strength", Range(0, 3)) = 1
        _CavityStrength("Cavity Strength", Range(0, 1)) = 0.85
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 0.8
        _BlendSoften("Grass-Earth Blend Desaturate", Range(0, 1)) = 0.3
        _BlendDarken("Grass-Earth Blend Darken", Range(0, 0.5)) = 0.12

        [NoScaleOffset] _MacroTex("Macro Variation", 2D) = "grey" {}
        _MacroScaleLarge("Macro Large Patch Size (m)", Float) = 70
        _MacroScaleMedium("Macro Medium Patch Size (m)", Float) = 22
        _MacroScaleFine("Macro Fine Mottle Size (m)", Float) = 5
        _MacroBrightness("Macro Brightness", Range(0, 0.5)) = 0.12
        _MacroFine("Macro Fine Mottle", Range(0, 0.3)) = 0.05
        _MacroWarm("Sunny Patches", Range(0, 1)) = 0.55
        _MacroLush("Lush Patches", Range(0, 1)) = 0.45
        _MacroWarmColor("Sunny Tint", Color) = (1.14, 1.07, 0.74, 1)
        _MacroLushColor("Lush Tint", Color) = (0.78, 0.9, 0.78, 1)
        _GrassSaturation("Grass Saturation", Range(0, 1.5)) = 1
        _GrassTint("Grass Tint", Color) = (1, 1, 1, 1)

        [NoScaleOffset] _CloseDetail("Close-up Detail", 2D) = "grey" {}
        [NoScaleOffset] _CloseDetailNormal("Close-up Detail Normal", 2D) = "bump" {}
        _DetailSize("Close-up Detail Size (m)", Float) = 0.6
        _DetailEarth("Close-up Detail On Earth", Range(0, 1)) = 0.45
        _DetailGrass("Close-up Detail On Grass", Range(0, 1)) = 0.2
        _DetailNormal("Close-up Detail Normal", Range(0, 1)) = 0.45
        _DetailDistance("Close-up Detail Distance (m)", Float) = 5

        _MapRect("Map Rect (x min, z min, x max, z max)", Vector) = (0, 0, 1, 1)
        _Reach("Scenery Reach Out From The Map (x, z)", Vector) = (35, 90, 0, 0)
        _HazeStart("Haze Starts (share of the reach)", Range(0, 1)) = 0.3
        _HazeEnd("Haze Full (share of the reach)", Range(0, 1.5)) = 1
        _HazeMax("Haze At Its Fullest", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _LayerIds, _LayerIds2;
            float _RichHeightTransition, _NormalStrength, _CavityStrength, _OcclusionStrength, _BlendSoften, _BlendDarken;
            float _MacroScaleLarge, _MacroScaleMedium, _MacroScaleFine, _MacroBrightness, _MacroFine, _MacroWarm, _MacroLush;
            float4 _MacroWarmColor, _MacroLushColor, _GrassTint;
            float _GrassSaturation;
            float _DetailSize, _DetailEarth, _DetailGrass, _DetailNormal, _DetailDistance;
            float4 _MapRect, _Reach;
            float _HazeStart, _HazeEnd, _HazeMax;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_AlbedoArray); SAMPLER(sampler_AlbedoArray);
            TEXTURE2D_ARRAY(_NormalArray); SAMPLER(sampler_NormalArray);
            TEXTURE2D_ARRAY(_MaskArray); SAMPLER(sampler_MaskArray);
            TEXTURE2D(_LayerData);
            TEXTURE2D(_MacroTex); SAMPLER(sampler_MacroTex);
            TEXTURE2D(_CloseDetail); SAMPLER(sampler_CloseDetail);
            TEXTURE2D(_CloseDetailNormal);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float4 weights : TEXCOORD1; float4 weights2 : TEXCOORD2; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 weights : TEXCOORD2;
                float4 weights2 : TEXCOORD3;
                half4 color : COLOR;
                float fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.weights = i.weights;
                o.weights2 = i.weights2;
                o.color = i.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // The map's terrain UV, carried on past its edge: the layers' tiling continues seamlessly.
                float2 uv = (i.positionWS.xz - _MapRect.xy) / (_MapRect.zw - _MapRect.xy);
                float2 dx = ddx(uv), dy = ddy(uv);
                float weight[8] = { i.weights.x, i.weights.y, i.weights.z, i.weights.w, i.weights2.x, i.weights2.y, i.weights2.z, i.weights2.w };
                float ids[8] = { _LayerIds.x, _LayerIds.y, _LayerIds.z, _LayerIds.w, _LayerIds2.x, _LayerIds2.y, _LayerIds2.z, _LayerIds2.w };
                half3 albedo[8]; half3 nrm[8]; half4 mask[8]; float h[8]; float isEarth[8];
                float hmax = 0;
                [unroll] for (int s = 0; s < 8; s++)
                {
                    albedo[s] = 0; nrm[s] = half3(0, 0, 1); mask[s] = half4(1, 1, 0, 0); h[s] = 0; isEarth[s] = 0;
                    int id = (int)ids[s];
                    [branch] if (weight[s] > 0.001)
                    {
                        float4 st = LOAD_TEXTURE2D(_LayerData, int2(id, 0));
                        float4 tint = LOAD_TEXTURE2D(_LayerData, int2(id, 1));
                        float4 extra = LOAD_TEXTURE2D(_LayerData, int2(id, 2));
                        float2 luv = uv * st.xy + st.zw;
                        float2 ldx = dx * st.xy, ldy = dy * st.xy;
                        albedo[s] = SAMPLE_TEXTURE2D_ARRAY_GRAD(_AlbedoArray, sampler_AlbedoArray, luv, id, ldx, ldy).rgb * tint.rgb;
                        half2 nxy = SAMPLE_TEXTURE2D_ARRAY_GRAD(_NormalArray, sampler_NormalArray, luv, id, ldx, ldy).rg * 2.0 - 1.0;
                        nxy *= tint.a * _NormalStrength;
                        nrm[s] = half3(nxy, sqrt(saturate(1.0 - dot(nxy, nxy))));
                        mask[s] = SAMPLE_TEXTURE2D_ARRAY_GRAD(_MaskArray, sampler_MaskArray, luv, id, ldx, ldy);
                        h[s] = (mask[s].b + extra.x) * weight[s];
                        isEarth[s] = extra.y;
                    }
                    hmax = max(hmax, h[s]);
                }
                float hw[8]; float hsum = 0;
                [unroll] for (int k = 0; k < 8; k++) { hw[k] = (max(h[k] + _RichHeightTransition - hmax, 0.0) + 1e-5) * weight[k]; hsum += hw[k]; }
                half3 albedoMix = 0; half3 n = 0; half4 m = 0; float earthShare = 0;
                [unroll] for (int k3 = 0; k3 < 8; k3++)
                {
                    float f = hw[k3] / max(hsum, 1e-6);
                    albedoMix += albedo[k3] * f; n += nrm[k3] * f; m += mask[k3] * f; earthShare += isEarth[k3] * f;
                }

                float detailFade = saturate(1.0 - distance(i.positionWS, GetCameraPositionWS()) / max(_DetailDistance, 0.01));
                detailFade *= detailFade;
                [branch] if (detailFade > 0.001)
                {
                    float2 duv = i.positionWS.xz / max(_DetailSize, 0.01);
                    half4 dc = SAMPLE_TEXTURE2D(_CloseDetail, sampler_CloseDetail, duv);
                    half4 dnm = SAMPLE_TEXTURE2D(_CloseDetailNormal, sampler_CloseDetail, duv) * 2.0 - 1.0;
                    half amount = lerp(_DetailGrass, _DetailEarth, earthShare) * detailFade;
                    half tone = lerp(dc.g, dc.r, earthShare) * 2.0;
                    albedoMix *= lerp(1.0, tone, amount);
                    n.xy += lerp(dnm.ba, dnm.rg, earthShare) * _DetailNormal * amount;
                }

                float between = saturate(4.0 * earthShare * (1.0 - earthShare));
                half grey = dot(albedoMix, half3(0.3, 0.59, 0.11));
                albedoMix = lerp(albedoMix, lerp(albedoMix, grey.xxx, _BlendSoften) * (1.0 - _BlendDarken), between);

                n.z += 1e-3;
                half3 normalTS = normalize(n);
                half3 colour = albedoMix * lerp(1.0, m.r, _CavityStrength);
                half occlusion = lerp(1.0, m.g, _OcclusionStrength);
                half smoothness = m.a;

                float2 wp = i.positionWS.xz;
                half m1 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp / _MacroScaleLarge).r;
                half m2 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp / _MacroScaleMedium + 0.37).g;
                half m3 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp / _MacroScaleFine + 0.71).b;
                half patch = m1 * 0.55 + m2 * 0.45;
                colour *= 1.0 + (m1 - 0.5) * 2.0 * _MacroBrightness + (m3 - 0.5) * 2.0 * _MacroFine;
                colour = lerp(colour, colour * _MacroWarmColor.rgb, smoothstep(0.54, 0.70, patch) * _MacroWarm);
                colour = lerp(colour, colour * _MacroLushColor.rgb, smoothstep(0.46, 0.30, patch) * _MacroLush);
                half lum = dot(colour, half3(0.3, 0.59, 0.11));
                half3 graded = max(lerp(lum.xxx, colour, _GrassSaturation), 0) * _GrassTint.rgb;
                colour = lerp(colour, graded, 1.0 - earthShare);
                colour *= i.color.rgb;   // forest floor further out

                // The terrain's tangent frame (as URP's terrain builds it), so the layers' normal maps light alike.
                float3 normalWS = normalize(i.normalWS);
                float3 tangentWS = normalize(cross(float3(0, 0, 1), normalWS));
                float3 bitangentWS = cross(normalWS, tangentWS);
                InputData data = (InputData)0;
                data.positionWS = i.positionWS;
                data.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(-tangentWS, bitangentWS, normalWS)));
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                data.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                data.fogCoord = i.fogFactor;
                data.bakedGI = SampleSH(data.normalWS);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                data.shadowMask = half4(1, 1, 1, 1);
                // URP's PBR, as the terrain gets it, but with the sun taken as reaching here: URP leaves its per-object
                // "lit by the main light" flag (unity_LightData.z) at 0 for these generated meshes.
                half alpha = 1;
                BRDFData brdf;
                InitializeBRDFData(colour, 0, half3(0, 0, 0), smoothness, alpha, brdf);
                Light sun = GetMainLight(data.shadowCoord, data.positionWS, half4(1, 1, 1, 1));
                sun.distanceAttenuation = 1;
                half3 lit = GlobalIllumination(brdf, data.bakedGI, occlusion, data.positionWS, data.normalWS, data.viewDirectionWS)
                            + LightingPhysicallyBased(brdf, sun, data.normalWS, data.viewDirectionWS);

                float2 outside = max(max(_MapRect.xy - i.positionWS.xz, i.positionWS.xz - _MapRect.zw), 0);
                float reach = max(outside.x / _Reach.x, outside.y / _Reach.y);
                half haze = saturate((reach - _HazeStart) / max(_HazeEnd - _HazeStart, 1e-3)) * _HazeMax;
                half3 result = lerp(lit, unity_FogColor.rgb, haze);
                result = MixFog(result, i.fogFactor);
                return half4(result, 1);
            }
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
