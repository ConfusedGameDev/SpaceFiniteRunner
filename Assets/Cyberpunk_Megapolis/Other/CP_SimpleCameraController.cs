using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AE_Camera
{
    public class AE_SimpleCameraController : MonoBehaviour
    {
        [System.Serializable]
        private class CameraState
        {
            public float yaw;
            public float pitch;
            public float roll;
            public Vector3 position;

            public void SetFromTransform(Transform targetTransform)
            {
                Vector3 euler = targetTransform.eulerAngles;
                pitch = euler.x;
                yaw = euler.y;
                roll = euler.z;
                position = targetTransform.position;
            }

            public void Translate(Vector3 localTranslation)
            {
                Vector3 worldTranslation = Quaternion.Euler(pitch, yaw, roll) * localTranslation;
                position += worldTranslation;
            }

            public void LerpTowards(CameraState target, float positionLerpFactor, float rotationLerpFactor)
            {
                yaw = Mathf.Lerp(yaw, target.yaw, rotationLerpFactor);
                pitch = Mathf.Lerp(pitch, target.pitch, rotationLerpFactor);
                roll = Mathf.Lerp(roll, target.roll, rotationLerpFactor);

                position.x = Mathf.Lerp(position.x, target.position.x, positionLerpFactor);
                position.y = Mathf.Lerp(position.y, target.position.y, positionLerpFactor);
                position.z = Mathf.Lerp(position.z, target.position.z, positionLerpFactor);
            }

            public void ApplyToTransform(Transform targetTransform)
            {
                targetTransform.position = position;
                targetTransform.rotation = Quaternion.Euler(pitch, yaw, roll);
            }
        }

        [Header("Movement")]
        [SerializeField, Min(0.01f)] private float moveSpeed = 10f;
        [SerializeField, Min(1f)] private float sprintMultiplier = 4f;
        [SerializeField, Min(0.001f)] private float positionLerpTime = 0.2f;

        [Header("Rotation")]
        [SerializeField] private AnimationCurve mouseSensitivityCurve = new AnimationCurve(
            new Keyframe(0f, 0.5f, 0f, 5f),
            new Keyframe(1f, 2.5f, 0f, 0f)
        );

        [SerializeField, Min(0.001f)] private float rotationLerpTime = 0.01f;
        [SerializeField] private bool invertY = false;

        [Header("Options")]
        [SerializeField] private bool quitOnEscape = true;

        [Header("New Input System")]
        [Tooltip("Mouse delta in the new Input System is stronger than the old Input axes. 0.02 usually gives similar camera rotation.")]
        [SerializeField, Min(0.001f)] private float newInputMouseDeltaMultiplier = 0.02f;

        private readonly CameraState targetCameraState = new CameraState();
        private readonly CameraState interpolatedCameraState = new CameraState();

        private float runtimeMoveSpeed;
        private float runtimeSprintMultiplier;
        private float runtimePositionLerpTime;
        private float runtimeRotationLerpTime;
        private bool runtimeInvertY;
        private bool runtimeQuitOnEscape;
        private AnimationCurve runtimeMouseSensitivityCurve;

        private void Awake()
        {
            CacheRuntimeSettings();
        }

        private void OnEnable()
        {
            CacheRuntimeSettings();

            targetCameraState.SetFromTransform(transform);
            interpolatedCameraState.SetFromTransform(transform);
        }

        private void OnDisable()
        {
            UnlockCursor();
        }

        private void CacheRuntimeSettings()
        {
            runtimeMoveSpeed = Mathf.Max(0.01f, moveSpeed);
            runtimeSprintMultiplier = Mathf.Max(1f, sprintMultiplier);
            runtimePositionLerpTime = Mathf.Max(0.001f, positionLerpTime);
            runtimeRotationLerpTime = Mathf.Max(0.001f, rotationLerpTime);
            runtimeInvertY = invertY;
            runtimeQuitOnEscape = quitOnEscape;

            if (mouseSensitivityCurve == null || mouseSensitivityCurve.length == 0)
            {
                runtimeMouseSensitivityCurve = new AnimationCurve(
                    new Keyframe(0f, 0.5f, 0f, 5f),
                    new Keyframe(1f, 2.5f, 0f, 0f)
                );
            }
            else
            {
                runtimeMouseSensitivityCurve = new AnimationCurve(mouseSensitivityCurve.keys);
            }
        }

        private void Update()
        {
            HandleEscape();
            HandleCursor();
            HandleRotation();
            HandleTranslation();
            ApplyInterpolation();
        }

        private void HandleEscape()
        {
            if (!runtimeQuitOnEscape)
                return;

            if (!IsEscapePressed())
                return;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleCursor()
        {
            if (IsRightMousePressed())
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (IsRightMouseReleased())
            {
                UnlockCursor();
            }
        }

        private void HandleRotation()
        {
            if (!IsRightMouseHeld())
                return;

            Vector2 mouseDelta = GetMouseDelta();

            float ySign = runtimeInvertY ? 1f : -1f;
            mouseDelta.y *= ySign;

            float sensitivity = runtimeMouseSensitivityCurve.Evaluate(mouseDelta.magnitude);

            targetCameraState.yaw += mouseDelta.x * sensitivity;
            targetCameraState.pitch += mouseDelta.y * sensitivity;
        }

        private void HandleTranslation()
        {
            Vector3 inputDirection = GetMovementInput();

            if (inputDirection.sqrMagnitude > 1f)
                inputDirection.Normalize();

            float currentSpeed = runtimeMoveSpeed;

            if (IsSprintHeld())
                currentSpeed *= runtimeSprintMultiplier;

            Vector3 translation = inputDirection * currentSpeed * Time.deltaTime;
            targetCameraState.Translate(translation);
        }

        private Vector3 GetMovementInput()
        {
            Vector3 direction = Vector3.zero;

            if (IsKeyHeld_W())
                direction += Vector3.forward;

            if (IsKeyHeld_S())
                direction += Vector3.back;

            if (IsKeyHeld_A())
                direction += Vector3.left;

            if (IsKeyHeld_D())
                direction += Vector3.right;

            if (IsKeyHeld_E())
                direction += Vector3.up;

            if (IsKeyHeld_Q())
                direction += Vector3.down;

            return direction;
        }

        private void ApplyInterpolation()
        {
            float positionLerpFactor = GetLerpFactor(runtimePositionLerpTime);
            float rotationLerpFactor = GetLerpFactor(runtimeRotationLerpTime);

            interpolatedCameraState.LerpTowards(targetCameraState, positionLerpFactor, rotationLerpFactor);
            interpolatedCameraState.ApplyToTransform(transform);
        }

        private float GetLerpFactor(float lerpTime)
        {
            return 1f - Mathf.Exp((Mathf.Log(0.01f) / lerpTime) * Time.deltaTime);
        }

        private void UnlockCursor()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private bool IsEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private bool IsRightMousePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        private bool IsRightMouseReleased()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(1);
#endif
        }

        private bool IsRightMouseHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton(1);
#endif
        }

        private Vector2 GetMouseDelta()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
                return Vector2.zero;

            return Mouse.current.delta.ReadValue() * newInputMouseDeltaMultiplier;
#else
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
        }

        private bool IsSprintHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
                return false;

            return Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        }

        private bool IsKeyHeld_W()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.wKey.isPressed;
