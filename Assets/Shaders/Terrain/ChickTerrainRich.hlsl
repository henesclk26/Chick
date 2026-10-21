#ifndef CHICK_TERRAIN_RICH_INCLUDED
#define CHICK_TERRAIN_RICH_INCLUDED

// The farm ground in one pass: the twenty terrain layers come from texture arrays (colour, normal, mask), the four
// strongest at each pixel are mixed by their painted height, so grass stands over earth with a ragged edge instead of
// a soft smear; the cavity of each layer darkens its cracks and the feet of its pebbles, and a world-space macro
// texture lays large sunny and lush patches over everything. The layer colours themselves are used unchanged.

TEXTURE2D_ARRAY(_AlbedoArray);  SAMPLER(sampler_AlbedoArray);
TEXTURE2D_ARRAY(_NormalArray);  SAMPLER(sampler_NormalArray);
TEXTURE2D_ARRAY(_MaskArray);    SAMPLER(sampler_MaskArray);
TEXTURE2D(_LayerData);
TEXTURE2D(_Ctrl0); SAMPLER(sampler_Ctrl0);
TEXTURE2D(_Ctrl1);
TEXTURE2D(_Ctrl2);
TEXTURE2D(_Ctrl3);
TEXTURE2D(_Ctrl4);
TEXTURE2D(_MacroTex); SAMPLER(sampler_MacroTex);

float _LayerCount;
float _CtrlRes;
float _RichHeightTransition;
float _NormalStrength;
float _CavityStrength;
float _OcclusionStrength;
float _MacroScaleLarge;
float _MacroScaleMedium;
float _MacroScaleFine;
float _MacroBrightness;
float _MacroFine;
float _MacroWarm;
float _MacroLush;
float4 _MacroWarmColor;
float _BlendSoften;
float _BlendDarken;
float4 _MacroLushColor;

struct RichSurface
{
    half3 albedo;
    half3 normalTS;
    half occlusion;
    half smoothness;
};

