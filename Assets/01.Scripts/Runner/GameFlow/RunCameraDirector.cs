using UnityEngine;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// The runner's camera direction: attaches the chase rig to the ship,
    /// pulls out to the Far framing for a jump, cuts to the planted cinematic
    /// shot for a loop and for an off-track fall, drives the duel dolly, and
    /// plants the shot an ending asks for. Split out of the
    /// <see cref="GameManager"/> (refactor Step 8.5): the run decides WHAT
    /// happens, this decides how it is framed. Hand-placed beside the
    /// GameManager in <c>PF_Systems</c> and bound by it in Awake; it reads the
    /// run only through <see cref="IRunState"/>, and every knob it uses lives on
    /// <see cref="GameSettings"/> (the camera's own on the placed rig's asset).
    /// Without a placed rig it does nothing and the scene keeps its camera.
    /// </summary>
    public class RunCameraDirector : MonoBehaviour
    {
        ShipMotor motor;
        PolicePatrol patrol;
        GameSettings settings;
        IRunState run;

        OrbitCameraRig cameraRig;          // null when GameSettings has no camera asset
        CameraMode modeBeforeJump;         // the view a jump forced to Far hands back on landing (or on a respawn)
        bool jumpHoldsFar;                 // a takeoff forced Far and nothing has handed the view back yet
        float fallCameraLeft = -1f;        // seconds until the camera stops following an off-track fall, -1 = not counting
        bool fallCinematic;                // the rig is holding the planted shot for an off-track fall
        bool loopCinematic;                // the rig is holding the cinematic shot for a loop (and its fall)
        float loopCinematicHoldLeft = -1f; // real seconds the shot lingers past the exit, -1 = not releasing

        /// <summary>The chase rig, or null without a camera asset — the speed lines read its mode.</summary>
        public OrbitCameraRig Rig => cameraRig;

        /// <summary>The scene's director (hand-placed beside the GameManager); added only when the scene has none.</summary>
        public static RunCameraDirector Ensure(Component host)
        {
            var director = host.GetComponent<RunCameraDirector>();
            return director != null ? director : host.gameObject.AddComponent<RunCameraDirector>();
        }

        /// <summary>
        /// Binds the director to the run and attaches the rig to the ship.
        /// Called once, from the GameManager's Awake. A null patrol just
        /// means no duel framing.
        /// </summary>
        public void Bind(ShipMotor ship, PolicePatrol chaser, GameSettings runSettings, IRunState runState)
        {
            Unsubscribe();
            motor = ship;
            patrol = chaser;
            settings = runSettings;
            run = runState;

            // The chase camera: the scene's hand-placed rig (PF_CameraController,
            // carrying its own settings asset — Fighter_CameraSettings), pointed
            // at the ship root. A scene without a rig keeps its camera as is.
            // (Refactor Step 10.3: GameSettings no longer holds a second copy of
            // the rig's asset to hand it.)
            if (motor != null && CameraRigInstaller.FindRig(motor.gameObject.scene) != null)
                cameraRig = CameraRigInstaller.Attach(motor, null);

            if (motor == null) return;
            motor.TookOff += OnTookOff;
            motor.Landed += OnLanded;
            motor.LoopEntered += OnLoopEntered;
            motor.StateChanged += OnShipStateChanged;
            motor.FellOff += OnFellOff;
            motor.RespawnStarted += OnRespawnStarted;
        }

        void Unsubscribe()
        {
            if (motor == null) return;
            motor.TookOff -= OnTookOff;
            motor.Landed -= OnLanded;
            motor.LoopEntered -= OnLoopEntered;
            motor.StateChanged -= OnShipStateChanged;
            motor.FellOff -= OnFellOff;
            motor.RespawnStarted -= OnRespawnStarted;
        }

        void OnDestroy() => Unsubscribe();

        void Update()
        {
            if (run == null) return;
            UpdateLoopCinematicHold();
            UpdateFallCamera();

            // The duel camera, driven every frame rather than on the run's
            // edges: the orbit dollies in as an attack run closes and eases back
            // out on whatever ends it — a kill, a shove, an abort, the run ending.
            if (cameraRig != null)
                cameraRig.SetDuelFraming(patrol != null && !run.RunOver ? patrol.DuelCloseness : 0f);
        }

        // ------------------------------------------------------------- endings

        /// <summary>
        /// Plants the rig's cinematic shot for an ending (the win's fly-past,
        /// the fall off the end) and takes the camera out of the player's
        /// hands. Any loop or fall shot still armed is disarmed so it cannot
        /// cut back out from under this one. Returns false when there is no
        /// rig or the camera asset refuses the shot.
        /// </summary>
        public bool PlantEndingShot()
        {
            if (cameraRig == null) return false;
            loopCinematic = false;
            loopCinematicHoldLeft = -1f;
            fallCameraLeft = -1f;
            cameraRig.SetCinematic(true); // a no-op if a loop shot is already live
            bool planted = cameraRig.Cinematic;
            if (planted) cameraRig.hasPlayerControl = false;
            return planted;
        }

        /// <summary>Cuts back to the live chase view and hands the camera back to the player.</summary>
        public void ReleaseEndingShot()
        {
            if (cameraRig == null) return;
            cameraRig.SetCinematic(false);
            cameraRig.hasPlayerControl = true;
        }

        /// <summary>A retry: no loop or fall shot left armed, no ending shot left planted, no jump's Far left forced.</summary>
        public void ResetForRun()
        {
            RestoreJumpView(instant: true);
            EndLoopCinematic();
            EndFallCamera();
            ReleaseEndingShot();
        }

        // ------------------------------------------------------------- jumps

        // A jump: the camera pulls out to the Far framing for the arc and
        // hands the player's view back on landing (a no-op if it was Far).
        // The cycle is locked meanwhile — ShipMotor.BlockModeCycle. Running
        // off an open edge takes off the same way but never lands — the
        // respawn teleports the ship back — so the respawn hands it back too.
        // A second takeoff before the view is back keeps the first one's
        // view, never the forced Far.
        void OnTookOff()
        {
            if (cameraRig == null) return;
            if (!jumpHoldsFar) modeBeforeJump = cameraRig.Mode;
            jumpHoldsFar = true;
            cameraRig.SetMode(CameraMode.Far, instant: false);
        }

        void OnLanded() => RestoreJumpView(instant: false);

        void RestoreJumpView(bool instant)
        {
            if (!jumpHoldsFar) return;
            jumpHoldsFar = false;
            if (cameraRig != null && modeBeforeJump != CameraMode.Far)
                cameraRig.SetMode(modeBeforeJump, instant);
        }

        // ------------------------------------------------------------- loops

        // A loop: the picture cuts to the rig's cinematic side shot for the
        // ride round — and, when the ship was too slow, through the fall too,
        // so the shot never cuts mid-drop. Released once the ship is Grounded
        // again (a clean exit, or the fall's landing) plus a short hold, real
        // seconds, so the exit registers before the chase view cuts back. The
        // slow-mo rides the same window on its own (LoopSlowMo).
        void OnLoopEntered(bool passed)
        {
            if (!settings.loopCinematic || cameraRig == null) return;
            loopCinematic = true;
            loopCinematicHoldLeft = -1f;
            cameraRig.SetCinematic(true);
        }

        void OnShipStateChanged(ShipState state)
        {
            if (loopCinematic && state == ShipState.Grounded && loopCinematicHoldLeft < 0f)
                loopCinematicHoldLeft = settings.loopCinematicHoldSeconds;
        }

        void UpdateLoopCinematicHold()
        {
            if (!loopCinematic || loopCinematicHoldLeft < 0f) return;
            loopCinematicHoldLeft -= Time.unscaledDeltaTime;
            if (loopCinematicHoldLeft <= 0f) EndLoopCinematic();
        }

        void EndLoopCinematic()
        {
            if (!loopCinematic) return;
            loopCinematic = false;
            loopCinematicHoldLeft = -1f;
            if (cameraRig != null) cameraRig.SetCinematic(false);
        }

        // ------------------------------------------------------------- falls

        // Over an open edge: after a beat of following the fall the camera
        // plants itself and just watches the ship go.
        void OnFellOff() => fallCameraLeft = Mathf.Max(0f, settings.fallCameraFollowSeconds);

        void UpdateFallCamera()
        {
            if (fallCameraLeft < 0f) return;
            fallCameraLeft -= Time.deltaTime;
            if (fallCameraLeft > 0f) return;
            fallCameraLeft = -1f;
            if (cameraRig == null || cameraRig.Cinematic) return;
            cameraRig.SetCinematic(true);
            fallCinematic = cameraRig.Cinematic;
        }

        // Back on the track: hand the picture back and cut the camera along
        // with the teleport instead of letting it damp across the gap.
        void OnRespawnStarted(Vector3 teleport)
        {
            EndFallCamera();
            RestoreJumpView(instant: true);
            if (cameraRig != null) cameraRig.NotifyWarp(teleport);
        }

        void EndFallCamera()
        {
            fallCameraLeft = -1f;
            if (!fallCinematic) return;
            fallCinematic = false;
            if (cameraRig != null) cameraRig.SetCinematic(false);
        }
    }
}
