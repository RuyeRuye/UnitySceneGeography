using UnityEngine;

namespace SceneGeography
{
    public sealed partial class GeoReference
    {
        GeoReferenceFrame cachedLocalFrame;
        GeoLocalMapping cachedLocalMapping;

        /// <summary>取得当前标定的不可变局部数值映射，不读取/验证 Root 世界矩阵；旧映射可独立使用。</summary>
        public bool TryCaptureLocalMapping(out GeoLocalMapping mapping, out GeoError error)
        {
            mapping = null;
            if (!RequireFrame(out error))
            {
                return false;
            }

            if (cachedLocalFrame != frame)
            {
                cachedLocalFrame = frame;
                cachedLocalMapping = new GeoLocalMapping(frame, converter);
            }

            mapping = cachedLocalMapping;
            return true;
        }

        /// <summary>把局部位置转换为指定坐标标准；仅需标定，不读取 Root 世界变换。</summary>
        public bool TryLocalToCoordinate(
            in GeoVector3d position, in GeoCoordinateReference outputReference,
            out GeoCoordinate coordinate, out GeoError error)
        {
            coordinate = default;
            return TryCaptureLocalMapping(out var mapping, out error) &&
                mapping.TryLocalToCoordinate(position, outputReference, out coordinate, out error);
        }

        /// <summary>带 ENU 现实米偏移的放置转换为 Root 局部位置。</summary>
        public bool TryGetLocalPosition(in GeoPlacement placement, out GeoVector3d position, out GeoError error)
        {
            position = default;
            return TryCaptureLocalMapping(out var mapping, out error) &&
                mapping.TryGetLocalPosition(placement, out position, out error);
        }

        /// <summary>地理姿态转换为 Root 局部数值旋转。</summary>
        public bool TryGetLocalRotation(in GeoAttitude attitude, out Quaternion rotation, out GeoError error)
        {
            rotation = default;
            return TryCaptureLocalMapping(out var mapping, out error) &&
                mapping.TryGetLocalRotation(attitude, out rotation, out error);
        }

        /// <summary>带 ENU 偏移的地理放置和姿态转换为 Root 局部 Pose。</summary>
        public bool TryGetLocalPose(in GeoPlacement placement, in GeoAttitude attitude, out GeoLocalPose pose, out GeoError error)
        {
            pose = default;
            return TryCaptureLocalMapping(out var mapping, out error) &&
                mapping.TryGetLocalPose(placement, attitude, out pose, out error);
        }

        /// <summary>地理姿态转换为数值世界旋转，使用当前 Root 变换。</summary>
        public bool TryGetWorldRotation(in GeoAttitude attitude, out Quaternion rotation, out GeoError error)
        {
            rotation = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryGetWorldRotation(attitude, out rotation, out error);
        }

        /// <summary>给定经纬高和地理姿态计算 Root 局部 Pose，不需要目标物体或绑定组件。</summary>
        public bool TryGeoToLocalPose(
            in GeoCoordinate coordinate, in GeoAttitude attitude,
            out GeoLocalPose pose, out GeoError error)
        {
            pose = default;
            return TryCaptureLocalMapping(out var mapping, out error) &&
                mapping.TryGeoToLocalPose(coordinate, attitude, out pose, out error);
        }

        /// <summary>数值 Root 局部 Pose 反算为指定标准的地理 Pose。</summary>
        public bool TryLocalPoseToGeo(
            in GeoLocalPose pose, in GeoCoordinateReference outputReference,
            out GeoGeographicPose geographicPose, out GeoError error)
        {
            geographicPose = default;
            return TryCaptureLocalMapping(out var mapping, out error) &&
                mapping.TryLocalPoseToGeo(pose, outputReference, out geographicPose, out error);
        }

        /// <summary>把数值局部旋转测量为地理姿态，不要求 Root 世界变换有效。</summary>
        public bool TryLocalRotationToAttitude(Quaternion rotation, out GeoAttitude attitude, out GeoError error)
        {
            attitude = default;
            return TryCaptureLocalMapping(out var mapping, out error) &&
                mapping.TryLocalRotationToAttitude(rotation, out attitude, out error);
        }

        /// <summary>把数值世界旋转测量为地理姿态；需有效 Root，不读写任何目标 Transform。</summary>
        public bool TryWorldRotationToAttitude(Quaternion rotation, out GeoAttitude attitude, out GeoError error)
        {
            attitude = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryWorldRotationToAttitude(rotation, out attitude, out error);
        }

        /// <summary>给定经纬高和地理姿态计算数值世界 Pose。</summary>
        public bool TryGeoToWorldPose(
            in GeoCoordinate coordinate, in GeoAttitude attitude,
            out Pose pose, out GeoError error)
        {
            pose = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryGeoToWorldPose(coordinate, attitude, out pose, out error);
        }

        /// <summary>将数值世界 Pose 反算为指定标准的地理 Pose。</summary>
        public bool TryWorldPoseToGeo(
            in Pose pose, in GeoCoordinateReference outputReference,
            out GeoGeographicPose geographicPose, out GeoError error)
        {
            geographicPose = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryWorldPoseToGeo(pose, outputReference, out geographicPose, out error);
        }
    }
}
