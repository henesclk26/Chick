#ifndef CHICK_TREE_SWAY_INCLUDED
#define CHICK_TREE_SWAY_INCLUDED

// A light breeze for trees: the whole tree leans to and fro about its pivot (the foot of the trunk), more the higher
// up (about 11 cm at the top of a 10 m tree, at most half again that in a gust), each tree on its own beat from where
// it stands, and the leaves flutter a little on top; in a gust (_ChickWindGust) it bows downwind and tosses there.
// Trunk and crown move together, so branches stay on the trunk.
// Per material, from Lit properties this shader has no other use for (they sit in the material constant buffer, so
// the SRP batcher still works): TREE_SWAY_SHARE the share of the sway (1 = full), TREE_FLUTTER_SHARE the leaf flutter
// (1 on leaves, 0 on bark). Needs the object's own transform, so the trees must not be statically batched.

#define TREE_SWAY_SCALE 0.0035      // metres of lean per metre^1.5 of height
#define TREE_SWAY_SPEED 0.9         // radians per second of the main sway
#define TREE_FLUTTER 0.02           // metres of leaf flutter

// The passing gust (WindGusts): xy its direction on the ground, z its strength (0 calm, 1 at its height). A global,
// so it costs the SRP batcher nothing; zero (no gust) when nothing sets it.
float4 _ChickWindGust;

float3 TreeSwayOS(float3 positionOS)
{
    float3 pivotWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float3 positionWS = TransformObjectToWorld(positionOS);
    float height = max(positionWS.y - pivotWS.y, 0.0);
    float phase = dot(pivotWS.xz, float2(0.173, 0.291));
    float t = _Time.y;

    // the breeze comes and goes, and each tree is on its own beat
    float gust = 0.7 + 0.3*sin(t*0.21 + phase*0.5);
    float lean = TREE_SWAY_SCALE*height*sqrt(height)*TREE_SWAY_SHARE*gust;
    float2 along = float2(0.82, 0.57), across = float2(-0.57, 0.82);
    float2 sway = along*(sin(t*TREE_SWAY_SPEED + phase) + 0.35*sin(t*TREE_SWAY_SPEED*2.3 + phase*1.9))
                + across*0.3*sin(t*TREE_SWAY_SPEED*1.4 + phase*2.3);
    positionWS.xz += sway*lean;

    // in a gust the tree bows downwind and tosses there, and the leaves shake harder
    float gustNow = _ChickWindGust.z;
    float bow = TREE_SWAY_SCALE*height*sqrt(height)*TREE_SWAY_SHARE*gustNow;
    positionWS.xz += _ChickWindGust.xy*bow*(1.6 + 0.7*sin(t*2.6 + phase*1.3));

    // leaves: a quick small flutter, out of step from leaf to leaf, none at the foot
    float flutter = TREE_FLUTTER*(1.0 + 1.5*gustNow)*TREE_FLUTTER_SHARE*saturate(height*0.3)*sin(t*(3.1 + 2.0*gustNow) + dot(positionWS, float3(1.7, 2.3, 1.3)));
    positionWS += float3(0.6, 0.35, 0.7)*flutter;

    return TransformWorldToObject(positionWS);
}

#endif
