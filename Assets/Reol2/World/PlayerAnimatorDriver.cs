using Unity.Profiling;
using UnityEngine;

namespace Reol2.World
{
    internal enum ExplorerMoveMode { Land, Swim, Flight }
    internal enum ExplorerPose { Ground, Crouch, Takeoff, Air, Land, Swim, Flight }

    [DisallowMultipleComponent, RequireComponent(typeof(Animator))]
    public sealed class PlayerAnimatorDriver : MonoBehaviour
    {
        private static readonly ProfilerMarker DriveMarker = new("Reol2.Player.Animation");
        private static readonly ProfilerMarker IkMarker = new("Reol2.Player.FootIK");
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int[] StateHashes =
        {
            Animator.StringToHash("Base Layer.Ground"), Animator.StringToHash("Base Layer.Crouch"),
            Animator.StringToHash("Base Layer.Takeoff"), Animator.StringToHash("Base Layer.Air"),
            Animator.StringToHash("Base Layer.Land"), Animator.StringToHash("Base Layer.Swim"),
            Animator.StringToHash("Base Layer.Flight")
        };

        private Animator animator;
        private Transform feet;
        private bool grounded;
        private float airborneTime, landingTime;
        private float groundSpeed;
        private float leftWeight, rightWeight;
        internal ExplorerPose Pose { get; private set; }
        internal Animator Animator => animator;
        internal float IkWeight => Mathf.Max(leftWeight, rightWeight);

        private void Awake()
        {
            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            var explorer = GetComponentInParent<ContinentExplorer>();
            feet = explorer != null ? explorer.transform : transform;
        }

        internal void ResetMotion()
        {
            airborneTime = landingTime = groundSpeed = leftWeight = rightWeight = 0f;
            grounded = false;
            Pose = ExplorerPose.Ground;
            animator.SetFloat(Speed, 0f);
            animator.Play(StateHashes[(int)Pose], 0, 0f);
        }

        internal void ApplyMotion(Vector3 velocity, ExplorerMoveMode mode, bool onGround, bool crouched, bool jumped, float dt)
        {
            using var marker = DriveMarker.Auto();
            grounded = onGround && mode == ExplorerMoveMode.Land;
            groundSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            float speed = mode == ExplorerMoveMode.Swim ? velocity.magnitude : groundSpeed;
            animator.SetFloat(Speed, speed, 0.075f, dt);

            ExplorerPose next;
            if (mode != ExplorerMoveMode.Land)
            {
                airborneTime = landingTime = 0f;
                next = mode == ExplorerMoveMode.Swim ? ExplorerPose.Swim : ExplorerPose.Flight;
            }
            else if (grounded)
            {
                if (airborneTime >= 0.16f)
                    landingTime = 0.18f;
                airborneTime = 0f;
                landingTime = Mathf.Max(0f, landingTime - dt);
                next = crouched ? ExplorerPose.Crouch : landingTime > 0f ? ExplorerPose.Land : ExplorerPose.Ground;
            }
            else
            {
                airborneTime += dt;
                landingTime = 0f;
                next = jumped || (Pose == ExplorerPose.Takeoff && airborneTime < 0.19f) ? ExplorerPose.Takeoff
                    : airborneTime >= 0.09f ? ExplorerPose.Air : Pose;
            }
            if (next == Pose)
                return;
            Pose = next;
            animator.CrossFadeInFixedTime(StateHashes[(int)Pose], next == ExplorerPose.Takeoff ? 0.045f : 0.10f, 0);
        }

        private void OnAnimatorIK(int layer)
        {
            using var marker = IkMarker.Auto();
            bool plantsFeet = grounded && groundSpeed < 6.5f;
            PlaceFoot(AvatarIKGoal.LeftFoot, plantsFeet, ref leftWeight);
            PlaceFoot(AvatarIKGoal.RightFoot, plantsFeet, ref rightWeight);
        }

        private void PlaceFoot(AvatarIKGoal foot, bool plantsFeet, ref float weight)
        {
            const float sole = 0.076f;
            Vector3 ankle = animator.GetIKPosition(foot);
            float targetWeight = 0f;
            if (plantsFeet && Physics.Raycast(ankle + Vector3.up * 0.25f, Vector3.down, out RaycastHit hit, 0.56f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.52f)
            {
                Vector3 target = hit.point + hit.normal * sole;
                float lifting = ankle.y - target.y;
                if (Mathf.Abs(lifting) < 0.24f)
                {
                    float swing = Mathf.Max(ankle.y - feet.position.y - sole, 0f);
                    targetWeight = 1f - WorldNoise.Ramp((swing - 0.07f) / 0.15f);
                    animator.SetIKPosition(foot, target);
                    animator.SetIKRotation(foot, Quaternion.FromToRotation(Vector3.up, hit.normal) * animator.GetIKRotation(foot));
                }
            }
            weight = plantsFeet ? Mathf.MoveTowards(weight, targetWeight, Time.deltaTime * 13f) : 0f;
            // An invalid target always keeps the evaluated animation pose.
            if (targetWeight <= 0f)
                weight = 0f;
            animator.SetIKPositionWeight(foot, weight);
            animator.SetIKRotationWeight(foot, weight * 0.7f);
        }
    }
}
