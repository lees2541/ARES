using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonProto
{
    /// <summary>
    /// 맵 테스트용 1인칭 컨트롤러. 걷기/달리기(스태미나)/웅크리기/점프/손전등/물체 잡기.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PrototypePlayer : MonoBehaviour
    {
        public Transform cameraRoot;
        public Light flashlight;

        [Header("Movement")]
        public float walkSpeed = 4f;
        public float sprintSpeed = 6.5f;
        public float crouchSpeed = 2f;
        public float jumpHeight = 1.1f;
        public float gravity = -22f;
        public float mouseSensitivity = 0.1f;
        public float standHeight = 1.8f;
        public float crouchHeight = 1.1f;
        public float staminaMax = 5f;

        [Header("Grab")]
        public float grabDistance = 3f;
        public float holdDistance = 1.8f;

        CharacterController cc;
        float pitch, vy, stamina;
        bool crouching, sprinting;
        float moveInput;
        int timesCaught;
        float caughtMessageUntil;

        public bool IsCrouching => crouching;

        /// <summary>적이 들을 수 있는 반경. 웅크리거나 멈춰 있으면 0.</summary>
        public float NoiseRadius(float sprintRange, float walkRange)
        {
            if (crouching || moveInput < 0.1f || !cc.isGrounded) return 0f;
            return sprinting ? sprintRange : walkRange;
        }

        /// <summary>적에게 잡힘 → 들고 있던 것을 놓고 착륙지로 돌아간다(프로토타입용 페널티).</summary>
        public void Caught(EnemyAI by)
        {
            Release();
            timesCaught++;
            caughtMessageUntil = Time.time + 2.5f;
            var spawn = SpawnMarker.FindFirst(SpawnKind.PlayerSpawn);
            if (spawn == null) return;
            cc.enabled = false;
            transform.SetPositionAndRotation(spawn.transform.position, Quaternion.Euler(0f, spawn.transform.eulerAngles.y, 0f));
            cc.enabled = true;
            vy = 0f;
        }
        Rigidbody held;
        Collider heldCol;
        SpawnMarker aimed;

        void Start()
        {
            cc = GetComponent<CharacterController>();
            stamina = staminaMax;
            var spawn = SpawnMarker.FindFirst(SpawnKind.PlayerSpawn);
            if (spawn != null)
            {
                cc.enabled = false;
                transform.SetPositionAndRotation(spawn.transform.position, Quaternion.Euler(0f, spawn.transform.eulerAngles.y, 0f));
                cc.enabled = true;
            }
            SetCursorLocked(true);
        }

        void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            float dt = Time.deltaTime;
            bool locked = Cursor.lockState == CursorLockMode.Locked;

            if (kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            if (!locked && mouse.leftButton.wasPressedThisFrame) { SetCursorLocked(true); return; }

            if (locked)
            {
                Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
                transform.Rotate(0f, d.x, 0f);
                pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
                cameraRoot.localEulerAngles = new Vector3(pitch, 0f, 0f);
            }

            // 웅크리기 (머리 위가 막혀 있으면 일어서지 않음)
            if (kb.cKey.wasPressedThisFrame || kb.leftCtrlKey.wasPressedThisFrame)
            {
                if (!crouching) crouching = true;
                else if (!Physics.Raycast(transform.position + Vector3.up * crouchHeight, Vector3.up, standHeight - crouchHeight + 0.1f, ~0, QueryTriggerInteraction.Ignore))
                    crouching = false;
            }
            cc.height = Mathf.MoveTowards(cc.height, crouching ? crouchHeight : standHeight, dt * 6f);
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
            cameraRoot.localPosition = new Vector3(0f, cc.height - 0.15f, 0f);

            // 이동
            var mv = new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0),
                                 (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            mv = Vector2.ClampMagnitude(mv, 1f);
            bool sprint = kb.leftShiftKey.isPressed && !crouching && mv.y > 0f && stamina > 0.05f;
            sprinting = sprint;
            moveInput = mv.magnitude;
            stamina = sprint ? stamina - dt : Mathf.Min(staminaMax, stamina + dt * 0.6f);
            float speed = crouching ? crouchSpeed : sprint ? sprintSpeed : walkSpeed;
            if (held != null) speed *= 0.8f;
            var move = (transform.right * mv.x + transform.forward * mv.y) * speed;

            if (cc.isGrounded)
            {
                if (vy < 0f) vy = -2f;
                if (kb.spaceKey.wasPressedThisFrame && !crouching) vy = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            vy += gravity * dt;
            move.y = vy;
            cc.Move(move * dt);

            if (kb.fKey.wasPressedThisFrame && flashlight != null) flashlight.enabled = !flashlight.enabled;

            // 잡기
            aimed = null;
            if (held == null && Physics.Raycast(cameraRoot.position, cameraRoot.forward, out var hit, grabDistance, ~0, QueryTriggerInteraction.Ignore))
                aimed = hit.rigidbody != null ? hit.rigidbody.GetComponent<SpawnMarker>() : null;

            if (locked && mouse.leftButton.wasPressedThisFrame && held == null && aimed != null) Grab(aimed.GetComponent<Rigidbody>());
            if (mouse.leftButton.wasReleasedThisFrame && held != null) Release();
        }

        void FixedUpdate()
        {
            if (held == null) return;
            var target = cameraRoot.position + cameraRoot.forward * holdDistance;
            var delta = target - held.position;
            if (delta.magnitude > 2.5f) { Release(); return; }
            held.linearVelocity = delta * 14f;
            held.angularVelocity *= 0.85f;
        }

        void Grab(Rigidbody rb)
        {
            held = rb;
            heldCol = rb.GetComponent<Collider>();
            if (heldCol != null) Physics.IgnoreCollision(heldCol, cc, true);
            held.useGravity = false;
        }

        void Release()
        {
            if (held != null)
            {
                held.useGravity = true;
                if (heldCol != null) Physics.IgnoreCollision(heldCol, cc, false);
            }
            held = null;
            heldCol = null;
        }

        void OnGUI()
        {
            GUI.Label(new Rect(12, 10, 600, 22), "WASD move | Shift sprint | C crouch | Space jump | F flashlight | LMB grab | Esc cursor");
            GUI.Box(new Rect(12, 34, 160, 14), GUIContent.none);
            GUI.Box(new Rect(12, 34, 160 * (stamina / staminaMax), 14), GUIContent.none);

            var cx = Screen.width * 0.5f;
            var cy = Screen.height * 0.5f;
            GUI.Label(new Rect(cx - 4, cy - 10, 20, 20), "+");
            if (Time.time < caughtMessageUntil)
            {
                var style = new GUIStyle(GUI.skin.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter };
                style.normal.textColor = Color.red;
                GUI.Label(new Rect(0, cy - 120, Screen.width, 60), $"CAUGHT! (x{timesCaught})", style);
            }

            var m = held != null ? held.GetComponent<SpawnMarker>() : aimed;
            if (m != null)
                GUI.Label(new Rect(cx + 14, cy - 10, 220, 22), (held != null ? "Holding" : "[LMB] Grab") + $"  loot ${m.value}");
        }
    }
}
