// Player-driven foliage bending. FoliageBendDriver fills these globals every frame:
// _FoliageBendTrail[i]  = xyz position, w = life (1 = now, 0 = fully recovered). Slot 0 is the player, the rest
//                         are time-spaced samples of the path walked, newest first. Consecutive slots form segments.
// _FoliageBendParams    = x max tilt (radians), y vertical range, z cull radius around slot 0, w enabled (0/1).
// _FoliageBendMotion    = xy last walking direction (XZ), z plant reach in object units, w player contact radius.
// The whole plant leans as one piece (direction and amount are per object), so blades never tear apart.
// Contact is measured against path segments and faded by age, so plants ease back up instead of snapping.
// Needs a real object matrix: bending renderers must not be static batched.
#ifndef CHICK_FOLIAGE_BEND_INCLUDED
#define CHICK_FOLIAGE_BEND_INCLUDED

#define FOLIAGE_BEND_TRAIL 16

float4 _FoliageBendTrail[FOLIAGE_BEND_TRAIL];
float4 _FoliageBendParams;
float4 _FoliageBendMotion;

float3 FoliageBendOS(float3 positionOS)
{
    if (_FoliageBendParams.w <= 0.0)
        return positionOS;

    float3 pivotWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float2 toPlayer = pivotWS.xz - _FoliageBendTrail[0].xz;
    if (dot(toPlayer, toPlayer) > _FoliageBendParams.z * _FoliageBendParams.z)
        return positionOS;   // far plants skip the loop entirely

    float3 positionWS = TransformObjectToWorld(positionOS);
    float height = positionWS.y - pivotWS.y;
    if (height <= 1e-4)
        return positionOS;   // roots stay planted

    float4x4 objectToWorld = GetObjectToWorldMatrix();
    float scaleXZ = length(float3(objectToWorld[0].x, objectToWorld[1].x, objectToWorld[2].x));
    float touchRange = max(_FoliageBendMotion.w + _FoliageBendMotion.z * scaleXZ, 1e-4);

    // Blend lean directions by strength; the strongest contact sets the amount.
    float2 lean = 0.0;
    float amount = 0.0;
    [unroll]
    for (int i = 0; i < FOLIAGE_BEND_TRAIL - 1; i++)
    {
        float4 newer = _FoliageBendTrail[i];
        float4 older = _FoliageBendTrail[i + 1];
        if (max(newer.w, older.w) <= 0.0)
            continue;

        float2 segment = newer.xz - older.xz;
        float segmentSq = dot(segment, segment);
        float t = segmentSq > 1e-8 ? saturate(dot(pivotWS.xz - older.xz, segment) / segmentSq) : 1.0;
        float3 closest = lerp(older.xyz, newer.xyz, t);
        float life = lerp(older.w, newer.w, t);

        float2 away = pivotWS.xz - closest.xz;
        float contact = saturate(1.0 - length(away) / touchRange);
        contact = contact * contact * contact * (contact * (contact * 6.0 - 15.0) + 10.0);   // soft lean-in at the edge
        contact *= life * life * (3.0 - 2.0 * life);   // ease back up as this part of the path ages
        contact *= saturate(1.0 - abs(pivotWS.y - closest.y) / _FoliageBendParams.y);
        if (contact <= 0.0)
            continue;

        // Off-centre contact pushes the plant away; a plant walked over leans along the walking direction.
        // A freshly started segment is only a frame or two of movement, so its direction is noise: blend
        // from the smoothed walking direction until the segment is long enough to trust.
        float2 walk = _FoliageBendMotion.xy;
        if (segmentSq > 1e-8)
        {
            float segmentLen = sqrt(segmentSq);
            float2 blended = lerp(walk, segment / segmentLen, saturate(segmentLen / 0.12));
            float blendedLen = length(blended);
            if (blendedLen > 1e-4)
                walk = blended / blendedLen;
        }
        float2 dir = away + walk * (_FoliageBendMotion.w * 0.5);
        float len = length(dir);
        if (len > 1e-4)
            lean += dir / len * contact;
        amount = max(amount, contact);
    }
    float leanLen = length(lean);
    if (amount <= 1e-3 || leanLen <= 1e-4)
        return positionOS;

    float s, c;
    sincos(_FoliageBendParams.x * amount, s, c);
    // Lean around the root: every vertex tilts the same way, the tip moves out and drops.
    positionWS.xz += (lean / leanLen) * height * s;
    positionWS.y = pivotWS.y + height * c;
    return TransformWorldToObject(positionWS);
}

#endif
