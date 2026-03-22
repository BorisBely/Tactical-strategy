using UnityEngine;
using UnityEngine.InputSystem;

namespace PmcOperator.Player
{
    /// <summary>
    /// Шаг 1: базовое движение третьего лица — CharacterController, гравитация, прыжок, спринт, обзор мышью.
    /// Камера ожидается дочерней к <see cref="m_CameraPivot"/> (поворот по pitch).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PmcPlayerMotor : MonoBehaviour
    {
        #region Constants

        private const float c_MinPitchDegrees = -60f;
        private const float c_MaxPitchDegrees = 75f;

        #endregion

        #region Serialized Fields

        [Header("References")]
        [SerializeField] private CharacterController m_CharacterController;
        [SerializeField] private Transform m_CameraPivot;

        [Header("Movement")]
        [SerializeField] private float m_WalkSpeed = 3f;
        [SerializeField] private float m_SprintSpeed = 5f;
        [SerializeField] private float m_JumpHeight = 1.2f;
        [SerializeField] private float m_Gravity = -20f;

        [Header("Look")]
        [SerializeField, Range(0.01f, 5f)] private float m_MouseSensitivityX = 0.15f;
        [SerializeField, Range(0.01f, 5f)] private float m_MouseSensitivityY = 0.12f;

        [Header("Cursor")]
        [SerializeField] private bool m_LockCursorOnPlay = true;

        #endregion

        #region Private Fields

        private float m_PitchDegrees;
        private float m_VerticalVelocity;
        private bool m_IsSprinting;

        #endregion

        #region Public Properties

        /// <summary> Текущая горизонтальная скорость (без Y), м/с. </summary>
        public Vector3 HorizontalVelocity { get; private set; }

        /// <summary> Идёт ли спринт (удержание Shift и есть ввод движения). </summary>
        public bool IsSprinting => m_IsSprinting;

        /// <summary> Точка обзора для pitch (можно использовать для оружия на шаге 4). </summary>
        public Transform CameraPivot => m_CameraPivot;

        #endregion

        #region Unity Lifecycle

        private void Reset()
        {
            m_CharacterController = GetComponent<CharacterController>();
        }

        private void Awake()
        {
            if (m_CharacterController == null)
            {
                m_CharacterController = GetComponent<CharacterController>();
            }

            if (m_CameraPivot == null)
            {
                Debug.LogError($"{nameof(PmcPlayerMotor)}: назначьте Camera Pivot (дочерний Transform для наклона камеры).", this);
            }
        }

        private void Start()
        {
            if (m_LockCursorOnPlay)
            {
                LockCursor();
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                ToggleCursorLock();
            }

            ReadInput(out Vector2 moveAxes, out Vector2 mouseDelta, out bool jumpPressed, out bool sprintHeld);

            ApplyLook(mouseDelta);
            ApplyMovement(moveAxes, sprintHeld, jumpPressed);
        }

        #endregion

        #region Private Methods

        private void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ToggleCursorLock()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                UnlockCursor();
            }
            else
            {
                LockCursor();
            }
        }

        private void ReadInput(out Vector2 moveAxes, out Vector2 mouseDelta, out bool jumpPressed, out bool sprintHeld)
        {
            moveAxes = Vector2.zero;
            mouseDelta = Vector2.zero;
            jumpPressed = false;
            sprintHeld = false;

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard == null)
            {
                return;
            }

            float horizontal = 0f;
            if (keyboard.aKey.isPressed)
            {
                horizontal -= 1f;
            }

            if (keyboard.dKey.isPressed)
            {
                horizontal += 1f;
            }

            float vertical = 0f;
            if (keyboard.wKey.isPressed)
            {
                vertical += 1f;
            }

            if (keyboard.sKey.isPressed)
            {
                vertical -= 1f;
            }

            moveAxes = new Vector2(horizontal, vertical);
            if (moveAxes.sqrMagnitude > 1f)
            {
                moveAxes.Normalize();
            }

            jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
            sprintHeld = keyboard.leftShiftKey.isPressed;

            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                mouseDelta = mouse.delta.ReadValue();
            }
        }

        private void ApplyLook(Vector2 mouseDelta)
        {
            if (m_CameraPivot == null || Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            float yaw = mouseDelta.x * m_MouseSensitivityX;
            float pitchDelta = mouseDelta.y * m_MouseSensitivityY;

            transform.Rotate(0f, yaw, 0f, Space.World);

            m_PitchDegrees -= pitchDelta;
            m_PitchDegrees = Mathf.Clamp(m_PitchDegrees, c_MinPitchDegrees, c_MaxPitchDegrees);
            m_CameraPivot.localRotation = Quaternion.Euler(m_PitchDegrees, 0f, 0f);
        }

        private void ApplyMovement(Vector2 moveAxes, bool sprintHeld, bool jumpPressed)
        {
            bool hasInput = moveAxes.sqrMagnitude > 0.0001f;
            m_IsSprinting = sprintHeld && hasInput;

            float speed = m_IsSprinting ? m_SprintSpeed : m_WalkSpeed;

            Vector3 worldMove = transform.right * moveAxes.x + transform.forward * moveAxes.y;
            if (worldMove.sqrMagnitude > 1f)
            {
                worldMove.Normalize();
            }

            worldMove *= speed;

            if (m_CharacterController.isGrounded)
            {
                m_VerticalVelocity = -0.5f;

                if (jumpPressed)
                {
                    m_VerticalVelocity = Mathf.Sqrt(m_JumpHeight * -2f * m_Gravity);
                }
            }
            else
            {
                m_VerticalVelocity += m_Gravity * Time.deltaTime;
            }

            Vector3 velocity = new Vector3(worldMove.x, m_VerticalVelocity, worldMove.z);
            HorizontalVelocity = new Vector3(worldMove.x, 0f, worldMove.z);

            m_CharacterController.Move(velocity * Time.deltaTime);
        }

        #endregion

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_CharacterController == null)
            {
                m_CharacterController = GetComponent<CharacterController>();
            }
        }
#endif
    }
}
