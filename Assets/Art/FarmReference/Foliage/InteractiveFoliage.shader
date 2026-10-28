// URP Lit that leans away from the player. Lighting, shadows, SSAO and Forward+ come straight from the URP
// Lit passes; only the vertex position is bent (see FoliageBend.hlsl). Properties mirror URP Lit so the
// stock Lit inspector and existing Lit material settings carry over unchanged.
Shader "Chick/InteractiveFoliage"
{
    Properties
    {
        // Specular vs Metallic workflow
        _WorkflowMode("WorkflowMode", Float) = 1.0

        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)

        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0

        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}

        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap("Specular", 2D) = "white" {}

        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1.0

        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}

        _Parallax("Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}

        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}

        [HDR] _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}

        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailAlbedoMap("Detail Albedo x2", 2D) = "linearGrey" {}
        _DetailNormalMapScale("Scale", Range(0.0, 2.0)) = 1.0
        [Normal] _DetailNormalMap("Normal Map", 2D) = "bump" {}

        // SRP batching compatibility for Clear Coat (Not used in Lit)
        [HideInInspector] _ClearCoatMask("_ClearCoatMask", Float) = 0.0
        [HideInInspector] _ClearCoatSmoothness("_ClearCoatSmoothness", Float) = 0.0

        // Blending state
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        [HideInInspector] _AddPrecomputedVelocity("_AddPrecomputedVelocity", Float) = 0.0
        [HideInInspector] _XRMotionVectorsPass("_XRMotionVectorsPass", Float) = 1.0

        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        // Editmode props
        _QueueOffset("Queue offset", Float) = 0.0

        // ObsoleteProperties
        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _GlossMapScale("Smoothness", Float) = 0.0
        [HideInInspector] _Glossiness("Smoothness", Float) = 0.0
        [HideInInspector] _GlossyReflections("EnvironmentReflections", Float) = 0.0

        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex FoliageLitVertex
            #pragma shader_feature_local_vertex _FOLIAGE_BEND_LOCAL
            #pragma fragment FoliageLitFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _GROUND_TINT
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _SPECULAR_SETUP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            // the material's share of the lean: _DetailNormalMapScale (no detail maps on foliage; it sits in the
            // material constant buffer, so the SRP batcher still works). 1 = the grass's full lean; flowers lie over less.
            #define FOLIAGE_BEND_SHARE saturate(_DetailNormalMapScale)
            // a partial lean (the flowers) leans only away from the bird; the grass's full lean also follows its walk
            #define FOLIAGE_BEND_WALK (_DetailNormalMapScale >= 0.999 ? 1.0 : 0.0)
            #include "FoliageBend.hlsl"

            Varyings FoliageLitVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                input.positionOS.xyz = FoliageBendOS(input.positionOS.xyz);
                return LitPassVertex(input);
            }

            // Optional ground tint (_GROUND_TINT): the plant takes the colour of the ground under it, read from a
            // top-down ground colour map. The map and its settings are global (GroundTintGlobals), so materials
            // without the keyword and the SRP batcher are unaffected.
            #if defined(_GROUND_TINT)
            TEXTURE2D(_ChickGroundTintMap); SAMPLER(sampler_ChickGroundTintMap);
            float4 _ChickGroundTintRect;    // xy: world min (x, z), zw: 1 / world size
            float4 _ChickGroundTintParams;  // x: strength, y: brightness, z: saturation
            float4 _ChickGroundTintDry;     // rgb: the golden grass colour over earth; a: the earth share where a blade is half golden
            #endif

            // URP's LitPassFragment with the ground tint applied to the albedo
            void FoliageLitFragment(
                Varyings input
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);

            #if defined(_GROUND_TINT)
                // the map: rgb = the grass colour of the ground here, a = how much of the ground here is earth
                float2 guv = (input.positionWS.xz - _ChickGroundTintRect.xy)*_ChickGroundTintRect.zw;
                half4 groundMap = SAMPLE_TEXTURE2D(_ChickGroundTintMap, sampler_ChickGroundTintMap, guv);
                half3 green = groundMap.rgb;
                half lum = dot(green, half3(0.3, 0.59, 0.11));
                green = max(lerp(lum.xxx, green, _ChickGroundTintParams.z), 0)*_ChickGroundTintParams.y;
                // every blade is green over grass and golden over earth, so a clump on the edge turns from one to the other
                half earth = smoothstep(_ChickGroundTintDry.a - 0.17, _ChickGroundTintDry.a + 0.17, groundMap.a);
                half3 target = lerp(green, _ChickGroundTintDry.rgb, earth);
                half3 shade = surfaceData.albedo/max(_BaseColor.rgb, half3(1e-3, 1e-3, 1e-3));
                // per material amount: _DetailAlbedoMapScale (unused here, no detail maps; it lives in the material
                // constant buffer, so the SRP batcher still works): 1 = the global strength
                surfaceData.albedo = lerp(surfaceData.albedo, shade*target, saturate(_ChickGroundTintParams.x*_DetailAlbedoMapScale));
            #endif

            #ifdef LOD_FADE_CROSSFADE
                LODFadeCrossFade(input.positionCS);
            #endif

                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

            #if defined(_DBUFFER)
                ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
            #endif

                InitializeBakedGIData(input, inputData);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));
                outColor = color;

            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex FoliageShadowVertex
            #pragma shader_feature_local_vertex _FOLIAGE_BEND_LOCAL
            #pragma fragment ShadowPassFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            // the material's share of the lean: _DetailNormalMapScale (no detail maps on foliage; it sits in the
            // material constant buffer, so the SRP batcher still works). 1 = the grass's full lean; flowers lie over less.
            #define FOLIAGE_BEND_SHARE saturate(_DetailNormalMapScale)
            // a partial lean (the flowers) leans only away from the bird; the grass's full lean also follows its walk
            #define FOLIAGE_BEND_WALK (_DetailNormalMapScale >= 0.999 ? 1.0 : 0.0)
            #include "FoliageBend.hlsl"

            Varyings FoliageShadowVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                input.positionOS.xyz = FoliageBendOS(input.positionOS.xyz);
                return ShadowPassVertex(input);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex FoliageDepthVertex
            #pragma shader_feature_local_vertex _FOLIAGE_BEND_LOCAL
            #pragma fragment DepthOnlyFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            // the material's share of the lean: _DetailNormalMapScale (no detail maps on foliage; it sits in the
            // material constant buffer, so the SRP batcher still works). 1 = the grass's full lean; flowers lie over less.
            #define FOLIAGE_BEND_SHARE saturate(_DetailNormalMapScale)
            // a partial lean (the flowers) leans only away from the bird; the grass's full lean also follows its walk
            #define FOLIAGE_BEND_WALK (_DetailNormalMapScale >= 0.999 ? 1.0 : 0.0)
            #include "FoliageBend.hlsl"

            Varyings FoliageDepthVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                input.position.xyz = FoliageBendOS(input.position.xyz);
                return DepthOnlyVertex(input);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex FoliageDepthNormalsVertex
            #pragma shader_feature_local_vertex _FOLIAGE_BEND_LOCAL
            #pragma fragment DepthNormalsFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            // the material's share of the lean: _DetailNormalMapScale (no detail maps on foliage; it sits in the
            // material constant buffer, so the SRP batcher still works). 1 = the grass's full lean; flowers lie over less.
            #define FOLIAGE_BEND_SHARE saturate(_DetailNormalMapScale)
            // a partial lean (the flowers) leans only away from the bird; the grass's full lean also follows its walk
            #define FOLIAGE_BEND_WALK (_DetailNormalMapScale >= 0.999 ? 1.0 : 0.0)
            #include "FoliageBend.hlsl"

            Varyings FoliageDepthNormalsVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                input.positionOS.xyz = FoliageBendOS(input.positionOS.xyz);
                return DepthNormalsVertex(input);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
    CustomEditor "UnityEditor.Rendering.Universal.ShaderGUI.LitShader"
}
