using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Simulation;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// The police patrol chasing the ship down the track. Rubber-band chase:
    /// its target speed is the ship's current speed times a factor, floored
    /// by a minimum that starts at the launch speed and slowly ramps up — so
    /// it speeds up with the player but never drops below that threshold,
    /// and without boost orbs it always closes in.
    ///
    /// <b>It drives the same <see cref="TrackBody"/> the ship does</b>, through
    /// a <see cref="PatrolDriver"/> that outputs steer / throttle / brake: the
    /// rubber band is simply the body's cruise target (thrust and over-cruise
    /// bleed both set to the catch-up accel, so the speed moves toward it
    /// exactly as the old MoveTowards did), and everything else — steering
    /// force, grip on flat sweeps, open edges, ramps and jumps, tubes, the
    /// swept pickup query — is the ship's own physics. So it steers for the
    /// ship, goes after boost orbs (which it uses up), rounds a ramp or jumps
    /// it, brakes for flat sweeps, and can fall off: a fall redeploys it
    /// behind the ship and never touches the player. It takes every loop
    /// perfectly (no gate is asked of it). Like the ship it ticks in
    /// FixedUpdate and renders an interpolated pose, so
    /// <see cref="DistanceTravelled"/> / <see cref="GapToShip"/> are the
    /// rendered values (minimap) and the catch is judged on the ticks' own.
    ///
    /// <b>Catch</b>: inside the catch distance the patrol stops gaining and
    /// sits on the ship's tail; it catches when it is also within
    /// <see cref="PatrolDefinition.catchLateral"/> across the track, or after
    /// <see cref="PatrolDefinition.sustainedCatchSeconds"/> there — a
    /// last-moment dodge works, dodging forever does not (GameManager polls
    /// <see cref="HasCaught"/>).
    /// The chase is never allowed to go stale: outrun the patrol past the
    /// redeploy distance and a fresh one cuts in just behind the ship, already
    /// faster than it, so the only way to shake it is to boost again.
    /// A scene object (referenced by the GameManager, which wires it up via
    /// <see cref="Init"/>); all chase tunables live on its
    /// <see cref="PatrolDefinition"/> asset, cloned at init so the debug menu
    /// edits a live run and never the asset on disk. The cruiser visual is
    /// still built from code — the scene object is just an empty holder.
    /// </summary>
    public class PolicePatrol : MonoBehaviour
    {
        // Inline so the chase sliders are reachable without leaving the scene.
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        PatrolDefinition definition;

        [Tooltip("All cruiser look tunables live on this asset — add new knobs there, not here.")]
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        PatrolVisualSettings visualSettings;

        ShipMotor target;
        TrackManager track;
        PatrolDefinition runtimeDef; // clone of the asset, the only copy ever mutated

        float redeployDistance;    // gap that retires this patrol for a fresh one, meters (0 = never)
        float redeployGap;         // meters behind the ship the fresh patrol drops in at
        float redeploySpeedFactor; // fresh patrol's speed as a multiple of the ship's

        float minSpeed;       // current floor: baseSpeed + accumulated ramp
        TrackBody body;       // the same track-space body the ship rides
        readonly PatrolDriver driver = new();
        float lastTickTime = float.NegativeInfinity; // Time.fixedTime of the last tick, for the render's blend
        float tailTimer;      // seconds spent inside the catch distance
        float shownBank;
        float warnCooldown;
        bool warned;          // proximity warning already raised for this approach
        float blinkTimer;
        bool blinkState;

        Transform visual;
        GameObject redLight;
        GameObject blueLight;

        // Half width / half height of the cruiser's pickup volume, metres.
        static readonly Vector2 PickupReach = new(2.5f, 2.3f);

        /// <summary>Metres from the track start (negative behind the start line), as RENDERED this frame.</summary>
        public float DistanceTravelled { get; private set; }
        public bool HasCaught { get; private set; }

        /// <summary>The track-space body the patrol drives; null until <see cref="Init"/>.</summary>
        public TrackBody Body => body;

        /// <summary>The gap on the simulation's own state — what the catch and the driver judge by.</summary>
        float SimGap => target != null && target.Body != null && body != null ? target.Body.Distance - body.Distance : float.MaxValue;
        /// <summary>How many patrols have joined the chase this run (1 = the launch patrol).</summary>
        public int PatrolNumber { get; private set; } = 1;
        public float GapToShip => target != null ? target.DistanceTravelled - DistanceTravelled : float.MaxValue;

        /// <summary>The live chase tunables — the runtime clone, so the debug menu can edit them mid-run. Null until <see cref="Init"/>.</summary>
        public PatrolDefinition Definition => runtimeDef;

        /// <summary>
        /// A fresh patrol just cut in behind the ship; the argument is its
        /// number. The patrol only rumbles here — the story line announcing it
        /// is the GameManager's (it owns the message texts on GameSettings).
        /// </summary>
        public event System.Action<int> Redeployed;

        /// <summary>
        /// The patrol closed inside its warn distance; the argument is the gap
        /// in meters. Raised once per approach (re-armed when the ship opens
        /// the gap past the warn distance again) — the GameManager turns it
        /// into the patrol's taunt line, gated by GameSettings.
        /// </summary>
        public event System.Action<float> Warned;

        /// <summary>
        /// True while the patrol stands still and cannot catch: the ship is
        /// off the track or waiting to relaunch. Separate from the motor's
        /// pause on purpose — the run's clock keeps running through a hold.
        /// </summary>
        public bool Hold { get; private set; }

        /// <summary>
        /// Starts or ends a hold. Ending it guarantees the ship a head start:
        /// a patrol closer than <paramref name="minGapOnRelease"/> is dropped
        /// back to exactly that gap (the respawn moved the ship, not the patrol).
        /// </summary>
        public void SetHold(bool hold, float minGapOnRelease = 0f)
        {
            if (Hold == hold) return;
            Hold = hold;
            if (hold || target == null) return;

            if (body != null && SimGap < minGapOnRelease)
            {
                body.Distance = target.Body.Distance - minGapOnRelease;
                body.SnapInterpolation();
                ApplyPose(1f);
            }
            tailTimer = 0f;
            warnCooldown = 0f;
            warned = GapToShip <= (runtimeDef != null ? runtimeDef.warnDistance : 0f); // no taunt for a gap the respawn made
        }

        /// <summary>Proximity rumble that grows as the patrol closes in (GameSettings.patrolProximityRumble).</summary>
        public bool ProximityRumble { get; set; } = true;

        /// <summary>
        /// Wires the scene patrol up for a run: clones its definition (with
        /// any armed <see cref="PatrolDebugSettings"/> overrides on top),
        /// builds the cruiser visual and launches the chase. Called by the
        /// GameManager in Awake; the object stays inert without it.
        /// </summary>
        public void Init(ShipMotor target)
        {
            if (this.target != null) this.target.PadImpulse -= OnShipImpulse;
            this.target = target;
            if (target != null) target.PadImpulse += OnShipImpulse;
            track = FindFirstObjectByType<TrackManager>();

            if (definition == null)
            {
                Debug.LogError($"{nameof(PolicePatrol)} has no {nameof(PatrolDefinition)} asset assigned — falling back to defaults.", this);
                definition = ScriptableObject.CreateInstance<PatrolDefinition>();
            }
            runtimeDef = Instantiate(definition);
            PatrolDebugSettings.Load().ApplyTo(runtimeDef);

            // The body is this component's own object: its events die with it.
            body = track != null ? new TrackBody(track) : null;
            if (body != null)
            {
                body.PickedUp += OnPickedUp;
                body.LeftTrack += OnLeftTrack;
            }

            BuildVisual();
            Launch();
        }

        /// <summary>
        /// Arms the "never lose them for good" rule: once the ship is more than
        /// <paramref name="distance"/> meters clear, this patrol drops out and a
        /// fresh one takes over <paramref name="gap"/> meters back, running at
        /// <paramref name="speedFactor"/> times the ship's speed. Pass 0 distance
        /// to disable. Kept separate from Spawn so the chase tunables stay on the
        /// GameSettings asset without growing its argument list.
        /// </summary>
        public void SetRedeployRule(float distance, float gap, float speedFactor)
        {
            redeployDistance = distance;
            redeployGap = gap;
            redeploySpeedFactor = speedFactor;
        }

        /// <summary>Resets the chase to the launch gap behind the start line.</summary>
        public void Launch()
        {
            if (runtimeDef == null || body == null) return; // scene object never Init'd (patrol disabled)
            body.Reset(-runtimeDef.startGap, runtimeDef.baseSpeed);
            minSpeed = runtimeDef.baseSpeed;
            HasCaught = false;
            Hold = false;
            tailTimer = 0f;
            warnCooldown = 0f;
            warned = false;
            PatrolNumber = 1;
            ApplyPose(1f);
        }

        void OnDestroy()
        {
            if (target != null) target.PadImpulse -= OnShipImpulse;
        }

        /// <summary>
        /// Boost share: every speed-up the ship collects (orbs, ramp takeoffs —
        /// anything through <see cref="ShipMotor.AddSpeedImpulse"/>) hands the
        /// patrol <see cref="PatrolDefinition.boostShare"/> of the ship's ACTUAL
        /// gain at once. Without it a boost opened a gap the rubber band only
        /// closed at catch-up accel, so a few orbs left the patrol for dead;
        /// with it the patrol reacts in the same frame and only the remaining
        /// share is breathing room. Brakes are the player's problem alone —
        /// sharing them would make brake pads harmless. The floor is left
        /// untouched: the share is speed, not a new minimum, so a coasting ship
        /// still gets the ordinary rubber band rather than a boosted floor.
        /// </summary>
        void OnShipImpulse(float rawMagnitude)
        {
            if (rawMagnitude <= 0f || runtimeDef == null || body == null || HasCaught) return;
            float gain = target.Definition != null ? target.Definition.ScalePadEffect(rawMagnitude) : rawMagnitude;
            body.ForwardSpeed += gain * runtimeDef.boostShare;
        }

        // Its own pickups: only boost orbs mean anything to it. The orb is
        // used up (it disappears) and a share of its boost is speed above the
        // rubber band's target, which then bleeds back at the catch-up accel.
        // Brake pads and coins are the player's alone.
        void OnPickedUp(ITrackPickup pickup)
        {
            if (pickup is SpeedPad pad && pad.IsBoostOrb)
                body.ForwardSpeed += pad.Take() * runtimeDef.orbBoostShare;
        }

        // Over an open edge (a mistimed ramp, a slide the driver misjudged):
        // this cruiser is gone and a fresh one drops in behind the ship. The
        // player is never frozen or penalised by it — and, unlike outrunning
        // a patrol, it does not raise the rubber band's floor.
        void OnLeftTrack(int side) => Redeploy(raiseFloor: false);

        void Update()
        {
            if (target == null || track == null || runtimeDef == null || body == null) return;

            Blink(Time.deltaTime);

            if (!HasCaught && !target.Paused && !Hold)
            {
                // Proximity rumble that grows as the patrol closes in. The
                // haptics channel self-fades when this stops being refreshed
                // (pause, catch, hold, or the patrol falling behind again).
                float gap = GapToShip;
                if (ProximityRumble && gap <= runtimeDef.warnDistance && runtimeDef.warnDistance > 0f)
                    HapticsSystem.Instance.SetChaseIntensity(1f - Mathf.Clamp01(gap / runtimeDef.warnDistance));
            }

            // Between two ticks the pose is the track-space blend of them; with
            // no fresh tick (paused, held, caught) it rests on the current state.
            ApplyPose(Mathf.Clamp01((Time.time - lastTickTime) / Mathf.Max(Time.fixedDeltaTime, 1e-5f)));
        }

        void FixedUpdate()
        {
            if (target == null || track == null || runtimeDef == null || body == null) return;
            // Freezes with the ship: tuning screen open or run over — and
            // holds while the ship is off the track or waiting to relaunch.
            if (HasCaught || target.Paused || Hold || target.Body == null) return;

            float dt = Time.fixedDeltaTime;
            GameSettings rules = target.DashSettings;
            int substeps = Mathf.Max(1, rules != null ? rules.simSubsteps : 1);

            body.BeginTick();
            float h = dt / substeps;
            for (int i = 0; i < substeps && !HasCaught; i++) Step(h, rules);
            lastTickTime = Time.fixedTime;
        }

        void Step(float dt, GameSettings rules)
        {
            // Rubber band: chase the ship's speed (scaled), but never drop
            // below the floor — the launch speed plus the accumulated ramp.
            minSpeed += runtimeDef.ramp * dt;
            float desired = Mathf.Max(minSpeed, target.CurrentSpeed * runtimeDef.rubberBand);

            float gap = SimGap;
            bool onTail = gap <= runtimeDef.catchDistance;
            // On the ship's tail it stops gaining: it matches the ship and
            // works on the sideways gap instead of driving through it.
            if (onTail) desired = Mathf.Min(desired, target.CurrentSpeed);

            BodyControls controls = driver.Drive(body, target.Body, track, runtimeDef, gap, out float speedCap);
            desired = Mathf.Min(desired, speedCap);

            // The rubber band IS the body's speed model: cruise = the target,
            // thrust and over-cruise bleed = the catch-up accel, so the speed
            // moves toward the target at that rate either way.
            body.Params = new BodyParams
            {
                impulseBlendRate = 1000f,
                cruiseSpeed = desired,
                thrust = runtimeDef.catchUpAccel,
                brakeDecel = runtimeDef.brakeDecel,
                coastDrag = 0f,
                passiveDeceleration = runtimeDef.catchUpAccel,
                lateralSpeed = runtimeDef.lateralSpeed,
                handlingResponse = runtimeDef.handlingResponse,
                gripBase = runtimeDef.gripBase,
                gripPerSpeed = runtimeDef.gripPerSpeed,
                slideThreshold = 4f,
                slideSpeedLoss = 0.1f,
                jumpStrength = 1f,
                wallHitCooldownSeconds = 0.5f,
                pickupReach = PickupReach,
                edgeOverhang = rules != null ? rules.edgeOverhang : 1f,
                edgeGraceSeconds = rules != null ? rules.edgeGraceSeconds : 0.25f,
            };
            body.Step(dt, controls);
            if (body.State == ShipState.OffTrack) return; // it fell: OnLeftTrack has already redeployed it

            // Never through the ship: the tail is as close as it gets.
            if (SimGap < 1f) body.Distance = target.Body.Distance - 1f;

            if (redeployDistance > 0f && SimGap > redeployDistance) Redeploy(raiseFloor: true);

            UpdateCatch(dt);
            if (!HasCaught) WarnIfClose(dt);
        }

        // Inside the catch distance: caught when also close enough across the
        // track, or after long enough there whatever the sideways gap.
        void UpdateCatch(float dt)
        {
            if (SimGap > runtimeDef.catchDistance) { tailTimer = 0f; return; }
            tailTimer += dt;

            float across = target.Body.Lateral - body.Lateral;
            if (track.SectionAt(body.Distance) is TubeSection { Unbounded: true } tube)
                across = Mathf.Repeat(across + tube.Circumference * 0.5f, tube.Circumference) - tube.Circumference * 0.5f;

            if (Mathf.Abs(across) <= runtimeDef.catchLateral || tailTimer >= runtimeDef.sustainedCatchSeconds)
                HasCaught = true;
        }

        /// <summary>
        /// A fresh interceptor cuts in just behind the ship — same cruiser,
        /// new number. When the old one was OUTRUN it arrives above the ship's
        /// current speed and that speed becomes the rubber band's new floor,
        /// so coasting is never enough: the player has to find more boosts to
        /// open the gap again. When the old one merely fell off the track the
        /// floor is left alone.
        /// </summary>
        void Redeploy(bool raiseFloor)
        {
            float speed = raiseFloor
                ? Mathf.Max(minSpeed, target.CurrentSpeed * redeploySpeedFactor)
                : Mathf.Max(minSpeed, target.CurrentSpeed * runtimeDef.rubberBand);
            if (raiseFloor) minSpeed = speed;
            body.Reset(target.Body.Distance - redeployGap, speed);
            tailTimer = 0f;
            warnCooldown = 0f;
            warned = false;
            PatrolNumber++;
            ApplyPose(1f);

            HapticsSystem.Instance.Pulse(0.6f, 0.4f, 0.4f);
            Redeployed?.Invoke(PatrolNumber);
        }

        // One warning per approach: it fires when the gap first drops inside
        // the warn distance and re-arms once the ship has opened it again (the
        // cooldown keeps a gap hovering on the line from flapping). The gap
        // itself is on the minimap every frame, so no readout is repeated.
        void WarnIfClose(float dt)
        {
            warnCooldown -= dt;
            float gap = SimGap;
            if (gap > runtimeDef.warnDistance)
            {
                if (warnCooldown <= 0f) warned = false;
                return;
            }
            if (warned) return;
            warned = true;
            warnCooldown = 1.5f;
            Warned?.Invoke(gap);
        }

        void ApplyPose(float alpha)
        {
            if (body == null)
            {
                // Edit-mode preview, or never Init'd: nothing to seat.
                return;
            }

            float distance = body.DistanceAt(alpha);
            float lateral = body.LateralAt(alpha);
            float height = body.HeightAt(alpha);
            DistanceTravelled = distance;

            Vector3 pos;
            Quaternion rot;
            if (distance >= 0f)
            {
                track.GetPoseAtDistance(distance, lateral, out pos, out rot);
            }
            else
            {
                // Before the start line: extrapolate straight back from it.
                track.GetPoseAtDistance(0f, lateral, out pos, out rot);
                pos += rot * Vector3.back * -distance;
            }
            // The lift is along the track's up, so it survives roll (loops, tubes).
            if (height > 0f) pos += rot * (Vector3.up * height);

            transform.SetPositionAndRotation(pos, rot);

            // Hover bob on the visual child only, same trick as the ship —
            // plus a lean into its steering and the nose following a jump.
            if (visual != null)
            {
                float bob = (Mathf.PerlinNoise(Time.time * 1.3f, 0.53f) - 0.5f) * 0.8f;
                visual.localPosition = new Vector3(0f, 2f + bob, 0f);
                float dt = Time.deltaTime;
                float bank = -Mathf.Clamp(body.BankDemand, -1.25f, 1.25f) * 25f;
                shownBank = dt > 0f ? Mathf.Lerp(shownBank, bank, 1f - Mathf.Exp(-6f * dt)) : bank;
                visual.localRotation = Quaternion.Euler(body.PitchDegrees, 0f, shownBank);
            }
        }

        void Blink(float dt)
        {
            blinkTimer += dt;
            if (blinkTimer < 0.25f) return;
            blinkTimer = 0f;
            blinkState = !blinkState;
            if (redLight != null) redLight.SetActive(blinkState);
            if (blueLight != null) blueLight.SetActive(!blinkState);
        }

        /// <summary>Destroys the built cruiser visual — the runtime build or the baked editor preview.</summary>
        void TearDownVisual()
        {
            var existing = visual != null ? visual : transform.Find("Visual");
            if (existing != null) Kill(existing.gameObject);
            visual = null;
            redLight = null;
            blueLight = null;
        }

        /// <summary>Editor bake: regenerates the cruiser preview from the visual settings so the prefab is visible before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview() => BuildVisual();

        // Cop cruiser built from primitives: dark hull, white cabin, two side
        // skids and an alternating red/blue light bar. Materials and
        // proportions come from the PatrolVisualSettings asset (hardcoded
        // fallbacks keep an unwired patrol working).
        void BuildVisual()
        {
            TearDownVisual();
            var vs = visualSettings;

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            visual.localScale = Vector3.one * (vs != null ? vs.overallScale : 1.6f);

            Material bodyMat = vs != null && vs.bodyMaterial != null ? vs.bodyMaterial : MakeMaterial(new Color(0.08f, 0.09f, 0.14f));
            Material trimMat = vs != null && vs.trimMaterial != null ? vs.trimMaterial : MakeMaterial(Color.white);
            Material redMat = vs != null && vs.redLightMaterial != null ? vs.redLightMaterial : MakeMaterial(new Color(1f, 0.1f, 0.1f), emissive: true);
            Material blueMat = vs != null && vs.blueLightMaterial != null ? vs.blueLightMaterial : MakeMaterial(new Color(0.25f, 0.45f, 1f), emissive: true);

            Vector3 hullSize = vs != null ? vs.hullSize : new Vector3(3f, 0.9f, 6f);
            Vector3 cabinPos = vs != null ? vs.cabinPosition : new Vector3(0f, 0.7f, -0.4f);
            Vector3 cabinSize = vs != null ? vs.cabinSize : new Vector3(2f, 0.7f, 2.6f);
            Vector3 skidPos = vs != null ? vs.skidPosition : new Vector3(1.8f, -0.1f, 0f);
            Vector3 skidSize = vs != null ? vs.skidSize : new Vector3(0.6f, 0.5f, 4f);
            Vector3 lightPos = vs != null ? vs.lightPosition : new Vector3(0.55f, 1.35f, -0.4f);
            Vector3 lightScale = Vector3.one * (vs != null ? vs.lightDiameter : 0.7f);

            AddPart(PrimitiveType.Cube, Vector3.zero, hullSize, bodyMat);
            AddPart(PrimitiveType.Cube, cabinPos, cabinSize, trimMat);
            AddPart(PrimitiveType.Cube, new Vector3(-skidPos.x, skidPos.y, skidPos.z), skidSize, bodyMat);
            AddPart(PrimitiveType.Cube, skidPos, skidSize, bodyMat);

            redLight = AddPart(PrimitiveType.Sphere, new Vector3(-lightPos.x, lightPos.y, lightPos.z), lightScale, redMat);
            blueLight = AddPart(PrimitiveType.Sphere, lightPos, lightScale, blueMat);
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        GameObject AddPart(PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat)
        {
            var part = GameObject.CreatePrimitive(type);
            Kill(part.GetComponent<Collider>()); // purely visual
            part.transform.SetParent(visual, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = mat;
            return part;
        }

        static Material MakeMaterial(Color color, bool emissive = false)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 3f);
            }
            return mat;
        }
    }
}
