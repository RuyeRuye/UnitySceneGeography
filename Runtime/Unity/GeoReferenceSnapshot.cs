using System;
using UnityEngine;

namespace SceneGeography
{
    /// <summary>不可变的捕获时刻映射，不再读取 Transform；旧快照在 Root/标定改变后仍表示历史状态。</summary>
    public sealed partial class GeoReferenceSnapshot
    {
        readonly GeoReferenceFrame frame;
        readonly GeoCoordinateConverter converter;
        readonly GeoRootPose root;

        /// <summary>捕获的标定版本；成功提交或 Clear 后变化，失败求解不改变。</summary>
        public long CalibrationVersion { get; }

        /// <summary>捕获时的 Root 世界姿态版本，不包含重新拟合。</summary>
        public long RootPoseVersion { get; }

        /// <summary>每现实米对应的世界单位数，等于标定局部比例乘 Root 世界统一缩放。</summary>
        public double WorldUnitsPerMeter => frame.LocalUnitsPerMeter * root.Scale;

        /// <summary>捕获时刻世界空间中的参考东、北、上单位方向。</summary>
        public GeoWorldAxes WorldAxes { get; }

        internal GeoReferenceSnapshot(
            GeoReferenceFrame frame,
            GeoCoordinateConverter converter,
            GeoRootPose root,
            long calibrationVersion,
            long rootVersion)
        {
            this.frame = frame;
            this.converter = converter;
            this.root = root;
            CalibrationVersion = calibrationVersion;
            RootPoseVersion = rootVersion;
            LocalMapping = new GeoLocalMapping(frame, converter);
            var axes = frame.LocalAxes;
            WorldAxes = new GeoWorldAxes(
                GeoUnity.F(root.Rotation.Apply(axes.East)).normalized,
                GeoUnity.F(root.Rotation.Apply(axes.North)).normalized,
                GeoUnity.F(root.Rotation.Apply(axes.Up)).normalized);
            geographicWorldBasis = Quaternion.LookRotation(WorldAxes.North, WorldAxes.Up);
        }

        /// <summary>标准化输入后转换为 Root 局部双精度位置；不要求 Root 世界矩阵合法。</summary>
        public bool TryGeoToLocal(in GeoCoordinate coordinate, out GeoVector3d localPosition, out GeoError error)
        {
            localPosition = default;
            return converter.TryToWgs84(coordinate, out var wgs, out error) &&
                frame.TryWgs84ToLocal(wgs, out localPosition, out error);
        }

        /// <summary>将 WGS84 椭球高转换为 Root 局部双精度位置，并检查范围；不读取 Root 世界姿态。</summary>
        public bool TryWgs84ToLocal(in Wgs84Coordinate coordinate, out GeoVector3d localPosition, out GeoError error) =>
            frame.TryWgs84ToLocal(coordinate, out localPosition, out error);

        /// <summary>将 Root 局部双精度位置反算为 WGS84 椭球高，并检查范围。</summary>
        public bool TryLocalToWgs84(in GeoVector3d localPosition, out Wgs84Coordinate coordinate, out GeoError error) =>
            frame.TryLocalToWgs84(localPosition, out coordinate, out error);

        /// <summary>获取 Root 局部参考东、北、上单位方向；不要求 Root 世界矩阵合法。</summary>
        public bool TryGetLocalAxes(out GeoAxes3d axes, out GeoError error)
        {
            axes = frame.LocalAxes;
            error = default;
            return true;
        }

        /// <summary>获取捕获映射的世界参考东、北、上单位方向，不是逐点真北。</summary>
        public bool TryGetWorldAxes(out GeoWorldAxes axes, out GeoError error)
        {
            axes = WorldAxes;
            error = default;
            return true;
        }

        /// <summary>标准化明确来源坐标并转换为世界位置；失败输出不可用，Root 无效时不回退到零坐标。</summary>
        public bool TryGeoToWorld(in GeoCoordinate coordinate, out Vector3 worldPosition, out GeoError error)
        {
            worldPosition = default;
            return TryGeoToLocal(coordinate, out var local, out error) &&
                TryToWorld(local, out worldPosition, out error);
        }

