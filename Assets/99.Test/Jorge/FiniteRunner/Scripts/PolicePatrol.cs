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

        /// <summary>Resets the chase to the launch gap behind the start line.</summary>
        public void Launch()
        {
            DistanceTravelled = -startGap;
            minSpeed = baseSpeed;
            currentSpeed = baseSpeed;
            HasCaught = false;
            warnCooldown = 0f;
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

                if (GapToShip <= catchDistance) HasCaught = true;
                else WarnIfClose(dt);
            }

            ApplyPose();
        }

        void WarnIfClose(float dt)
        {
            warnCooldown -= dt;
            if (GapToShip > warnDistance || warnCooldown > 0f) return;
            warnCooldown = 1.5f;

            // Spawned well ahead of the ship (GameManager.patrolAlertLeadMeters)
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
