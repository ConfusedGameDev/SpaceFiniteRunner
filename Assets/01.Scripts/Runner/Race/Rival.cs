using UnityEngine;

using ConfusedGameDev.FiniteRunner.Contracts;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Simulation;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Race
{
    /// <summary>
    /// One pooled rival racer (RaceModePRD D1, D10, D12): a shell built once at
    /// bind — model, <see cref="TrackBody"/>, <see cref="RivalDriver"/> — and
    /// never destroyed mid-run. It is in one of the <see cref="RacerState"/>s:
    /// on the grid; <b>racing</b> in full sim round the ship (the body steps
    /// under the driver, the model drawn at a track-space blend of the last
    /// two ticks, like the patrol); a <b>ghost</b> out of sight (distance +
    /// pace only, hidden, it cannot crash or pick anything up); <b>finished</b>
    /// off an end ramp (flown on ballistically, then parked); or <b>DNF</b>
    /// (exploded, parked). The <see cref="RaceSystem"/> decides every
    /// transition; the rival only carries them out. It owns no collider —
    /// every contact is analytic.
    /// </summary>
    public class Rival : MonoBehaviour
    {
        /// <summary>Index in the race's rival list.</summary>
        public int Index { get; private set; }
        public RivalEntry Entry { get; private set; }
        public RacerState State { get; private set; } = RacerState.Grid;
        public TrackBody Body { get; private set; }
        public RivalDriver Driver { get; } = new();

        /// <summary>Track distance — the body's in full sim, the ghost's own otherwise, the finish line once finished.</summary>
        public float Distance => State == RacerState.Racing || State == RacerState.Grid ? Body.Distance : ghostDistance;
        /// <summary>Forward speed, m/s, in either fidelity.</summary>
        public float Speed => State == RacerState.Racing || State == RacerState.Grid ? Body.ForwardSpeed : ghostSpeed;
        /// <summary>Lateral in full sim; the lane it will be re-seated near as a ghost.</summary>
        public float Lateral => State == RacerState.Racing || State == RacerState.Grid ? Body.Lateral : Driver.HomeLateral;
        /// <summary>Finishing place, 0 while it has none.</summary>
        public int FinishPlace { get; private set; }
        /// <summary>Average speed while in full sim / as a ghost, for the debug readout (the ghost pace check).</summary>
        public float FullSimAverage => fullSimSeconds > 0f ? fullSimMeters / fullSimSeconds : 0f;
        public float GhostAverage => ghostSeconds > 0f ? ghostMeters / ghostSeconds : 0f;

        /// <summary>Share of a boost orb's speed it gets on taking one — refreshed by the system from the live definition.</summary>
        public float OrbBoostShare { get; set; }

        /// <summary>Its form this run (rolled by the system at the grid): multipliers on the field's thrust and on its pace, the phase of its surge wave, and its reaction at lights out.</summary>
        public float RunThrustScale { get; set; } = 1f;
        public float RunPaceScale { get; set; } = 1f;
        public float SurgePhase { get; set; }
        public float LaunchDelay { get; set; }

        /// <summary>Touching the ship last tick — a contact only scores on the tick it begins.</summary>
        public bool InContact { get; set; }

        /// <summary>Rams from behind it can still take; 0 = wrecked. Refilled on the grid.</summary>
        public int RamHull { get; set; }
        /// <summary>Time.time of its last contact with the ship (-inf = never): a wreck soon after is the player's kill.</summary>
        public float LastShipContactTime { get; set; } = float.NegativeInfinity;
        // The last couple of seconds of full sim (time, distance, lateral,
        // line, speed), for the wreck note: what it was doing on the way in.
        const int TraceLength = 128;
        readonly Vector4[] trace = new Vector4[TraceLength];
        readonly float[] traceLine = new float[TraceLength];
        int traceHead, traceCount;

        /// <summary>Records one full-sim tick for the wreck trace.</summary>
        public void RecordTrace(float line)
        {
            trace[traceHead] = new Vector4(Time.time, Body.Distance, Body.Lateral, Body.ForwardSpeed);
            traceLine[traceHead] = line;
            traceHead = (traceHead + 1) % TraceLength;
            if (traceCount < TraceLength) traceCount++;
        }

        /// <summary>Where it was (distance, lateral) and where it was steering, <paramref name="secondsAgo"/> before now; "" with no record that old.</summary>
        public string TraceAt(float secondsAgo)
        {
            float at = Time.time - secondsAgo;
            for (int k = 1; k <= traceCount; k++)
            {
                int i = (traceHead - k + TraceLength) % TraceLength;
                if (trace[i].x <= at)
                    return $"-{secondsAgo:0.0}s: {trace[i].y:0} m lat {trace[i].z:0.0} line {traceLine[i]:0.0} {trace[i].w * 3.6f:0} km/h";
            }
            return string.Empty;
        }

        /// <summary>How it was wrecked, for the debug readout ("" while it is not): the cause, the mistake it was making, where.</summary>
        public string WreckNote { get; set; } = string.Empty;

        /// <summary>Where its wreck was seen (the fireball), and whether it was in view at all — what the kill cam frames.</summary>
        public Vector3 WreckPosition { get; private set; }
        public bool WreckedInView { get; private set; }
        public float WreckLateral { get; private set; }

        /// <summary>A beam caught it this substep: acted on by the system after the body's step, never inside the sweep.</summary>
        public bool LaserPending { get; set; }

        /// <summary>The body left the track this tick: <c>true</c> by an end ramp, <c>false</c> any other way (an open edge, a gap at the end). Read and cleared by the system.</summary>
        public bool? PendingExit { get; set; }

        TrackManager track;
        Transform visual;
        Material placeholderMaterial;
        readonly System.Collections.Generic.List<Material> paintedMaterials = new();
        float ghostDistance, ghostSpeed, prevGhostDistance;
        float fullSimMeters, fullSimSeconds, ghostMeters, ghostSeconds;
        float shownBank;

        // The off-track flight (finished: on along the lip's line; DNF: a fall).
        bool flying;
        Vector3 flyPosition, prevFlyPosition, flyVelocity;
        Quaternion flyRotation;
        float flyGravity, flyTimer, flyFor;

        /// <summary>Builds the shell's body and model. Called once by the pool.</summary>
        public void Build(int index, RivalEntry entry, TrackManager track, int seed)
        {
            Index = index;
            Entry = entry;
            this.track = track;
            Body = new TrackBody(track);
            Body.LeftTrack += _ => PendingExit = false;
            Body.ReachedEnd += tookRamp => PendingExit = tookRamp;
            Body.PickedUp += OnPickedUp;
            Driver.Configure(entry, seed);

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            visual.localPosition = Vector3.up * entry.hoverHeight;
            if (entry.prefab != null)
            {
                var model = Instantiate(entry.prefab, visual);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0f, entry.yawOffset, 0f);
                model.transform.localScale = Vector3.one * entry.scale;
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Destroy(collider);
                if (entry.paintTint.a > 0f) Paint(model, entry.paintTint);
            }
            else
            {
                // A placeholder hull the size of its contact box, in its map colour.
                var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(hull.GetComponent<Collider>());
                hull.name = "Placeholder";
                hull.transform.SetParent(visual, false);
                hull.transform.localScale = new Vector3(entry.halfWidth * 2f, 2f, entry.halfLength * 2f);
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    placeholderMaterial = new Material(shader) { color = entry.mapColor };
                    placeholderMaterial.EnableKeyword("_EMISSION");
                    placeholderMaterial.SetColor("_EmissionColor", entry.mapColor * 1.5f);
                    hull.GetComponent<Renderer>().sharedMaterial = placeholderMaterial;
                }
            }
            gameObject.SetActive(false);
        }

        // Its own pickups: a boost orb is used up and a share of its boost is
        // speed above the pace, bleeding back. A laser gate whose beam it
        // crossed this step wrecks it — the patrol's own test (D34's
        // LaserGate.Touches over the stretch the step covered), flagged and
        // acted on after the step. It never calls LaserGate's RaiseHit: that
        // event is the ship's, and the GameManager burns the player's hull on
        // it. Everything else is the ship's.
        void OnPickedUp(ITrackPickup pickup)
        {
            if (pickup is SpeedPad pad && pad.IsBoostOrb) Body.ForwardSpeed += pad.Take() * OrbBoostShare;
            else if (pickup is LaserGate gate && !LaserPending
                     && (Body.State == ShipState.Grounded || Body.State == ShipState.Airborne)
                     && Body.Distance > Body.SweepFrom && Body.Distance - Body.SweepFrom <= 400f
                     && gate.Touches(Body.SweepFrom, Body.Distance, Body.Lateral, Body.Height, new Vector2(Entry.halfWidth, 3f)))
                LaserPending = true;
        }

        // Its own copies of the model's materials, the main colour blended
        // toward the tint — once per shell at build, so the models stay
        // batched with each other and the shared assets are never written.
        // Covers the pack's RealToon (_MainColor) and URP Lit (_BaseColor).
        static readonly int MainColorId = Shader.PropertyToID("_MainColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Paint(GameObject model, Color tint)
        {
            var copies = new System.Collections.Generic.Dictionary<Material, Material>();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    var original = shared[i];
                    if (original == null) continue;
                    if (!copies.TryGetValue(original, out var copy))
                    {
                        copy = new Material(original) { name = original.name + "_" + Entry.displayName };
                        int id = copy.HasProperty(MainColorId) ? MainColorId : copy.HasProperty(BaseColorId) ? BaseColorId : -1;
                        if (id != -1)
                        {
                            Color was = copy.GetColor(id);
                            Color to = new(tint.r, tint.g, tint.b, was.a);
                            copy.SetColor(id, Color.Lerp(was, to, tint.a));
                        }
                        copies.Add(original, copy);
                        paintedMaterials.Add(copy);
                    }
                    shared[i] = copy;
                }
                renderer.sharedMaterials = shared;
            }
        }

        void OnDestroy()
        {
            if (placeholderMaterial != null) Destroy(placeholderMaterial);
            foreach (var material in paintedMaterials) if (material != null) Destroy(material);
        }

        // ---- transitions (the system's) ----

        /// <summary>On the grid at <paramref name="distance"/> / <paramref name="lateral"/>, at rest.</summary>
        public void PlaceOnGrid(float distance, float lateral)
        {
            Body.Reset(distance, 0f);
            Body.Lateral = lateral;
            Body.SnapInterpolation();
            Driver.HomeLateral = lateral;
            ghostDistance = prevGhostDistance = distance;
            ghostSpeed = 0f;
            FinishPlace = 0;
            PendingExit = null;
            flying = false;
            InContact = false;
            LaserPending = false;
            WreckNote = string.Empty;
            traceHead = traceCount = 0;
            RamHull = Mathf.Max(1, Entry.ramHull);
            LastShipContactTime = float.NegativeInfinity;
            fullSimMeters = fullSimSeconds = ghostMeters = ghostSeconds = 0f;
            State = RacerState.Grid;
            gameObject.SetActive(true);
            SetVisible(true);
            ApplyPose(1f);
        }

        /// <summary>Lights out: off the grid at the launch speed, full sim.</summary>
        public void Launch(float speed)
        {
            Body.ForwardSpeed = speed;
            State = RacerState.Racing;
        }

        /// <summary>Full sim → ghost: keeps distance and speed, hides the model.</summary>
        public void ToGhost()
        {
            ghostDistance = prevGhostDistance = Body.Distance;
            ghostSpeed = Body.ForwardSpeed;
            Driver.HomeLateral = Mathf.Clamp(Body.Lateral, -track.HalfWidth, track.HalfWidth);
            State = RacerState.Ghost;
            SetVisible(false);
        }

        /// <summary>Ghost → full sim: a fresh body at its distance and speed, on <paramref name="lateral"/>.</summary>
        public void Seat(float lateral)
        {
            Body.Reset(ghostDistance, ghostSpeed);
            Body.Lateral = lateral;
            Body.SnapInterpolation();
            State = RacerState.Racing;
            SetVisible(true);
            ApplyPose(1f);
        }

        /// <summary>One ghost tick: toward <paramref name="pace"/> at <paramref name="accel"/>, then on along the track.</summary>
        public void StepGhost(float dt, float pace, float accel)
        {
            prevGhostDistance = ghostDistance;
            ghostSpeed = Mathf.MoveTowards(ghostSpeed, pace, accel * dt);
            ghostDistance += ghostSpeed * dt;
            ghostMeters += ghostSpeed * dt;
            ghostSeconds += dt;
        }

        /// <summary>Counts a full-sim tick toward the average-speed readout.</summary>
        public void CountFullSim(float dt)
        {
            fullSimMeters += Body.ForwardSpeed * dt;
            fullSimSeconds += dt;
        }

        /// <summary>
        /// Its place is fixed. From full sim it flies on off the lip's line
        /// (<paramref name="gravity"/>) for <paramref name="flightSeconds"/>,
        /// then is parked; a ghost that crosses the line is parked at once.
        /// </summary>
        public void Finish(int place, float gravity, float flightSeconds)
        {
            bool wasVisible = State == RacerState.Racing;
            FinishPlace = place;
            ghostDistance = track.HasEnd ? track.EndDistance : Distance;
            State = RacerState.Finished;
            if (wasVisible) BeginFlight(gravity, flightSeconds);
            else SetVisible(false);
        }

        /// <summary>Out of the race: a fireball where it was (when it was seen), the model gone.</summary>
        public void Destroyed(ExplosionLook look)
        {
            bool wasVisible = State == RacerState.Racing;
            WreckedInView = wasVisible;
            WreckLateral = Lateral;
            WreckPosition = visual != null ? visual.position : transform.position;
            ghostDistance = Distance;
            State = RacerState.Dnf;
            flying = false;
            if (wasVisible && look.textures != null && look.textures.Count > 0)
                FX.ExplosionVfx.SpawnFireball(visual.position, look.textures, look.scale, look.lifetime, look.particles);
            SetVisible(false);
        }

        /// <summary>Parked: hidden, inert until the next grid.</summary>
        public void Park()
        {
            flying = false;
            SetVisible(false);
        }

        void BeginFlight(float gravity, float seconds)
        {
            track.GetPoseAtDistance(Body.Distance, Body.Lateral, out Vector3 position, out Quaternion rotation);
            position += rotation * (Vector3.up * Body.Height);
            flyVelocity = rotation * new Vector3(Body.TotalLateralVelocity, Body.VerticalVelocity, Body.ForwardSpeed);
            flyRotation = rotation;
            flyPosition = position;
            prevFlyPosition = transform.position; // from where it is SEEN, so the flight starts without a pop
            flyGravity = gravity;
            flyTimer = 0f;
            flyFor = seconds;
            flying = true;
        }

        /// <summary>One tick of the off-track flight; parks itself when it is over.</summary>
        public void StepFlight(float dt)
        {
            if (!flying) return;
            prevFlyPosition = flyPosition;
            flyVelocity += Vector3.down * (flyGravity * dt);
            flyPosition += flyVelocity * dt;
            if (flyVelocity.sqrMagnitude > 1f) flyRotation = Quaternion.LookRotation(flyVelocity, flyRotation * Vector3.up);
            flyTimer += dt;
            if (flyTimer >= flyFor) Park();
        }

        void SetVisible(bool visible)
        {
            if (visual != null && visual.gameObject.activeSelf != visible) visual.gameObject.SetActive(visible);
        }

        /// <summary>Seats the model between its last two ticks (alpha 0..1).</summary>
        public void ApplyPose(float alpha)
        {
            if (flying)
            {
                transform.SetPositionAndRotation(Vector3.Lerp(prevFlyPosition, flyPosition, alpha), flyRotation);
                return;
            }
            if (State != RacerState.Racing && State != RacerState.Grid) return;

            float distance = Body.DistanceAt(alpha);
            float lateral = Body.LateralAt(alpha);
            float height = Body.HeightAt(alpha);
            Vector3 pos;
            Quaternion rot;
            if (distance >= 0f) track.GetPoseAtDistance(distance, lateral, out pos, out rot);
            else
            {
                // Before the start line: straight back from it.
                track.GetPoseAtDistance(0f, lateral, out pos, out rot);
                pos += rot * Vector3.back * -distance;
            }
            if (height > 0f) pos += rot * (Vector3.up * height);
            transform.SetPositionAndRotation(pos, rot);

            if (visual != null)
            {
                float bob = (Mathf.PerlinNoise(Time.time * 1.3f, Index * 0.37f) - 0.5f) * 0.8f;
                visual.localPosition = Vector3.up * (Entry.hoverHeight + bob);
                float bank = -Mathf.Clamp(Body.BankDemand, -1.25f, 1.25f) * 25f;
                float dt = Time.deltaTime;
                shownBank = dt > 0f ? Mathf.Lerp(shownBank, bank, 1f - Mathf.Exp(-6f * dt)) : bank;
                visual.localRotation = Quaternion.Euler(Body.PitchDegrees, 0f, shownBank);
            }
        }
    }

    /// <summary>The explosion's look, read off the run's settings by the system and handed to a rival as it goes.</summary>
    public struct ExplosionLook
    {
        public System.Collections.Generic.IReadOnlyList<Texture2D> textures;
        public float scale, lifetime;
        public int particles;
    }
}
