Shader "Farm/Soft Day Sky"
{
    Properties
    {
        _SkyColor ("Sky", Color) = (0.16,0.48,0.83,1)
        _HorizonColor ("Horizon", Color) = (0.72,0.87,0.98,1)
        _CloudColor ("Clouds", Color) = (1,1,1,1)
        _HorizonBlendHeight ("Horizon Transition Height", Range(0.1, 1)) = 0.65
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _SkyColor, _HorizonColor, _CloudColor;
            float _HorizonBlendHeight;
            struct v2f { float4 pos:SV_POSITION; float3 direction:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION) { v2f o; o.pos=UnityObjectToClipPos(vertex);o.direction=vertex.xyz;return o; }
            float hash(float3 p)
            {
                p = frac(p * .1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float noise(float3 p)
            {
                float3 cell = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float lower = lerp(
                    lerp(hash(cell), hash(cell + float3(1,0,0)), f.x),
                    lerp(hash(cell + float3(0,1,0)), hash(cell + float3(1,1,0)), f.x), f.y);
                float upper = lerp(
                    lerp(hash(cell + float3(0,0,1)), hash(cell + float3(1,0,1)), f.x),
                    lerp(hash(cell + float3(0,1,1)), hash(cell + float3(1,1,1)), f.x), f.y);
                return lerp(lower, upper, f.z);
            }
            float cloudNoise(float3 p)
            {
                // Multi-scale detail gives thin broken fibres rather than a
                // blurred silhouette or a hard, uniformly white cutout.
                float sum = 0;
                float weight = .50794;
                float footprint = max(length(ddx(p)), length(ddy(p)));
                [unroll] for (int octave = 0; octave < 6; octave++)
                {
                    float resolved = 1.0 - smoothstep(.35, .9, footprint);
                    sum += lerp(.5, noise(p), resolved) * weight;
                    p = p * 2.03 + float3(17.1, 31.7, 47.3);
                    footprint *= 2.03;
                    weight *= .5;
                }
                return sum;
            }
            half4 frag(v2f i):SV_Target
            {
                float3 d=normalize(i.direction);
                float h=saturate(d.y);
                float3 color=lerp(_HorizonColor.rgb,_SkyColor.rgb,smoothstep(0,max(.1,_HorizonBlendHeight),h));
                // Intersect a broad, gently curved shell (radius 13, centre 12 below
                // the viewer). Unlike xz / max(y, .15), this stays continuous
                // at the horizon: no clamped UV band or downward cloud streaks.
                float shellDistance = 25.0 / (sqrt(144.0 * h * h + 25.0) + 12.0 * h);
                float3 p = d * shellDistance * 2.2;
                p += float3(_Time.y * .004, 0, 0);
                // Gently bend the wind-aligned fibres; both layers live on the
                // continuous shell, so there is no UV seam or horizon clamp.
                float3 warp = float3(noise(p * .8 + 11.3),
                    noise(p * .8 + 29.1), noise(p * .8 + 53.7)) - .5;
                float3 q = p + warp * .2;
                // Isolated rounded banks with detailed edges and broad blue gaps.
                float banks = noise(q * 1.4 + 7.9) * .72
                    + noise(q * 2.9 + 23.4) * .28;
                float detail = cloudNoise(q * 4.0);
                float body = banks + (detail - .5) * .18;
                float edgeWidth = max(.075, fwidth(body));
                float patches = smoothstep(.74 - edgeWidth, .74 + edgeWidth, body);
                float fibres = cloudNoise(q * float3(1.2, 2.8, 3.2) + 83.1);
                // Localised translucent trails in the gaps, never a full-sky veil.
                float wispMask = smoothstep(.58, .82, noise(p * .7 + 61.2));
                float wisps = pow(saturate((fibres - .44) * 3.0), 1.7) * wispMask;
                float density = patches * (1.0 + detail * .8)
                    + wisps * .27 * (1.0 - patches);

                // Distant clouds dissolve into the existing time-of-day horizon
                // colour. Below the horizon the sky is completely cloud-free.
                float horizonFade = smoothstep(.025, .22, h);
                float clouds = (1.0 - exp2(-density * 2.0)) * horizonFade;
                float3 cloudShade = lerp(_SkyColor.rgb, _CloudColor.rgb, .88);
                float3 cloudColor = lerp(_CloudColor.rgb, cloudShade,
                    saturate(patches * (1.0 - detail)));
                color = lerp(color, cloudColor, clouds);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