RichSurface ComputeRichSurface(float2 uv, float3 positionWS)
{
    // all twenty weights
    float2 ctrlUV = (uv*(_CtrlRes - 1.0) + 0.5)/_CtrlRes;
    float4 c[5];
    c[0] = SAMPLE_TEXTURE2D(_Ctrl0, sampler_Ctrl0, ctrlUV);
    c[1] = SAMPLE_TEXTURE2D(_Ctrl1, sampler_Ctrl0, ctrlUV);
    c[2] = SAMPLE_TEXTURE2D(_Ctrl2, sampler_Ctrl0, ctrlUV);
    c[3] = SAMPLE_TEXTURE2D(_Ctrl3, sampler_Ctrl0, ctrlUV);
    c[4] = SAMPLE_TEXTURE2D(_Ctrl4, sampler_Ctrl0, ctrlUV);

    // the five strongest; each counts only by how far it stands above the sixth, so a layer fades out exactly
    // as it leaves the five and no seam shows where the strongest set changes
    float best[6] = { -1.0, -1.0, -1.0, -1.0, -1.0, -1.0 };
    int id[6] = { 0, 0, 0, 0, 0, 0 };
    [unroll] for (int i = 0; i < 20; i++)
    {
        float w = c[i >> 2][i & 3];
        if (i >= (int)_LayerCount) w = 0;
        int n = i;
        [unroll] for (int k = 0; k < 6; k++)
        {
            if (w > best[k]) { float tw = best[k]; int tn = id[k]; best[k] = w; id[k] = n; w = tw; n = tn; }
        }
    }
    float floorW = max(best[5], 0.0);
    float weight[5];
    float wsum = 0;
    [unroll] for (int j = 0; j < 5; j++) { weight[j] = max(best[j] - floorW, 0.0); wsum += weight[j]; }
    [unroll] for (int j2 = 0; j2 < 5; j2++) weight[j2] /= max(wsum, 1e-5);

    // one gradient for every layer, so mip selection stays smooth where the strongest set changes
    float2 dx = ddx(uv), dy = ddy(uv);

    half3 albedo[5]; half3 nrm[5]; half4 mask[5]; float h[5]; float isEarth[5];
    float hmax = 0;
    [unroll] for (int s = 0; s < 5; s++)
    {
        albedo[s] = 0; nrm[s] = half3(0, 0, 1); mask[s] = half4(1, 1, 0, 0); h[s] = 0; isEarth[s] = 0;
        [branch] if (weight[s] > 0.001)
        {
            float4 st = LOAD_TEXTURE2D(_LayerData, int2(id[s], 0));      // xy: tiling over the terrain, zw: offset
            float4 tint = LOAD_TEXTURE2D(_LayerData, int2(id[s], 1));    // rgb: colour remap, a: normal strength
            float4 extra = LOAD_TEXTURE2D(_LayerData, int2(id[s], 2));   // x: height offset
            float2 luv = uv*st.xy + st.zw;
            float2 ldx = dx*st.xy, ldy = dy*st.xy;
            albedo[s] = SAMPLE_TEXTURE2D_ARRAY_GRAD(_AlbedoArray, sampler_AlbedoArray, luv, id[s], ldx, ldy).rgb*tint.rgb;
            half2 nxy = SAMPLE_TEXTURE2D_ARRAY_GRAD(_NormalArray, sampler_NormalArray, luv, id[s], ldx, ldy).rg*2.0 - 1.0;
            nxy *= tint.a*_NormalStrength;
            nrm[s] = half3(nxy, sqrt(saturate(1.0 - dot(nxy, nxy))));
            mask[s] = SAMPLE_TEXTURE2D_ARRAY_GRAD(_MaskArray, sampler_MaskArray, luv, id[s], ldx, ldy);
            h[s] = (mask[s].b + extra.x)*weight[s];
            isEarth[s] = extra.y;
        }
        hmax = max(hmax, h[s]);
    }

    // height-based mix: the higher ground wins the shared texels, with a soft rim of _RichHeightTransition
    float hw[5]; float hsum = 0;
    [unroll] for (int k2 = 0; k2 < 5; k2++) { hw[k2] = (max(h[k2] + _RichHeightTransition - hmax, 0.0) + 1e-5)*weight[k2]; hsum += hw[k2]; }

    half3 albedoMix = 0; half3 n = 0; half4 m = 0; float earthShare = 0;
    [unroll] for (int k3 = 0; k3 < 5; k3++)
    {
        float f = hw[k3]/max(hsum, 1e-6);
        albedoMix += albedo[k3]*f; n += nrm[k3]*f; m += mask[k3]*f; earthShare += isEarth[k3]*f;
    }

    // where grass and earth mix, a calmer, darker olive instead of the bright yellow a plain mix of green and orange gives
    float between = saturate(4.0*earthShare*(1.0 - earthShare));
    half grey = dot(albedoMix, half3(0.3, 0.59, 0.11));
    albedoMix = lerp(albedoMix, lerp(albedoMix, grey.xxx, _BlendSoften)*(1.0 - _BlendDarken), between);

    RichSurface o;
    o.albedo = albedoMix;
    n.z += 1e-3;
    o.normalTS = normalize(n);
    o.albedo *= lerp(1.0, m.r, _CavityStrength);
    o.occlusion = lerp(1.0, m.g, _OcclusionStrength);
    o.smoothness = m.a;

    // large sunny / lush patches and a fine mottle, in world space, over everything
    float2 wp = positionWS.xz;
    half m1 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp/_MacroScaleLarge).r;
    half m2 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp/_MacroScaleMedium + 0.37).g;
    half m3 = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp/_MacroScaleFine + 0.71).b;
    half patch = m1*0.55 + m2*0.45;
    o.albedo *= 1.0 + (m1 - 0.5)*2.0*_MacroBrightness + (m3 - 0.5)*2.0*_MacroFine;
    o.albedo = lerp(o.albedo, o.albedo*_MacroWarmColor.rgb, smoothstep(0.54, 0.70, patch)*_MacroWarm);
    o.albedo = lerp(o.albedo, o.albedo*_MacroLushColor.rgb, smoothstep(0.46, 0.30, patch)*_MacroLush);
    return o;
}

#ifdef CHICK_TERRAIN_RICH_FORWARD
void RichFragment(
    Varyings IN
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
    )
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
#ifdef _ALPHATEST_ON
    ClipHoles(IN.uvMainAndLM.xy);
#endif
    RichSurface s = ComputeRichSurface(IN.uvMainAndLM.xy, IN.positionWS);

    InputData inputData;
    InitializeInputData(IN, s.normalTS, inputData);
    SetupTerrainDebugTextureData(inputData, IN.uvMainAndLM.xy);

    half3 albedo = s.albedo;
    half metallic = 0;
    half occlusion = s.occlusion;
    half smoothness = s.smoothness;
#if defined(_DBUFFER)
    half3 specular = half3(0.0h, 0.0h, 0.0h);
    ApplyDecal(IN.clipPos, albedo, specular, inputData.normalWS, metallic, occlusion, smoothness);
#endif
    InitializeBakedGIData(IN, inputData);

    half4 color = UniversalFragmentPBR(inputData, albedo, metallic, half3(0.0h, 0.0h, 0.0h), smoothness, occlusion, half3(0, 0, 0), 1.0h);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    outColor = half4(color.rgb, 1.0h);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}
#endif

#ifdef CHICK_TERRAIN_RICH_DEPTHNORMALS
void RichDepthNormalsFragment(
    VaryingsDepthNormal IN
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
    )
{
#ifdef _ALPHATEST_ON
    ClipHoles(IN.uvMainAndLM.xy);
#endif
    RichSurface s = ComputeRichSurface(IN.uvMainAndLM.xy, float3(0, 0, 0));
    half3 normalWS = TransformTangentToWorld(s.normalTS, half3x3(-IN.tangent.xyz, IN.bitangent.xyz, IN.normal.xyz));
    outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}
#endif

#endif
