using System;
using UnityEngine;

namespace SceneGeography
{
    /// <summary>不依赖 GameObject、Transform 或绑定组件的不可变局部数值映射，可直接由 Core 求解结果构造。</summary>
    /// <remarks>位置使用 double；姿态使用 Unity 四元数值。重新标定不会改变已经取得的映射。</remarks>
    public sealed class GeoLocalMapping
    {
        readonly GeoReferenceFrame frame;
        readonly GeoCoordinateConverter converter;
        readonly Quaternion geographicBasis;

        /// <summary>Root 局部空间中的固定参考东、北、上轴。</summary>
        public GeoAxes3d LocalAxes => frame.LocalAxes;

        /// <summary>每现实米对应的模型局部单位数，不包含 Root 世界缩放。</summary>
        public double LocalUnitsPerMeter => frame.LocalUnitsPerMeter;

        /// <summary>以纯数学标定解构造数值映射；省略 converter 时使用默认坐标转换器。</summary>
        public GeoLocalMapping(GeoReferenceFrame frame, GeoCoordinateConverter converter = null)
        {
            this.frame = frame ?? throw new ArgumentNullException(nameof(frame));
            this.converter = converter ?? GeoCoordinateConverter.CreateDefault();
            geographicBasis = Quaternion.LookRotation(GeoUnity.F(frame.LocalAxes.North), GeoUnity.F(frame.LocalAxes.Up));
        }

        /// <summary>将明确参考的经纬高转换为 Root 局部 double 位置；不读取任何 Transform。</summary>
        public bool TryGeoToLocal(in GeoCoordinate coordinate, out GeoVector3d position, out GeoError error)
        {
            position = default;
            return converter.TryToWgs84(coordinate, out var wgs, out error) &&
                frame.TryWgs84ToLocal(wgs, out position, out error);
        }

        /// <summary>将 Root 局部位置反算为指定水平与高度参考，包含范围和高程模型检查。</summary>
        public bool TryLocalToCoordinate(
            in GeoVector3d position,
            in GeoCoordinateReference outputReference,
            out GeoCoordinate coordinate,
            out GeoError error)
        {
            coordinate = default;
            return frame.TryLocalToWgs84(position, out var wgs, out error) &&
                converter.TryFromWgs84(wgs, outputReference, out coordinate, out error);
        }

        /// <summary>将经纬高和 ENU 现实米偏移转换为 Root 局部位置，偏移后的终点也检查范围。</summary>
        public bool TryGetLocalPosition(in GeoPlacement placement, out GeoVector3d position, out GeoError error)
        {
            position = default;
            if (!TryGeoToLocal(placement.Coordinate, out var origin, out error) ||
                !frame.TryMetersOffsetToLocal(placement.Offset, out var offset, out error))
            {
                return false;
            }

            var result = origin + offset;
            if (!frame.CheckRange(frame.ToEngine(result), out error))
            {
                return false;
            }

            position = result;
            return true;
        }

        /// <summary>将地理姿态转换为 Root 局部旋转；不包含模型轴修正。</summary>
        public bool TryGetLocalRotation(in GeoAttitude attitude, out Quaternion rotation, out GeoError error)
        {
            rotation = default;
            error = default;
            if (!attitude.IsValid)
            {
                return GeoError.Fail(GeoErrorCode.InvalidOrientationRequest, "Specify a valid geographic attitude.", out error);
            }

            rotation = (geographicBasis * attitude.GeographicRotation).normalized;
            return true;
        }

        /// <summary>测量数值局部旋转相对参考地理基底的姿态；竖直时保留四元数并标记角度有效性。</summary>
        public bool TryLocalRotationToAttitude(Quaternion rotation, out GeoAttitude attitude, out GeoError error)
        {
            attitude = default;
            if (!GeoUnity.Unit(rotation))
            {
                return GeoError.Fail(GeoErrorCode.InvalidOrientationRequest, "Rotation must be a finite unit quaternion.", out error);
            }

            return GeoAttitude.TryFromRotation(Quaternion.Inverse(geographicBasis) * rotation, out attitude, out error);
        }

        /// <summary>经纬高和地理姿态直接转换为局部 Pose，不要求存在 Reference 或绑定组件。</summary>
        public bool TryGeoToLocalPose(
            in GeoCoordinate coordinate,
            in GeoAttitude attitude,
            out GeoLocalPose pose,
            out GeoError error)
        {
            pose = default;
            if (!TryGeoToLocal(coordinate, out var position, out error) ||
                !TryGetLocalRotation(attitude, out var rotation, out error))
            {
                return false;
            }

            pose = new GeoLocalPose(position, rotation);
            return true;
        }

        /// <summary>带 ENU 偏移的地理放置和姿态转换为局部 Pose，偏移不改变输入坐标。</summary>
        public bool TryGetLocalPose(
            in GeoPlacement placement,
            in GeoAttitude attitude,
            out GeoLocalPose pose,
            out GeoError error)
        {
            pose = default;
            if (!TryGetLocalPosition(placement, out var position, out error) ||
                !TryGetLocalRotation(attitude, out var rotation, out error))
            {
                return false;
            }

            pose = new GeoLocalPose(position, rotation);
            return true;
        }

        /// <summary>纯数值局部 Pose 反算为指定标准的地理 Pose；返回最终位置，不推断或撤销原始偏移。</summary>
        public bool TryLocalPoseToGeo(
            in GeoLocalPose pose,
            in GeoCoordinateReference outputReference,
            out GeoGeographicPose geographicPose,
            out GeoError error)
        {
            geographicPose = default;
            if (!TryLocalToCoordinate(pose.Position, outputReference, out var coordinate, out error) ||
                !TryLocalRotationToAttitude(pose.Rotation, out var attitude, out error))
            {
                return false;
            }

            geographicPose = new GeoGeographicPose(coordinate, attitude);
            return true;
        }
    }
}
