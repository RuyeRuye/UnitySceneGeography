using System;
using UnityEngine;

namespace SceneGeography
{
    /// <summary>不可变地理放置请求，由明确参考的坐标与固定 ENU 现实米偏移组成。</summary>
    public readonly struct GeoPlacement
    {
        /// <summary>带明确水平和高度参考的地理坐标。</summary>
        public GeoCoordinate Coordinate { get; }

        /// <summary>东、北、上的现实米偏移，不是 Unity 世界轴偏移。</summary>
        public GeoEnuOffset Offset { get; }

        /// <summary>不可变地理放置请求，由明确参考的坐标与固定 ENU 现实米偏移组成。</summary>
        public GeoPlacement(GeoCoordinate coordinate, GeoEnuOffset offset = default)
        {
            if (!coordinate.TryValidate(out var error))
            {
                throw new ArgumentException(error.Message);
            }

            if (!offset.EngineVector.IsFinite)
            {
                throw new ArgumentException("Offset must be finite.");
            }

            Coordinate = coordinate;
            Offset = offset;
        }
    }

    /// <summary>Horizontal 只求水平朝向；Full3D 保留目标高度差。</summary>
    public enum GeoLookMode
    {
        Horizontal,
        Full3D
    }

    /// <summary>固定航向、地理目标或固定世界目标；Unspecified 无效。</summary>
    public enum GeoOrientationKind
    {
        Unspecified,
        Heading,
        GeoTarget,
        WorldTarget
    }

    /// <summary>相对基础朝向的角度偏移；yaw 右转、pitch 抬头、roll 顺时针为正。</summary>
    public readonly struct GeoRotationOffset
    {
        /// <summary>相对基础朝向向右的偏航角，单位度。</summary>
        public double YawDegrees { get; }

        /// <summary>相对基础朝向的抬头角，单位度。</summary>
        public double PitchUpDegrees { get; }

        /// <summary>相对基础朝向沿前向轴的顺时针侧倾角，单位度。</summary>
        public double RollClockwiseDegrees { get; }

        /// <summary>相对基础朝向的角度偏移；yaw 右转、pitch 抬头、roll 顺时针为正。</summary>
        public GeoRotationOffset(double yawDegrees, double pitchUpDegrees, double rollClockwiseDegrees)
        {
            GeoMath.RequireFinite(yawDegrees, nameof(yawDegrees));
            GeoMath.RequireFinite(pitchUpDegrees, nameof(pitchUpDegrees));
            GeoMath.RequireFinite(rollClockwiseDegrees, nameof(rollClockwiseDegrees));
            YawDegrees = yawDegrees;
            PitchUpDegrees = pitchUpDegrees;
            RollClockwiseDegrees = rollClockwiseDegrees;
        }
    }

