// Player-driven foliage bending. FoliageBendDriver fills these globals every frame:
// _FoliageBendTrail[i]  = xyz position, w = life (1 = now, 0 = fully recovered). Slot 0 is the player, the rest
//                         are time-spaced samples of the path walked, newest first. Consecutive slots form segments.
// _FoliageBendParams    = x max tilt (radians), y vertical range, z cull radius around slot 0, w enabled (0/1).
// _FoliageBendMotion    = xy last walking direction (XZ), z plant reach in object units, w player contact radius.
// The whole plant leans as one piece (direction and amount are per object), so blades never tear apart; with
// _FOLIAGE_BEND_LOCAL (wide grass clumps) each vertex is measured at its own spot instead.
// Contact is measured against path segments and faded by age, so plants ease back up instead of snapping.
// A shader may define FOLIAGE_BEND_SHARE before the include to take only a share of the tilt (1 = all of it), and
// FOLIAGE_BEND_WALK 0 to lean only away from the bird, never along its walking direction (which flips when it turns).
// Needs a real object matrix: bending renderers must not be static batched.
#ifndef CHICK_FOLIAGE_BEND_INCLUDED
#define CHICK_FOLIAGE_BEND_INCLUDED

#define FOLIAGE_BEND_TRAIL 16
#ifndef FOLIAGE_BEND_SHARE
#define FOLIAGE_BEND_SHARE 1.0
#endif
#ifndef FOLIAGE_BEND_WALK
#define FOLIAGE_BEND_WALK 1.0
#endif

float4 _FoliageBendTrail[FOLIAGE_BEND_TRAIL];
float4 _FoliageBendParams;
float4 _FoliageBendMotion;

// Bushes (_FOLIAGE_BEND_BUSH): a bush is too big and too round to lean like a blade of grass, and it must keep its
// shape: nothing pushes its leaves outwards. As one piece it only sways a little away from a bird brushing past, its
// foot planted and its top moving most (a shear, so it keeps its size); the sway fades out towards the bush's centre,
// as with the flowers, so a bird turning there does not flip it from side to side. Inside it, the leaves around a
// moving bird rustle: a small shiver in place, a few centimetres, that dies away once the bird stands still or leaves.
#define BUSH_SWAY_SHARE 0.25    // of the grass's full lean
#define BUSH_REACH 0.8          // metres around the bush's centre (scaled with it) within which it sways
#define BUSH_RUSTLE_RADIUS 2.2  // the rustle reaches this many times the bird's contact radius
#define BUSH_RUSTLE 0.035       // metres the leaves shiver by at most
#define BUSH_STEP 0.06          // metres between trail samples at which the bird counts as fully moving

float3 FoliageBushOS(float3 positionOS)
{
    float3 pivotWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float3 positionWS = TransformObjectToWorld(positionOS);
    float4x4 objectToWorld = GetObjectToWorldMatrix();
    float scaleXZ = length(float3(objectToWorld[0].x, objectToWorld[1].x, objectToWorld[2].x));
    float swayRange = max(_FoliageBendMotion.w + BUSH_REACH * scaleXZ, 1e-4);
    float2 toPlayer = pivotWS.xz - _FoliageBendTrail[0].xz;
    float far = swayRange + _FoliageBendParams.z;
    if (dot(toPlayer, toPlayer) > far * far)
        return positionOS;
    float height = positionWS.y - pivotWS.y;
    if (height <= 1e-4)
        return positionOS;

    float rustleRadius = max(_FoliageBendMotion.w * BUSH_RUSTLE_RADIUS, 1e-4);
    float2 lean = 0.0;
    float sway = 0.0;
    float rustle = 0.0;
    [unroll]
    for (int i = 0; i < FOLIAGE_BEND_TRAIL - 1; i++)
    {
        float4 newer = _FoliageBendTrail[i];
        float4 older = _FoliageBendTrail[i + 1];
        if (max(newer.w, older.w) <= 0.0)
            continue;
        float2 segment = newer.xz - older.xz;
        float segmentSq = dot(segment, segment);

        // the bush as a whole, measured at its centre
        float t = segmentSq > 1e-8 ? saturate(dot(pivotWS.xz - older.xz, segment) / segmentSq) : 1.0;
        float3 closest = lerp(older.xyz, newer.xyz, t);
        float life = lerp(older.w, newer.w, t);
        life = life * life * (3.0 - 2.0 * life);
        float2 away = pivotWS.xz - closest.xz;
        float awayLen = length(away);
        float contact = saturate(1.0 - awayLen / swayRange);
        contact = contact * contact * (3.0 - 2.0 * contact) * life;
        contact *= saturate(1.0 - abs(pivotWS.y - closest.y) / _FoliageBendParams.y);
        contact *= smoothstep(0.0, swayRange * 0.45, awayLen);   // fades out towards the centre: no flipping
        if (contact > 0.0 && awayLen > 1e-3)
        {
            lean += away / awayLen * contact;
            sway = max(sway, contact);
        }

        // the leaves near this stretch of the walk rustle, as much as the bird was moving along it
        t = segmentSq > 1e-8 ? saturate(dot(positionWS.xz - older.xz, segment) / segmentSq) : 1.0;
        closest = lerp(older.xyz, newer.xyz, t);
        life = lerp(older.w, newer.w, t);
        float near = saturate(1.0 - length(positionWS.xz - closest.xz) / rustleRadius);
        float moving = saturate(sqrt(segmentSq) / BUSH_STEP);
        rustle = max(rustle, near * near * moving * life);
    }

    // the foot stays, the top moves most
    float rise = smoothstep(0.05, 0.6, height);
    float leanLen = length(lean);
    if (sway > 1e-3 && leanLen > 1e-4)
        positionWS.xz += lean / leanLen * tan(_FoliageBendParams.x * sway * BUSH_SWAY_SHARE) * height * rise;
    if (rustle > 1e-3)
    {
        // a shiver in place, its phase drifting slowly across the bush so neighbouring leaves move nearly together
        float phase = dot(positionWS.xz, float2(3.1, 2.3)) + positionWS.y * 2.7;
        float time = _Time.y;
        float3 shiver = float3(sin(time * 11.0 + phase), 0.45 * sin(time * 13.0 + phase * 0.7), cos(time * 9.0 + phase * 1.3));
        positionWS += shiver * BUSH_RUSTLE * rustle * rise;
    }
    return TransformWorldToObject(positionWS);
}