#else
            return Input.GetKey(KeyCode.W);
#endif
        }

        private bool IsKeyHeld_S()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.sKey.isPressed;
#else
            return Input.GetKey(KeyCode.S);
#endif
        }

        private bool IsKeyHeld_A()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.aKey.isPressed;
#else
            return Input.GetKey(KeyCode.A);
#endif
        }

        private bool IsKeyHeld_D()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.dKey.isPressed;
#else
            return Input.GetKey(KeyCode.D);
#endif
        }

        private bool IsKeyHeld_E()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.isPressed;
#else
            return Input.GetKey(KeyCode.E);
#endif
        }

        private bool IsKeyHeld_Q()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.qKey.isPressed;
#else
            return Input.GetKey(KeyCode.Q);
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            moveSpeed = Mathf.Max(0.01f, moveSpeed);
            sprintMultiplier = Mathf.Max(1f, sprintMultiplier);
            positionLerpTime = Mathf.Max(0.001f, positionLerpTime);
            rotationLerpTime = Mathf.Max(0.001f, rotationLerpTime);
            newInputMouseDeltaMultiplier = Mathf.Max(0.001f, newInputMouseDeltaMultiplier);

            if (mouseSensitivityCurve == null || mouseSensitivityCurve.length == 0)
            {
                mouseSensitivityCurve = new AnimationCurve(
                    new Keyframe(0f, 0.5f, 0f, 5f),
                    new Keyframe(1f, 2.5f, 0f, 0f)
                );
            }
        }
#endif
    }
}