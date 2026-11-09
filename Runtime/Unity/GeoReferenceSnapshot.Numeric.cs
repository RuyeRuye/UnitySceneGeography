using UnityEngine;

namespace SceneGeography
{
    public sealed partial class GeoReferenceSnapshot
    {
        readonly Quaternion geographicWorldBasis;

        /// <summary>捕获标定的独立局部数值映射；不读取 Root 或任何场景对象。</summary>
        public GeoLocalMapping LocalMapping { get; private set; }

        /// <summary>将实际世界旋转测量为地理姿态，不读取 Transform，也不自动移除模型修正。</summary>
        public bool TryWorldRotationToAttitude(Quaternion rotation, out GeoAttitude attitude, out GeoError error)
        {
            attitude = default;
            if (!GeoUnity.Unit(rotation))
            {
                return GeoError.Fail(GeoErrorCode.InvalidOrientationRequest, "Rotation must be a finite unit quaternion.", out error);
            }

            return GeoAttitude.TryFromRotation(Quaternion.Inverse(geographicWorldBasis) * rotation, out attitude, out error);
        }

        /// <summary>将地理姿态转换为数值世界旋转；不包含模型轴修正，支持竖直姿态。</summary>
        public bool TryGetWorldRotation(in GeoAttitude attitude, out Quaternion rotation, out GeoError error)
        {
            rotation = default;
            error = default;
            if (!attitude.IsValid)
            {
                return GeoError.Fail(GeoErrorCode.InvalidOrientationRequest, "Specify a valid geographic attitude.", out error);
            }

            rotation = (geographicWorldBasis * attitude.GeographicRotation).normalized;
            return true;
        }

        /// <summary>给定经纬高与地理姿态，计算数值世界 Pose，不需要 Anchor 或 Orientation。</summary>
        public bool TryGeoToWorldPose(
            in GeoCoordinate coordinate,
            in GeoAttitude attitude,
            out Pose pose,
            out GeoError error)
        {
            pose = default;
            if (!TryGeoToWorld(coordinate, out var position, out error) ||
                !TryGetWorldRotation(attitude, out var rotation, out error))
            {
                return false;
            }

            pose = new Pose(position, rotation);
            return true;
        }

        /// <summary>数值世界 Pose 反算为指定水平/高度参考及地理姿态，不推断输入来源或模型修正。</summary>
        public bool TryWorldPoseToGeo(
            in Pose pose,
            in GeoCoordinateReference outputReference,
            out GeoGeographicPose geographicPose,
            out GeoError error)
        {
            geographicPose = default;
            if (!TryWorldToCoordinate(pose.position, outputReference, out var coordinate, out error) ||
                !TryWorldRotationToAttitude(pose.rotation, out var attitude, out error))
            {
                return false;
            }

            geographicPose = new GeoGeographicPose(coordinate, attitude);
            return true;
        }
    }
}
