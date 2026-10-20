// The track reveal (Bastion-style assembly): every stamped track piece
// flies up into place as the reveal front passes it. One rule shared by the
// hand-written NeonRoad shader (every pass) and the Road / laser graphs (a
// Custom Function node on Vertex Position), so road, walls and markers move
// as one.
//
// Per piece, baked once by TrackDecorator.Bend into TEXCOORD3:
//   x = the piece's stamp distance (metres of track), y = a 0..1 seed,
//   z = 1 (baked; 0 = a mesh that never was — the laser beam sheet, an
//   unbent rigid piece — which the reveal leaves alone).
// Every vertex of a piece carries the same values, so the piece moves rigidly
// about its own origin (the stamp pose) along its own up.
//
// Globals, written by TrackReveal (Runner) every frame. A global never set
// reads 0, so _TrackRevealOn = 0 — the Scene view, the authoring tools, the
// city and any scene without the system — is the assembled track.
#ifndef FINITERUNNER_TRACK_REVEAL_INCLUDED
#define FINITERUNNER_TRACK_REVEAL_INCLUDED

float _TrackRevealOn;        // 1 = reveal live
float _TrackRevealFront;     // track distance where a piece STARTS rising
float _TrackRevealBand;      // metres behind the front by which it is seated
float _TrackRevealStagger;   // metres of per-piece random delay
float _TrackRevealDrop;      // metres below its rest a piece starts
float _TrackRevealTilt;      // radians a piece starts tipped about its X
float _TrackRevealScale;     // scale a piece starts at (0..1)
float _TrackRevealOvershoot; // ease-out-back overshoot (0 = plain ease out)

float3 TrackRevealRotate(float3 v, float3 axis, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
}

// Object-space position in, object-space position out.
float3 TrackRevealApply(float3 positionOS, float4 data)
{
    if (_TrackRevealOn < 0.5 || data.z < 0.5) return positionOS;

    float start = data.x + data.y * _TrackRevealStagger; // later seeds start later
    float t = saturate((_TrackRevealFront - start) / max(1e-3, _TrackRevealBand));
    if (t >= 1.0) return positionOS;

    float3 pivot = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    if (t <= 0.0) return TransformWorldToObject(pivot - normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0))) * _TrackRevealDrop); // collapsed out of sight

    // Ease out with an optional overshoot: the piece lands and settles.
    float u = t - 1.0;
    float c = _TrackRevealOvershoot;
    float eased = 1.0 + (c + 1.0) * u * u * u + c * u * u;
    float left = 1.0 - eased; // 1 at the start, 0 seated (negative = past rest)

    float3 up = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0)));
    float3 axis = normalize(mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0)));
    float side = data.y < 0.5 ? -1.0 : 1.0;

    float3 rel = TransformObjectToWorld(positionOS) - pivot;
    rel = TrackRevealRotate(rel, axis, _TrackRevealTilt * left * side);
    rel *= lerp(1.0, _TrackRevealScale, saturate(left));
    float3 world = pivot + rel - up * (_TrackRevealDrop * left);
    return TransformWorldToObject(world);
}

// Shader Graph Custom Function entry (File mode, name "TrackReveal").
void TrackReveal_float(float3 PositionOS, float4 Data, out float3 Out)
{
    Out = TrackRevealApply(PositionOS, Data);
}

void TrackReveal_half(float3 PositionOS, float4 Data, out float3 Out)
{
    Out = TrackRevealApply(PositionOS, Data);
}

#endif
