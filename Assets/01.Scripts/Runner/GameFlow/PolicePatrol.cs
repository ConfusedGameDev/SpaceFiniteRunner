using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// The police patrol chasing the ship down the track. Rubber-band chase:
    /// its target speed is the ship's current speed times a factor, floored
    /// by a minimum that starts at the launch speed and slowly ramps up — so
    /// it speeds up with the player but never drops below that threshold,
    /// and without boost orbs it always closes in. When the gap drops to the
    /// catch distance the run is over (GameManager polls <see cref="HasCaught"/>).
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
        float currentSpeed;
        float warnCooldown;
        bool warned;          // proximity warning already raised for this approach
        float blinkTimer;
        bool blinkState;

        Transform visual;
        GameObject redLight;
        GameObject blueLight;

        public float DistanceTravelled { get; private set; }
        public bool HasCaught { get; private set; }
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
            this.target = target;
            track = FindFirstObjectByType<TrackManager>();

            if (definition == null)
            {
                Debug.LogError($"{nameof(PolicePatrol)} has no {nameof(PatrolDefinition)} asset assigned — falling back to defaults.", this);
                definition = ScriptableObject.CreateInstance<PatrolDefinition>();
            }
            runtimeDef = Instantiate(definition);
            PatrolDebugSettings.Load().ApplyTo(runtimeDef);

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
            if (runtimeDef == null) return; // scene object never Init'd (patrol disabled)
            DistanceTravelled = -runtimeDef.startGap;
            minSpeed = runtimeDef.baseSpeed;
            currentSpeed = runtimeDef.baseSpeed;
            HasCaught = false;
            warnCooldown = 0f;
            warned = false;
            PatrolNumber = 1;
            ApplyPose();
        }

        void Update()
        {
            if (target == null || track == null || runtimeDef == null) return;

            Blink(Time.deltaTime);

            // Freezes with the ship: tuning screen open or run over.
            if (!HasCaught && !target.Paused)
            {
                float dt = Time.deltaTime;

                // Rubber band: chase the ship's speed (scaled), but never drop
                // below the floor — the launch speed plus the accumulated ramp.
                minSpeed += runtimeDef.ramp * dt;
                float desired = Mathf.Max(minSpeed, target.CurrentSpeed * runtimeDef.rubberBand);
                currentSpeed = Mathf.MoveTowards(currentSpeed, desired, runtimeDef.catchUpAccel * dt);

                DistanceTravelled += currentSpeed * dt;

                if (redeployDistance > 0f && GapToShip > redeployDistance) Redeploy();

                if (GapToShip <= runtimeDef.catchDistance) HasCaught = true;
                else WarnIfClose(dt);

                // Proximity rumble that grows as the patrol closes in. The
                // haptics channel self-fades when this stops being refreshed
                // (pause, catch, or the patrol falling behind again).
                float gap = GapToShip;
                if (ProximityRumble && gap <= runtimeDef.warnDistance && runtimeDef.warnDistance > 0f)
                    HapticsSystem.Instance.SetChaseIntensity(1f - Mathf.Clamp01(gap / runtimeDef.warnDistance));
            }

            ApplyPose();
        }

        /// <summary>
        /// The old patrol is left in the dust, so a fresh interceptor cuts in
        /// just behind the ship — same cruiser, new number. It arrives above the
        /// ship's current speed and that speed becomes the rubber band's new
        /// floor, so coasting is never enough: the player has to find more
        /// boosts to open the gap again.
        /// </summary>
        void Redeploy()
        {
            float speed = Mathf.Max(minSpeed, target.CurrentSpeed * redeploySpeedFactor);
            minSpeed = speed;
            currentSpeed = speed;
            DistanceTravelled = target.DistanceTravelled - redeployGap;
            warnCooldown = 0f;
            warned = false;
            PatrolNumber++;
            ApplyPose();

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
            float gap = GapToShip;
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

        void ApplyPose()
        {
            Vector3 pos;
            Quaternion rot;

            if (DistanceTravelled >= 0f)
            {
                track.GetPoseAtDistance(DistanceTravelled, 0f, out pos, out rot);
            }
            else
            {
                // Before the start line: extrapolate straight back from it.
                track.GetPoseAtDistance(0f, 0f, out pos, out rot);
                pos += rot * Vector3.back * -DistanceTravelled;
            }

            transform.SetPositionAndRotation(pos, rot);

            // Hover bob on the visual child only, same trick as the ship.
            if (visual != null)
            {
                float bob = (Mathf.PerlinNoise(Time.time * 1.3f, 0.53f) - 0.5f) * 0.8f;
                visual.localPosition = new Vector3(0f, 2f + bob, 0f);
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
