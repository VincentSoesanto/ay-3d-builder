using Builder.Input;
using Builder.View;
using UnityEngine;

namespace Builder.CameraControl
{
    public enum CameraMode { Fly, Orbit }

    /// <summary>
    /// Game-style camera, on the Main Camera.
    /// Fly:   WASD / left stick moves (flat, like Minecraft creative), Space / A up, Alt / B down, Shift / L3 fast,
    ///        mouse / right stick looks around.
    /// Orbit: O / R3 toggles. Circles around the selection (or the whole model); mouse / right stick turns, wheel / left stick zooms.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        [SerializeField] BuilderInput input;
        [SerializeField] DocumentView documentView;
        [SerializeField] Tools.Selection selection;

        [Header("Fly")]
        [SerializeField] float moveSpeed = 5f;             // meters per second
        [SerializeField] float fastMultiplier = 3f;

        [Header("Look")]
        [SerializeField] float mouseSensitivity = 0.12f;   // degrees per pixel
        [SerializeField] float stickLookSpeed = 140f;      // degrees per second at full stick

        [Header("Orbit")]
        [SerializeField] float zoomSpeed = 0.15f;          // fraction of the distance per wheel step
        [SerializeField] float minDistance = 0.5f;
        [SerializeField] float maxDistance = 200f;

        public CameraMode Mode { get; private set; } = CameraMode.Fly;

        float yaw, pitch;                 // degrees
        Vector3 orbitTarget;
        float orbitDistance;

        /// <summary>Puts the camera at <paramref name="position"/>, looking at <paramref name="target"/>.</summary>
        public void LookFrom(Vector3 position, Vector3 target)
        {
            transform.position = position;
            var e = Quaternion.LookRotation(target - position).eulerAngles;
            yaw = e.y;
            pitch = NormalizeAngle(e.x);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void Start()
        {
            var e = transform.eulerAngles;
            yaw = e.y;
            pitch = NormalizeAngle(e.x);
        }

        void Update()
        {
            if (input.Pressed(input.ToggleCamera))
            {
                if (Mode == CameraMode.Fly) EnterOrbit();
                else Mode = CameraMode.Fly;   // keep the current position and view direction
            }

            // Mouse movement is already "per frame"; stick values are "per second", so only the stick uses deltaTime.
            Vector2 look = input.MouseLook * mouseSensitivity
                         + input.StickLook * (stickLookSpeed * Time.deltaTime);
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -89f, 89f);
            var rotation = Quaternion.Euler(pitch, yaw, 0f);

            if (Mode == CameraMode.Fly) UpdateFly(rotation);
            else UpdateOrbit(rotation);
        }

        void UpdateFly(Quaternion rotation)
        {
            Vector2 move = input.MoveValue;
            // Forward/right ignore looking up/down, so W never makes you sink or climb.
            var flatRotation = Quaternion.Euler(0f, yaw, 0f);
            var dir = flatRotation * new Vector3(move.x, 0f, move.y);
            if (input.Held(input.Up)) dir += Vector3.up;
            if (input.Held(input.Down)) dir += Vector3.down;

            float speed = moveSpeed * (input.Held(input.Fast) ? fastMultiplier : 1f);
            transform.SetPositionAndRotation(transform.position + dir * (speed * Time.deltaTime), rotation);
        }

        void EnterOrbit()
        {
            Mode = CameraMode.Orbit;
            // Orbit the selection if something is selected, otherwise the whole model.
            var bounds = selection != null && selection.Count > 0 ? selection.Bounds : documentView.GetModelBounds();
            orbitTarget = bounds?.center ?? Vector3.zero;
            orbitDistance = Mathf.Clamp(Vector3.Distance(transform.position, orbitTarget), minDistance, maxDistance);

            // Turn to face the target from where we are now, so switching doesn't jump.
            var toTarget = Quaternion.LookRotation(orbitTarget - transform.position).eulerAngles;
            yaw = toTarget.y;
            pitch = NormalizeAngle(toTarget.x);
        }

        void UpdateOrbit(Quaternion rotation)
        {
            // Wheel up = closer. We only use the wheel's direction, because its raw size differs between devices.
            float wheel = input.Blocked || input.PrecisionOn ? 0f : input.Zoom.ReadValue<float>();   // precision: wheel = up/down
            float steps = Mathf.Abs(wheel) > 0.01f ? Mathf.Sign(wheel) : 0f;
            // Left stick up/down (or W/S) also zooms, smoothly: about 4 "steps" per second at full tilt.
            steps += input.MoveValue.y * 4f * Time.deltaTime;
            orbitDistance = Mathf.Clamp(orbitDistance * Mathf.Pow(1f - zoomSpeed, steps), minDistance, maxDistance);

            var position = orbitTarget - rotation * Vector3.forward * orbitDistance;
            transform.SetPositionAndRotation(position, rotation);
        }

        static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;
    }
}