float3 FoliageBendOS(float3 positionOS)
{
    if (_FoliageBendParams.w <= 0.0)
        return positionOS;
#if defined(_FOLIAGE_BEND_BUSH)
    return FoliageBushOS(positionOS);
#endif

    float3 pivotWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float3 positionWS = TransformObjectToWorld(positionOS);
#if defined(_FOLIAGE_BEND_LOCAL)
    // Wide clumps (_FOLIAGE_BEND_LOCAL): each blade answers for itself, measured at its own spot on the ground, so
    // only the part of the clump the bird actually walks through leans; the rest stands.
    float3 contactWS = float3(positionWS.x, pivotWS.y, positionWS.z);
#else
    float3 contactWS = pivotWS;
#endif
    float2 toPlayer = contactWS.xz - _FoliageBendTrail[0].xz;
    if (dot(toPlayer, toPlayer) > _FoliageBendParams.z * _FoliageBendParams.z)
        return positionOS;   // far plants skip the loop entirely

    float height = positionWS.y - pivotWS.y;
    if (height <= 1e-4)
        return positionOS;   // roots stay planted

#if defined(_FOLIAGE_BEND_LOCAL)
    float touchRange = max(_FoliageBendMotion.w, 1e-4);   // the bird's own contact radius, no plant reach
#else
    float4x4 objectToWorld = GetObjectToWorldMatrix();
    float scaleXZ = length(float3(objectToWorld[0].x, objectToWorld[1].x, objectToWorld[2].x));
    float touchRange = max(_FoliageBendMotion.w + _FoliageBendMotion.z * scaleXZ, 1e-4);
#endif

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
        float t = segmentSq > 1e-8 ? saturate(dot(contactWS.xz - older.xz, segment) / segmentSq) : 1.0;
        float3 closest = lerp(older.xyz, newer.xyz, t);
        float life = lerp(older.w, newer.w, t);

        float2 away = contactWS.xz - closest.xz;
        float contact = saturate(1.0 - length(away) / touchRange);
        contact = contact * contact * contact * (contact * (contact * 6.0 - 15.0) + 10.0);   // soft lean-in at the edge
        contact *= life * life * (3.0 - 2.0 * life);   // ease back up as this part of the path ages
        contact *= saturate(1.0 - abs(contactWS.y - closest.y) / _FoliageBendParams.y);
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
        float2 dir = away + walk * (_FoliageBendMotion.w * 0.5 * FOLIAGE_BEND_WALK);
        float len = length(dir);
        // Without the walking-direction push the lean fades out towards the plant's centre, so a bird turning or
        // crossing over it tips it through upright to the other side instead of flipping it in one frame.
        float push = contact * lerp(saturate(len / (touchRange * 0.35)), 1.0, FOLIAGE_BEND_WALK);
        if (len > 1e-4)
            lean += dir / len * push;
        amount = max(amount, push);
    }
    float leanLen = length(lean);
    if (amount <= 1e-3 || leanLen <= 1e-4)
        return positionOS;

    float s, c;
    sincos(_FoliageBendParams.x * amount * FOLIAGE_BEND_SHARE, s, c);
    // Lean around the root: every vertex tilts the same way, the tip moves out and drops.
    positionWS.xz += (lean / leanLen) * height * s;
    positionWS.y = pivotWS.y + height * c;
    return TransformWorldToObject(positionWS);
}

#endif
