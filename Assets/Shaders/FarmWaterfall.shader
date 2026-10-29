// Running water for the waterfall by the chicken yard (Tools/Chick/Map/Waterfall): the stream and the falling sheet
// it turns into, the thin sheet in front of it and the foam where it lands. Texture-free: two layers of value noise
// scroll along the flow and turn into white streaks, over a body that is deeper in the middle and paler at the edges,
// with foam lines along the banks and sun sparkles on the moving surface. The mesh carries the flow: uv.x runs across
// (0..1), uv.y along it in "flow metres" (metres scaled by how fast the water runs there, so one strip can run slow
// down the slope and fast over the edge and still join up), uv2.x along it from 0 at the start to 1 at the end (start
// and end foam, a fade), uv2.y extra white water (the edge of the fall, where it lands). On the landing foam, rings
// ripple outward. Lit softly by the sun and sky, fogged like the rest of the scene. Transparent, both sides, no shadows.
Shader "Farm/Waterfall"
{
    Properties
    {
        _DeepColor ("Water", Color) = (0.2, 0.55, 0.6, 1)
        _ShallowColor ("Thin Water", Color) = (0.62, 0.88, 0.9, 1)
        _FoamColor ("Foam", Color) = (0.95, 0.98, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.78
        _Speed ("Flow Speed (m/s)", Float) = 2.4
        _StreakScale ("Streaks Across, Along (per m)", Vector) = (7, 0.9, 0, 0)
        _Streaks ("Streak Foam", Range(0, 1)) = 0.75
        _StartFoam ("Foam At The Start", Range(0, 1)) = 0.6
        _EndFoam ("Foam At The End", Range(0, 1)) = 0.8
        _FoamLength ("Start / End Foam Length (share)", Range(0.01, 0.5)) = 0.12
        _EdgeFoam ("Foam Along The Banks", Range(0, 1)) = 0
        _EdgeFade ("Fade Across The Edges", Range(0, 1)) = 1
        _EdgeSoftness ("Edge Softness (share)", Range(0.01, 0.5)) = 0.18
        _FadeIn ("Fade In Over (share)", Range(0, 0.5)) = 0
        _FadeOut ("Fade Toward The End", Range(0, 1)) = 0
        _Rings ("Rings Rippling Outward", Range(0, 1)) = 0
        _Sparkle ("Sun Sparkles", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _FoamColor, _StreakScale;
                float _Opacity, _Speed, _Streaks, _StartFoam, _EndFoam, _FoamLength, _EdgeFoam, _EdgeFade, _EdgeSoftness;
                float _FadeIn, _FadeOut, _Rings, _Sparkle;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 flow : TEXCOORD1;   // along 0..1, extra white water
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                o.flow = i.uv2;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 p = float2(i.uv.x * _StreakScale.x, (i.uv.y - t) * _StreakScale.y);
                float n1 = Noise(p);
                float n2 = Noise(p * float2(2.3, 1.7) + float2(5.2, 1.3) - float2(0, t * 0.4));
                float n3 = Noise(p * float2(4.1, 3.3) + float2(1.7, 8.1));
                float streak = smoothstep(0.58, 0.8, n1 * 0.6 + n2 * 0.4) * _Streaks;

                float along = i.flow.x;
                float startFoam = (1 - smoothstep(0, _FoamLength, along)) * _StartFoam;
                float endFoam = smoothstep(1 - _FoamLength, 1, along) * _EndFoam;
                float edges = min(i.uv.x, 1 - i.uv.x);
                float bankFoam = (1 - smoothstep(0.04, 0.2 + 0.1 * n2, edges)) * _EdgeFoam;
                float rings = smoothstep(0.55, 0.9, sin((i.uv.y - t) * 9 + n2 * 2.5) * 0.5 + 0.5) * (1 - along) * _Rings;
                float foam = saturate(streak + (startFoam + endFoam + i.flow.y) * (0.6 + 0.4 * n2) + bankFoam * (0.5 + 0.5 * n3) + rings);

                // Deeper and darker in the middle, paler toward the edges, broken up by the moving noise.
                float middle = sin(saturate(i.uv.x) * 3.14159);
                half3 colour = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(middle * 0.8 + (n2 - 0.5) * 0.5));
                colour = lerp(colour, _FoamColor.rgb, foam);
                // Soft light: the sun's colour and the sky, without the per-object light flags.
                Light sun = GetMainLight();
                colour *= sun.color * 0.45 + SampleSH(float3(0, 1, 0)) * 0.9 + 0.15;

                // Sparkles: the sun caught on the moving surface, its normal ruffled by the noise.
                float3 n = normalize(i.normalWS) * (front ? 1 : -1);
                n = normalize(n + float3(n2 - 0.5, 0, n3 - 0.5) * 0.9);
                float3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float glint = pow(saturate(dot(reflect(-sun.direction, n), view)), 90) * smoothstep(0.55, 0.85, n3);
                colour += sun.color * glint * _Sparkle * (1 - foam);

                float across = lerp(1, smoothstep(0, _EdgeSoftness, i.uv.x) * smoothstep(0, _EdgeSoftness, 1 - i.uv.x), _EdgeFade);
                float alpha = lerp(_Opacity, 1, foam) * across * lerp(1, 1 - along, _FadeOut) * (_FadeIn > 0 ? smoothstep(0, _FadeIn, along) : 1);
                colour = MixFog(colour, i.fogFactor);
                return half4(colour, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
