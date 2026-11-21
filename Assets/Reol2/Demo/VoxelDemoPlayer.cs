using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Reol2.Demo
{
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class VoxelDemoPlayer : MonoBehaviour
    {
        private const float EyeHeight = 1.35f;
        private const float WalkSpeed = 3.1f;
        private const float Gravity = 19f;
        private const float GroundingSpeed = -3f;
        private const float MouseSensitivity = 0.16f;

        [SerializeField] private VoxelDemoTerrain terrain = null;
        [SerializeField] private Camera view = null;
        [SerializeField] private InputActionAsset actions = null;
        [SerializeField] private LineRenderer digOutline = null;
        [SerializeField] private LineRenderer fillOutline = null;
        [SerializeField] private Text hint = null;

        private CharacterController motor;
        private InputActionMap input;
        private InputAction move;
        private InputAction look;
        private InputAction digAction;
        private float yaw;
        private float pitch;
        private float verticalSpeed;
        private int editCount;
        private bool hasEnteredChamber;

        private void Awake()
        {
            motor = GetComponent<CharacterController>();
            input = actions.FindActionMap("Player", throwIfNotFound: true).Clone();
            move = input.FindAction("Move", throwIfNotFound: true);
            look = input.FindAction("Look", throwIfNotFound: true);
            digAction = input.FindAction("Attack", throwIfNotFound: true);
        }

        private void OnEnable() => input.Enable();

        private void OnDisable()
        {
            input.Disable();
            SetMouseCaptured(false);
        }

        private void OnDestroy() => input?.Dispose();

        private void Start()
        {
            ResetPosition();
            SetMouseCaptured(true);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard?.escapeKey.wasPressedThisFrame == true)
                SetMouseCaptured(false);

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (mouse?.leftButton.wasPressedThisFrame == true)
                    SetMouseCaptured(true);
                ShowAim(false);
                SetHint("Click to capture mouse");
                return;
            }

            if (keyboard?.rKey.wasPressedThisFrame == true)
            {
                terrain.ResetTerrain();
                editCount = 0;
                hasEnteredChamber = false;
                ResetPosition();
            }

            Vector2 aimDelta = look.ReadValue<Vector2>();
            float sensitivity = look.activeControl?.device is Gamepad ? 120f * Time.deltaTime : MouseSensitivity;
            yaw += aimDelta.x * sensitivity;
            pitch = Mathf.Clamp(pitch - aimDelta.y * sensitivity, -55f, 70f);

            MoveForFrame(move.ReadValue<Vector2>(), Time.deltaTime);
            UpdateCamera();
            UpdateAim(digAction.WasPressedThisFrame(), mouse?.rightButton.wasPressedThisFrame == true);
        }

        public void MoveForFrame(Vector2 movement, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            Vector3 direction = heading * new Vector3(movement.x, 0f, movement.y);
            direction = Vector3.ClampMagnitude(direction, 1f);
            if (direction.sqrMagnitude > 0.01f)
            {
                Quaternion facing = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, 880f * deltaTime);
            }

            verticalSpeed = motor.isGrounded && verticalSpeed <= 0f
                ? GroundingSpeed
                : Mathf.Max(-24f, verticalSpeed - Gravity * deltaTime);
            motor.Move((direction * WalkSpeed + Vector3.up * verticalSpeed) * deltaTime);
            if (transform.position.y < -2f)
                ResetPosition();
            if (transform.position.x > 1.45f && transform.position.y < 3.5f
                && Mathf.Abs(transform.position.z - 4.5f) < 0.9f)
                hasEnteredChamber = true;
        }

        public void ResetPosition()
        {
            motor.enabled = false;
            yaw = 90f;
            pitch = 11f;
            verticalSpeed = 0f;
            transform.SetPositionAndRotation(VoxelDemoTerrain.Trailhead, Quaternion.Euler(0f, yaw, 0f));
            motor.enabled = true;
            UpdateCamera();
        }

        private void UpdateCamera()
        {
            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 eye = transform.position + Vector3.up * EyeHeight;
            Vector3 target = eye + Vector3.up * 0.28f + heading * Vector3.right * 0.25f;
            Vector3 offset = target - eye - orbit * Vector3.forward * 3.85f;
            float distance = offset.magnitude;
            if (Physics.SphereCast(eye, 0.18f, offset / distance, out RaycastHit obstruction, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0.20f, obstruction.distance - 0.08f);

            view.transform.SetPositionAndRotation(eye + offset.normalized * distance, orbit);
        }

        private void UpdateAim(bool digPressed, bool fillPressed)
        {
            Ray ray = view.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            Vector3 eye = transform.position + Vector3.up * EyeHeight;
            if (!terrain.TryPick(ray, eye, out Vector3Int dig, out Vector3Int fill))
            {
                ShowAim(false);
                SetHint(hasEnteredChamber ? "Chamber reached" : "Follow the lanterns up the slope");
                return;
            }

            bool canFill = terrain.CanFill(fill, motor);
            OutlineCell(digOutline, dig, true);
            OutlineCell(fillOutline, fill, canFill && fill != dig);
            if (digPressed && terrain.TryEdit(dig, fill: false))
                editCount++;
            else if (fillPressed && canFill && terrain.TryEdit(fill, fill: true, motor))
                editCount++;

            if (hasEnteredChamber)
                SetHint("Chamber reached  /  Fill the opening to block it again");
            else if (editCount > 0)
                SetHint("Clear the passage from floor to head height");
            else
                SetHint("Dig at the stone between the two lanterns");
        }

        private static void OutlineCell(LineRenderer outline, Vector3Int cell, bool visible)
        {
            outline.transform.position = (Vector3)cell + Vector3.one * 0.5f;
            outline.enabled = visible;
        }

        private void ShowAim(bool visible)
        {
            digOutline.enabled = visible;
            fillOutline.enabled = visible;
        }

        private void SetHint(string message)
        {
            if (hint.text != message)
                hint.text = message;
        }

        private static void SetMouseCaptured(bool captured)
        {
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }
    }
}