        /// <summary>把 WGS84 椭球高转换为世界位置，包含标定比例及 Root 世界姿态。</summary>
        public bool TryWgs84ToWorld(in Wgs84Coordinate coordinate, out Vector3 worldPosition, out GeoError error)
        {
            worldPosition = default;
            return frame.TryWgs84ToLocal(coordinate, out var local, out error) &&
                TryToWorld(local, out worldPosition, out error);
        }

        /// <summary>把世界位置反算为 WGS84 经纬度和绝对椭球高，并检查有效范围。</summary>
        public bool TryWorldToWgs84(Vector3 worldPosition, out Wgs84Coordinate coordinate, out GeoError error) =>
            frame.TryLocalToWgs84(root.ToLocal(GeoUnity.D(worldPosition)), out coordinate, out error);

        /// <summary>把世界位置转换为指定水平和高度参考；范围/高程模型不支持时明确失败。</summary>
        public bool TryWorldToCoordinate(
            Vector3 worldPosition,
            in GeoCoordinateReference outputReference,
            out GeoCoordinate coordinate,
            out GeoError error)
        {
            coordinate = default;
            return TryWorldToWgs84(worldPosition, out var wgs, out error) &&
                converter.TryFromWgs84(wgs, outputReference, out coordinate, out error);
        }

        /// <summary>把地理坐标及 ENU 现实米偏移转换为世界位置，最终偏移后仍检查范围。</summary>
        public bool TryGetWorldPosition(in GeoPlacement placement, out Vector3 worldPosition, out GeoError error)
        {
            worldPosition = default;
            if (!TryGeoToLocal(placement.Coordinate, out var local, out error) ||
                !frame.TryMetersOffsetToLocal(placement.Offset, out var delta, out error))
            {
                return false;
            }

            // 偏移终点也要检查范围，不能用一个范围内的 GPS 加大偏移绕过限制。
            local += delta;
            if (!frame.CheckRange(frame.ToEngine(local), out error))
            {
                return false;
            }

            return TryToWorld(local, out worldPosition, out error);
        }

        /// <summary>把东、北、上现实米偏移转换为世界位移，不包含位置平移项。</summary>
        public bool TryMetersOffsetToWorld(in GeoEnuOffset offset, out Vector3 worldDisplacement, out GeoError error)
        {
            worldDisplacement = default;
            if (!frame.TryMetersOffsetToLocal(offset, out var local, out error))
            {
                return false;
            }

            var d = GeoUnity.F(root.Rotation.Apply(local) * root.Scale);
            if (!GeoUnity.Finite(d))
            {
                return GeoError.Fail(GeoErrorCode.InvalidNumber, "World displacement overflow.", out error);
            }

            worldDisplacement = d;
            return true;
        }

        /// <summary>将有限非负现实米长度换算为世界单位，包含 Root 世界统一缩放。</summary>
        public bool TryMetersToWorldUnits(double meters, out double worldUnits, out GeoError error) =>
            Length(meters, WorldUnitsPerMeter, out worldUnits, out error);

        /// <summary>将有限非负世界长度换算为现实米。</summary>
        public bool TryWorldUnitsToMeters(double worldUnits, out double meters, out GeoError error) =>
            Length(worldUnits, 1 / WorldUnitsPerMeter, out meters, out error);
        static bool Length(double input, double scale, out double result, out GeoError error)
        {
            result = 0;
            error = default;
            if (!GeoMath.Finite(input) || input < 0 || !GeoMath.Finite(input * scale))
            {
                return GeoError.Fail(GeoErrorCode.InvalidNumber, "Length must be finite and nonnegative.", out error);
            }

            result = input * scale;
            return true;
        }

        bool TryToWorld(GeoVector3d local, out Vector3 world, out GeoError error)
        {
            world = GeoUnity.F(root.ToWorld(local));
            error = default;
            if (GeoUnity.Finite(world))
            {
                return true;
            }

            world = default;
            return GeoError.Fail(GeoErrorCode.InvalidNumber, "World position overflow.", out error);
        }