    /// <summary>不可变地理朝向请求；通过工厂显式选择来源，default 无效。</summary>
    public readonly struct GeoOrientationRequest
    {
        /// <summary>朝向请求的来源：固定航向、地理目标或世界位置目标。</summary>
        public GeoOrientationKind Kind { get; }
        internal GeoHeading Heading { get; }
        internal GeoCoordinate TargetGeo { get; }
        internal Vector3 TargetWorld { get; }
        internal GeoLookMode LookMode { get; }
        internal GeoRotationOffset Offset { get; }

        GeoOrientationRequest(
            GeoOrientationKind kind,
            GeoHeading heading,
            GeoCoordinate geo,
            Vector3 world,
            GeoLookMode mode,
            GeoRotationOffset offset)
        {
            Kind = kind;
            Heading = heading;
            TargetGeo = geo;
            TargetWorld = world;
            LookMode = mode;
            Offset = offset;
        }

        /// <summary>创建固定地理航向及相对偏角请求，北 0、东 90 度。</summary>
        public static GeoOrientationRequest ForHeading(GeoHeading heading, GeoRotationOffset offset = default) =>
            new GeoOrientationRequest(GeoOrientationKind.Heading, heading, default, default, default, offset);

        /// <summary>创建看向明确参考地理目标的请求，可选择水平或完整三维朝向。</summary>
        public static GeoOrientationRequest ForGeoTarget(GeoCoordinate target, GeoLookMode mode, GeoRotationOffset offset = default)
        {
            if (!target.TryValidate(out var e))
            {
                throw new ArgumentException(e.Message);
            }

            ValidateMode(mode);
            return new GeoOrientationRequest(GeoOrientationKind.GeoTarget, default, target, default, mode, offset);
        }

        /// <summary>创建看向固定世界位置的请求；追踪 Transform 请使用 GeoOrientation.SetTransformTarget。</summary>
        public static GeoOrientationRequest ForWorldTarget(Vector3 target, GeoLookMode mode, GeoRotationOffset offset = default)
        {
            if (!GeoUnity.Finite(target))
            {
                throw new ArgumentException("Target must be finite.");
            }

            ValidateMode(mode);
            return new GeoOrientationRequest(GeoOrientationKind.WorldTarget, default, default, target, mode, offset);
        }

        static void ValidateMode(GeoLookMode mode)
        {
            if (mode != GeoLookMode.Horizontal && mode != GeoLookMode.Full3D)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
    }

    /// <summary>世界空间中的参考东、北、上单位方向，不包含比例缩放。</summary>
    public readonly struct GeoWorldAxes
    {
        /// <summary>东向单位方向；具体为局部空间或世界空间由所属类型决定。</summary>
        public Vector3 East { get; }

        /// <summary>北向单位方向，不是逐点重新计算的真北。</summary>
        public Vector3 North { get; }

        /// <summary>参考 Up 单位方向，不强制等于世界 Y。</summary>
        public Vector3 Up { get; }

        internal GeoWorldAxes(Vector3 east, Vector3 north, Vector3 up)
        {
            East = east;
            North = north;
            Up = up;
        }

        /// <summary>获取东、西、南、北、上、下单位方向；无效枚举抛出参数异常。</summary>
        public Vector3 GetDirection(GeoCardinalDirection direction)
        {
            switch (direction)
            {
                case GeoCardinalDirection.East:
                    return East;
                case GeoCardinalDirection.West:
                    return -East;
                case GeoCardinalDirection.North:
                    return North;
                case GeoCardinalDirection.South:
                    return -North;
                case GeoCardinalDirection.Up:
                    return Up;
                case GeoCardinalDirection.Down:
                    return -Up;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }
    }

    internal static class GeoUnity
    {
        internal static GeoVector3d D(Vector3 p) => new GeoVector3d(p.x, p.y, p.z);
        internal static Vector3 F(GeoVector3d p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);
        internal static bool Finite(Vector3 v) => GeoMath.Finite(v.x) && GeoMath.Finite(v.y) && GeoMath.Finite(v.z);
        internal static bool Unit(Quaternion q) => GeoMath.Finite(q.x) && GeoMath.Finite(q.y) && GeoMath.Finite(q.z) && GeoMath.Finite(q.w) &&
        Math.Abs((double)q.x * q.x + (double)q.y * q.y + (double)q.z * q.z + (double)q.w * q.w - 1) < 1e-4;
    }

    internal readonly struct GeoRootPose
    {
        internal readonly GeoVector3d Position;
        internal readonly Matrix3d Rotation;
        internal readonly double Scale;
        internal GeoRootPose(GeoVector3d position, Matrix3d rotation, double scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        internal GeoVector3d ToWorld(GeoVector3d local) => Position + Rotation.Apply(local) * Scale;
        internal GeoVector3d ToLocal(GeoVector3d world) => Rotation.Inverse(world - Position) / Scale;
    }

    internal static class GeoRootTransform
    {
        internal static bool TryCapture(
            Transform root,
            bool hasRoot,
            out GeoRootPose pose,
            out Matrix4x4 matrix,
            out GeoError error)
        {
            pose = default;
            matrix = Matrix4x4.identity;
            error = default;
            if (hasRoot && root == null)
            {
                return GeoError.Fail(
                    GeoErrorCode.RootDestroyed,
                    "The configured Root has been destroyed; explicitly reconfigure it.",
                    out error);
            }

            if (hasRoot)
            {
                matrix = root.localToWorldMatrix;
            }

            return TryMatrix(matrix, out pose, out error);
        }

        internal static bool TryMatrix(Matrix4x4 matrix, out GeoRootPose pose, out GeoError error)
        {
            pose = default;
            error = default;
            for (int i = 0; i < 16; i++)
            {
                if (!GeoMath.Finite(matrix[i]))
                {
                    return GeoError.Fail(GeoErrorCode.InvalidRootTransform, "Root matrix must be finite.", out error);
                }
            }

            var x = GeoUnity.D(matrix.GetColumn(0));
            var y = GeoUnity.D(matrix.GetColumn(1));
            var z = GeoUnity.D(matrix.GetColumn(2));
            double sx = x.Magnitude, sy = y.Magnitude, sz = z.Magnitude, s = (sx + sy + sz) / 3;
            if (s <= 1e-8 || Math.Max(Math.Abs(sx - s), Math.Max(Math.Abs(sy - s), Math.Abs(sz - s))) / s > 1e-4)
            {
                return GeoError.Fail(
                    GeoErrorCode.InvalidRootTransform,
                    "Root and all ancestors must compose to uniform nonzero scale.",
                    out error);
            }

            x /= sx;
            y /= sy;
            z /= sz;
            if (Math.Abs(GeoVector3d.Dot(x, y)) > 1e-5 || Math.Abs(GeoVector3d.Dot(x, z)) > 1e-5 ||
                Math.Abs(GeoVector3d.Dot(y, z)) > 1e-5 ||
                GeoVector3d.Dot(GeoVector3d.Cross(x, y), z) < .9999)
            {
                return GeoError.Fail(GeoErrorCode.InvalidRootTransform, "Root cannot contain shear or reflection.", out error);
            }

            // Orthogonalize the accepted floating-point matrix before using transpose as inverse.
            z = z.Normalized;
            x = GeoVector3d.Cross(y, z).Normalized;
            y = GeoVector3d.Cross(z, x).Normalized;
            pose = new GeoRootPose(new GeoVector3d(matrix.m03, matrix.m13, matrix.m23), new Matrix3d(x, y, z), s);
            return true;
        }

        internal static bool Same(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
