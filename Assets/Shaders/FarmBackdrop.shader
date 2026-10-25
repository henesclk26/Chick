// The trees of the scenery around the map (Map/Backdrop Forest): vertex-coloured low-poly trees, softly lit by the sun
// (with its shadows) and the sky, and hazed into the horizon colour the further out from the map they stand. The haze
// is this material's own (on top of the game's distance fog), so the map itself stays clear. Its colour follows the
// day: TimeOfDayVisualController sets the fog colour to the sky's horizon. The ground is Farm/Backdrop Ground.
// Set up by Tools/Chick/Map/Add Backdrop Forest.
Shader "Farm/Backdrop"
{
    Properties
    {
        _MapRect ("Map Rect (x min, z min, x max, z max)", Vector) = (0, 0, 1, 1)
        _Reach ("Scenery Reach Out From The Map (x, z)", Vector) = (35, 90, 0, 0)
        _HazeStart ("Haze Starts (share of the reach)", Range(0, 1)) = 0.3
        _HazeEnd ("Haze Full (share of the reach)", Range(0, 1.5)) = 1
        _HazeMax ("Haze At Its Fullest", Range(0, 1)) = 0.55
        _Wrap ("Light Wrap (softness)", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _HazeStart, _HazeEnd, _HazeMax, _Wrap;
            float4 _MapRect, _Reach;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half4 color : COLOR; float fogFactor : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                half3 albedo = i.color.rgb;

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse = saturate((dot(n, sun.direction) + _Wrap) / (1 + _Wrap));
                half3 colour = albedo * (sun.color * diffuse * sun.shadowAttenuation + SampleSH(n));

                // How far out into the scenery: 0 at the map's edge, 1 at the scenery's far side.
                float2 outside = max(max(_MapRect.xy - i.positionWS.xz, i.positionWS.xz - _MapRect.zw), 0);
                float reach = max(outside.x / _Reach.x, outside.y / _Reach.y);
                half haze = saturate((reach - _HazeStart) / max(_HazeEnd - _HazeStart, 1e-3)) * _HazeMax;
                colour = lerp(colour, unity_FogColor.rgb, haze);
                colour = MixFog(colour, i.fogFactor);   // and the game's own distance fog, like everything else
                return half4(colour, 1);
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
