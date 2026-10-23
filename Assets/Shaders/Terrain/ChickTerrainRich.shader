// The farm ground: URP's terrain lighting with Chick's one-pass, height-blended mix of all twenty layers, their
// normal / cavity / occlusion maps and a world-space macro variation (see ChickTerrainRich.hlsl).
// Set up by Tools/Chick/Ground/Rich Ground Surface (V1).
Shader "Chick/Terrain Rich"
{
    Properties
    {
        _AlbedoArray("Layer Colours (array)", 2DArray) = "" {}
        _NormalArray("Layer Normals (array, RG)", 2DArray) = "" {}
        _MaskArray("Layer Masks (array: cavity, AO, height, smoothness)", 2DArray) = "" {}
        [NoScaleOffset] _LayerData("Layer Data", 2D) = "black" {}
        [NoScaleOffset] _Ctrl0("Splat 0-3", 2D) = "red" {}
        [NoScaleOffset] _Ctrl1("Splat 4-7", 2D) = "black" {}
        [NoScaleOffset] _Ctrl2("Splat 8-11", 2D) = "black" {}
        [NoScaleOffset] _Ctrl3("Splat 12-15", 2D) = "black" {}
        [NoScaleOffset] _Ctrl4("Splat 16-19", 2D) = "black" {}
        _LayerCount("Layer Count", Float) = 20
        _CtrlRes("Splat Resolution", Float) = 1024

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

        [NoScaleOffset] _CloseDetail("Close-up Detail (R earth, G grass; 0.5 = none)", 2D) = "grey" {}
        [NoScaleOffset] _CloseDetailNormal("Close-up Detail Relief (RG earth, BA grass)", 2D) = "grey" {}
        _DetailSize("Close-up Detail Tile (m)", Float) = 0.6
        _DetailEarth("Close-up Detail on Earth", Range(0, 1.5)) = 1
        _DetailGrass("Close-up Detail on Grass", Range(0, 1.5)) = 0.6
        _DetailNormal("Close-up Detail Relief", Range(0, 2)) = 1
        _DetailDistance("Close-up Detail Distance (m)", Float) = 8

        // set by the terrain engine
        [HideInInspector] _Control("Control (RGBA)", 2D) = "red" {}
        [HideInInspector] _MainTex("BaseMap (RGB)", 2D) = "grey" {}
        [HideInInspector] _BaseColor("Main Color", Color) = (1,1,1,1)
        [HideInInspector] _TerrainHolesTexture("Holes Map (RGB)", 2D) = "white" {}
    }

    HLSLINCLUDE
    #pragma multi_compile_fragment __ _ALPHATEST_ON
    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Geometry-100" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "False" "TerrainCompatible" = "True"}

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SplatmapVert
            #pragma fragment RichFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #define _NORMALMAP 1
            #define CHICK_TERRAIN_RICH_FORWARD 1
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
            #include "ChickTerrainRich.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags{"LightMode" = "DepthOnly"}
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags{"LightMode" = "DepthNormals"}
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalOnlyVertex
            #pragma fragment RichDepthNormalsFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #define _NORMALMAP 1
            #define CHICK_TERRAIN_RICH_DEPTHNORMALS 1
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitDepthNormalsPass.hlsl"
            #include "ChickTerrainRich.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #define SCENESELECTIONPASS
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
            ENDHLSL
        }

        UsePass "Hidden/Nature/Terrain/Utilities/PICKING"
    }
    // one pass draws all twenty layers, so no add pass; far patches use this same shader
    Dependency "BaseMapShader" = "Chick/Terrain Rich"
    Dependency "BaseMapGenShader" = "Hidden/Universal Render Pipeline/Terrain/Lit (Basemap Gen)"

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
