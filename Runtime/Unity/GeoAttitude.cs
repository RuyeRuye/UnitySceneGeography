using System;
using UnityEngine;

namespace SceneGeography
{
    /// <summary>相对固定地理基底（+X 东、+Y 上、+Z 北）的不可变姿态，不包含模型轴修正。</summary>
    /// <remarks>四元数在竖直姿态仍有效；航向和侧倾在极点不唯一，读取前检查 HasHeading/HasRoll。</remarks>
    public readonly struct GeoAttitude
    {
        /// <summary>是否由有效单位四元数构造；default 无效。</summary>
        public bool IsValid { get; }

        /// <summary>相对东/上/北基底的单位四元数，不是世界旋转或标准 ENU 轴顺序的四元数。</summary>
        public Quaternion GeographicRotation { get; }

        /// <summary>前向水平投影是否足够稳定，能够定义航向。</summary>
        public bool HasHeading { get; }

        /// <summary>北 0°、东 90°，范围 [0,360)；仅 HasHeading 为 true 时有效。</summary>
        public GeoHeading Heading { get; }

        /// <summary>前向的仰角，抬头为正，范围 [-90,90] 度，竖直时仍有效。</summary>
        public double PitchUpDegrees { get; }

        /// <summary>是否能够相对零侧倾姿态唯一描述侧倾。</summary>
        public bool HasRoll => HasHeading;

        /// <summary>与 GeoRotationOffset 一致的顺时针侧倾，范围 [-180,180) 度；仅 HasRoll 为 true 时有效。</summary>
        public double RollClockwiseDegrees { get; }

        GeoAttitude(Quaternion rotation)
        {
            IsValid = true;
            GeographicRotation = rotation;
            var forward = rotation * Vector3.forward;
            double horizontal = Math.Sqrt((double)forward.x * forward.x + (double)forward.z * forward.z);
            PitchUpDegrees = Math.Atan2(forward.y, horizontal) * Mathf.Rad2Deg;
            HasHeading = horizontal > 1e-6;
            Heading = default;
            RollClockwiseDegrees = 0;
            if (HasHeading)
            {
                Heading = GeoHeading.FromDegrees(Math.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
                var level = Quaternion.LookRotation(forward, Vector3.up);
                var up = rotation * Vector3.up;
                double roll = Math.Atan2(Vector3.Dot(up, level * Vector3.right), Vector3.Dot(up, level * Vector3.up)) * Mathf.Rad2Deg;
                RollClockwiseDegrees = (roll + 540) % 360 - 180;
            }
        }

        /// <summary>从地理基底单位四元数测量姿态；非法、非单位四元数返回 false，不接受 default。</summary>
        public static bool TryFromRotation(Quaternion rotation, out GeoAttitude attitude, out GeoError error)
        {
            attitude = default;
            error = default;
            if (!GeoUnity.Unit(rotation))
            {
                return GeoError.Fail(GeoErrorCode.InvalidOrientationRequest, "Rotation must be a finite unit quaternion.", out error);
            }

            attitude = new GeoAttitude(rotation.normalized);
            return true;
        }

        /// <summary>以航向、抬头和顺时针侧倾（度）构造姿态，顺序为 yaw → pitchUp → rollClockwise。</summary>
        /// <remarks>角度可跨越一周；测量结果规范化，竖直姿态不保留不唯一的输入航向/侧倾。</remarks>
        public static GeoAttitude FromDegrees(double headingDegrees, double pitchUpDegrees = 0, double rollClockwiseDegrees = 0)
        {
            GeoMath.RequireFinite(headingDegrees, nameof(headingDegrees));
            GeoMath.RequireFinite(pitchUpDegrees, nameof(pitchUpDegrees));
            GeoMath.RequireFinite(rollClockwiseDegrees, nameof(rollClockwiseDegrees));
            var rotation = Quaternion.AngleAxis((float)(headingDegrees % 360), Vector3.up)
                * Quaternion.AngleAxis((float)(-pitchUpDegrees % 360), Vector3.right)
                * Quaternion.AngleAxis((float)(-rollClockwiseDegrees % 360), Vector3.forward);
            return new GeoAttitude(rotation.normalized);
        }
    }

    /// <summary>Root 局部空间中的纯数值 Pose；位置为 double 模型单位，旋转为 Unity 单位四元数。</summary>
    public readonly struct GeoLocalPose
    {
        /// <summary>Root 局部位置，不是某个物体直属父级下的 localPosition。</summary>
        public GeoVector3d Position { get; }

        /// <summary>Root 局部旋转，不包含 Root 世界旋转。</summary>
        public Quaternion Rotation { get; }

        /// <summary>从有限局部位置和单位四元数构造；非法输入抛出 ArgumentException。</summary>
        public GeoLocalPose(GeoVector3d position, Quaternion rotation)
        {
            if (!position.IsFinite || !GeoUnity.Unit(rotation))
            {
                throw new ArgumentException("A finite position and unit rotation are required.");
            }

            Position = position;
            Rotation = rotation.normalized;
        }
    }

    /// <summary>地理坐标与地理姿态的不可变组合；坐标包含明确的水平和高度参考。</summary>
    public readonly struct GeoGeographicPose
    {
        /// <summary>指定水平与高度参考下的经纬高。</summary>
        public GeoCoordinate Coordinate { get; }

        /// <summary>相对固定参考地理基底的姿态，与坐标的 WGS/GCJ/BD 类型无关。</summary>
        public GeoAttitude Attitude { get; }

        internal GeoGeographicPose(GeoCoordinate coordinate, GeoAttitude attitude)
        {
            Coordinate = coordinate;
            Attitude = attitude;
        }
    }
}