        /// <summary>将世界方向投影到地理水平面并求北向东顺时针航向；纯竖直方向失败。</summary>
        public bool TryWorldDirectionToHeading(Vector3 worldForward, out GeoHeading heading, out GeoError error)
        {
            heading = default;
            error = default;
            if (!GeoUnity.Finite(worldForward))
            {
                return GeoError.Fail(GeoErrorCode.InvalidNumber, "Direction must be finite.", out error);
            }

            var d = GeoUnity.D(worldForward).Normalized;
            double e = GeoVector3d.Dot(d, GeoUnity.D(WorldAxes.East)), n = GeoVector3d.Dot(d, GeoUnity.D(WorldAxes.North));
            if (e * e + n * n < 1e-12)
            {
                return GeoError.Fail(
                    GeoErrorCode.DegenerateLookDirection,
                    "Direction has no stable horizontal heading.",
                    out error);
            }

            heading = GeoHeading.FromDegrees(Math.Atan2(e, n) / GeoMath.Deg);
            return true;
        }

        /// <summary>从源世界位置求地理朝向及 yaw→pitchUp→rollClockwise 偏角旋转；重合或近竖直不稳定方向失败。</summary>
        public bool TryGetWorldRotation(
            Vector3 sourceWorldPosition,
            in GeoOrientationRequest request,
            out Quaternion rotation,
            out GeoError error)
        {
            rotation = default;
            error = default;
            if (!GeoUnity.Finite(sourceWorldPosition))
            {
                return GeoError.Fail(GeoErrorCode.InvalidNumber, "Source must be finite.", out error);
            }

            Vector3 forward;
            if (request.Kind == GeoOrientationKind.Heading)
            {
                double a = request.Heading.Degrees * GeoMath.Deg;
                forward = WorldAxes.North * (float)Math.Cos(a) + WorldAxes.East * (float)Math.Sin(a);
            }
            else if (request.Kind == GeoOrientationKind.GeoTarget || request.Kind == GeoOrientationKind.WorldTarget)
            {
                var target = request.TargetWorld;
                if (request.Kind == GeoOrientationKind.GeoTarget &&
                    !TryGeoToWorld(request.TargetGeo, out target, out error))
                {
                    return false;
                }

                var delta = GeoUnity.D(target) - GeoUnity.D(sourceWorldPosition);
                if (request.LookMode == GeoLookMode.Horizontal)
                {
                    var up = GeoUnity.D(WorldAxes.Up);
                    delta -= up * GeoVector3d.Dot(delta, up);
                }

                if (delta.Magnitude / WorldUnitsPerMeter < 1e-6)
                {
                    return GeoError.Fail(
                        GeoErrorCode.DegenerateLookDirection,
                        "Target is coincident or has no horizontal direction.",
                        out error);
                }

                forward = GeoUnity.F(delta.Normalized);
            }
            else
            {
                return GeoError.Fail(GeoErrorCode.InvalidOrientationRequest, "Specify an orientation request.", out error);
            }

            if (!GeoUnity.Finite(forward) || Math.Abs(Vector3.Dot(forward.normalized, WorldAxes.Up)) > 1 - 1e-6)
            {
                return GeoError.Fail(GeoErrorCode.DegenerateLookDirection, "Forward is parallel to reference Up.", out error);
            }

            var offset = request.Offset;
            // 基础姿态后按局部 yaw、pitch、roll 叠加；Unity X/Z 正向需取反才能表达抬头/顺时针为正。
            rotation = Quaternion.LookRotation(forward, WorldAxes.Up)
                * Quaternion.AngleAxis((float)(offset.YawDegrees % 360), Vector3.up)
                * Quaternion.AngleAxis((float)(-offset.PitchUpDegrees % 360), Vector3.right)
                * Quaternion.AngleAxis((float)(-offset.RollClockwiseDegrees % 360), Vector3.forward);
            return true;
        }

        /// <summary>在同一捕获映射中计算位置和旋转；任一步失败不返回可用 Pose。</summary>
        public bool TryGetWorldPose(
            in GeoPlacement placement,
            in GeoOrientationRequest orientation,
            out Pose pose,
            out GeoError error)
        {
            pose = default;
            if (!TryGetWorldPosition(placement, out var position, out error) ||
                !TryGetWorldRotation(position, orientation, out var rotation, out error))
            {
                return false;
            }

            pose = new Pose(position, rotation);
            return true;
        }
    }
}
