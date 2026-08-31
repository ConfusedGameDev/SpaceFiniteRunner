using UnityEngine;
using UnityEngine.InputSystem;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Air-time slow motion for the PLAYER's car: once every wheel has been
    /// off the ground for <see cref="CarConfig.airSlowMoDelay"/> the world
    /// clock drops to <see cref="CarConfig.airSlowMoScale"/> and, for the rest
    /// of the jump, the right stick / arrow keys pitch and roll the car while
    /// the left stick / W-S push the clock slower or faster inside the
    /// min–max band. Landing blends the clock back to 1. Only the player
    /// carries this component (<see cref="CarController"/> adds it in Start
    /// when its driver is a <see cref="CarInput"/>), so a police cruiser
    /// flying off a ramp never slows the game.
    ///
    /// <c>Time.timeScale</c> has other owners — every menu writes 0 on open
    /// and exactly 1 on close (<c>PauseMenu</c>, <c>CityMapScreen</c>,
    /// <c>MissionBriefScreen</c>, <c>GameOverScreen</c>) and none of them
    /// touch <c>fixedDeltaTime</c> — so this component follows one ownership
    /// rule: it only ENTERS when the clock reads exactly 1, it remembers the
    /// value it last wrote and CANCELS silently (restoring the fixed step
    /// only) the moment the clock reads anything else, and it never writes
    /// the clock again until it re-enters. A pause mid-jump therefore
    /// freezes cleanly, the resume lands on 1 and, if the car is still in
    /// the air, slow-mo simply re-arms. <c>fixedDeltaTime</c> is scaled
    /// with the clock so the physics stays smooth at 0.35 and is restored
    /// on every exit, including destruction (a scene reload mid-air).
    ///
    /// Air control writes the rigidbody's local angular velocity toward the
    /// stick — pitch about X (forward = nose down), roll about Z (right =
    /// roll right), yaw left to the physics — at
    /// <see cref="CarConfig.airControlRate"/>, authored in SIM degrees per
    /// second: what the player sees is rate × clock scale, and the car lands
    /// with exactly the spin it shows, never a hidden ×(1/scale) surprise.
    /// A neutral stick applies nothing, so the natural tumble is untouched.
    /// Both physics backends share the rigidbody, and with the wheels in the
    /// air neither applies tire forces, so the same code serves both.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    [DisallowMultipleComponent]
    public class AirTimeSlowMo : MonoBehaviour
    {
        const float StickDeadzone = 0.15f;

        CarController car;
        float baseFixedDelta;
        float airTimer;
        float blend;            // 0 normal clock .. 1 full slow-mo, unscaled seconds
        bool owning;            // we wrote the clock last, and it still reads our value
        float appliedScale = 1f;
        float scaleAxis;        // -1 slower .. +1 faster, the left stick / W-S
        Vector2 rotateAxis;     // x roll, y pitch — the right stick / arrows

        /// <summary>True while the jump owns the clock and the stick — the camera hands its pan over for that window.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>0..1 how deep into slow motion the clock is, for any effect that wants to ride along.</summary>
        public static float Blend { get; private set; }

        /// <summary>Add the component to a car that has none yet — the CarController.Start hook for the player.</summary>
        public static AirTimeSlowMo Ensure(CarController car) =>
            car.GetComponent<AirTimeSlowMo>() ?? car.gameObject.AddComponent<AirTimeSlowMo>();

        void Awake()
        {
            car = GetComponent<CarController>();
            baseFixedDelta = Time.fixedDeltaTime;
        }

        void Update()
        {
            CarConfig config = car.config;
            if (config == null || !config.airSlowMo || !car.enabled)
            {
                Drop();
                airTimer = 0f;
                Publish();
                return;
            }

            bool airborne = !car.IsGrounded;
            airTimer = airborne ? airTimer + Time.deltaTime : 0f;

            // Someone else took the clock (a menu opened, or closed back to
            // 1): it is theirs now. Restore the fixed step and stand down.
            if (owning && !Mathf.Approximately(Time.timeScale, appliedScale))
                Cancel();

            if (!owning)
            {
                bool clockFree = Mathf.Approximately(Time.timeScale, 1f);
                if (airborne && airTimer >= config.airSlowMoDelay && clockFree)
                {
                    owning = true;
                    blend = 0f;
                    scaleAxis = 0f;
                    rotateAxis = Vector2.zero;
                }
                else
                {
                    Publish();
                    return;
                }
            }

            ReadInput();

            float target = airborne ? 1f : 0f;
            float seconds = airborne ? config.airSlowMoBlendIn : config.airSlowMoBlendOut;
            blend = seconds > 0f ? Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime / seconds) : target;

            if (!airborne && blend <= 0f)
            {
                Release();
                Publish();
                return;
            }

            // The stick slides the resting scale inside the band; released, it
            // sits on the default. The band is clamped around the default so
            // a min above it (or a max below it) can never invert the axis.
            float resting = scaleAxis >= 0f
                ? Mathf.Lerp(config.airSlowMoScale, Mathf.Max(config.airSlowMoScale, config.airSlowMoMaxScale), scaleAxis)
                : Mathf.Lerp(config.airSlowMoScale, Mathf.Min(config.airSlowMoScale, config.airSlowMoMinScale), -scaleAxis);
            Apply(Mathf.Lerp(1f, resting, blend));
            Publish();
        }

        /// <summary>
        /// Right stick (else arrows) → rotation; left stick Y (else W/S) →
        /// clock. Polled straight off the Input System like CarInput — the
        /// project has no .inputactions asset. The keyboard only fills an axis
        /// the pad left at rest, so the two devices never add up.
        /// </summary>
        void ReadInput()
        {
            Vector2 rotate = Vector2.zero;
            float clock = 0f;

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                if (stick.magnitude > StickDeadzone) rotate = stick;

                float left = gamepad.leftStick.y.ReadValue();
                if (Mathf.Abs(left) > StickDeadzone) clock = left;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (rotate == Vector2.zero)
                {
                    if (keyboard.leftArrowKey.isPressed) rotate.x -= 1f;
                    if (keyboard.rightArrowKey.isPressed) rotate.x += 1f;
                    if (keyboard.upArrowKey.isPressed) rotate.y += 1f;
                    if (keyboard.downArrowKey.isPressed) rotate.y -= 1f;
                }
                if (clock == 0f)
                {
                    if (keyboard.wKey.isPressed) clock += 1f;
                    if (keyboard.sKey.isPressed) clock -= 1f;
                }
            }

            rotateAxis = Vector2.ClampMagnitude(rotate, 1f);
            scaleAxis = Mathf.Clamp(clock, -1f, 1f);
        }

        /// <summary>
        /// Air control. Only the pitch (local X) and roll (local Z) components
        /// of the angular velocity are steered, each toward the stick's share
        /// of the rate at the response's acceleration; yaw is left alone.
        /// Unity's convention: +X rotation dips the nose, +Z lifts the right
        /// side — hence the sign on roll.
        /// </summary>
        void FixedUpdate()
        {
            if (!owning || blend <= 0f) return;
            CarConfig config = car.config;
            Rigidbody body = car.Body;
            if (config == null || body == null || rotateAxis.sqrMagnitude < 0.0001f) return;

            float rate = config.airControlRate * Mathf.Deg2Rad;
            float step = config.airControlResponse * Mathf.Deg2Rad * Time.fixedDeltaTime;
            Vector3 local = transform.InverseTransformDirection(body.angularVelocity);
            local.x = Mathf.MoveTowards(local.x, rotateAxis.y * rate, step);   // forward = nose down
            local.z = Mathf.MoveTowards(local.z, -rotateAxis.x * rate, step);  // right = roll right
            body.angularVelocity = transform.TransformDirection(local);
        }

        void OnDisable()
        {
            Drop();
            airTimer = 0f;
            Publish();
        }

        // ---------------------------------------------------------------- clock

        void Apply(float scale)
        {
            appliedScale = scale;
            Time.timeScale = scale;
            Time.fixedDeltaTime = baseFixedDelta * scale;
        }

        /// <summary>The jump is over and the clock is still ours: hand it back at exactly 1.</summary>
        void Release()
        {
            Apply(1f);
            owning = false;
            blend = 0f;
        }

        /// <summary>Another owner has the clock: leave it alone, only the fixed step is ours to restore.</summary>
        void Cancel()
        {
            Time.fixedDeltaTime = baseFixedDelta;
            owning = false;
            blend = 0f;
        }

        /// <summary>Stand down whichever way is right for who holds the clock now — toggled off, disabled, destroyed.</summary>
        void Drop()
        {
            if (!owning) return;
            if (Mathf.Approximately(Time.timeScale, appliedScale)) Release();
            else Cancel();
        }

        void Publish()
        {
            IsActive = owning && blend > 0f;
            Blend = owning ? blend : 0f;
        }
    }
}
