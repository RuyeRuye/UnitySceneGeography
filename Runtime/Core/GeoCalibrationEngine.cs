using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SceneGeography
{
    /// <summary>独立于 Unity 的三模式标定求解器；只计算，不移动场景物体。</summary>
    public static class GeoCalibrationEngine
    {
        /// <summary>计算正向旋转、正统一比例和平移并检查 Fit/Check 阈值；失败返回报告和错误，不产生可用解。</summary>
        public static bool TrySolve(
            GeoCalibrationRequest request,
            GeoCoordinateConverter converter,
            out GeoCalibrationSolution solution,
            out GeoCalibrationReport report,
            out GeoError error)
        {
            solution = null;
            error = default;
            report = new GeoCalibrationReport
            {
                Mode = request?.Mode ?? default,
                ConversionMetadata = converter?.Metadata
            };
            if (request == null || converter == null)
            {
                return Fail(
                    report,
                    GeoErrorCode.InvalidConfiguration,
                    "Request and converter are required.",
                    out error);
            }

            var options = request.Options;
            report.LocalFrameId = options.ExpectedLocalFrameId;
            if (!options.TryValidate(out error))
            {
                report.Failure = error;
                return false;
            }

            var diagnostics = new List<GeoDiagnostic>();
            var fits = new List<GeoControlPoint>();
            var fitGeo = new List<Wgs84Coordinate>();
            var normalized = new Wgs84Coordinate[request.Points.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < request.Points.Count; i++)
            {
                var p = request.Points[i];
                if (p == null)
                {
                    return Fail(report, GeoErrorCode.InvalidControlPoint, "Control point is null.", out error);
                }

                if (!ids.Add(p.Id))
                {
                    return Fail(
                        report,
                        GeoErrorCode.DuplicateControlPointId,
                        "Duplicate control point ID.",
                        out error,
                        p.Id);
                }

                if (!converter.TryToWgs84(p.Coordinate, out normalized[i], out var conversionError))
                {
                    error = new GeoError(conversionError.Code, conversionError.Message, p.Id);
                    report.Failure = error;
                    return false;
                }

                if (p.Role == GeoControlPointRole.Fit)
                {
                    fits.Add(p);
                    fitGeo.Add(normalized[i]);
                }
                else
                {
                    report.CheckCount++;
                }

                if (p.Coordinate.Reference.HeightReference.IsAssumed)
                {
                    diagnostics.Add(new GeoDiagnostic("AssumedHeightZero", "The absolute ellipsoidal height origin is assumed.", p.Id));
                }
            }

            report.FitCount = fits.Count;
            int required = request.Mode == GeoCalibrationMode.SingleAnchor ? 1 : request.Mode == GeoCalibrationMode.Upright ? 2 : 3;
            if (fits.Count < required)
            {
                return Fail(
                    report,
                    GeoErrorCode.InsufficientFitPoints,
                    "Not enough fit points for this mode.",
                    out error);
            }

            if ((options.MaximumCheckRmsMeters.HasValue || options.MaximumCheckResidualMeters.HasValue) &&
                report.CheckCount == 0)
            {
                return Fail(
                    report,
                    GeoErrorCode.CheckPointsRequired,
                    "Check thresholds require independent check points.",
                    out error);
            }

            if (request.Scale.Kind == 0)
            {
                return Fail(report, GeoErrorCode.InvalidConfiguration, "Specify the scale policy.", out error);
            }

            // Check 点完全不参与原点和拟合，避免用验证数据反过来改善待验证结果。
            // 除以最大权重只改变共同因子，保持解不变并降低权重求和溢出的可能。
            var weights = new double[fits.Count];
            double maxWeight = 0;
            foreach (var p in fits)
            {
                maxWeight = Math.Max(maxWeight, p.Weight);
            }

            double total = 0;
            var ecefCenter = GeoVector3d.Zero;
            for (int i = 0; i < fits.Count; i++)
            {
                weights[i] = fits[i].Weight / maxWeight;
                total += weights[i];
                ecefCenter += Wgs84Geodesy.ToEcef(fitGeo[i]) * weights[i];
            }

            // 在 ECEF 中求中心，避免经度跨日期线时直接平均角度得到错误原点。
            Wgs84Coordinate origin;
            if (options.ReferenceOrigin.HasValue)
            {
                origin = options.ReferenceOrigin.Value;
            }
            else if (!Wgs84Geodesy.TryFromEcef(ecefCenter / total, out origin, out error))
            {
                report.Failure = error;
                return false;
            }

            if (!GeoMath.ValidateCoordinate(origin.LatitudeDegrees, origin.LongitudeDegrees, origin.EllipsoidHeightMeters, out error))
            {
                report.Failure = error;
                return false;
            }

            if (Math.Abs(origin.LatitudeDegrees) > 89.9)
            {
                return Fail(
                    report,
                    GeoErrorCode.UndefinedGeodeticPosition,
                    "ENU origin must be within latitude [-89.9,89.9].",
                    out error);
            }

            var tangent = new EnuTangentFrame(origin);
            report.Origin = origin;
            var from = new GeoVector3d[fits.Count];
            var to = new GeoVector3d[fits.Count];
            var fromCenter = GeoVector3d.Zero;
            var toCenter = GeoVector3d.Zero;
            double maxRadius = 0;
            for (int i = 0; i < fits.Count; i++)
            {
                from[i] = tangent.ToEngine(fitGeo[i]);
                to[i] = fits[i].LocalPosition;
                fromCenter += from[i] * (weights[i] / total);
                toCenter += to[i] * (weights[i] / total);
                maxRadius = Math.Max(maxRadius, from[i].Magnitude);
            }

            if (maxRadius > 5000)
            {
                diagnostics.Add(new GeoDiagnostic("FarFromOrigin", "Fit points extend more than 5 km from the ENU origin."));
            }

            Matrix3d rotation = Matrix3d.Identity;
            double scale = request.Scale.Kind == 2 ? request.Scale.Value : 1;
            if (request.Mode == GeoCalibrationMode.SingleAnchor)
            {
                if (!TryUp(request.Up, out var up, out error))
                {
                    report.Failure = error;
                    return false;
                }

                var direction = request.Direction;
                if (!direction.IsFinite || direction.Magnitude < 1e-12)
                {
                    return Fail(
                        report,
                        GeoErrorCode.InvalidHeadingDirection,
                        "Heading direction is zero or invalid.",
                        out error);
                }

                var h = direction - up * GeoVector3d.Dot(direction, up);
                if (h.Magnitude / direction.Magnitude < 1e-8)
                {
                    return Fail(report, GeoErrorCode.InvalidHeadingDirection, "Heading is parallel to Up.", out error);
                }

                if (Math.Abs(GeoVector3d.Dot(direction.Normalized, up)) > Math.Sin(5 * GeoMath.Deg))
                {
                    diagnostics.Add(new GeoDiagnostic("HeadingVectorProjected", "Heading direction was projected by more than 5 degrees."));
                }

                h = h.Normalized;
                double angle = request.Heading.Degrees * GeoMath.Deg;
                var north = h * Math.Cos(angle) - GeoVector3d.Cross(up, h) * Math.Sin(angle);
                var east = GeoVector3d.Cross(up, north).Normalized;
                rotation = new Matrix3d(east, up, north.Normalized);
            }
            else if (request.Mode == GeoCalibrationMode.Upright)
            {
                if (!TryUp(request.Up, out var up, out error))
                {
                    report.Failure = error;
                    return false;
                }

                var seed = Math.Abs(GeoVector3d.Dot(up, GeoVector3d.UnitZ)) < .9 ? GeoVector3d.UnitZ : GeoVector3d.UnitX;
                var north0 = (seed - up * GeoVector3d.Dot(seed, up)).Normalized;
                var east0 = GeoVector3d.Cross(up, north0).Normalized;
                // Upright 只拟合水平 yaw 和比例；高度误差留在报告中，不通过倾斜或单独拉伸 Y 吸收。
                double a = 0, b = 0, variance = 0, maxBaseline = 0;
                for (int i = 0; i < fits.Count; i++)
                {
                    var x = from[i] - fromCenter;
                    var d = to[i] - toCenter;
                    double px = GeoVector3d.Dot(d, east0), pz = GeoVector3d.Dot(d, north0);
                    a += weights[i] * (x.X * px + x.Z * pz);
                    b += weights[i] * (x.Z * px - x.X * pz);
                    variance += weights[i] * (x.X * x.X + x.Z * x.Z);
                    maxBaseline = Math.Max(maxBaseline, Math.Sqrt(x.X * x.X + x.Z * x.Z) * 2);
                }

                double amplitude = Math.Sqrt(a * a + b * b);
                if (maxBaseline < options.MinimumBaselineMeters || variance < 1e-20 || amplitude < 1e-20)
                {
                    return Fail(
                        report,
                        GeoErrorCode.DegenerateBaseline,
                        "A horizontal geographic and model baseline is required.",
                        out error);
                }

                double yaw = Math.Atan2(b, a), c = Math.Cos(yaw), s = Math.Sin(yaw);
                rotation = new Matrix3d(east0 * c - north0 * s, up, east0 * s + north0 * c);
                if (request.Scale.Kind == 1)
                {
                    scale = amplitude / variance;
                }

                if (maxBaseline < 10)
                {
                    diagnostics.Add(new GeoDiagnostic("ShortBaseline", "A longer measured baseline improves heading stability."));
                }
            }
            else
            {
                double radius = 0;
                foreach (var p in from)
                {
                    radius = Math.Max(radius, (p - fromCenter).Magnitude);
                }

                if (radius * 2 < options.MinimumBaselineMeters)
                {
                    return Fail(report, GeoErrorCode.DegenerateBaseline, "Geographic baseline is too short.", out error);
                }

                if (SymmetricEigen.DistributionRatio(from, weights, fromCenter) < options.NearCollinearRatioThreshold ||
                    SymmetricEigen.DistributionRatio(to, weights, toCenter) < options.NearCollinearRatioThreshold)
                {
                    return Fail(
                        report,
                        GeoErrorCode.NearCollinearPoints,
                        "At least two independent spatial directions are required.",
                        out error);
                }

                rotation = SymmetricEigen.FitRotation(from, to, weights, fromCenter, toCenter);
                if (request.Scale.Kind == 1)
                {
                    double numerator = 0, denominator = 0;
                    for (int i = 0; i < fits.Count; i++)
                    {
                        var a = from[i] - fromCenter;
                        var b = to[i] - toCenter;
                        numerator += weights[i] * GeoVector3d.Dot(rotation.Apply(a), b);
                        denominator += weights[i] * a.SqrMagnitude;
                    }

                    scale = numerator / denominator;
                }

                if (fits.Count == 3)
                {
                    diagnostics.Add(new GeoDiagnostic(
                            "MinimalSpatialFit",
                            "Three points cannot independently verify registration or all reflection errors."));
                }
            }

            if (!GeoMath.Finite(scale) || scale < options.MinimumScale || scale > options.MaximumScale)
            {
                return Fail(
                    report,
                    GeoErrorCode.ScaleOutOfRange,
                    "Estimated scale is outside configured limits.",
                    out error);
            }

            var translation = toCenter - rotation.Apply(fromCenter) * scale;
            if (!translation.IsFinite)
            {
                return Fail(report, GeoErrorCode.InvalidNumber, "Calibration overflow.", out error);
            }

            var frame = new GeoReferenceFrame(tangent, rotation, translation, scale, options.MaximumDistanceFromOriginMeters);
            report.LocalUnitsPerMeter = scale;
            report.LocalAxes = frame.LocalAxes;
            var residuals = new List<GeoControlPointResidual>();
            for (int i = 0; i < request.Points.Count; i++)
            {
                var p = request.Points[i];
                if (!frame.TryWgs84ToLocal(normalized[i], out var predicted, out error))
                {
                    report.Failure = error;
                    return false;
                }

                // 局部误差先去旋转再除比例，回到 (East,Up,North) 现实米制，避免模型比例污染验收阈值。
                var e = rotation.Inverse(p.LocalPosition - predicted) / scale;
                if (!e.IsFinite || !GeoMath.Finite(e.SqrMagnitude))
                {
                    return Fail(report, GeoErrorCode.InvalidNumber, "Residual overflow.", out error, p.Id);
                }

                residuals.Add(new GeoControlPointResidual(p, normalized[i], predicted, e));
            }

            report.Residuals = residuals.AsReadOnly();
            report.FitMetrics = Metrics(residuals, GeoControlPointRole.Fit);
            if (report.CheckCount > 0)
            {
                report.CheckMetrics = Metrics(residuals, GeoControlPointRole.Check);
            }

            if (!options.MaximumFitRmsMeters.HasValue && !options.MaximumFitVerticalRmsMeters.HasValue &&
                !options.MaximumCheckRmsMeters.HasValue &&
                !options.MaximumCheckResidualMeters.HasValue)
            {
                diagnostics.Add(new GeoDiagnostic(
                        "AccuracyThresholdNotConfigured",
                        "No application accuracy acceptance threshold was specified."));
            }

            report.Diagnostics = diagnostics.AsReadOnly();
            report.DataFingerprint = Fingerprint(request, converter.Metadata);
            var fm = report.FitMetrics.Value;
            var cm = report.CheckMetrics;
            if (Exceeded(fm.SpatialRmsMeters, options.MaximumFitRmsMeters) ||
                Exceeded(fm.VerticalRmsMeters, options.MaximumFitVerticalRmsMeters) ||
                (cm.HasValue &&
                (Exceeded(cm.Value.SpatialRmsMeters, options.MaximumCheckRmsMeters) ||
                Exceeded(cm.Value.MaximumResidualMeters, options.MaximumCheckResidualMeters))))
            {
                return Fail(
                    report,
                    GeoErrorCode.ResidualLimitExceeded,
                    "Calibration does not meet configured fit/check error limits.",
                    out error);
            }

            report.Success = true;
            solution = new GeoCalibrationSolution(frame, report);
            return true;
        }

        static bool TryUp(GeoVector3d input, out GeoVector3d up, out GeoError error)
        {
            up = input.Normalized;
            error = default;
            return input.IsFinite && GeoMath.Finite(input.SqrMagnitude) && input.Magnitude > 1e-12 ||
                GeoError.Fail(GeoErrorCode.InvalidUp, "Up must be finite and nonzero.", out error);
        }

        static bool Exceeded(double value, double? limit) => limit.HasValue && value > limit.Value;
        static bool Fail(
            GeoCalibrationReport report,
            GeoErrorCode code,
            string message,
            out GeoError error,
            string id = null)
        {
            error = new GeoError(code, message, id);
            report.Failure = error;
            return false;
        }

        static GeoResidualStatistics Metrics(List<GeoControlPointResidual> points, GeoControlPointRole role)
        {
            double mw = 0;
            foreach (var p in points)
            {
                if (p.Role == role)
                {
                    mw = Math.Max(mw, p.Weight);
                }
            }

            double h = 0, v = 0, sum = 0, max = 0;
            foreach (var p in points)
            {
                if (p.Role == role)
                {
                    var e = p.EnuErrorMeters;
                    double a = e.EastMeters * e.EastMeters + e.NorthMeters * e.NorthMeters, b = e.UpMeters * e.UpMeters, w = p.Weight / mw;
                    sum += w;
                    h += w * a;
                    v += w * b;
                    max = Math.Max(max, Math.Sqrt(a + b));
                }
            }

            return new GeoResidualStatistics(Math.Sqrt(h / sum), Math.Sqrt(v / sum), Math.Sqrt((h + v) / sum), max);
        }

        static string Fingerprint(GeoCalibrationRequest request, GeoConversionMetadata metadata)
        {
            var s = new StringBuilder();
            Action<object> add = value =>
            {
                string text = value is double number ? number.ToString("R", CultureInfo.InvariantCulture) : value is IFormattable formatted ? formatted.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "<null>";
                s.Append(text.Length).Append(':').Append(text);
            };
            add(request.Mode);
            add(metadata.ProviderId);
            add(metadata.AlgorithmVersion);
            add(request.Up.X);
            add(request.Up.Y);
            add(request.Up.Z);
            add(request.Direction.X);
            add(request.Direction.Y);
            add(request.Direction.Z);
            add(request.Heading.Degrees);
            add(request.Scale.Kind);
            add(request.Scale.Value);
            var options = request.Options;
            add(options.ExpectedLocalFrameId);
            add(options.ReferenceOrigin.HasValue);
            if (options.ReferenceOrigin.HasValue)
            {
                var origin = options.ReferenceOrigin.Value;
                add(origin.LatitudeDegrees);
                add(origin.LongitudeDegrees);
                add(origin.EllipsoidHeightMeters);
            }

            add(options.MinimumBaselineMeters);
            add(options.NearCollinearRatioThreshold);
            add(options.MinimumScale);
            add(options.MaximumScale);
            add(options.MaximumFitRmsMeters);
            add(options.MaximumFitVerticalRmsMeters);
            add(options.MaximumCheckRmsMeters);
            add(options.MaximumCheckResidualMeters);
            add(options.MaximumDistanceFromOriginMeters);
            foreach (var p in request.Points)
            {
                add(p.Id);
                add(p.Role);
                add(p.LocalPosition.X);
                add(p.LocalPosition.Y);
                add(p.LocalPosition.Z);
                add(p.Coordinate.LatitudeDegrees);
                add(p.Coordinate.LongitudeDegrees);
                add(p.Coordinate.HeightMeters);
                add(p.Weight);
                var reference = p.Coordinate.Reference;
                var height = reference.HeightReference;
                add(reference.HorizontalType);
                add(height.Kind);
                add(height.EllipsoidZeroMeters);
                add(height.ModelId);
                add(height.IsAssumed);
            }

            ulong hash = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(s.ToString()))
            {
                hash = unchecked((hash ^ b) * 1099511628211UL);
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
