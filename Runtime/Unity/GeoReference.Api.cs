using UnityEngine;

namespace SceneGeography
{
    /// <summary>场景地理系统门面；Unity 主线程上配置/捕获 Root，采用 WGS84→ECEF→固定 ENU→局部→世界链。</summary>
    public sealed partial class GeoReference
    {
        /// <summary>标准化明确来源坐标并转换为世界位置；失败输出不可用，Root 无效时不回退到零坐标。</summary>
        public bool TryGeoToWorld(in GeoCoordinate coordinate, out Vector3 worldPosition, out GeoError error)
        {
            worldPosition = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryGeoToWorld(coordinate, out worldPosition, out error);
        }

        /// <summary>把 WGS84 椭球高转换为世界位置，包含标定比例及 Root 世界姿态。</summary>
        public bool TryWgs84ToWorld(in Wgs84Coordinate coordinate, out Vector3 worldPosition, out GeoError error)
        {
            worldPosition = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryWgs84ToWorld(coordinate, out worldPosition, out error);
        }

        /// <summary>把世界位置反算为 WGS84 经纬度和绝对椭球高，并检查有效范围。</summary>
        public bool TryWorldToWgs84(Vector3 worldPosition, out Wgs84Coordinate coordinate, out GeoError error)
        {
            coordinate = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryWorldToWgs84(worldPosition, out coordinate, out error);
        }

        /// <summary>把世界位置转换为指定水平和高度参考；范围/高程模型不支持时明确失败。</summary>
        public bool TryWorldToCoordinate(
            Vector3 worldPosition,
            in GeoCoordinateReference outputReference,
            out GeoCoordinate coordinate,
            out GeoError error)
        {
            coordinate = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryWorldToCoordinate(worldPosition, outputReference, out coordinate, out error);
        }

        /// <summary>把地理坐标及 ENU 现实米偏移转换为世界位置，最终偏移后仍检查范围。</summary>
        public bool TryGetWorldPosition(in GeoPlacement placement, out Vector3 worldPosition, out GeoError error)
        {
            worldPosition = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryGetWorldPosition(placement, out worldPosition, out error);
        }

        /// <summary>获取捕获映射的世界参考东、北、上单位方向，不是逐点真北。</summary>
        public bool TryGetWorldAxes(out GeoWorldAxes axes, out GeoError error)
        {
            axes = default;
            return TryCaptureSnapshot(out var current, out error) && current.TryGetWorldAxes(out axes, out error);
        }

        /// <summary>将世界方向投影到地理水平面并求北向东顺时针航向；纯竖直方向失败。</summary>
        public bool TryWorldDirectionToHeading(Vector3 worldForward, out GeoHeading heading, out GeoError error)
        {
            heading = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryWorldDirectionToHeading(worldForward, out heading, out error);
        }

        /// <summary>把东、北、上现实米偏移转换为世界位移，不包含位置平移项。</summary>
        public bool TryMetersOffsetToWorld(in GeoEnuOffset offset, out Vector3 worldDisplacement, out GeoError error)
        {
            worldDisplacement = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryMetersOffsetToWorld(offset, out worldDisplacement, out error);
        }

        /// <summary>将有限非负现实米长度换算为世界单位，包含 Root 世界统一缩放。</summary>
        public bool TryMetersToWorldUnits(double meters, out double worldUnits, out GeoError error)
        {
            worldUnits = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryMetersToWorldUnits(meters, out worldUnits, out error);
        }

        /// <summary>将有限非负世界长度换算为现实米。</summary>
        public bool TryWorldUnitsToMeters(double worldUnits, out double meters, out GeoError error)
        {
            meters = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryWorldUnitsToMeters(worldUnits, out meters, out error);
        }

        /// <summary>从源世界位置求地理朝向及 yaw→pitchUp→rollClockwise 偏角旋转；重合或近竖直不稳定方向失败。</summary>
        public bool TryGetWorldRotation(
            Vector3 sourceWorldPosition,
            in GeoOrientationRequest request,
            out Quaternion rotation,
            out GeoError error)
        {
            rotation = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryGetWorldRotation(sourceWorldPosition, request, out rotation, out error);
        }

        /// <summary>在同一捕获映射中计算位置和旋转；任一步失败不返回可用 Pose。</summary>
        public bool TryGetWorldPose(
            in GeoPlacement placement,
            in GeoOrientationRequest orientation,
            out Pose pose,
            out GeoError error)
        {
            pose = default;
            return TryCaptureSnapshot(out var current, out error) &&
                current.TryGetWorldPose(placement, orientation, out pose, out error);
        }
    }
}
