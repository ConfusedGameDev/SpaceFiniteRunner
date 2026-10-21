using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

using ConfusedGameDev.FiniteRunner.FX;
namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>
    /// The PICTURE of one laser: the two emitters of the
    /// <c>PF_LaserSystem</c> prefab and the beam fired from A's shoot point
    /// to B's. It knows nothing about damage — a <see cref="LaserGate"/> owns
    /// the beam's track-space segment and hands this its two world muzzles
    /// every frame (<see cref="SetEndpoints"/>). <b>Everything is posed about
    /// the shoot point, never about the emitter's own pivot</b>: the emitter
    /// models carry unfrozen transforms (LaserA's pivot sits ~110 m from its
    /// mesh), so each emitter is turned until its shoot point looks down the
    /// beam, rolled about that line — the barrel, its local Z — and then
    /// slid until the shoot point is on the muzzle. Stateless, so a rotor
    /// turning its beam and a fixed gate run the same code. The beam is two
    /// code-built LineRenderers (a wide glow, a thin hot core) on one shared
    /// additive material tinted through vertex colour, flickering on scaled
    /// time so a pause freezes it. A WAVY beam is the same two lines bent
    /// into a triangle wave running from A to B: a vertex at each muzzle and
    /// one ON every corner of the wave — nowhere else — so the corners stay
    /// sharp however the wave slides.
    /// <b>Two looks</b> (<see cref="LaserGateDefinition.look"/>): the above
    /// is the PROCEDURAL one; the PREFAB one is the laser model authored in
    /// the prefab — an emitter bar and the beam sheet it projects. Its shoot
    /// point sits on the bar where the sheet starts (forward down the sheet,
    /// right across it): the model is posed on muzzle B firing at A, the sheet
    /// facing along the track so the ship sees it flat on. <b>The model keeps
    /// its authored size</b> (times <see cref="LaserGateDefinition.prefabModelScale"/>),
    /// never stretched: a gate drawing it is always VERTICAL and takes its
    /// height and burn width FROM the sheet (<see cref="PrefabSheetLength"/>,
    /// <see cref="PrefabSheetHalfWidth"/>), so the picture never lies. A copy
    /// of the sheet turned half round the beam shows the side its
    /// front-face-only shader would cull. A prefab with no authored model
    /// falls back to the procedural look.
    /// <b>The sheet grows from the bar</b> (<see cref="SetExtension"/>): it
    /// hangs from a pivot on its top edge (<c>SheetPivot</c>, forward down the
    /// sheet like the shoot point), and scaling that pivot's Z pulls the sheet
    /// up into the bar or lets it down onto the road. The gate drives it off
    /// the ship's distance; nothing here knows why.
    /// <b>The spark rig is nested, not built</b>: the prefab carries a
    /// <c>PF_LaserFloorSparks</c> instance (<see cref="FloorSparks"/>) for the
    /// gate to play where the sheet meets the road — its look is that
    /// prefab's, tuned by hand.
    /// </summary>
    public class LaserBeam : MonoBehaviour
    {
        [Tooltip("Emitter the beam leaves from (the LaserA model root).")]
        [SerializeField] Transform emitterA;
        [Tooltip("Emitter the beam arrives at (the LaserB model root).")]
        [SerializeField] Transform emitterB;
        [Tooltip("Muzzle of emitter A, a child of it. Its forward is the firing direction.")]
        [SerializeField] Transform shootPointA;
        [Tooltip("Muzzle of emitter B, a child of it. Its forward is the firing direction.")]
        [SerializeField] Transform shootPointB;

        [Tooltip("Root of the authored laser model (the Prefab look): its emitter bar and beam sheet. Empty = found by name (laserTypeA).")]
        [SerializeField] Transform prefabModel;
        [Tooltip("The model's beam sheet, stretched to the beam. Empty = the model's 'Plane' child.")]
        [SerializeField] Transform prefabSheet;
        [Tooltip("Where the sheet leaves the bar, a child of the model: forward down the sheet, right across it. Empty = one is derived from the sheet's mesh at run time.")]
        [SerializeField] Transform prefabShootPoint;
        [Tooltip("Parent of the sheet on its TOP edge (where it leaves the bar), forward down the sheet: scaling its Z grows the sheet from the bar. Empty = one is made at run time.")]
        [SerializeField] Transform prefabSheetPivot;
        [Tooltip("The nested spark rig (PF_LaserFloorSparks) the gate plays where the sheet meets the road. Empty = the 'FloorSparks' child; none = no sparks.")]
        [SerializeField] LaserFloorSparks floorSparks;

        static Material fallbackMaterial; // domain reload is off: checked for a destroyed one on every use

        LaserGateDefinition definition;
        LineRenderer glow, core;
        Quaternion shootLocalA, shootLocalB; // each shoot point's rotation relative to its emitter
        Vector3 baseScaleA, baseScaleB;
        float spinAngle;
        float flickerSeed;
        bool ready;
        bool wavy;
        Vector3[] wavePoints = new Vector3[2]; // scratch, grown on demand
        bool procedural, prefab;     // which looks this beam draws
        Quaternion shootLocalModel;  // the model's shoot point rotation relative to the model root
        Renderer[] sheetRenderers;   // the sheet and its back copy: hidden while fully retracted
        float extension = 1f;

        /// <summary>
        /// How far an emitter's model reaches out from its barrel line, metres,
        /// at its configured scale — the larger of the two. The emitters roll
        /// about that line, so this is how far below a horizontal beam they can
        /// ever dip: what the gate lifts the picture by to keep them off the road.
        /// </summary>
        public float EmitterRadius { get; private set; }

        /// <summary>The authored model is drawn (the Prefab or Both look, and the prefab has one).</summary>
        public bool DrawsPrefabModel => prefab;

        /// <summary>The nested spark rig for the sheet's foot; null when the prefab carries none.</summary>
        public LaserFloorSparks FloorSparks => floorSparks;

        /// <summary>Name of the nested spark rig under the prefab's root.</summary>
        public const string FloorSparksName = "FloorSparks";

        /// <summary>How far the authored sheet reaches from the bar, metres, at its configured scale — the height of a gate drawing it.</summary>
        public float PrefabSheetLength { get; private set; }

        /// <summary>Half the authored sheet's width, metres, at its configured scale — what a gate drawing it burns either side of its axis.</summary>
        public float PrefabSheetHalfWidth { get; private set; }

        /// <summary>Wires the run's definition, sizes the emitters and builds the beam. Called once by the generator; <paramref name="wave"/> = this gate rolled the zigzag.</summary>
        public void Configure(LaserGateDefinition def, bool wave = false)
        {
            definition = def;
            wavy = wave;
            ResolveReferences();

            // Detection is analytic: whatever colliders the models carry are only a picture.
            foreach (var col in GetComponentsInChildren<Collider>(true)) Kill(col);

            prefab = def.DrawsPrefab && SetUpPrefabModel(def);
            if (def.DrawsPrefab && !prefab)
                Debug.LogWarning($"LaserBeam: '{name}' has no authored laser model (a model with a beam sheet) — drawing the procedural beam instead.", this);
            procedural = def.DrawsProcedural || !prefab;
            if (prefabModel != null) prefabModel.gameObject.SetActive(prefab);
            if (emitterA != null) emitterA.gameObject.SetActive(procedural);
            if (emitterB != null) emitterB.gameObject.SetActive(procedural);
            if (procedural && !SetUpProcedural(def))
            {
                Debug.LogError($"LaserBeam: '{name}' needs LaserA / LaserB, each with a ShootPoint child.", this);
                procedural = false;
            }
            ready = procedural || prefab;
        }

        // Out of physics NOW (a queued Destroy leaves it there for the frame,
        // where the sheet's convex collider fails to cook at its pulled-in
        // scale), then gone.
        static void Kill(Collider col)
        {
            col.enabled = false;
            Destroy(col);
        }

        // The old look: the two emitters, sized, and the glow + core lines.
        bool SetUpProcedural(LaserGateDefinition def)
        {
            if (emitterA == null || emitterB == null || shootPointA == null || shootPointB == null) return false;

            shootLocalA = Quaternion.Inverse(emitterA.rotation) * shootPointA.rotation;
            shootLocalB = Quaternion.Inverse(emitterB.rotation) * shootPointB.rotation;
            baseScaleA = emitterA.localScale;
            baseScaleB = emitterB.localScale;
            emitterA.localScale = baseScaleA * def.emitterScale;
            emitterB.localScale = baseScaleB * def.emitterScale;
            EmitterRadius = Mathf.Max(EmitterRadius, Mathf.Max(BarrelRadius(emitterA, shootPointA), BarrelRadius(emitterB, shootPointB)));

            Material material = def.beamMaterial != null ? def.beamMaterial : FallbackMaterial();
            float glowWidth = def.beamRadius * 2f * def.glowWidthFactor;
            glow = BuildLine("Glow", material, def.beamColor, glowWidth);
            core = BuildLine("Core", material, def.coreColor, glowWidth / 3f);
            flickerSeed = Random.value * 100f;
            return true;
        }

        // The authored look: the model at its authored size times the scale,
        // its sheet measured — never stretched.
        bool SetUpPrefabModel(LaserGateDefinition def)
        {
            if (prefabModel == null || prefabSheet == null) return false;
            prefabModel.localScale *= def.prefabModelScale;
            if (prefabShootPoint == null) prefabShootPoint = DeriveShootPoint(prefabModel, prefabSheet);
            if (prefabShootPoint == null) return false;

            MeasureSheet(prefabShootPoint, prefabSheet, out float sheetLength, out float sheetWidth);
            if (sheetLength < 1e-3f || sheetWidth < 1e-3f) return false;
            PrefabSheetLength = sheetLength;
            PrefabSheetHalfWidth = sheetWidth * 0.5f;
            if (prefabSheetPivot == null || !prefabSheet.IsChildOf(prefabSheetPivot))
                prefabSheetPivot = DeriveSheetPivot(prefabModel, prefabSheet, prefabShootPoint);

            // The sheet's shader draws front faces only, and a gate seen from
            // past it shows its back: a copy turned half round the beam fills
            // that side in.
            Transform back = Instantiate(prefabSheet.gameObject, prefabSheet.parent).transform;
            back.name = prefabSheet.name + "_Back";
            back.RotateAround(prefabShootPoint.position, prefabShootPoint.forward, 180f);
            foreach (var col in back.GetComponentsInChildren<Collider>(true)) Kill(col); // the original's are only queued for destruction, so the copy has its own
            sheetRenderers = prefabSheetPivot.GetComponentsInChildren<Renderer>(true); // the copy sits under the pivot too: it grows with the sheet

            shootLocalModel = Quaternion.Inverse(prefabModel.rotation) * prefabShootPoint.rotation;
            EmitterRadius = Mathf.Max(EmitterRadius, BarrelRadius(prefabModel, prefabShootPoint));
            return true;
        }

        /// <summary>How far the sheet reaches down the shoot point's forward, and how wide it is across its right, in world metres.</summary>
        public static void MeasureSheet(Transform shootPoint, Transform sheet, out float length, out float width)
        {
            length = 0f;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            foreach (Vector3 corner in MeshCorners(sheet))
            {
                Vector3 v = corner - shootPoint.position;
                length = Mathf.Max(length, Vector3.Dot(v, shootPoint.forward));
                float x = Vector3.Dot(v, shootPoint.right);
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
            }
            width = maxX > minX ? maxX - minX : 0f;
        }

        /// <summary>
        /// A shoot point for an authored model that carries none: on the
        /// sheet's long axis, at the end nearest the rest of the model (the
        /// bar it leaves), forward down the sheet and right across it. Made a
        /// child of <paramref name="model"/>; null when the sheet has no mesh.
        /// </summary>
        public static Transform DeriveShootPoint(Transform model, Transform sheet)
        {
            var filter = sheet.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return null;
            Bounds local = filter.sharedMesh.bounds;
            Vector3 centre = sheet.TransformPoint(local.center);

            // The sheet's two broad axes in world, the longer one being the beam.
            Vector3 ax = sheet.TransformVector(new Vector3(local.size.x, 0f, 0f));
            Vector3 ay = sheet.TransformVector(new Vector3(0f, local.size.y, 0f));
            Vector3 az = sheet.TransformVector(new Vector3(0f, 0f, local.size.z));
            Vector3[] axes = { ax, ay, az };
            System.Array.Sort(axes, (p, q) => q.sqrMagnitude.CompareTo(p.sqrMagnitude));
            Vector3 along = axes[0], across = axes[1];
            if (along.sqrMagnitude < 1e-6f || across.sqrMagnitude < 1e-6f) return null;

            // The bar: every other renderer of the model.
            Vector3 bar = centre + Vector3.up; // no bar at all: fire downward
            bool any = false;
            var barBounds = new Bounds();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform == sheet || r.transform.IsChildOf(sheet)) continue;
                if (!any) { barBounds = r.bounds; any = true; } else barBounds.Encapsulate(r.bounds);
            }
            if (any) bar = barBounds.center;
            if (Vector3.Dot(bar - centre, along) > 0f) along = -along; // fire away from the bar

            var point = new GameObject("ShootPoint").transform;
            point.SetParent(model, false);
            point.position = centre - along * 0.5f;
            Vector3 forward = along.normalized;
            point.rotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, across.normalized));
            return point;
        }

        /// <summary>
        /// The pivot the sheet grows from: on the shoot point's line, level
        /// with the sheet's top edge (the corner nearest the bar), turned like
        /// the shoot point so its forward runs down the sheet. Made a child of
        /// <paramref name="model"/> with the sheet reparented under it, world
        /// pose kept. Measure the sheet first — this is its full length.
        /// </summary>
        public static Transform DeriveSheetPivot(Transform model, Transform sheet, Transform shootPoint)
        {
            float top = float.PositiveInfinity;
            foreach (Vector3 corner in MeshCorners(sheet))
                top = Mathf.Min(top, Vector3.Dot(corner - shootPoint.position, shootPoint.forward));
            if (float.IsInfinity(top)) top = 0f;

            var pivot = new GameObject(SheetPivotName).transform;
            pivot.SetParent(model, false);
            pivot.SetPositionAndRotation(shootPoint.position + shootPoint.forward * top, shootPoint.rotation);
            sheet.SetParent(pivot, true);
            return pivot;
        }

        /// <summary>Name of the sheet's pivot under the model.</summary>
        public const string SheetPivotName = "SheetPivot";

        /// <summary>
        /// How much of the sheet hangs out of the bar: 0 = pulled all the way
        /// in (hidden), 1 = its full length, down on the road. The authored
        /// look only — the procedural lines always draw whole.
        /// </summary>
        public void SetExtension(float extension01)
        {
            if (!prefab || prefabSheetPivot == null) return;
            float e = Mathf.Clamp01(extension01);
            if (Mathf.Approximately(e, extension)) return;
            extension = e;
            prefabSheetPivot.localScale = new Vector3(1f, 1f, Mathf.Max(1e-4f, e)); // never 0: a degenerate matrix
            bool shown = e > 1e-3f;
            if (sheetRenderers != null)
                foreach (var r in sheetRenderers) if (r != null && r.enabled != shown) r.enabled = shown;
        }

        static IEnumerable<Vector3> MeshCorners(Transform root)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                    yield return filter.transform.TransformPoint(new Vector3((i & 1) != 0 ? b.max.x : b.min.x,
                                                                             (i & 2) != 0 ? b.max.y : b.min.y,
                                                                             (i & 4) != 0 ? b.max.z : b.min.z));
            }
        }

        /// <summary>Hides emitter A: a vertical beam rises straight out of the road, where A's body would be buried.</summary>
        public void HideEmitterA() // the prefab model sits on B, so it has nothing to hide
        {
            if (emitterA != null) emitterA.gameObject.SetActive(false);
        }

        // Farthest any mesh corner of the emitter sits from the line through its
        // shoot point along the barrel. Rotation-invariant, so any pose will do.
        static float BarrelRadius(Transform emitter, Transform shootPoint)
        {
            float radius = 0f;
            Vector3 axis = shootPoint.forward;
            foreach (Vector3 corner in MeshCorners(emitter))
                radius = Mathf.Max(radius, Vector3.ProjectOnPlane(corner - shootPoint.position, axis).magnitude);
            return radius;
        }

        /// <summary>
        /// Puts the beam between two world points. <paramref name="up"/> keeps
        /// the emitters upright; <paramref name="fallbackUp"/> takes over when
        /// the beam itself runs along it (the vertical gate).
        /// </summary>
        public void SetEndpoints(Vector3 muzzleA, Vector3 muzzleB, Vector3 up, Vector3 fallbackUp)
        {
            if (!ready) return;
            Vector3 along = muzzleB - muzzleA;
            if (along.sqrMagnitude < 1e-6f) return;
            Vector3 dir = along.normalized;
            // The wave swings along the track's up; a beam that RUNS along it
            // (the vertical gate) swings across the track instead.
            Vector3 waveAxis = Mathf.Abs(Vector3.Dot(dir, up)) > 0.98f
                ? Vector3.Cross(dir, fallbackUp).normalized
                : Vector3.ProjectOnPlane(up, dir).normalized;
            if (Mathf.Abs(Vector3.Dot(dir, up)) > 0.98f) up = fallbackUp;

            if (prefab)
            {
                // B fires at A, the sheet's right along the wave axis — so it
                // faces along the track (a rotor's turns with the blade).
                Pose(prefabModel, prefabShootPoint, shootLocalModel, muzzleB, -dir, Vector3.Cross(-dir, waveAxis), 0f);
            }
            if (!procedural) return;

            spinAngle = Mathf.Repeat(spinAngle + definition.emitterSpinDegPerSec * Time.deltaTime, 360f);
            Pose(emitterA, shootPointA, shootLocalA, muzzleA, dir, up, spinAngle);
            Pose(emitterB, shootPointB, shootLocalB, muzzleB, -dir, up, spinAngle);

            float flicker = 1f + definition.flickerAmount
                          * (Mathf.PerlinNoise(Time.time * definition.flickerFrequency, flickerSeed) * 2f - 1f);
            float glowWidth = definition.beamRadius * 2f * definition.glowWidthFactor;
            int count = wavy ? BuildWave(muzzleA, dir, along.magnitude, waveAxis) : BuildStraight(muzzleA, muzzleB);
            SetLine(glow, count, glowWidth * flicker);
            SetLine(core, count, glowWidth / 3f * (2f - flicker)); // the core breathes against the glow
        }

        int BuildStraight(Vector3 from, Vector3 to)
        {
            wavePoints[0] = from;
            wavePoints[1] = to;
            return 2;
        }

        // offset(s) = amplitude × envelope(s) × tri(s / wavelength − speed × t),
        // tri(p) = 4|frac(p) − ½| − 1: corners wherever p is a multiple of ½.
        // Vertices go on the muzzles and on exactly those corners. Scaled
        // time, like the flicker: a pause freezes the wave.
        int BuildWave(Vector3 origin, Vector3 dir, float length, Vector3 axis)
        {
            float wavelength = Mathf.Max(0.5f, definition.waveLength);
            float shift = Mathf.Repeat(definition.waveSpeed * Time.time, 1f); // in wavelengths; a whole one changes nothing
            float half = wavelength * 0.5f;

            // Corner k sits at s = (k/2 + shift) × wavelength.
            int first = Mathf.CeilToInt(-shift * 2f + 1e-4f); // the first corner at or past the muzzle
            int corners = Mathf.Max(0, Mathf.FloorToInt((length - ((first * 0.5f + shift) * wavelength)) / half) + 1);
            int count = corners + 2;
            if (wavePoints.Length < count) wavePoints = new Vector3[Mathf.NextPowerOfTwo(count)];

            wavePoints[0] = origin + axis * WaveOffset(0f, length, wavelength, shift);
            int n = 1;
            for (int i = 0; i < corners; i++)
            {
                int k = first + i;
                float s = (k * 0.5f + shift) * wavelength;
                if (s <= 1e-3f || s >= length - 1e-3f) continue;
                float sign = (k & 1) == 0 ? 1f : -1f; // even corners are crests (tri = +1), odd ones troughs
                wavePoints[n++] = origin + dir * s + axis * (definition.waveAmplitude * Envelope(s, length) * sign);
            }
            wavePoints[n++] = origin + dir * length + axis * WaveOffset(length, length, wavelength, shift);
            return n;
        }

        // The wave at any point of the beam — only the two muzzles need it
        // (0 there with a taper; the wave's own value with none).
        float WaveOffset(float s, float length, float wavelength, float shift)
        {
            float p = s / wavelength - shift;
            float tri = 4f * Mathf.Abs(p - Mathf.Floor(p) - 0.5f) - 1f;
            return definition.waveAmplitude * Envelope(s, length) * tri;
        }

        // 0 on the muzzles, 1 in the middle: the beam still leaves and enters the shoot points.
        float Envelope(float s, float length)
        {
            float taper = definition.waveTaper;
            if (taper <= 0f) return 1f;
            return Mathf.Clamp01(Mathf.Min(s, length - s) / taper);
        }

        // Shoot point looking down the beam, rolled about it, then pinned on
        // the muzzle — the emitter's own pivot never enters into it.
        static void Pose(Transform emitter, Transform shootPoint, Quaternion shootLocal,
                         Vector3 muzzle, Vector3 fireDirection, Vector3 up, float roll)
        {
            Quaternion shootRotation = Quaternion.AngleAxis(roll, fireDirection) * Quaternion.LookRotation(fireDirection, up);
            emitter.rotation = shootRotation * Quaternion.Inverse(shootLocal);
            emitter.position += muzzle - shootPoint.position;
        }

        void ResolveReferences()
        {
            if (emitterA == null) emitterA = transform.Find("LaserA");
            if (emitterB == null) emitterB = transform.Find("LaserB");
            if (shootPointA == null && emitterA != null) shootPointA = emitterA.Find("ShootPoint");
            if (shootPointB == null && emitterB != null) shootPointB = emitterB.Find("ShootPoint");
            if (prefabModel == null) prefabModel = transform.Find("laserTypeA");
            if (prefabSheetPivot == null && prefabModel != null) prefabSheetPivot = prefabModel.Find(SheetPivotName);
            if (prefabSheet == null && prefabSheetPivot != null) prefabSheet = prefabSheetPivot.Find("Plane");
            if (prefabSheet == null && prefabModel != null) prefabSheet = prefabModel.Find("Plane");
            if (prefabShootPoint == null && prefabModel != null) prefabShootPoint = prefabModel.Find("ShootPoint");
            if (floorSparks == null && transform.Find(FloorSparksName) is Transform rig) floorSparks = rig.GetComponent<LaserFloorSparks>();
        }

        LineRenderer BuildLine(string lineName, Material material, Color color, float width)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.numCornerVertices = 0; // a wavy beam's corners stay sharp
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.generateLightingData = false;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        void SetLine(LineRenderer line, int count, float width)
        {
            line.positionCount = count;
            line.SetPositions(wavePoints); // copies the first positionCount points
            line.widthMultiplier = width;
        }

        // Safety net when the definition carries no material: the same
        // additive URP particle Unlit the barrel-roll trail falls back to (it
        // multiplies the line's vertex colour, which plain URP Unlit ignores).
        static Material FallbackMaterial()
        {
            if (fallbackMaterial != null) return fallbackMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader) { name = "LaserBeam (runtime)" };
            material.SetFloat("_Surface", 1f); // transparent
            material.SetFloat("_Blend", 2f);   // additive
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.One);
            material.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
            material.SetInt("_DstBlendAlpha", (int)BlendMode.One);
            material.SetInt("_ZWrite", 0);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", Color.white);
            fallbackMaterial = material;
            return material;
        }
    }
}
