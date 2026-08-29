using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Art_Equilibrium
{
    public class AE_Door : MonoBehaviour
    {
        private bool trig;
        private bool open;
        private bool isKeyPressed;

        [Header("Door Settings")]
        public float smooth = 2.0f;
        public float DoorOpenAngle = 87.0f;

        private Quaternion defaultRot;
        private Quaternion openRot;
        private Vector3 defaultLocalPos;
        private Vector3 targetLocalSlidePos;

        [Header("Door Type")]
        public bool isSlidingDoor = false;
        public Vector3 slideOffset = new Vector3(1, 0, 0);

        [Header("GUI Settings")]
        public string openMessage = "Open E";
        public string closeMessage = "Close E";
        public Font messageFont;
        public int fontSize = 24;
        public Color fontColor = Color.white;
        public Vector2 messagePosition = new Vector2(0.5f, 0.5f);

        private string doorMessage = "";

        [Header("Audio Settings")]
        public AudioClip openSound;
        public AudioClip closeSound;
        private AudioSource audioSource;

        private void Start()
        {
            defaultRot = transform.rotation;
            openRot = Quaternion.Euler(
                transform.eulerAngles.x,
                transform.eulerAngles.y + DoorOpenAngle,
                transform.eulerAngles.z
            );

            defaultLocalPos = transform.localPosition;
            targetLocalSlidePos = defaultLocalPos + slideOffset;

            isKeyPressed = false;

            audioSource = GetComponent<AudioSource>();

            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }

        private void Update()
        {
            MoveDoor();

            if (IsInteractPressed() && trig && !isKeyPressed)
            {
                open = !open;
                isKeyPressed = true;
                PlayDoorSound();
            }

            if (IsInteractReleased())
            {
                isKeyPressed = false;
            }

            doorMessage = trig ? (open ? closeMessage : openMessage) : "";
        }

        private void MoveDoor()
        {
            if (isSlidingDoor)
            {
                Vector3 targetPos = open ? targetLocalSlidePos : defaultLocalPos;
                transform.localPosition = Vector3.Lerp(
                    transform.localPosition,
                    targetPos,
                    Time.deltaTime * smooth
                );
            }
            else
            {
                Quaternion targetRot = open ? openRot : defaultRot;
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRot,
                    Time.deltaTime * smooth
                );
            }
        }

        private bool IsInteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }

        private bool IsInteractReleased()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasReleasedThisFrame;
#else
            return Input.GetKeyUp(KeyCode.E);
#endif
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(doorMessage))
                return;

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize
            };

            style.normal.textColor = fontColor;

            if (messageFont != null)
                style.font = messageFont;

            float screenWidth = Screen.width;
            float screenHeight = Screen.height;

            Vector2 labelSize = style.CalcSize(new GUIContent(doorMessage));

            float labelX = screenWidth * messagePosition.x - labelSize.x / 2f;
            float labelY = screenHeight * messagePosition.y - labelSize.y / 2f;

            GUI.Label(
                new Rect(labelX, labelY, labelSize.x, labelSize.y),
                doorMessage,
                style
            );
        }

        private void OnTriggerEnter(Collider coll)
        {
            if (coll.CompareTag("Player"))
            {
                doorMessage = open ? closeMessage : openMessage;
                trig = true;
            }
        }

        private void OnTriggerExit(Collider coll)
        {
            if (coll.CompareTag("Player"))
            {
                doorMessage = "";
                trig = false;
                isKeyPressed = false;
            }
        }

        private void PlayDoorSound()
        {
            if (audioSource == null)
                return;

            if (open && openSound != null)
            {
                audioSource.clip = openSound;
                audioSource.Play();
            }
            else if (!open && closeSound != null)
            {
                audioSource.clip = closeSound;
                audioSource.Play();
            }
        }
    }
}