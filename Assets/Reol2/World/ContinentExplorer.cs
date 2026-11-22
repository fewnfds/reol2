using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Reol2.World
{
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class ContinentExplorer : MonoBehaviour
    {
        internal const float WalkSpeed = 1.25f;
        internal const float JogSpeed = 4.8f;
        internal const float SprintSpeed = 9f;
        internal const float CrouchSpeed = 0.9f;
        private const float EyeHeight = 1.38f;

        [SerializeField] private ContinentTerrain terrain;
        [SerializeField] private Camera view;
        [SerializeField] private LineRenderer selection;
        private CharacterController motor;
        private PlayerAnimatorDriver body;
        private WorldEnvironment environment;
        private GUIStyle hudStyle;
        private float yaw;
        private float pitch = 12f;
        private float verticalSpeed;
        private float standingHeight, standingStep;
        private Vector3 standingCenter;
        private bool crouched;
        private float nextHud;
        private string hud;
        private string prospectMessage;
        private float prospectUntil;
        private bool flying;
        private bool limited;
        private bool choosingDestination;
        private string destinationX, destinationY, destinationZ;
        private string arrivalMessage;

        public int LimitedMoves { get; private set; }
        public WorldPosition WorldFeet => terrain.ToWorld(transform.position);
        public int OriginShifts { get; private set; }
        internal Vector3 ActualVelocity { get; private set; }
        internal bool IsCrouched => crouched;
        internal bool Grounded { get; private set; }
        internal WorldEnvironment Environment => environment;
        private float CurrentEyeHeight => crouched ? 0.86f : EyeHeight;

        private void Awake()
        {
            motor = GetComponent<CharacterController>();
            standingHeight = motor.height;
            standingCenter = motor.center;
            standingStep = motor.stepOffset;
            body = GetComponentInChildren<PlayerAnimatorDriver>();
            if (!TryGetComponent(out environment))
                environment = gameObject.AddComponent<WorldEnvironment>();
        }

        private void Start()
        {
            EnterAt(terrain.WorldLanding);
            CaptureMouse(true);
        }

        private void OnDisable() => CaptureMouse(false);

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (terrain.IsLargeWorld && keyboard?.tKey.wasPressedThisFrame == true && !choosingDestination)
            {
                WorldPosition here = WorldFeet;
                destinationX = here.X.ToString("F2", CultureInfo.InvariantCulture);
                destinationY = (here.Y + 2d).ToString("F2", CultureInfo.InvariantCulture);
                destinationZ = here.Z.ToString("F2", CultureInfo.InvariantCulture);
                arrivalMessage = null;
                choosingDestination = true;
                CaptureMouse(false);
            }
            if (choosingDestination)
            {
                if (keyboard?.escapeKey.wasPressedThisFrame == true)
                {
                    choosingDestination = false;
                    CaptureMouse(true);
                }
                terrain.Stream(transform.position, Vector3.zero);
                IdleBody();
                UpdateView();
                selection.enabled = false;
                return;
            }
            if (keyboard?.escapeKey.wasPressedThisFrame == true)
                CaptureMouse(false);
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                terrain.Stream(transform.position, Vector3.zero);
                IdleBody();
                UpdateView();
                selection.enabled = false;
                if (mouse?.leftButton.wasPressedThisFrame == true)
                    CaptureMouse(true);
                return;
            }

            if (keyboard?.fKey.wasPressedThisFrame == true)
            {
                flying = !flying;
                verticalSpeed = 0f;
            }
            if (keyboard?.homeKey.wasPressedThisFrame == true)
            {
                flying = false;
                EnterAt(terrain.WorldLanding);
            }
            if (terrain.IsLargeWorld && keyboard != null)
            {
                if (keyboard.f1Key.wasPressedThisFrame && EnterAt(terrain.OceanEntry))
                    flying = false;
                if (keyboard.f2Key.wasPressedThisFrame)
                    EnterFlight(terrain.WestCaveEntrance, 90f, 22f);
                if (keyboard.f3Key.wasPressedThisFrame)
                    EnterFlight(terrain.EastCaveEntrance, -90f, 22f);
                if (keyboard.f4Key.wasPressedThisFrame)
                    EnterFlight(terrain.DeepChamber, 0f, 25f);
                if (keyboard.f5Key.wasPressedThisFrame)
                    EnterFlight(terrain.BasinEntry, 0f, 28f);
                if (keyboard.f6Key.wasPressedThisFrame && EnterAt(terrain.DeepSeaEntry))
                    flying = false;
                if (keyboard.f7Key.wasPressedThisFrame)
                    EnterFlight(terrain.MountainOverview, 30f, 51f);
            }

            if (mouse != null)
            {
                Vector2 look = mouse.delta.ReadValue();
                yaw = Mathf.Repeat(yaw + look.x * 0.16f, 360f);
                pitch = Mathf.Clamp(pitch - look.y * 0.16f, -75f, 84f);
            }
            var input = keyboard == null ? Vector2.zero : new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            float up = keyboard == null ? 0f : (keyboard.spaceKey.isPressed ? 1f : 0f) - (keyboard.cKey.isPressed ? 1f : 0f);
            bool fast = keyboard?.leftShiftKey.isPressed == true;
            if (keyboard?.rightBracketKey.wasPressedThisFrame == true)
                environment.Clock.Advance(3 * WorldClock.Hour);
            MoveForFrame(input, up, fast, flying, Mathf.Min(Time.deltaTime, 0.05f),
                keyboard?.spaceKey.wasPressedThisFrame == true, keyboard?.cKey.isPressed == true,
                keyboard?.leftAltKey.isPressed == true);
            UpdateView();
            UpdateSelection(mouse?.leftButton.wasPressedThisFrame == true, mouse?.rightButton.wasPressedThisFrame == true,
                terrain.IsLargeWorld && keyboard?.gKey.wasPressedThisFrame == true);
            if (Time.unscaledTime >= nextHud)
            {
                nextHud = Time.unscaledTime + 0.25f;
                WorldPosition p = WorldFeet;
                string mode = flying ? "FLIGHT" : terrain.IsWater(transform.position + Vector3.up * 0.7f) ? "SWIM" : "WALK";
                long hour = environment.Clock.Tick / WorldClock.Hour;
                hud = $"{mode}   {p.X:F0}, {p.Y:F1}, {p.Z:F0} m   |   Day {hour / 24 + 1} {hour % 24:00}:00   {environment.Climate.Temperature:F0} C   |   Pages {terrain.ActivePages}"
                    + (limited ? "   |   Terrain catching up" : "");
            }
        }

        public void EnterAt(Vector3 point)
        {
            EnterAt(terrain.ToWorld(point));
        }

        public bool EnterAt(WorldPosition point)
        {
            if (!terrain.AllowsEntry(point))
                return false;
            WorldPosition previous = WorldFeet;
            terrain.CenterWorld(point);
            Vector3 destination = terrain.ToLocal(point);
            terrain.PrepareEntry(destination);
            if (terrain.IsLargeWorld)
            {
                Physics.SyncTransforms();
                float halfSegment = Mathf.Max(0f, standingHeight * 0.5f - motor.radius);
                Vector3 center = destination + standingCenter;
                if (!terrain.HasCoverage(destination, destination) || Physics.CheckCapsule(
                    center - Vector3.up * (halfSegment - 0.05f), center + Vector3.up * halfSegment,
                    motor.radius - motor.skinWidth, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    terrain.CenterWorld(previous);
                    SetFeet(terrain.ToLocal(previous));
                    terrain.PrepareEntry(transform.position);
                    Physics.SyncTransforms();
                    UpdateView();
                    return false;
                }
            }
            SetFeet(destination);
            SetCrouch(false, checkClearance: false);
            verticalSpeed = 0f;
            ActualVelocity = Vector3.zero;
            Grounded = false;
            body?.ResetMotion();
            limited = false;
            selection.enabled = false;
            UpdateView();
            return true;
        }

        private void SetFeet(Vector3 point)
        {
            motor.enabled = false;
            transform.position = point;
            motor.enabled = true;
        }

        private void EnterFlight(WorldPosition point, float heading, float tilt)
        {
            if (!EnterAt(point))
                return;
            flying = true;
            Look(heading, tilt);
        }

        public void MoveForFrame(Vector2 input, float lift, bool fast, bool flight, float dt) =>
            MoveForFrame(input, lift, fast, flight, dt, false, false);

        internal void MoveForFrame(Vector2 input, float lift, bool fast, bool flight, float dt, bool jump, bool crouch,
            bool walk = false)
        {
            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            Vector3 move = heading * new Vector3(input.x, 0f, input.y);
            move = Vector3.ClampMagnitude(move, 1f);
            if (move.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(move);

            bool swimming = terrain.IsWater(transform.position + Vector3.up * 0.7f);
            ExplorerMoveMode mode = flight ? ExplorerMoveMode.Flight : swimming ? ExplorerMoveMode.Swim : ExplorerMoveMode.Land;
            bool onLand = mode == ExplorerMoveMode.Land;
            SetCrouch(onLand && crouch);
            float speed = mode switch
            {
                ExplorerMoveMode.Flight => fast ? 68f : 18f,
                ExplorerMoveMode.Swim => fast ? 10f : 5.6f,
                _ => crouched ? CrouchSpeed : fast ? SprintSpeed : walk ? WalkSpeed : JogSpeed
            };
            bool groundedBefore = motor.isGrounded || (limited && Grounded);
            bool jumpAccepted = onLand && jump && groundedBefore && !crouched;
            if (flight || swimming)
                verticalSpeed = Mathf.Clamp(lift, -1f, 1f) * (flight ? 15f : 4f);
            else if (jumpAccepted)
                verticalSpeed = 5.8f;
            else
                verticalSpeed = groundedBefore && verticalSpeed <= 0f ? -2.8f : Mathf.Max(-30f, verticalSpeed - 19f * dt);

            CollisionFlags flags = MoveWithVelocity(move * speed + Vector3.up * verticalSpeed, dt);
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
                verticalSpeed = 0f;
            bool leftGround = jumpAccepted && ActualVelocity.y > 0.05f;
            if (jumpAccepted && !leftGround)
                verticalSpeed = 0f;
            Grounded = onLand && !leftGround && ((flags & CollisionFlags.Below) != 0
                || (groundedBefore && limited && ActualVelocity.sqrMagnitude < 0.001f));
            body?.ApplyMotion(ActualVelocity, mode, Grounded, crouched, leftGround, dt);
        }

        private void IdleBody() => body?.ApplyMotion(Vector3.zero, flying ? ExplorerMoveMode.Flight :
            terrain.IsWater(transform.position + Vector3.up * 0.7f) ? ExplorerMoveMode.Swim : ExplorerMoveMode.Land,
            Grounded, crouched, false, Time.deltaTime);

        private void SetCrouch(bool value, bool checkClearance = true)
        {
            if (crouched == value)
                return;
            float offset = standingHeight - 1.10f;
            if (!value && checkClearance)
            {
                Vector3 top = transform.position + standingCenter + Vector3.up * (standingHeight * 0.5f - motor.radius);
                if (!terrain.HasCoverage(transform.position, transform.position)
                    || Physics.CheckCapsule(top - Vector3.up * offset, top, motor.radius - motor.skinWidth,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return;
            }
            crouched = value;
            motor.height = value ? standingHeight - offset : standingHeight;
            motor.center = value ? standingCenter - Vector3.up * (offset * 0.5f) : standingCenter;
            motor.stepOffset = value ? Mathf.Min(0.15f, standingStep) : standingStep;
        }

        internal CollisionFlags MoveWithVelocity(Vector3 velocity, float dt)
        {
            if (terrain.NeedsShift(transform.position))
            {
                WorldPosition point = WorldFeet;
                terrain.CenterWorld(point);
                SetFeet(terrain.ToLocal(point));
                Physics.SyncTransforms();
                OriginShifts++;
            }
            Vector3 feet = transform.position;
            terrain.Stream(feet, velocity);
            Vector3 requested = velocity * Mathf.Max(0f, dt);
            Vector3 allowed = terrain.LimitDisplacement(feet, requested);
            limited = (allowed - requested).sqrMagnitude > 0.000001f;
            if (limited)
                LimitedMoves++;
            CollisionFlags flags = motor.Move(allowed);
            ActualVelocity = dt > 0f ? (transform.position - feet) / dt : Vector3.zero;
            return flags;
        }

        internal void Look(float heading, float tilt)
        {
            yaw = heading;
            pitch = tilt;
            transform.rotation = Quaternion.Euler(0f, heading, 0f);
            UpdateView();
        }

        internal void UpdateView()
        {
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 eye = transform.position + Vector3.up * CurrentEyeHeight;
            Vector3 offset = -(orbit * Vector3.forward) * 4.6f + orbit * Vector3.right * 0.32f;
            Vector3 direction = offset.normalized;
            float distance = offset.magnitude;
            if (Physics.SphereCast(eye, 0.16f, direction, out RaycastHit obstacle, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0f, obstacle.distance - 0.10f);
            view.transform.SetPositionAndRotation(eye + direction * distance, orbit);

            bool underwater = terrain.IsWater(view.transform.position);
            bool underground = !underwater && terrain.IsUnderground(view.transform.position);
            environment.Apply(view, terrain, WorldFeet, underwater, underground);
        }

        private void UpdateSelection(bool digPressed, bool fillPressed, bool prospectPressed)
        {
            Vector3 eye = transform.position + Vector3.up * CurrentEyeHeight;
            Ray sight = view.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            selection.enabled = terrain.Pick(sight, eye, out Vector3Int dig, out Vector3Int fill,
                out WorldPosition sample, out bool placed);
            if (prospectPressed)
            {
                prospectMessage = selection.enabled ? terrain.Prospect(sample, placed) : "Aim at reachable terrain to prospect";
                prospectUntil = Time.unscaledTime + 5f;
            }
            if (!selection.enabled)
                return;

            selection.transform.position = terrain.ToLocal(new WorldPosition(dig.x + 0.5, dig.y + 0.5, dig.z + 0.5));
            if (digPressed)
                terrain.TryEdit(dig, fill: false, motor);
            else if (fillPressed)
                terrain.TryEdit(fill, fill: true, motor);
        }

        private void OnGUI()
        {
            hudStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }
            };
            GUI.Box(new Rect(12, 11, 810, terrain.IsLargeWorld ? 86 : 61), GUIContent.none);
            GUI.Label(new Rect(23, 16, 785, 24), hud ?? "REOL 2   |   CONTINENT TEST", hudStyle);
            GUI.Label(new Rect(23, 41, 785, 23),
                "WASD move   Alt walk   Shift run   Space jump/rise   C crouch/dive   F fly   L/R dig/fill   ] time");
            if (terrain.IsLargeWorld)
                GUI.Label(new Rect(23, 64, 785, 23), "F1 sea   F2/F3 caves   F4 deep cavern   F5 lake   F6 trench   F7 peaks   T travel   G prospect");
            if (Time.unscaledTime < prospectUntil)
                GUI.Box(new Rect(12, 102, 430, 28), prospectMessage);
            if (choosingDestination)
                DestinationPanel();
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                GUI.DrawTexture(new Rect(Screen.width * 0.5f - 5f, Screen.height * 0.5f - 1f, 10f, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(Screen.width * 0.5f - 1f, Screen.height * 0.5f - 5f, 2f, 10f), Texture2D.whiteTexture);
            }
        }

        private void DestinationPanel()
        {
            var panel = new Rect((Screen.width - 436) / 2f, (Screen.height - 160) / 2f, 436, 160);
            GUILayout.BeginArea(panel, GUI.skin.box);
            GUILayout.Label("Travel to world coordinates (metres)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("X");
            destinationX = GUILayout.TextField(destinationX, 24);
            GUILayout.Label("Y");
            destinationY = GUILayout.TextField(destinationY, 24);
            GUILayout.Label("Z");
            destinationZ = GUILayout.TextField(destinationZ, 24);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Prepare destination"))
            {
                if (ParseCoordinate(destinationX, out double x) && ParseCoordinate(destinationY, out double y)
                    && ParseCoordinate(destinationZ, out double z) && EnterAt(new WorldPosition(x, y, z)))
                {
                    choosingDestination = false;
                    flying = true;
                    CaptureMouse(true);
                }
                else
                    arrivalMessage = "Choose clear space inside the world bounds.";
            }
            if (arrivalMessage != null)
                GUILayout.Label(arrivalMessage);
            GUILayout.Label("Esc to cancel");
            GUILayout.EndArea();
        }

        private static bool ParseCoordinate(string input, out double number) =>
            double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

        private static void CaptureMouse(bool captured)
        {
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }
    }
}
