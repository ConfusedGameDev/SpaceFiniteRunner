using UnityEngine;

namespace FiniteRunner
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
    /// Spawned and configured entirely from code by the GameManager — visual,
    /// lights and all — so no scene wiring is needed.
    /// </summary>
    public class PolicePatrol : MonoBehaviour
    {
        ShipMotor target;
        TrackManager track;

        float baseSpeed;      // m/s — launch speed and the rubber band's initial floor
        float ramp;           // m/s per second added to the floor
        float rubberBand;     // ship-speed multiplier the patrol chases
        float catchUpAccel;   // m/s per second the patrol may change speed by
        float startGap;       // meters behind the start line at launch
        float catchDistance;  // gap that counts as caught, meters
        float warnDistance;   // gap below which warnings spawn, meters
        float alertLead;      // meters ahead of the ship the warnings spawn at

        float redeployDistance;    // gap that retires this patrol for a fresh one, meters (0 = never)
        float redeployGap;         // meters behind the ship the fresh patrol drops in at
        float redeploySpeedFactor; // fresh patrol's speed as a multiple of the ship's

        float minSpeed;       // current floor: baseSpeed + accumulated ramp
        float currentSpeed;
        float warnCooldown;
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

        public static PolicePatrol Spawn(ShipMotor target, float speedKmh, float rampKmhPerSecond,
                                         float rubberBandFactor, float catchUpAccelKmhPerSecond,
                                         float startGap, float catchDistance, float warnDistance,
                                         float alertLead)
        {
            var go = new GameObject("PolicePatrol");
            var patrol = go.AddComponent<PolicePatrol>();
            patrol.target = target;
            patrol.track = FindFirstObjectByType<TrackManager>();
            patrol.baseSpeed = speedKmh / 3.6f;
            patrol.ramp = rampKmhPerSecond / 3.6f;
            patrol.rubberBand = rubberBandFactor;
            patrol.catchUpAccel = catchUpAccelKmhPerSecond / 3.6f;
            patrol.startGap = startGap;
            patrol.catchDistance = catchDistance;
            patrol.warnDistance = warnDistance;
            patrol.alertLead = alertLead;
            patrol.BuildVisual();
            patrol.Launch();
            return patrol;
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
            DistanceTravelled = -startGap;
            minSpeed = baseSpeed;
            currentSpeed = baseSpeed;
            HasCaught = false;
            warnCooldown = 0f;
            PatrolNumber = 1;
            ApplyPose();
        }

        void Update()
        {
            if (target == null || track == null) return;

            Blink(Time.deltaTime);

            // Freezes with the ship: tuning screen open or run over.
            if (!HasCaught && !target.Paused)
            {
                float dt = Time.deltaTime;

                // Rubber band: chase the ship's speed (scaled), but never drop
                // below the floor — the launch speed plus the accumulated ramp.
                minSpeed += ramp * dt;
                float desired = Mathf.Max(minSpeed, target.CurrentSpeed * rubberBand);
                currentSpeed = Mathf.MoveTowards(currentSpeed, desired, catchUpAccel * dt);

                DistanceTravelled += currentSpeed * dt;

                if (redeployDistance > 0f && GapToShip > redeployDistance) Redeploy();

                if (GapToShip <= catchDistance) HasCaught = true;
                else WarnIfClose(dt);

                // Proximity rumble that grows as the patrol closes in. The
                // haptics channel self-fades when this stops being refreshed
                // (pause, catch, or the patrol falling behind again).
                float gap = GapToShip;
                if (gap <= warnDistance && warnDistance > 0f)
                    HapticsSystem.Instance.SetChaseIntensity(1f - Mathf.Clamp01(gap / warnDistance));
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
            PatrolNumber++;
            ApplyPose();

            FloatingTextSystem.Instance.DisplayText(
                $"PATROL {PatrolNumber} INBOUND", new Color(1f, 0.35f, 0.3f), 1.6f, alertLead, 3.5f);
            HapticsSystem.Instance.Pulse(0.6f, 0.4f, 0.4f);
        }

        void WarnIfClose(float dt)
        {
            warnCooldown -= dt;
            if (GapToShip > warnDistance || warnCooldown > 0f) return;
            warnCooldown = 1.5f;

            // Spawned well ahead of the ship (GameSettings.patrolAlertLeadMeters)
            // so the warning stays readable instead of being left behind
            // instantly at these speeds.
            FloatingTextSystem.Instance.DisplayText(
                $"PATROL {GapToShip:0} M", new Color(1f, 0.35f, 0.3f), 1.2f, alertLead, 3f);
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

        // Cop cruiser built from primitives: dark hull, white cabin, two side
        // skids and an alternating red/blue light bar.
        void BuildVisual()
        {
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            visual.localScale = Vector3.one * 1.6f;

            var bodyMat = MakeMaterial(new Color(0.08f, 0.09f, 0.14f));
            var trimMat = MakeMaterial(Color.white);

            AddPart(PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(3f, 0.9f, 6f), bodyMat);
            AddPart(PrimitiveType.Cube, new Vector3(0f, 0.7f, -0.4f), new Vector3(2f, 0.7f, 2.6f), trimMat);
            AddPart(PrimitiveType.Cube, new Vector3(-1.8f, -0.1f, 0f), new Vector3(0.6f, 0.5f, 4f), bodyMat);
            AddPart(PrimitiveType.Cube, new Vector3(1.8f, -0.1f, 0f), new Vector3(0.6f, 0.5f, 4f), bodyMat);

            redLight = AddPart(PrimitiveType.Sphere, new Vector3(-0.55f, 1.35f, -0.4f), Vector3.one * 0.7f,
                               MakeMaterial(new Color(1f, 0.1f, 0.1f), emissive: true));
            blueLight = AddPart(PrimitiveType.Sphere, new Vector3(0.55f, 1.35f, -0.4f), Vector3.one * 0.7f,
                                MakeMaterial(new Color(0.25f, 0.45f, 1f), emissive: true));
        }

        GameObject AddPart(PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat)
        {
            var part = GameObject.CreatePrimitive(type);
            Destroy(part.GetComponent<Collider>()); // purely visual
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
