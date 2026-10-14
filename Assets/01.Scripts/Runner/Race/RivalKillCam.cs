using UnityEngine;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.Contracts;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.Race
{
    /// <summary>
    /// The race's kill cam (RaceModePRD D4, R7): a rival wrecked in view near
    /// the ship is SHOWN. For <see cref="RaceDefinition.killCamSeconds"/> of
    /// real time the world clock drops to
    /// <see cref="RaceDefinition.killCamTimeScale"/>, the picture cuts to a
    /// shot planted beside the explosion (the rig's focus shot,
    /// <see cref="OrbitCameraRig.SetFocusShot"/>), the ship holds its line on
    /// autopilot (stick, brake and dash ignored, throttle held) and contact
    /// with the field is off; then it cuts back and the clock eases home.
    ///
    /// Sits beside the <see cref="RaceSystem"/> on <c>PF_RaceSystem</c>. It
    /// decides nothing on its own: the composition root (the GameManager)
    /// asks <see cref="TryPlay"/> on every wreck, and keeps the countdown,
    /// the hull and the bounty's "+$N" in step with <see cref="Active"/> and
    /// <see cref="Ended"/> — the kill cam must never cost the player.
    ///
    /// Clock ownership follows <see cref="DodgeSlowMo"/> exactly: ENTER only
    /// when the clock reads 1, remember what was written, and if anyone else
    /// writes it (a menu) the kill cam ends at once, restoring only the fixed
    /// step — so the loop, the duel, the dodge and every menu win.
    /// </summary>
    [DisallowMultipleComponent]
    public class RivalKillCam : MonoBehaviour
    {
        RaceSystem race;
        IChaseTarget ship;
        IControlTakeover control;
        OrbitCameraRig rig;

        float baseFixedDelta;
        float appliedScale = 1f;
        bool owning;          // we wrote the clock last, and it still reads our value
        float blend;          // 0 normal clock .. 1 full slow motion
        float holdLeft;       // real seconds of full slow motion still to play
        bool holding;         // the shot is up (blend in + hold); false = blending out
        bool steering;        // we switched the ship's autopilot on
        float readyAt;        // unscaled time the cooldown ends

        /// <summary>True from the cut to the explosion until the clock is home again.</summary>
        public bool Active { get; private set; }

        /// <summary>Kill cams played since the scene loaded, and the real seconds the last one held the picture and the clock — the debug page's readout.</summary>
        public int PlayCount { get; private set; }
        public float LastShotSeconds { get; private set; }
        public float LastTotalSeconds { get; private set; }
        public float LowestScale { get; private set; } = 1f;
        float startedAt, cutBackAt;

        /// <summary>Raised once a kill cam is over (or cut short): the clock is back, the picture too.</summary>
        public event System.Action Ended;

        /// <summary>The kill cam beside <paramref name="race"/>: the placed one, else added (a scene laid out before M4).</summary>
        public static RivalKillCam Ensure(RaceSystem race)
        {
            var cam = race.GetComponent<RivalKillCam>();
            return cam != null ? cam : race.gameObject.AddComponent<RivalKillCam>();
        }

        void Awake() => baseFixedDelta = Time.fixedDeltaTime;

        /// <summary>Binds it to the run: the race it watches, the ship it holds, the rig it cuts. Any of them null leaves it idle.</summary>
        public void Bind(RaceSystem raceSystem, IChaseTarget chaseTarget, IControlTakeover takeover, OrbitCameraRig cameraRig)
        {
            Cancel();
            race = raceSystem;
            ship = chaseTarget;
            control = takeover;
            rig = cameraRig;
            readyAt = 0f;
        }

        /// <summary>
        /// Plays the kill cam for <paramref name="rival"/>'s wreck when it is
        /// worth one and nothing else owns the moment (R7.1): wrecked in view,
        /// within range, the clock free, the ship steady and short of the
        /// final run-up, nothing else holding the picture, the cooldown over,
        /// none playing already (R7.5). False = the explosion just plays.
        /// The caller has already checked the run is not decided.
        /// </summary>
        public bool TryPlay(Rival rival)
        {
            if (race == null || ship == null || rig == null || rival == null || !race.Bound) return false;
            RaceDefinition def = race.Definition;
            if (def == null || !def.killCam || Active) return false;
            if (!rival.WreckedInView || Mathf.Abs(rival.Distance - ship.Distance) > def.killCamRange) return false;
            if (!Mathf.Approximately(Time.timeScale, 1f) || DodgeSlowMo.IsActive || LoopSlowMo.IsActive || DuelSlowMo.IsActive) return false;
            if (Time.unscaledTime < readyAt || ship.Paused || !ship.Steady || ship.HasLeftTrackEnd) return false;
            TrackManager track = race.Track;
            if (track == null || (track.HasEnd && track.EndZoneStart >= 0f && ship.Distance >= track.EndZoneStart)) return false;
            if (rig.Cinematic || rig.FocusShot) return false;

            // The shot: planted out to the side of the explosion AWAY from the
            // ship and a little ahead along the track, so the wreck fills the
            // frame and the ship flies past behind it.
            float side = rival.WreckLateral >= ship.Lateral ? 1f : -1f;
            track.GetPoseAtDistance(Mathf.Max(0f, rival.Distance), rival.WreckLateral, out _, out Quaternion frame);
            Vector3 offset = def.killCamOffset;
            Vector3 camera = rival.WreckPosition + frame * new Vector3(side * offset.x, offset.y, offset.z);
            if (!rig.SetFocusShot(camera, rival.WreckPosition)) return false;

            owning = true;
            appliedScale = 1f;
            blend = 0f;
            holdLeft = def.killCamSeconds;
            holding = true;
            Active = true;
            race.ContactSuspended = true;
            PlayCount++;
            startedAt = Time.unscaledTime;
            LowestScale = 1f;
            if (def.debugReadout) Debug.Log($"[Race] kill cam: {rival.Entry.displayName}, {Mathf.Abs(rival.Distance - ship.Distance):0} m from the ship", this);

            // The ship holds its line: autopilot on the lateral it has, which
            // also swallows the stick, the brake and the dash (R7.4).
            if (control != null && !control.Autopilot)
            {
                control.AutopilotLateral = ship.Lateral;
                control.Autopilot = true;
                steering = true;
            }
            return true;
        }

        /// <summary>Ends a kill cam now (a restart, an ending): picture, clock and controls handed back. Raises <see cref="Ended"/> if one was playing.</summary>
        public void Cancel()
        {
            if (!Active) return;
            Finish();
        }

        void OnDisable() => Cancel();

        void Update()
        {
            if (!Active) return;
            RaceDefinition def = race != null ? race.Definition : null;

            // Someone else took the clock (a menu): the moment is theirs.
            if (def == null || !Mathf.Approximately(Time.timeScale, appliedScale))
            {
                Finish();
                return;
            }

            float dt = Time.unscaledDeltaTime;
            if (holding)
            {
                blend = def.KillCamBlendIn > 0f ? Mathf.MoveTowards(blend, 1f, dt / def.KillCamBlendIn) : 1f;
                if (blend >= 1f) holdLeft -= dt;
                if (holdLeft <= 0f)
                {
                    // The cut back comes first; the clock eases home under the chase view.
                    holding = false;
                    cutBackAt = Time.unscaledTime;
                    rig.ClearFocusShot();
                    ReleaseShip();
                    race.ContactSuspended = false;
                }
            }
            else
            {
                blend = def.KillCamBlendOut > 0f ? Mathf.MoveTowards(blend, 0f, dt / def.KillCamBlendOut) : 0f;
                if (blend <= 0f)
                {
                    Finish();
                    return;
                }
            }
            Apply(Mathf.Lerp(1f, Mathf.Clamp(def.killCamTimeScale, 0.05f, 1f), Mathf.SmoothStep(0f, 1f, blend)));
        }

        void Apply(float scale)
        {
            appliedScale = scale;
            LowestScale = Mathf.Min(LowestScale, scale);
            Time.timeScale = scale;
            Time.fixedDeltaTime = baseFixedDelta * scale;
        }

        // Everything handed back, once: the clock (only the fixed step when
        // someone else holds the scale), the picture, the ship, the contact.
        void Finish()
        {
            if (owning)
            {
                if (Mathf.Approximately(Time.timeScale, appliedScale)) Time.timeScale = 1f;
                Time.fixedDeltaTime = baseFixedDelta;
                owning = false;
            }
            if (rig != null) rig.ClearFocusShot();
            ReleaseShip();
            if (race != null) race.ContactSuspended = false;
            blend = 0f;
            holding = false;
            bool was = Active;
            Active = false;
            if (was)
            {
                LastShotSeconds = (cutBackAt > startedAt ? cutBackAt : Time.unscaledTime) - startedAt;
                LastTotalSeconds = Time.unscaledTime - startedAt;
                cutBackAt = 0f;
            }
            RaceDefinition def = race != null ? race.Definition : null;
            readyAt = Time.unscaledTime + (def != null ? def.killCamCooldown : 0f);
            if (was) Ended?.Invoke();
        }

        void ReleaseShip()
        {
            if (!steering) return;
            steering = false;
            if (control == null) return;
            control.Autopilot = false;
            control.ConsumeDashRequest(); // a press made during the shot is not a dash after it
        }
    }
}
