Shader "Chick/PondWater"
{
 // Shallow, clear pond water for URP. Texture-free: procedural wind ripples, ring ripples from
 // FarmPond (_PondRipples), depth-based tint, a shore that clears into wet sand, refraction of the bed, sky reflection
 // and a sun glint. Needs the camera depth and opaque textures (FarmPond requests them).
 Properties {
  _ShallowColor("Shallow tint",Color)=(0.78,0.93,0.86,1)
  _DeepColor("Water colour",Color)=(0.11,0.33,0.34,1)
  _ColorDepth("Depth for full colour (m)",Float)=0.065
  _Opacity("Water colour strength",Range(0,1))=0.72
  _FoamColor("Shore foam",Color)=(0.94,0.98,0.96,1)
  _FoamAmount("Shore foam strength",Range(0,1))=0.55
  _FoamScale("Shore foam bubbles per metre",Float)=22
  _FoamReach("Shore foam reach (m of depth)",Float)=0.014
  _FoamWidth("Wet sand band (m of height)",Float)=0.02
  _EdgeFade("Shore fade (m of depth)",Float)=0.025
  _WaveScale("Wind ripple scale",Float)=2.6
  _WaveStrength("Wind ripple strength",Range(0,1))=0.4
  _WaveSpeed("Wind ripple speed",Float)=0.22
  _RippleStrength("Ring ripple strength",Range(0,2))=1
  _Refraction("Refraction",Range(0,0.08))=0.02
  _Reflection("Reflection",Range(0,1))=0.55
  _Glint("Sun glint",Range(0,4))=1.6
 }
 SubShader {
  Tags{"RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True"}
  Pass {
   Name "ForwardLit"
   Tags{"LightMode"="UniversalForward"}
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Back
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

   CBUFFER_START(UnityPerMaterial)
    half4 _ShallowColor,_DeepColor,_FoamColor;
    float _FoamAmount,_FoamScale,_FoamReach;
    float _ColorDepth,_Opacity,_FoamWidth,_EdgeFade,_WaveScale,_WaveStrength,_WaveSpeed,_RippleStrength,_Refraction,_Reflection,_Glint;
   CBUFFER_END
   // x, z, start time, strength; written by FarmPond.
   float4 _PondRipples[12];
   float _PondTime;

   // Vertex alpha: 0 on the outline of the surface, 1 a few cells in (PondShoreV2), so the surface's own edge,
   // which lies under the bank, never shows as a line where the bank dips.
   struct A {float4 p:POSITION;half4 c:COLOR;};
   struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float4 screen:TEXCOORD1;half fog:TEXCOORD2;half edge:TEXCOORD3;};
   V vert(A a)
   {
    V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);
    o.screen=ComputeScreenPos(o.p);o.fog=ComputeFogFactor(o.p.z);o.edge=a.c.a;return o;
   }

   float Hash(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
   // Value noise with its analytic gradient: (value, d/dx, d/dy).
   float3 NoiseD(float2 p)
   {
    float2 i=floor(p),f=frac(p),u=f*f*(3-2*f),du=6*f*(1-f);
    float a=Hash(i),b=Hash(i+float2(1,0)),c=Hash(i+float2(0,1)),d=Hash(i+1);
    float k=a-b-c+d;
    return float3(a+(b-a)*u.x+(c-a)*u.y+k*u.x*u.y,du*float2(b-a+k*u.y,c-a+k*u.x));
   }
   float2 Hash2(float2 p)
   {
    float3 q=frac(p.xyx*float3(.1031,.1030,.0973));q+=dot(q,q.yzx+33.33);
    return frac((q.xx+q.yz)*q.zy);
   }
   // Cellular noise with feature points wobbling in time: (nearest, second nearest) distance.
   float2 Voronoi(float2 p,float t)
   {
    float2 i=floor(p),f=frac(p);float f1=8,f2=8;
    [unroll] for(int y=-1;y<=1;y++)
    [unroll] for(int x=-1;x<=1;x++)
    {
     float2 g=float2(x,y),h=Hash2(i+g);
     float d=length(g+.5+.38*sin(t+6.2831*h)-f);
     if(d<f1){f2=f1;f1=d;}else if(d<f2)f2=d;
    }
    return float2(f1,f2);
   }
   float3 SceneWorld(float2 uv)
   {
    float raw=SampleSceneDepth(uv);
    #if !UNITY_REVERSED_Z
     raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
    #endif
    return ComputeWorldSpacePosition(uv,raw,UNITY_MATRIX_I_VP);
   }

   half4 frag(V i):SV_Target
   {
    float2 uv=i.screen.xy/i.screen.w;
    float3 viewDir=normalize(_WorldSpaceCameraPos-i.world);
    float camDist=distance(_WorldSpaceCameraPos,i.world);

    // Wind ripples: three drifting octaves; detail fades with distance so the surface stays calm far away.
    float t=_Time.y*_WaveSpeed;
    float2 p=i.world.xz*_WaveScale;
    float2 slope=NoiseD(p+t*float2(1,.6)).yz+NoiseD(p*1.9-t*float2(.7,-1.1)).yz*.55;
    slope+=NoiseD(p*4.3+t*float2(-.4,.9)).yz*.28*saturate(1.6-camDist*.08);
    slope*=_WaveStrength*.08*_WaveScale;

    // Ring ripples from steps and sips.
    float ring=0;
    [unroll] for(int k=0;k<12;k++)
    {
     float4 r=_PondRipples[k];
     float age=_PondTime-r.z;
     if(r.w<=0||age<0||age>2.2)continue;
     float2 d=i.world.xz-r.xy;
     float dist=max(length(d),1e-4);
     float x=dist-age*.3;
     float w=.012+age*.012;
     float env=exp(-x*x/(w*w))*r.w*_RippleStrength*pow(1-age/2.2,2)/(1+dist*6);
     float wave=sin(x*180);
     slope+=d/dist*env*(cos(x*180)*180*.0009-2*x/(w*w)*wave*.0009);
     ring+=env*abs(wave);
    }
    float3 n=normalize(float3(-slope.x,1,-slope.y));

    // What lies beneath: refract only when the offset sample is really behind the water.
    float3 bed=SceneWorld(uv);
    // The shore height: the water's own height over the bed (negative on the bank), nudged by slow, soft noise and a
    // gentle swell, so the edge wanders and breathes instead of following the terrain's straight triangle edges.
    // The noise is turned off the world axes so its grid never shows.
    float2 fp=float2(i.world.x*.8-i.world.z*.6,i.world.x*.6+i.world.z*.8);
    float shoreH=i.world.y-bed.y
     +(NoiseD(fp*1.4+_Time.y*float2(.03,.02)).x-.5)*.014
     +(NoiseD(fp*4.1-_Time.y*float2(.02,.04)+3.7).x-.5)*.006
     +sin(_Time.y*.7+fp.x*.35)*.0015;
    float waterDepth=max(0,shoreH);
    float2 offset=n.xz*_Refraction*saturate(waterDepth*15);
    float2 refrUV=uv+offset;
    float3 refrBed=SceneWorld(refrUV);
    if(refrBed.y>i.world.y){refrUV=uv;refrBed=bed;}
    float thickness=distance(i.world,refrBed);
    half3 below=SampleSceneColor(refrUV);

    Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
    half3 skyLight=max(SampleSH(half3(0,1,0)),half3(.03,.035,.04));
    half3 lit=skyLight+sun.color*saturate(sun.direction.y)*.55;

    // Stylised depth colour (the pond is only a few centimetres deep): the bed shows clearly at the
    // margin and fades into teal towards the middle and at glancing angles.
    half depthT=smoothstep(0,1,saturate(waterDepth/_ColorDepth));
    half glance=saturate((thickness-waterDepth)/(_ColorDepth*4));
    half body=saturate(depthT*_Opacity+glance*.2);
    half3 water=below*lerp(half3(1,1,1),_ShallowColor.rgb,saturate(waterDepth*60))*lerp(1,.8,depthT);
    water=lerp(water,_DeepColor.rgb*lit,body);

    // Sky reflection with Fresnel, plus a tight sun glint.
    half nv=saturate(dot(n,viewDir));
    half fresnel=.02+.98*pow(1-nv,5);
    half3 R=reflect(-viewDir,n);
    half3 sky=GlossyEnvironmentReflection(R,.06h,1);
    sky=lerp(SampleSH(R),sky,.75);
    // The water thins out towards the shore: over the first _EdgeFade of depth it clears to the bed it lies on, and
    // its reflection and glint go with it, so no line marks the edge.
    half shore=smoothstep(0,_EdgeFade,waterDepth);
    shore*=shore;
    half3 color=lerp(water,sky,fresnel*_Reflection*shore);
    half3 h=normalize(sun.direction+viewDir);
    color+=sun.color*pow(saturate(dot(n,h)),350)*_Glint*sun.shadowAttenuation*shore;
    color+=_FoamColor.rgb*lit*saturate(ring*.9)*.18;

    // Wet sand: just above the waterline the bank is darker and a little richer, fading out over _FoamWidth of height.
    half wet=1-smoothstep(0,_FoamWidth,-shoreH);
    half3 sand=SampleSceneColor(uv);
    half3 wetSand=sand*lerp(half3(1,1,1),half3(.74,.7,.64),wet);
    color=lerp(wetSand,color,shore);

    // Shore foam: a soft lace of bubbles along the waterline, from two sizes of slowly wobbling cells (their edges;
    // the small ones break the large ones up, so it reads as bubbles, not cracks), with soft threads of varying width,
    // thicker right at the edge and thinning out within _FoamReach of depth, over a faint film at the very edge.
    // The reach swells and ebbs a little, so the foam laps in and out, and soft patches leave gaps in it. Only on the
    // water, and faded with distance so the fine lace never shimmers.
    half reach=_FoamReach*(.7+.6*NoiseD(fp*2.2+_Time.y*float2(.05,-.03)).x)*(1+.3*sin(_Time.y*.9+fp.y*.6));
    half foamZone=(1-smoothstep(0,reach,waterDepth))*smoothstep(-.004,.001,shoreH);
    float2 vor=Voronoi(fp*_FoamScale+_Time.y*float2(.05,.035),_Time.y*.7);
    float2 vor2=Voronoi(fp*_FoamScale*2.3-_Time.y*float2(.04,.06)+5.1,_Time.y*.9);
    float lace=min(vor.y-vor.x,(vor2.y-vor2.x)*1.7);
    float aa=max(fwidth(lace),1e-4);
    half thread=lerp(.025,.2,foamZone*foamZone)*(.65+.7*NoiseD(fp*_FoamScale*.6+2.3).x);
    half foam=(1-smoothstep(thread*.3,thread+aa*1.5,lace))*foamZone;
    foam=max(foam,foamZone*foamZone*.3);
    foam*=lerp(.35,1,smoothstep(.3,.62,NoiseD(fp*3.1-_Time.y*float2(.02,.05)+11).x));
    foam*=saturate(1.6-camDist*.06);
    color=lerp(color,_FoamColor.rgb*lit*1.05,saturate(foam*_FoamAmount));

    return half4(MixFog(color,i.fog),i.edge);
   }
   ENDHLSL
  }
 }
}
