using System;

namespace SceneGeography
{
    /// <summary>转换算法版本、数值收敛条件与支持范围说明；不是测绘精度声明。</summary>
    public sealed class GeoConversionMetadata
    {
        /// <summary>当前水平转换实现的标识。</summary>
        public string ProviderId => "SceneGeography.Coordtransform";

        /// <summary>当前水平转换算法版本。</summary>
        public string AlgorithmVersion => "1.0 (coordtransform forward; bounded iterative inverse)";

        /// <summary>逆转换的数值收敛阈值，单位米，不是数据精度。</summary>
        public double InverseToleranceMeters => 0.01;

        /// <summary>逆转换允许的最大迭代次数。</summary>
        public int MaximumIterations => 20;

        /// <summary>支持范围及限制说明；数值矩形不是行政边界。</summary>
        public string CoverageDescription => "Declared GCJ/BD input only; numerical envelope 73.66<E<135.05, 3.86<N<53.55. Not an administrative boundary or an accuracy guarantee.";
    }

    /// <summary>将明确来源坐标标准化为 WGS84 椭球高或反向转换；GCJ/BD 范围外及缺高程模型时明确失败。</summary>
    public sealed class GeoCoordinateConverter
    {
        /// <summary>当前算法元数据。</summary>
        public GeoConversionMetadata Metadata { get; } = new GeoConversionMetadata();

        /// <summary>创建支持 WGS84、GCJ-02 和 BD-09 的默认坐标转换器。</summary>
        public static GeoCoordinateConverter CreateDefault() => new GeoCoordinateConverter();

        /// <summary>纯数值转换水平类型及高度参考，不需要任何场景、Root 或标定。</summary>
        public bool TryConvert(
            in GeoCoordinate input,
            in GeoCoordinateReference outputReference,
            out GeoCoordinate output,
            out GeoError error)
        {
            output = default;
            return TryToWgs84(input, out var wgs, out error) &&
                TryFromWgs84(wgs, outputReference, out output, out error);
        }

        /// <summary>标准化外部水平与高度参考为 WGS84 椭球高；失败返回具体错误，不静默透传。</summary>
        public bool TryToWgs84(in GeoCoordinate input, out Wgs84Coordinate output, out GeoError error)
        {
            output = default;
            if (!input.TryValidate(out error))
            {
                return false;
            }

            var height = input.Reference.HeightReference;
            if (height.Kind == GeoHeightKind.Orthometric)
            {
                return GeoError.Fail(
                    GeoErrorCode.MissingHeightModel,
                    "No geoid model is installed; orthometric height cannot be treated as ellipsoidal.",
                    out error);
            }

            double lat = input.LatitudeDegrees, lon = input.LongitudeDegrees;
            if (input.Reference.HorizontalType != GeoCoordinateType.Wgs84)
            {
                if (!HorizontalConversionBackend.InEnvelope(lat, lon))
                {
                    return GeoError.Fail(
                        GeoErrorCode.UnsupportedCoordinateRegion,
                        "Outside the declared GCJ/BD numerical coverage envelope.",
                        out error);
                }

                if (input.Reference.HorizontalType == GeoCoordinateType.Bd09 &&
                    !HorizontalConversionBackend.TryInverse(lat, lon, true, out lat, out lon))
                {
                    return GeoError.Fail(GeoErrorCode.ConversionDidNotConverge, "BD-09 inverse did not converge.", out error);
                }

                if (!HorizontalConversionBackend.TryInverse(lat, lon, false, out lat, out lon))
                {
                    return GeoError.Fail(GeoErrorCode.ConversionDidNotConverge, "GCJ-02 inverse did not converge.", out error);
                }

                if (!HorizontalConversionBackend.InEnvelope(lat, lon))
                {
                    return GeoError.Fail(
                        GeoErrorCode.UnsupportedCoordinateRegion,
                        "Recovered WGS84 point is outside supported conversion coverage.",
                        out error);
                }
            }

            double h = input.HeightMeters + (height.Kind == GeoHeightKind.RelativeToEllipsoidZero ? height.EllipsoidZeroMeters : 0);
            return Wgs84Coordinate.TryCreate(lat, lon, h, out output, out error);
        }

