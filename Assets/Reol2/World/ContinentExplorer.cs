using System.Globalization;
using Reol2.Demo;
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
        private const float SwimEntryDepth = 0.72f;
        private const float SwimExitDepth = 0.55f;
        private const float SwimVerticalSpeed = 4f;
        private const float DivePitch = 25f;
        private const float SwimRadius = 0.73f;
        private const float SwimHalfSegment = 0.36f;
        internal const float SurfaceSwimDepth = 0.92f;
        private static readonly Vector2Int[] HomeProbes = { Vector2Int.zero, Vector2Int.right, Vector2Int.left,
            Vector2Int.up, Vector2Int.down, new(1, 1), new(-1, 1), new(1, -1), new(-1, -1) };

        [SerializeField] private ContinentTerrain terrain;
        [SerializeField] private Camera view;
        [SerializeField] private LineRenderer selection;
        private CharacterController motor;
        private PlayerAnimatorDriver body;
        private WorldEnvironment environment;
        private GUIStyle hudStyle;
        private readonly ExplorerMouse mouseControl = new();
        private readonly TestMovementSpeed movementSpeed = new();
        private float yaw;
        private float headingYaw;
        private float pitch = 12f;
        private float cameraDistance = 4.6f;
        private float verticalSpeed;
        private float standingHeight, standingStep;
        private Vector3 standingCenter;
        private bool crouched;
        private float nextHud;
        private string hud;
        private string prospectMessage;
        private float prospectUntil;
        private bool flying, diving;
        private bool limited;
        private bool choosingDestination;
        private bool suppressInputFrame;
        private bool centerAim;
        private WorldPosition? gradeStart;
        private string destinationX, destinationY, destinationZ;
        private string arrivalMessage;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
        private WorldRenderReport renderReport;
        internal float MovementMultiplier => movementSpeed.Multiplier;
#endif

        public int LimitedMoves { get; private set; }
        public WorldPosition WorldFeet => terrain.ToWorld(transform.position);
        public int OriginShifts { get; private set; }
        internal Vector3 ActualVelocity { get; private set; }
        internal bool IsCrouched => crouched;
        internal bool Grounded { get; private set; }
        internal WorldEnvironment Environment => environment;
        internal ExplorerMoveMode MoveMode { get; private set; }
        internal bool IsDiving => MoveMode == ExplorerMoveMode.Swim && diving;
        internal ExplorerMouseGesture MouseGesture => mouseControl.Gesture;
        private float CurrentEyeHeight => MoveMode == ExplorerMoveMode.Swim ? 0.98f : crouched ? 0.86f : EyeHeight;

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
            terrain.SetHorizonView(view);
            EnterStartingPoint();
            if (terrain.IsTerrainDemo)
                Look(186f, 29f);
            else if (terrain.Landforms is RegionalLandforms region)
                Look(Mathf.Atan2(region.LandingView.X, region.LandingView.Y) * Mathf.Rad2Deg, 3f);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
            string[] arguments = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(arguments, "-river-demo") >= 0)
                EnterRiverDemo();
#endif
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void EnterStartingPoint()
        {
            WorldPosition home = terrain.WorldLanding;
            if (EnterAt(home) || !terrain.IsLargeWorld || TryReturnHome())
                return;

            for (int lift = 16; lift <= WorldDefinition.MaxY; lift *= 2)
            {
                double height = System.Math.Min(home.Y + lift, WorldDefinition.MaxY - 3d);
                if (EnterFlight(new WorldPosition(home.X, height, home.Z), 0f, 25f))
                    return;
            }
        }

        private void OnDisable() => mouseControl.Release(wait: true);

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                mouseControl.Release(wait: true, restore: false);
                suppressInputFrame = true;
                if (selection != null)
                    selection.enabled = false;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            bool focused = Application.isFocused;
            if (focused && keyboard?.f9Key.wasPressedThisFrame == true && terrain.TryQuitWithoutTerrainSave())
                return;
            ProcessInput(ExplorerInput.Read(keyboard, Mouse.current), Mathf.Min(Time.deltaTime, 0.05f), focused);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
        private void LateUpdate()
        {
            if (!terrain.IsLargeWorld)
                return;
            renderReport ??= new WorldRenderReport(terrain, this, view);
            renderReport.Record();
            if (Application.isFocused && Keyboard.current?.f8Key.wasPressedThisFrame == true && !renderReport.Saving)
                StartCoroutine(renderReport.CaptureScreen(ShowToolMessage));
        }
