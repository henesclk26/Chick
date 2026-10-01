// Stylized time-of-day sky, driven every frame by TimeOfDayVisualController. Sun-aware horizon glow and
// anti-sun twilight band, soft sun disk, two lit cloud layers (puffy banks + high cirrus), dusk stars and moon,
// dithered gradients.
Shader "Farm/Stylized Sky"
{
    Properties
    {
        _SkyColor ("Zenith", Color) = (0.16,0.48,0.83,1)
        _HorizonColor ("Horizon", Color) = (0.72,0.87,0.98,1)
        _HorizonBlendHeight ("Horizon Transition Height", Range(0.1, 1)) = 0.65
        _SunDirection ("Sun Direction (towards sun)", Vector) = (0,0.5,-1,0)
        _SunColor ("Sun Color", Color) = (1,0.95,0.85,1)
        _SunGlowColor ("Sun-side Horizon Glow", Color) = (1,0.6,0.35,1)
        _AntiSunColor ("Anti-sun Twilight Band", Color) = (0.85,0.6,0.72,1)
        _Twilight ("Twilight Amount", Range(0,1)) = 0
        _SunSize ("Sun Size", Range(0.005, 0.1)) = 0.028
        _SunIntensity ("Sun Intensity", Float) = 16
        _SunHaloIntensity ("Sun Halo Intensity", Float) = 0.8
        _SunHaloWidth ("Sun Halo Width (radians)", Float) = 0.035
        _CloudLitColor ("Cloud Lit", Color) = (1,1,1,1)
        _CloudShadowColor ("Cloud Shadow", Color) = (0.7,0.78,0.9,1)
        _CloudCoverage ("Cloud Coverage", Range(0,1)) = 0.45
        _CloudSpeed ("Cloud Drift Speed", Float) = 0.012
        _StarIntensity ("Stars", Range(0,1)) = 0
        _MoonDirection ("Moon Direction", Vector) = (0.4,0.35,0.8,0)
        _MoonIntensity ("Moon", Range(0,1)) = 0
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
            float4 _SkyColor, _HorizonColor, _SunColor, _SunGlowColor, _AntiSunColor, _CloudLitColor, _CloudShadowColor;
            float4 _SunDirection, _MoonDirection;
            float _HorizonBlendHeight, _Twilight, _SunSize, _SunIntensity, _SunHaloIntensity, _SunHaloWidth;
            float _CloudCoverage, _CloudSpeed, _StarIntensity, _MoonIntensity;
            struct v2f { float4 pos : SV_POSITION; float3 direction : TEXCOORD0; float4 screen : TEXCOORD1; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.direction = vertex.xyz; o.screen = ComputeScreenPos(o.pos); return o;
            }
            float hash(float3 p) { p = frac(p * .1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }
            float noise(float3 p)
            {
                float3 c = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(hash(c), hash(c + float3(1,0,0)), f.x), lerp(hash(c + float3(0,1,0)), hash(c + float3(1,1,0)), f.x), f.y);
                float b = lerp(lerp(hash(c + float3(0,0,1)), hash(c + float3(1,0,1)), f.x), lerp(hash(c + float3(0,1,1)), hash(c + float3(1,1,1)), f.x), f.y);
                return lerp(a, b, f.z);
            }
            float fbm(float3 p, int octaves)
            {
                float sum = 0, w = .5;
                [loop] for (int i = 0; i < octaves; i++) { sum += noise(p) * w; p = p * 2.03 + float3(17.1, 31.7, 47.3); w *= .5; }
                return sum / (1 - pow(.5, octaves));
            }
            // Puffy cloud density on the sky shell, 0..1.
            float cumulus(float3 p)
            {
                float3 warp = float3(noise(p * .7 + 11.3), noise(p * .7 + 29.1), noise(p * .7 + 53.7)) - .5;
                float3 q = p + warp * .35;
                float shape = fbm(q * 1.1, 5);
                float cover = lerp(.72, .42, _CloudCoverage);
                return saturate((shape - cover) / (1.0 - cover) * 1.8);
            }
            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.direction);
                float3 sun = normalize(_SunDirection.xyz);
                float up = saturate(d.y);
                float sunLow = saturate(1 - sun.y * 2.4);                 // 1 near the horizon, 0 at high noon

                // Base gradient, identical to the previous sky.
                float3 color = lerp(_HorizonColor.rgb, _SkyColor.rgb, smoothstep(0, max(.1, _HorizonBlendHeight), up));

                // Warm glow gathered around the sun's azimuth, strongest low on the horizon.
                float3 dh = normalize(float3(d.x, 0, d.z) + 1e-5), sh = normalize(float3(sun.x, 0, sun.z) + 1e-5);
                float azimuth = dot(dh, sh) * .5 + .5;
                float glow = pow(azimuth, 3) * exp(-up * 5.5) * sunLow;
                color = lerp(color, _SunGlowColor.rgb, saturate(glow * .9));
                // Pink/violet twilight band on the side facing away from the sun.
                float band = pow(1 - azimuth, 2) * exp(-abs(up - .07) * 16) * _Twilight;
                color = lerp(color, _AntiSunColor.rgb, saturate(band * .6));

                // Additive sun: retain the authored warmth near the horizon while keeping
                // the high sun close to white. The angular edge uses pixel derivatives.
                float sd = dot(d, sun);
                float sunAngle = acos(clamp(sd, -1.0, 1.0));
                float diskAA = max(fwidth(sunAngle), 1e-5);
                float disk = 1.0 - smoothstep(max(0.0, _SunSize - diskAA), _SunSize + diskAA, sunAngle);
                float haloWidth = max(_SunHaloWidth, 1e-4);
                float halo = exp(-max(sunAngle - _SunSize, 0.0) / haloWidth);
                float rimWidth = max(diskAA * 1.5, 0.0015);
                float rim = exp(-pow((sunAngle - _SunSize) / rimWidth, 2.0));
                float3 sunTint = lerp(float3(1, 1, 1), _SunColor.rgb, saturate(sunLow * .85));
                color += _SunColor.rgb * (halo * _SunHaloIntensity);
                color += sunTint * (rim * 0.35);
                color += sunTint * (disk * _SunIntensity);

                // Clouds on a curved shell (continuous at the horizon), drifting with the wind.
                float h = up;
                float shell = 25.0 / (sqrt(144.0 * h * h + 25.0) + 12.0 * h);
                float3 p = d * shell * 2.2;
                p.xz += _Time.y * _CloudSpeed * float2(1, .35);
                float dens = cumulus(p);
                // Light the banks: denser towards the sun means self-shadowed.
                float3 toSun = float3(sh.x, 0, sh.z) * .13;
                float densSun = cumulus(p + toSun);
                float lit = saturate(1 - (densSun - dens) * 3.0);
                lit = lerp(lit, 1, .12);
                float silver = pow(saturate(sd), 10) * (1 - dens) * 1.4;      // bright rims near the sun
                float3 cloudColor = lerp(_CloudShadowColor.rgb, _CloudLitColor.rgb, lit) + _SunColor.rgb * silver;
                cloudColor *= lerp(1, .86, smoothstep(.55, 1, dens));        // thick cores read darker
                // Undersides pick up the horizon glow at sunrise/sunset.
                cloudColor = lerp(cloudColor, cloudColor * _SunGlowColor.rgb * 1.15, (1 - lit) * sunLow * .6);
                float horizonFade = smoothstep(.02, .2, h);
                float cloudAlpha = smoothstep(0, .35, dens) * horizonFade;

                // Thin, high cirrus streaks.
                float3 c = d * shell * 1.4; c.xz += _Time.y * _CloudSpeed * 1.6 * float2(1, .2);
                float cirrus = fbm(c * float3(.9, 1, 3.4) + 91.7, 4);
                cirrus = saturate((cirrus - .55) * 2.6) * smoothstep(.55, .85, noise(c * .6 + 5.1)) * horizonFade * .45;

                // Stars and moon fade in at dusk, hidden by clouds.
                float3 cell = floor(d * 180);
                float star = step(.9975, hash(cell)) * (.6 + .4 * sin(_Time.y * 2 + hash(cell + 7) * 40));
                float3 moonDir = normalize(_MoonDirection.xyz);
                float md = dot(d, moonDir);
                float moon = smoothstep(cos(.034), cos(.03), md);
                float moonShade = .75 + .25 * noise(d * 90);
                color += star * _StarIntensity * smoothstep(.05, .3, up) * (1 - cloudAlpha);
                color = lerp(color, float3(.95, .93, .88) * moonShade, moon * _MoonIntensity * (1 - cloudAlpha));
                color += float3(.6, .62, .75) * pow(saturate(md), 400) * .25 * _MoonIntensity;

                color = lerp(color, _CloudLitColor.rgb * lerp(1, .95, lit), cirrus * (1 - cloudAlpha));
                color = lerp(color, cloudColor, cloudAlpha);

                // Below the horizon: gently darkened horizon colour.
                color = lerp(color, _HorizonColor.rgb * .92, saturate(-d.y * 6));
                // Dither to remove gradient banding.
                float2 sp = i.screen.xy / max(i.screen.w, 1e-5) * _ScreenParams.xy;
                color += (frac(sin(dot(sp, float2(12.9898, 78.233))) * 43758.5453) - .5) / 255.0;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