        /// <summary>将 WGS84 椭球高转换到明确的目标水平和高度参考；范围或模型不支持时失败。</summary>
        public bool TryFromWgs84(
            in Wgs84Coordinate input,
            in GeoCoordinateReference outputReference,
            out GeoCoordinate output,
            out GeoError error)
        {
            output = default;
            if (!outputReference.TryValidate(out error) ||
                !GeoMath.ValidateCoordinate(input.LatitudeDegrees, input.LongitudeDegrees, input.EllipsoidHeightMeters, out error))
            {
                return false;
            }

            if (outputReference.HeightReference.Kind == GeoHeightKind.Orthometric)
            {
                return GeoError.Fail(GeoErrorCode.MissingHeightModel, "No geoid model is installed.", out error);
            }

            double lat = input.LatitudeDegrees, lon = input.LongitudeDegrees;
            if (outputReference.HorizontalType != GeoCoordinateType.Wgs84)
            {
                if (!HorizontalConversionBackend.InEnvelope(lat, lon))
                {
                    return GeoError.Fail(
                        GeoErrorCode.UnsupportedCoordinateRegion,
                        "Outside the GCJ/BD numerical coverage envelope.",
                        out error);
                }

                HorizontalConversionBackend.ForwardGcj(lat, lon, out lat, out lon);
                if (outputReference.HorizontalType == GeoCoordinateType.Bd09)
                {
                    HorizontalConversionBackend.ForwardBd(lat, lon, out lat, out lon);
                }

                if (!HorizontalConversionBackend.InEnvelope(lat, lon))
                {
                    return GeoError.Fail(
                        GeoErrorCode.UnsupportedCoordinateRegion,
                        "Converted point is outside supported input coverage.",
                        out error);
                }
            }

            double h = input.EllipsoidHeightMeters - (outputReference.HeightReference.Kind == GeoHeightKind.RelativeToEllipsoidZero
                ? outputReference.HeightReference.EllipsoidZeroMeters
                : 0);
            return GeoCoordinate.TryCreate(lat, lon, h, outputReference, out output, out error);
        }
    }

    // Forward formula adapted from wandergis/coordtransform (MIT); see ThirdParty/coordtransform-LICENSE.txt.
    // The envelope is only a numerical domain. The caller explicitly declares GCJ/BD provenance.
    internal static class HorizontalConversionBackend
    {
        const double A = 6378245.0, E2 = 0.00669342162296594323;
        const double Xpi = Math.PI * 3000 / 180;
        internal static bool InEnvelope(double lat, double lon) => lat > 3.86 && lat < 53.55 && lon > 73.66 && lon < 135.05;
        internal static void ForwardGcj(double lat, double lon, out double outLat, out double outLon)
        {
            double x = lon - 105, y = lat - 35;
            double dy = -100 + 2 * x + 3 * y + .2 * y * y + .1 * x * y + .2 * Math.Sqrt(Math.Abs(x));
            dy += (20 * Math.Sin(6 * x * Math.PI) + 20 * Math.Sin(2 * x * Math.PI)) * 2 / 3;
            dy += (20 * Math.Sin(y * Math.PI) + 40 * Math.Sin(y * Math.PI / 3)) * 2 / 3;
            dy += (160 * Math.Sin(y * Math.PI / 12) + 320 * Math.Sin(y * Math.PI / 30)) * 2 / 3;
            double dx = 300 + x + 2 * y + .1 * x * x + .1 * x * y + .1 * Math.Sqrt(Math.Abs(x));
            dx += (20 * Math.Sin(6 * x * Math.PI) + 20 * Math.Sin(2 * x * Math.PI)) * 2 / 3;
            dx += (20 * Math.Sin(x * Math.PI) + 40 * Math.Sin(x * Math.PI / 3)) * 2 / 3;
            dx += (150 * Math.Sin(x * Math.PI / 12) + 300 * Math.Sin(x * Math.PI / 30)) * 2 / 3;
            double r = lat * GeoMath.Deg, sin = Math.Sin(r), magic = 1 - E2 * sin * sin, sqrt = Math.Sqrt(magic);
            outLat = lat + dy * 180 / (A * (1 - E2) / (magic * sqrt) * Math.PI);
            outLon = lon + dx * 180 / (A / sqrt * Math.Cos(r) * Math.PI);
        }

        internal static void ForwardBd(double lat, double lon, out double outLat, out double outLon)
        {
            double z = Math.Sqrt(lon * lon + lat * lat) + .00002 * Math.Sin(lat * Xpi);
            double theta = Math.Atan2(lat, lon) + .000003 * Math.Cos(lon * Xpi);
            outLat = z * Math.Sin(theta) + .006;
            outLon = z * Math.Cos(theta) + .0065;
        }