#endif

        internal void ProcessInput(ExplorerInput input, float dt, bool focused)
        {
            if (!focused)
            {
                SuspendInput(restorePointer: false);
                suppressInputFrame = true;
                return;
            }
            if (choosingDestination || input.Cancel)
            {
                if (input.Cancel)
                {
                    choosingDestination = false;
                    gradeStart = null;
                    suppressInputFrame = true;
                }
                SuspendInput();
                return;
            }
            if (suppressInputFrame)
            {
                suppressInputFrame = false;
                return;
            }
            if (terrain.IsLargeWorld && input.Travel)
            {
                WorldPosition here = WorldFeet;
                destinationX = here.X.ToString("F2", CultureInfo.InvariantCulture);
                destinationY = (here.Y + 2d).ToString("F2", CultureInfo.InvariantCulture);
                destinationZ = here.Z.ToString("F2", CultureInfo.InvariantCulture);
                arrivalMessage = null;
                choosingDestination = true;
                SuspendInput();
                return;
            }

            movementSpeed.ApplyStep(input.SpeedStep);
            if (HandleDestination(input.Destination))
                return;
            if (input.ToggleFlight)
                flying = !flying;
            if (input.AdvanceTime)
                environment.Clock.Advance(3 * WorldClock.Hour);

            ExplorerMouseGesture previousGesture = MouseGesture;
            bool overSpeedHud = previousGesture == ExplorerMouseGesture.None && input.HasPointer
                && movementSpeed.HandlePointer(input.Pointer, input.PointerPressed);
            if (overSpeedHud)
                mouseControl.Release(wait: input.Orbit || input.Steer);
            Vector2 look = mouseControl.Update(input);
            yaw = Mathf.Repeat(yaw + look.x * 0.16f, 360f);
            pitch = Mathf.Clamp(pitch - look.y * 0.16f, -75f, 84f);
            if (input.Zoom != 0f && !overSpeedHud)
                cameraDistance = Mathf.Clamp(cameraDistance * Mathf.Pow(0.85f, Mathf.Clamp(input.Zoom, -1f, 1f)), 2f, 8f);

            bool steering = MouseGesture == ExplorerMouseGesture.Steer || MouseGesture == ExplorerMouseGesture.Advance;
            if (steering)
                headingYaw = yaw;
            else
            {
                float turn = input.Turn * 120f * dt;
                headingYaw = Mathf.Repeat(headingYaw + turn, 360f);
                if (MouseGesture != ExplorerMouseGesture.Orbit)
                    yaw = Mathf.Repeat(yaw + turn, 360f);
            }

            float strafe = Mathf.Clamp(input.Strafe + (steering ? input.Turn : 0f), -1f, 1f);
            float forward = MouseGesture == ExplorerMouseGesture.Advance ? 1f : input.Forward;
            float lift = (input.Rise ? 1f : 0f) - (input.Descend ? 1f : 0f);
            MoveCharacter(new Vector2(strafe, forward), lift, input.Run, flying, dt, input.Jump, input.Crouch, input.Walk,
                headingYaw, steering ? pitch : 0f, input.Rise || input.Descend, playerInput: true);
            UpdateView();

            centerAim = steering;
            bool releasing = MouseGesture == ExplorerMouseGesture.None && previousGesture != ExplorerMouseGesture.None;
            bool canAim = steering || (input.HasPointer && !overSpeedHud && view.pixelRect.Contains(input.Pointer));
            if (MouseGesture == ExplorerMouseGesture.Orbit || releasing || !canAim)
                selection.enabled = false;
            else
            {
                Ray sight = steering ? view.ViewportPointToRay(new Vector3(0.5f, 0.5f)) : view.ScreenPointToRay(input.Pointer);
                UpdateSelection(sight, input);
            }

            if (Time.unscaledTime >= nextHud)
            {
                nextHud = Time.unscaledTime + 0.25f;
                WorldPosition p = WorldFeet;
                string mode = MoveMode switch
                {
                    ExplorerMoveMode.Flight => "FLIGHT",
                    ExplorerMoveMode.Swim => diving ? "DIVE" : "SWIM",
                    _ => "WALK"
                };
                long hour = environment.Clock.Tick / WorldClock.Hour;
                hud = $"{mode}   {p.X:F0}, {p.Y:F1}, {p.Z:F0} m   |   Day {hour / 24 + 1} {hour % 24:00}:00   {environment.Climate.Temperature:F0} C   |   Pages {terrain.ActivePages}"
                    + (limited ? "   |   Terrain catching up" : "");
            }
        }

        private void SuspendInput(bool restorePointer = true)
        {
            mouseControl.Release(wait: true, restore: restorePointer);
            selection.enabled = false;
            centerAim = false;
            ActualVelocity = Vector3.zero;
            terrain.Stream(transform.position, Vector3.zero);
            body?.ApplyMotion(Vector3.zero, MoveMode, Grounded, crouched, false, Time.deltaTime);
            UpdateView();
        }

        private bool HandleDestination(Key key)
        {
            bool arrived = key == Key.Home ? TryReturnHome() : terrain.IsLargeWorld && (key switch
            {
                Key.F1 => EnterAt(terrain.OceanEntry),
                Key.F2 => EnterCave(west: true),
                Key.F3 => EnterCave(west: false),
                Key.F4 => EnterFlight(terrain.DeepChamber, 0f, 25f),
                Key.F5 => EnterFlight(terrain.BasinEntry, 0f, 28f),
                Key.F6 => EnterAt(terrain.DeepSeaEntry),
                Key.F7 => terrain.Landforms != null
                    ? EnterFlight(terrain.MountainOverview, 45f, 32f)
                    : EnterFlight(terrain.MountainOverview, 30f, 51f),
                _ => false
            });
            if (arrived)
                mouseControl.Release(wait: true);
            return arrived;
        }

        public void EnterAt(Vector3 point)
        {
            EnterAt(terrain.ToWorld(point));
        }

        public bool TryReturnHome()
        {
            WorldPosition home = terrain.WorldLanding;
            if (!terrain.IsLargeWorld)
                return EnterAt(home);

            WorldPosition previous = WorldFeet;
            terrain.CenterWorld(home);
            Vector3 local = terrain.ToLocal(home);
            terrain.PrepareEntry(local);
            motor.enabled = false;
            Vector3? destination = null;
            try
            {
                Physics.SyncTransforms();
                float minimumUp = Mathf.Cos(motor.slopeLimit * Mathf.Deg2Rad);
                foreach (Vector2Int offset in HomeProbes)
                {
                    Vector3 top = local + new Vector3(offset.x * 2.5f, 6f, offset.y * 2.5f);
                    if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, 12f, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore) || !hit.collider.transform.IsChildOf(terrain.transform)
                        || hit.normal.y < minimumUp)
                        continue;
                    Vector3 feet = hit.point + Vector3.up * 0.18f;
                    if (HasStandingRoom(feet))
                    {
                        destination = feet;
                        break;
                    }
                }
            }
            finally { motor.enabled = true; }
            if (destination.HasValue)
            {
                Arrive(destination.Value);
                return true;
            }
            RestoreAfterTravel(previous);
            ShowToolMessage("No clear landing near the starting point");
            return false;
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
                if (!HasStandingRoom(destination))
                {
                    RestoreAfterTravel(previous);
                    return false;
                }
            }
            Arrive(destination);
            return true;
        }

        private bool HasStandingRoom(Vector3 feet)
        {
            float halfSegment = Mathf.Max(0f, standingHeight * 0.5f - motor.radius);
            Vector3 center = feet + standingCenter;
            return terrain.HasCoverage(feet, feet) && !Physics.CheckCapsule(
                center - Vector3.up * (halfSegment - 0.05f), center + Vector3.up * halfSegment,
                motor.radius - motor.skinWidth, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void RestoreAfterTravel(WorldPosition previous)
        {
            terrain.CenterWorld(previous);
            SetFeet(terrain.ToLocal(previous));
            terrain.PrepareEntry(transform.position);
            Physics.SyncTransforms();
            UpdateView();
        }

        private void Arrive(Vector3 destination)
        {
            SetFeet(destination);
            gradeStart = null;
            SetCrouch(false, checkClearance: false);
            flying = false;
            MoveMode = ExplorerMoveMode.Land;
            MoveMode = ResolveMode(false, out float depth, out float column);
            diving = MoveMode == ExplorerMoveMode.Swim && depth > FloatDepth(column) + 0.12f;
            verticalSpeed = 0f;
            ActualVelocity = Vector3.zero;
            Grounded = false;
            body?.ResetMotion();
            body?.ApplyMotion(Vector3.zero, MoveMode, false, false, false, 0f);
            limited = false;
            selection.enabled = false;
            UpdateView();
        }

        private void SetFeet(Vector3 point)
        {
            motor.enabled = false;
            transform.position = point;
            motor.enabled = true;
        }

        internal bool EnterFlight(WorldPosition point, float heading, float tilt)
        {
            if (!EnterAt(point))
                return false;
            flying = true;
            MoveMode = ExplorerMoveMode.Flight;
            Look(heading, tilt);
            return true;
        }

        private bool EnterCave(bool west)
        {
            var path = terrain.CavePassage;
            int entrance = west ? 1 : path.Count - 2;
            WorldPosition mouth = path[entrance];
            WorldPosition next = path[entrance + (west ? 1 : -1)];
            var inward = new Vector3((float)(next.X - mouth.X), (float)(next.Y - mouth.Y), (float)(next.Z - mouth.Z)).normalized;
            const float approach = 60f;
            var point = new WorldPosition(mouth.X - inward.x * approach, mouth.Y - inward.y * approach,
                mouth.Z - inward.z * approach);
            double altitude = System.Math.Max(point.Y, terrain.SurfaceHeight(point) + 3f);
            Vector3 sight = inward * approach + Vector3.up * (float)(point.Y - altitude);
            Vector3 angles = Quaternion.LookRotation(sight).eulerAngles;
            return EnterFlight(new WorldPosition(point.X, altitude, point.Z), angles.y, Mathf.DeltaAngle(0f, angles.x));
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
        internal bool EnterRiverDemo()
        {
            if (terrain.Landforms is not RegionalLandforms region)
                return false;

            float bestWidth = 8f;
            WorldPosition feet = default;
            float heading = 0f, tilt = 0f;
            foreach (RiverReach reach in region.Rivers.Reaches)
            {
                float width = Mathf.Min(reach.Upstream.HalfWidth, reach.Downstream.HalfWidth);
                if (width <= bestWidth || width > 38f || reach.Downstream.Level < 55f)
                    continue;

                var middle = (reach.Upstream.Center + reach.Downstream.Center) * 0.5f;
                float level = (reach.Upstream.Level + reach.Downstream.Level) * 0.5f;
                WorldPosition channel = terrain.PlaceInRegion(middle, level - 0.8f);
                if (!terrain.TrySampleWaterColumn(channel, out float water, out _))
                    continue;
                double x = channel.X, z = channel.Z;

                var along = System.Numerics.Vector2.Normalize(reach.Downstream.Center - reach.Upstream.Center);
                var across = new System.Numerics.Vector2(-along.Y, along.X);
                var offset = across * (width + 23f) - along * 56f;
                var aboveBank = new WorldPosition(x + offset.X, water, z + offset.Y);
                float altitude = Mathf.Max(water + 41f, terrain.SurfaceHeight(aboveBank) + 22f);
                float sightlineHeight = (altitude + water) * 0.5f;
                var sightline = new WorldPosition(x + offset.X * 0.5f, sightlineHeight, z + offset.Y * 0.5f);
                if (altitude > water + 110f || terrain.SurfaceHeight(sightline) > sightlineHeight - 5f)
                    continue;

                bestWidth = width;
                feet = new WorldPosition(aboveBank.X, altitude, aboveBank.Z);
                heading = Mathf.Atan2(-offset.X, -offset.Y) * Mathf.Rad2Deg;
                tilt = Mathf.Atan2(altitude - water + EyeHeight, offset.Length()) * Mathf.Rad2Deg;
            }
            return bestWidth > 8f && EnterFlight(feet, heading, tilt);
        }
#endif

        public void MoveForFrame(Vector2 input, float lift, bool fast, bool flight, float dt) =>
            MoveForFrame(input, lift, fast, flight, dt, false, false);

        internal void MoveForFrame(Vector2 input, float lift, bool fast, bool flight, float dt, bool jump, bool crouch,
            bool walk = false) => MoveCharacter(input, lift, fast, flight, dt, jump, crouch, walk, yaw, 0f,
                verticalOverride: true, playerInput: false);

        private void MoveCharacter(Vector2 input, float lift, bool fast, bool flight, float dt, bool jump, bool crouch,
            bool walk, float heading, float tilt, bool verticalOverride, bool playerInput)
        {
            Quaternion rotation = Quaternion.Euler(0f, heading, 0f);
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 move = rotation * new Vector3(input.x, 0f, input.y);
            ExplorerMoveMode previousMode = MoveMode;
            MoveMode = ResolveMode(flight, out float surfaceDepth, out float columnDepth);
            if (MoveMode != previousMode)
            {
                verticalSpeed = 0f;
                diving = MoveMode == ExplorerMoveMode.Swim && previousMode == ExplorerMoveMode.Flight
                    && surfaceDepth > FloatDepth(columnDepth) + 0.12f;
            }
            if (playerInput)
                Face(rotation);
            else if (move.sqrMagnitude > 0.001f)
                Face(Quaternion.LookRotation(move));

            bool onLand = MoveMode == ExplorerMoveMode.Land;
            SetCrouch(onLand && crouch);
            float speed = MoveMode switch
            {
                ExplorerMoveMode.Flight => fast ? 68f : 18f,
                ExplorerMoveMode.Swim => fast ? 10f : 5.6f,
                _ => crouched ? CrouchSpeed : fast ? SprintSpeed : walk ? WalkSpeed : JogSpeed
            };
            bool groundedBefore = motor.isGrounded || (limited && Grounded);
            bool jumpAccepted = onLand && jump && groundedBefore && !crouched;
            Vector3 velocity;
            if (!onLand)
                velocity = FlyOrSwim(input, move, heading, tilt, speed, lift, dt, verticalOverride, playerInput, surfaceDepth, columnDepth);
            else
            {
                verticalSpeed = jumpAccepted ? 5.8f : groundedBefore && verticalSpeed <= 0f ? -2.8f : Mathf.Max(-30f, verticalSpeed - 19f * dt);
                velocity = move * (speed * movementSpeed.Multiplier) + Vector3.up * verticalSpeed;
            }

            CollisionFlags flags = MoveWithVelocity(velocity, dt);
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
                verticalSpeed = 0f;
            bool leftGround = jumpAccepted && ActualVelocity.y > 0.05f;
            if (jumpAccepted && !leftGround)
                verticalSpeed = 0f;
            Grounded = onLand && !leftGround && ((flags & CollisionFlags.Below) != 0
                || (groundedBefore && limited && ActualVelocity.sqrMagnitude < 0.001f));
            body?.ApplyMotion(ActualVelocity, MoveMode, Grounded, crouched, leftGround, dt);
        }

        private ExplorerMoveMode ResolveMode(bool flight, out float depth, out float column)
        {
            depth = column = 0f;
            if (flight)
                return ExplorerMoveMode.Flight;
            WorldPosition feet = WorldFeet;
            if (!terrain.TrySampleWaterColumn(feet, out float water, out float bottom))
                return ExplorerMoveMode.Land;
            depth = (float)(water - feet.Y);
            column = water - bottom;
            bool inWater = MoveMode == ExplorerMoveMode.Swim
                ? column >= SwimExitDepth : depth >= SwimEntryDepth && column >= SwimEntryDepth;
            return inWater ? ExplorerMoveMode.Swim : ExplorerMoveMode.Land;
        }

        private Vector3 FlyOrSwim(Vector2 input, Vector3 horizontal, float heading, float tilt, float speed, float lift,
            float dt, bool verticalOverride, bool playerInput, float depth, float column)
        {
            bool swimming = MoveMode == ExplorerMoveMode.Swim;
            float floatDepth = swimming ? FloatDepth(column) : 0f;
            if (swimming)
            {
                bool wantsDive = verticalOverride ? lift < 0f : input.y > 0f && tilt >= DivePitch;
                diving = wantsDive || (diving && depth > floatDepth + 0.02f);
                if (!diving)
                    return SwimAlongSurface(horizontal * (speed * movementSpeed.Multiplier), dt, depth - floatDepth);
            }

            float verticalLimit = swimming ? SwimVerticalSpeed : 15f;
            Vector3 velocity = horizontal * speed;
            if (verticalOverride)
            {
                velocity.y = Mathf.Clamp(lift, -1f, 1f) * verticalLimit;
                if (playerInput)
                    velocity = Vector3.ClampMagnitude(velocity, speed);
            }
            else if (input.y != 0f && tilt != 0f)
            {
                velocity = Quaternion.Euler(tilt, heading, 0f) * new Vector3(input.x, 0f, input.y) * speed;
                if (Mathf.Abs(velocity.y) > verticalLimit)
                    velocity *= verticalLimit / Mathf.Abs(velocity.y);
            }
            velocity *= movementSpeed.Multiplier;
            if (swimming && dt > 0f && velocity.y > 0f)
                velocity.y = Mathf.Min(velocity.y, Mathf.Max(0f, depth - floatDepth) / dt);
            return velocity;
        }

        private static float FloatDepth(float column) => Mathf.Clamp(column - 0.05f, SwimExitDepth, SurfaceSwimDepth);

        private Vector3 SwimAlongSurface(Vector3 horizontal, float dt, float heightChange)
        {
            if (dt <= 0f)
                return horizontal;
            if (horizontal.sqrMagnitude > 0f)
            {
                WorldPosition next = terrain.ToWorld(transform.position + horizontal * dt);
                if (terrain.TryGetWaterColumn(next, out float level, out float bottom) && level - bottom >= SwimExitDepth)
                    heightChange = (float)(level - next.Y) - FloatDepth(level - bottom);
            }
            float limit = SwimVerticalSpeed * movementSpeed.Multiplier;
            horizontal.y = Mathf.Clamp(heightChange / dt, -limit, limit);
            return horizontal;
        }

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
            if (MoveMode == ExplorerMoveMode.Swim)
                allowed = LimitSwimDisplacement(feet, allowed);
            CollisionFlags flags = motor.Move(allowed);
            ActualVelocity = dt > 0f ? (transform.position - feet) / dt : Vector3.zero;
            return flags;
        }

        internal void Look(float heading, float tilt)
        {
            yaw = heading;
            headingYaw = heading;
            pitch = tilt;
            Face(Quaternion.Euler(0f, heading, 0f));
            UpdateView();
        }

        private void Face(Quaternion rotation)
        {
            if (MoveMode == ExplorerMoveMode.Swim && rotation != transform.rotation)
            {
                SwimCapsule(transform.position, rotation * Vector3.forward, out Vector3 start, out Vector3 end);
                if (Physics.CheckCapsule(start, end, SwimRadius - 0.02f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return;
            }
            transform.rotation = rotation;
        }

        private static void SwimCapsule(Vector3 feet, Vector3 forward, out Vector3 start, out Vector3 end)
        {
            Vector3 hips = feet + Vector3.up * PlayerAnimatorDriver.SwimHipsHeight;
            start = hips - forward * SwimHalfSegment;
            end = hips + forward * SwimHalfSegment;
        }

        private Vector3 LimitSwimDisplacement(Vector3 feet, Vector3 displacement)
        {
            const float clearance = 0.02f;
            float distance = displacement.magnitude;
            if (distance <= 0.0001f)
                return displacement;
            // The regular capsule covers the vertical stroke. The waist-centred sweep covers the extended stroke.
            SwimCapsule(feet, transform.forward, out Vector3 start, out Vector3 end);
            Vector3 direction = displacement / distance;
            if (!Physics.CapsuleCast(start, end, SwimRadius, direction, out RaycastHit hit, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return displacement;
            if (hit.distance > clearance)
                return direction * (hit.distance - clearance);

            Vector3 slide = terrain.LimitDisplacement(feet, Vector3.ProjectOnPlane(displacement, hit.normal));
            float slideDistance = slide.magnitude;
            if (slideDistance > 0.0001f && Physics.CapsuleCast(start, end, SwimRadius, slide / slideDistance,
                out hit, slideDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return slide * (Mathf.Max(0f, hit.distance - clearance) / slideDistance);
            return slide;
        }

        internal void UpdateView()
        {
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 eye = transform.position + Vector3.up * CurrentEyeHeight;
            Vector3 offset = -(orbit * Vector3.forward) * cameraDistance + orbit * Vector3.right * 0.32f;
            if (MoveMode == ExplorerMoveMode.Swim && !diving)
            {
                WorldPosition camera = terrain.ToWorld(eye + offset);
                if (terrain.TryGetWaterColumn(camera, out float surface, out _))
                    offset.y += Mathf.Max(0f, (float)(surface - camera.Y) + 0.15f);
            }
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

        private void UpdateSelection(Ray sight, ExplorerInput input)
        {
            Vector3 eye = transform.position + Vector3.up * CurrentEyeHeight;
            selection.enabled = terrain.PickSurface(sight, eye, out RaycastHit hit);
            if (!selection.enabled)
            {
                if (terrain.IsLargeWorld && input.Prospect)
                    ShowToolMessage("Aim at reachable terrain to prospect");
                return;
            }
            terrain.GetEditCells(sight, hit, out Vector3Int dig, out Vector3Int fill, out WorldPosition sample, out bool placed);
            selection.transform.position = terrain.ToLocal(new WorldPosition(dig.x + 0.5, dig.y + 0.5, dig.z + 0.5));
            if (terrain.IsLargeWorld && input.Prospect)
                ShowToolMessage(terrain.Prospect(sample, placed));
            if (terrain.IsLargeWorld && input.Grade)
            {
                if (hit.normal.y < 0.5f)
                {
                    ShowToolMessage("Choose an upward-facing surface");
                    return;
                }
                var point = terrain.ToWorld(hit.point);
                if (gradeStart is WorldPosition start)
                {
                    if (terrain.TryGrade(start, point, motor))
                    {
                        gradeStart = null;
                        ShowToolMessage("Ground graded");
                    }
                    else
                        ShowToolMessage("No change. Choose gently sloping ground 2-32 m away, clear of your feet and cavities");
                }
                else
                    gradeStart = point;
                return;
            }
            if (input.Dig)
                terrain.TryEdit(dig, fill: false, motor);
            else if (input.Fill)
                terrain.TryEdit(fill, fill: true, motor);
        }

        private void ShowToolMessage(string message)
        {
            prospectMessage = message;
            prospectUntil = Time.unscaledTime + 5f;
        }

        private void OnGUI()
        {
            hudStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }
            };
            string saveStatus = terrain.TerrainSaveStatus;
            int hudHeight = terrain.IsLargeWorld ? (saveStatus == null ? 132 : 155) : 86;
            GUI.Box(new Rect(12, 11, 810, hudHeight), GUIContent.none);
            GUI.Label(new Rect(23, 16, 785, 24), hud ?? "REOL 2   |   CONTINENT TEST", hudStyle);
            GUI.Label(new Rect(23, 41, 785, 23),
                "W/S move   A/D turn   Q/E strafe   Shift sprint   Alt walk   C crouch   Space/X rise/dive   F fly");
            GUI.Label(new Rect(23, 64, 785, 23),
                "Drag L orbit / R steer   Both move   Wheel zoom   1 Dig cell / 2 Fill cell   G Prospect   ] time");
            if (terrain.IsLargeWorld)
            {
                const string travelKeys = "F1 sea   F2/F3 caves   F4 deep cavern   F5 lake   F6 trench   F7 peaks   T travel";
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
                GUI.Label(new Rect(23, 87, 785, 23), travelKeys + "   F8 save render report");
#else
                GUI.Label(new Rect(23, 87, 785, 23), travelKeys);
#endif
                GUI.Label(new Rect(23, 110, 785, 23), gradeStart.HasValue
                    ? "Grade: aim at the end and press 3   Esc cancel" : "3 mark two points to grade a slope");
                if (saveStatus != null)
                    GUI.Label(new Rect(23, 133, 785, 23), saveStatus);
            }
            if (Time.unscaledTime < prospectUntil)
                GUI.Box(new Rect(12, terrain.IsLargeWorld ? hudHeight + 18 : 128, 650, 28), prospectMessage);
            movementSpeed.DrawHud(Application.isFocused && !choosingDestination && MouseGesture == ExplorerMouseGesture.None);
            if (choosingDestination)
                DestinationPanel();
            if (centerAim && selection.enabled)
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
                    MoveMode = ExplorerMoveMode.Flight;
                    suppressInputFrame = true;
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

    }
}
