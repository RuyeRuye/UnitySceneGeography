using UnityEngine;
using UnityEngine.InputSystem;

namespace SceneGeography.Samples
{
    /// <summary>合成验证场景的自由相机；没有碰撞和重力，不要与其他移动器同时控制姿态。</summary>
    public sealed class GeoDemoCamera : MonoBehaviour
    {
        /// <summary>提供参考 Up 与现实米比例的地理系统；未初始化时不移动。</summary>
        [Tooltip("Geographic Reference providing Up and real-meter scale; movement requires initialization.")]
        public GeoReference reference;

        /// <summary>自由移动速度，单位现实米/秒；Shift 加速三倍，不包含碰撞或重力。</summary>
        [Tooltip("Free movement speed in real meters/second; Shift triples speed. No collision or gravity.")]
        public float speedMetersPerSecond = 15;

        /// <summary>右键旋转灵敏度，单位度/像素。</summary>
        [Tooltip("Right-mouse look sensitivity in degrees/pixel.")]
        public float mouseDegreesPerPixel = .15f;
        float pitch;

        /// <summary>设置相机姿态并同步俯仰缓存，防止下一次鼠标输入回弹。</summary>
        public void Teleport(Pose pose)
        {
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            Vector3 up = Vector3.up;
            if (reference != null && reference.TryGetWorldAxes(out var axes, out _))
            {
                up = axes.Up;
            }

            pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(transform.forward, up), -1, 1)) * Mathf.Rad2Deg;
        }

        void Update()
        {
            if (reference == null || !reference.TryCaptureSnapshot(out var snapshot, out _))
            {
                return;
            }

            var up = snapshot.WorldAxes.Up;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue() * mouseDegreesPerPixel;
                transform.rotation = Quaternion.AngleAxis(delta.x, up) * transform.rotation;
                float next = Mathf.Clamp(pitch + delta.y, -85, 85), change = next - pitch;
                pitch = next;
                transform.rotation = Quaternion.AngleAxis(-change, transform.right) * transform.rotation;
            }

            if (keyboard == null)
            {
                return;
            }

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            float f = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            float r = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
            float h = (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0);
            Vector3 move = Vector3.ClampMagnitude(forward * f + right * r + up * h, 1);
            float speed = (float)snapshot.WorldUnitsPerMeter * speedMetersPerSecond * (keyboard.leftShiftKey.isPressed ? 3 : 1);
            transform.position += move * speed * Time.deltaTime;
        }
    }
}