        internal static bool TryInverse(double lat, double lon, bool bd, out double outLat, out double outLon)
        {
            outLat = lat - (bd ? .006 : 0);
            outLon = lon - (bd ? .0065 : 0);
            for (int i = 0; i < 20; i++)
            {
                double a, b;
                if (bd)
                {
                    ForwardBd(outLat, outLon, out a, out b);
                }
                else
                {
                    ForwardGcj(outLat, outLon, out a, out b);
                }

                double dy = a - lat, dx = b - lon;
                double metersY = dy * 111320, metersX = dx * 111320 * Math.Cos(lat * GeoMath.Deg);
                if (metersX * metersX + metersY * metersY <= .0001)
                {
                    return true;
                }

                outLat -= dy;
                outLon -= dx;
            }

            return false;
        }
    }

    internal static class Wgs84Geodesy
    {
        internal const double A = 6378137, F = 1 / 298.257223563;
        internal const double E2 = F * (2 - F);
        internal static GeoVector3d ToEcef(in Wgs84Coordinate point)
        {
            double lat = point.LatitudeDegrees * GeoMath.Deg, lon = point.LongitudeDegrees * GeoMath.Deg;
            double sin = Math.Sin(lat), cos = Math.Cos(lat), n = A / Math.Sqrt(1 - E2 * sin * sin), h = point.EllipsoidHeightMeters;
            return new GeoVector3d((n + h) * cos * Math.Cos(lon), (n + h) * cos * Math.Sin(lon), (n * (1 - E2) + h) * sin);
        }

        internal static bool TryFromEcef(GeoVector3d p, out Wgs84Coordinate point, out GeoError error)
        {
            point = default;
            error = default;
            if (!p.IsFinite || !GeoMath.Finite(p.SqrMagnitude))
            {
                return GeoError.Fail(GeoErrorCode.InvalidNumber, "Non-finite ECEF position.", out error);
            }

            double radius = Math.Sqrt(p.X * p.X + p.Y * p.Y), b = A * (1 - F);
            if (p.Magnitude < 1)
            {
                return GeoError.Fail(
                    GeoErrorCode.UndefinedGeodeticPosition,
                    "Geodetic position is undefined near the Earth center.",
                    out error);
            }

            if (radius < 1e-8)
            {
                return Wgs84Coordinate.TryCreate(p.Z >= 0 ? 90 : -90, 0, Math.Abs(p.Z) - b, out point, out error);
            }

            double lon = Math.Atan2(p.Y, p.X), lat = Math.Atan2(p.Z, radius * (1 - E2));
            for (int i = 0; i < 30; i++)
            {
                double sin = Math.Sin(lat), n = A / Math.Sqrt(1 - E2 * sin * sin);
                double next = Math.Atan2(p.Z + E2 * n * sin, radius);
                if (Math.Abs(next - lat) < 1e-14)
                {
                    lat = next;
                    sin = Math.Sin(lat);
                    n = A / Math.Sqrt(1 - E2 * sin * sin);
                    double h = radius * Math.Cos(lat) + p.Z * sin - n * (1 - E2 * sin * sin);
                    return Wgs84Coordinate.TryCreate(lat / GeoMath.Deg, lon / GeoMath.Deg, h, out point, out error);
                }

                lat = next;
            }

            return GeoError.Fail(GeoErrorCode.ConversionDidNotConverge, "ECEF inverse did not converge.", out error);
        }
    }

    internal sealed class EnuTangentFrame
    {
        readonly GeoVector3d origin, east, north, up;
        internal Wgs84Coordinate Origin { get; }

        internal EnuTangentFrame(Wgs84Coordinate point)
        {
            Origin = point;
            origin = Wgs84Geodesy.ToEcef(point);
            double lat = point.LatitudeDegrees * GeoMath.Deg, lon = point.LongitudeDegrees * GeoMath.Deg;
            east = new GeoVector3d(-Math.Sin(lon), Math.Cos(lon), 0);
            north = new GeoVector3d(-Math.Sin(lat) * Math.Cos(lon), -Math.Sin(lat) * Math.Sin(lon), Math.Cos(lat));
            up = new GeoVector3d(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
        }

        internal GeoVector3d ToEngine(in Wgs84Coordinate point)
        {
            // 只排列 ENU 分量为 Unity 的 (East,Up,North)，不是重新定义地理轴。
            // 基于 ECEF 差值保留地球曲率，不能替换为经纬度固定线性缩放。
            var d = Wgs84Geodesy.ToEcef(point) - origin;
            return new GeoVector3d(GeoVector3d.Dot(east, d), GeoVector3d.Dot(up, d), GeoVector3d.Dot(north, d));
        }

        internal bool TryFromEngine(GeoVector3d q, out Wgs84Coordinate point, out GeoError error) =>
            Wgs84Geodesy.TryFromEcef(origin + east * q.X + up * q.Y + north * q.Z, out point, out error);
    }
}
