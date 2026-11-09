using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SceneGeography
{
    /// <summary>Fit 参与求解；Check 独立验收，不影响拟合和自动原点。</summary>
    public enum GeoControlPointRole
    {
        Fit,
        Check
    }

    /// <summary>SingleAnchor 单点方向；Upright 已知 Up 多点；Spatial 完整三维相似变换。</summary>
    public enum GeoCalibrationMode
    {
        SingleAnchor,
        Upright,
        Spatial
    }

    /// <summary>不可变控制点，将 Root 局部位置与明确参考的现实坐标关联；构造时校验 ID、权重和输入。</summary>
    public sealed class GeoControlPoint
    {
        /// <summary>请求内唯一且非空的控制点标识。</summary>
        public string Id { get; }

        /// <summary>Root 局部位置，单位模型局部单位，而非世界位置。</summary>
        public GeoVector3d LocalPosition { get; }

        /// <summary>带明确水平和高度参考的地理坐标。</summary>
        public GeoCoordinate Coordinate { get; }

        /// <summary>大于零的相对权重；参与加权拟合或同组 RMS，不用于最大残差。</summary>
        public double Weight { get; }

        /// <summary>该点用于 Fit 求解还是 Check 独立验收。</summary>
        public GeoControlPointRole Role { get; }

        /// <summary>不可变控制点，将 Root 局部位置与明确参考的现实坐标关联；构造时校验 ID、权重和输入。</summary>
        public GeoControlPoint(
            string id,
            GeoVector3d localPosition,
            GeoCoordinate coordinate,
            double weight = 1,
            GeoControlPointRole role = GeoControlPointRole.Fit)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Control point ID is required.");
            }

            if (!localPosition.IsFinite || !GeoMath.Finite(weight) || weight <= 0)
            {
                throw new ArgumentException("Invalid control point position or weight.");
            }

            if (role != GeoControlPointRole.Fit && role != GeoControlPointRole.Check)
            {
                throw new ArgumentException("Invalid point role.");
            }

            if (!coordinate.TryValidate(out var error))
            {
                throw new ArgumentException(error.Message);
            }

            Id = id;
            LocalPosition = localPosition;
            Coordinate = coordinate;
            Weight = weight;
            Role = role;
        }
    }

    /// <summary>正统一比例策略；必须显式选择估算或固定，default 未指定。</summary>
    public readonly struct GeoScalePolicy
    {
        internal int Kind { get; }
        internal double Value { get; }

        GeoScalePolicy(int kind, double value)
        {
            Kind = kind;
            Value = value;
        }

        /// <summary>从控制点估算正统一比例。</summary>
        public static GeoScalePolicy Estimate => new GeoScalePolicy(1, 0);

        /// <summary>使用已知正比例，单位 Root 局部单位/现实米；非法比例抛出参数异常。</summary>
        public static GeoScalePolicy Fixed(double localUnitsPerMeter)
        {
            if (!GeoMath.Finite(localUnitsPerMeter) || localUnitsPerMeter <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(localUnitsPerMeter));
            }

            return new GeoScalePolicy(2, localUnitsPerMeter);
        }
    }

    /// <summary>可变的求解与验收选项；设置类和请求会复制选项，之后修改原对象不影响已构造请求。</summary>
    public sealed class GeoCalibrationOptions
    {
        /// <summary>可选固定 ENU 原点，必须为 WGS84 椭球高；为空时由 Fit 点 ECEF 加权中心计算。</summary>
        public Wgs84Coordinate? ReferenceOrigin { get; set; }

        /// <summary>预期模型局部坐标身份；非空时 Unity Reference 要求与其标识一致。</summary>
        public string ExpectedLocalFrameId { get; set; }

        /// <summary>最小地理基线判定阈值，单位现实米。</summary>
        public double MinimumBaselineMeters { get; set; } = .01;

        /// <summary>Spatial 分布近共线判定阈值；越大越严格。</summary>
        public double NearCollinearRatioThreshold { get; set; } = 1e-4;

        /// <summary>允许的最小正比例，单位局部单位/现实米。</summary>
        public double MinimumScale { get; set; } = 1e-6;

        /// <summary>允许的最大正比例，单位局部单位/现实米。</summary>
        public double MaximumScale { get; set; } = 1e6;

        /// <summary>可选 Fit 三维加权 RMS 上限，单位现实米；null 不限制。</summary>
        public double? MaximumFitRmsMeters { get; set; }

        /// <summary>可选 Fit 竖直加权 RMS 上限，单位现实米；null 不限制。</summary>
        public double? MaximumFitVerticalRmsMeters { get; set; }

        /// <summary>可选 Check 三维加权 RMS 上限，单位现实米；设置时必须有 Check 点。</summary>
        public double? MaximumCheckRmsMeters { get; set; }

        /// <summary>可选 Check 单点最大三维残差上限，单位现实米，不加权。</summary>
        public double? MaximumCheckResidualMeters { get; set; }

        /// <summary>可选到 ENU 原点的三维现实米半径上限；包含最终放置偏移，不只是水平距离。</summary>
        public double? MaximumDistanceFromOriginMeters { get; set; }

        /// <summary>创建默认标定选项；误差和距离限制默认关闭，可按业务精度要求启用。</summary>
        public static GeoCalibrationOptions CreateDefault() => new GeoCalibrationOptions();
        internal GeoCalibrationOptions Copy() => (GeoCalibrationOptions)MemberwiseClone();
        internal bool TryValidate(out GeoError error)
        {
            error = default;
            if (!GeoMath.Finite(MinimumBaselineMeters) || MinimumBaselineMeters <= 0 ||
                !GeoMath.Finite(NearCollinearRatioThreshold) ||
                NearCollinearRatioThreshold <= 0 ||
                NearCollinearRatioThreshold >= 1 ||
                !GeoMath.Finite(MinimumScale) ||
                !GeoMath.Finite(MaximumScale) ||
                MinimumScale <= 0 ||
                MaximumScale < MinimumScale)
            {
                return GeoError.Fail(
                    GeoErrorCode.InvalidConfiguration,
                    "Invalid baseline, distribution or scale limits.",
                    out error);
            }

            double?[] limits =
            {
                MaximumFitRmsMeters,
                MaximumFitVerticalRmsMeters,
                MaximumCheckRmsMeters,
                MaximumCheckResidualMeters,
                MaximumDistanceFromOriginMeters
            };
            foreach (var value in limits)
            {
                if (value.HasValue && (!GeoMath.Finite(value.Value) || value.Value <= 0))
                {
                    return GeoError.Fail(
                        GeoErrorCode.InvalidConfiguration,
                        "Error/range limits must be finite and positive.",
                        out error);
                }
            }

            return true;
        }
    }

    /// <summary>复制单点标定条件：Root 局部 Up、已知方向及绑定航向、固定局部单位/现实米比例和选项。</summary>
    public sealed class GeoSingleAnchorSettings
    {
        internal GeoVector3d Up { get; }
        internal GeoVector3d Direction { get; }
        internal GeoHeading Heading { get; }
        internal double Scale { get; }
        internal GeoCalibrationOptions Options { get; }

        /// <summary>复制单点标定条件：Root 局部 Up、已知方向及绑定航向、固定局部单位/现实米比例和选项。</summary>
        public GeoSingleAnchorSettings(
            GeoVector3d localUp,
            GeoVector3d localHeadingDirection,
            GeoHeading boundHeading,
            double localUnitsPerMeter,
            GeoCalibrationOptions options)
        {
            Up = localUp;
            Direction = localHeadingDirection;
            Heading = boundHeading;
            Scale = localUnitsPerMeter;
            Options = (options ?? throw new ArgumentNullException(nameof(options))).Copy();
        }
    }

    /// <summary>复制已知 Root 局部 Up 的多点标定条件；只求水平旋转、正统一比例和平移，不用倾斜吸收高度误差。</summary>
    public sealed class GeoUprightSettings
    {
        internal GeoVector3d Up { get; }
        internal GeoScalePolicy Scale { get; }
        internal GeoCalibrationOptions Options { get; }

        /// <summary>复制已知 Root 局部 Up 的多点标定条件；只求水平旋转、正统一比例和平移，不用倾斜吸收高度误差。</summary>
        public GeoUprightSettings(GeoVector3d localUp, GeoScalePolicy scale, GeoCalibrationOptions options)
        {
            Up = localUp;
            Scale = scale;
            Options = (options ?? throw new ArgumentNullException(nameof(options))).Copy();
        }
    }

    /// <summary>复制完整三维标定的比例策略与选项；至少三个非共线 Fit 点。</summary>
    public sealed class GeoSpatialSettings
    {
        internal GeoScalePolicy Scale { get; }
        internal GeoCalibrationOptions Options { get; }

        /// <summary>复制完整三维标定的比例策略与选项；至少三个非共线 Fit 点。</summary>
        public GeoSpatialSettings(GeoScalePolicy scale, GeoCalibrationOptions options)
        {
            Scale = scale;
            Options = (options ?? throw new ArgumentNullException(nameof(options))).Copy();
        }
    }

    /// <summary>复制点列表及设置的不可变求解请求，不持有 Transform；点自身不可变。</summary>
    public sealed class GeoCalibrationRequest
    {
        /// <summary>本次标定模式。</summary>
        public GeoCalibrationMode Mode { get; }

        /// <summary>输入点的只读副本，包含 Fit 和 Check。</summary>
        public IReadOnlyList<GeoControlPoint> Points { get; }
        internal GeoCalibrationOptions Options { get; }
        internal GeoVector3d Up { get; }
        internal GeoVector3d Direction { get; }
        internal GeoHeading Heading { get; }
        internal GeoScalePolicy Scale { get; }

        GeoCalibrationRequest(
            GeoCalibrationMode mode,
            IReadOnlyList<GeoControlPoint> points,
            GeoCalibrationOptions options,
            GeoScalePolicy scale,
            GeoVector3d up = default,
            GeoVector3d direction = default,
            GeoHeading heading = default)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            var copy = new GeoControlPoint[points.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = points[i];
            }

            Points = Array.AsReadOnly(copy);
            Mode = mode;
            Options = options.Copy();
            Scale = scale;
            Up = up;
            Direction = direction;
            Heading = heading;
        }

        /// <summary>以一个 Fit 锚点、方向、Up 和固定比例创建请求；附加点必须全部为 Check。</summary>
        public static GeoCalibrationRequest SingleAnchor(
            GeoControlPoint anchor,
            GeoSingleAnchorSettings settings,
            IReadOnlyList<GeoControlPoint> checkPoints = null)
        {
            if (anchor == null || settings == null)
            {
                throw new ArgumentNullException();
            }

            if (anchor.Role != GeoControlPointRole.Fit)
            {
                throw new ArgumentException("The anchor must be a fit point.");
            }

            var points = new List<GeoControlPoint>
            {
                anchor
            };
            if (checkPoints != null)
            {
                foreach (var p in checkPoints)
                {
                    if (p == null || p.Role != GeoControlPointRole.Check)
                    {
                        throw new ArgumentException("Additional points must be check points.");
                    }

                    points.Add(p);
                }
            }

            return new GeoCalibrationRequest(
                GeoCalibrationMode.SingleAnchor,
                points,
                settings.Options,
                GeoScalePolicy.Fixed(settings.Scale),
                settings.Up,
                settings.Direction,
                settings.Heading);
        }

        /// <summary>以至少两个具有水平基线的 Fit 点创建已知 Up 的标定请求；可附带 Check。</summary>
        public static GeoCalibrationRequest Upright(IReadOnlyList<GeoControlPoint> points, GeoUprightSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return new GeoCalibrationRequest(GeoCalibrationMode.Upright, points, settings.Options, settings.Scale, settings.Up);
        }

        /// <summary>以至少三个非共线 Fit 点创建完整三维标定请求；三点共面允许，建议额外 Check。</summary>
        public static GeoCalibrationRequest Spatial(IReadOnlyList<GeoControlPoint> points, GeoSpatialSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return new GeoCalibrationRequest(GeoCalibrationMode.Spatial, points, settings.Options, settings.Scale);
        }
    }

    /// <summary>同组控制点在现实米制下的加权 RMS 与不加权最大三维残差。</summary>
    public readonly struct GeoResidualStatistics
    {
        /// <summary>东西与南北分量合成的加权 RMS，单位现实米。</summary>
        public double HorizontalRmsMeters { get; }

        /// <summary>Up 分量的加权 RMS，单位现实米。</summary>
        public double VerticalRmsMeters { get; }

        /// <summary>三个 ENU 分量合成的加权 RMS，单位现实米。</summary>
        public double SpatialRmsMeters { get; }

        /// <summary>不加权的单点最大三维残差，单位现实米。</summary>
        public double MaximumResidualMeters { get; }

        internal GeoResidualStatistics(double horizontal, double vertical, double spatial, double maximum)
        {
            HorizontalRmsMeters = horizontal;
            VerticalRmsMeters = vertical;
            SpatialRmsMeters = spatial;
            MaximumResidualMeters = maximum;
        }
    }

    /// <summary>单个控制点的原始/标准化坐标、实际/预测局部位置及现实米制残差。</summary>
    public sealed class GeoControlPointResidual
    {
        /// <summary>请求内唯一且非空的控制点标识。</summary>
        public string Id { get; }

        /// <summary>该点用于 Fit 求解还是 Check 独立验收。</summary>
        public GeoControlPointRole Role { get; }

        /// <summary>输入时声明的原始地理坐标。</summary>
        public GeoCoordinate SourceCoordinate { get; }

        /// <summary>经标准化后的 WGS84 绝对椭球高坐标。</summary>
        public Wgs84Coordinate NormalizedCoordinate { get; }

        /// <summary>控制点提供的 Root 局部位置。</summary>
        public GeoVector3d ActualLocalPosition { get; }

        /// <summary>标定模型根据地理坐标预测的 Root 局部位置。</summary>
        public GeoVector3d PredictedLocalPosition { get; }

        /// <summary>实际减预测的残差，转换回 ENU 现实米制后的东、北、上分量。</summary>
        public GeoEnuOffset EnuErrorMeters { get; }
        internal double Weight { get; }

        internal GeoControlPointResidual(GeoControlPoint p, Wgs84Coordinate coordinate, GeoVector3d predicted, GeoVector3d error)
        {
            Id = p.Id;
            Role = p.Role;
            SourceCoordinate = p.Coordinate;
            NormalizedCoordinate = coordinate;
            ActualLocalPosition = p.LocalPosition;
            PredictedLocalPosition = predicted;
            EnuErrorMeters = new GeoEnuOffset(error.X, error.Z, error.Y);
            Weight = p.Weight;
        }
    }

    /// <summary>一次求解的只读报告；失败时可有部分诊断，不能把缺失指标视为零误差。</summary>
    public sealed class GeoCalibrationReport
    {
        /// <summary>本次数学求解及验收阈值是否成功。</summary>
        public bool Success { get; internal set; }

        /// <summary>本次标定模式。</summary>
        public GeoCalibrationMode Mode { get; internal set; }

        /// <summary>参与拟合的控制点数。</summary>
        public int FitCount { get; internal set; }

        /// <summary>仅用于独立验收的控制点数。</summary>
        public int CheckCount { get; internal set; }

        /// <summary>本次求解实际采用的 WGS84 椭球高原点；失败时可能无值。</summary>
        public Wgs84Coordinate? Origin { get; internal set; }

        /// <summary>每现实米对应的 Root 局部单位数；不包含 Root 世界缩放。</summary>
        public double? LocalUnitsPerMeter { get; internal set; }

        /// <summary>Root 局部空间中的固定参考东、北、上轴。</summary>
        public GeoAxes3d? LocalAxes { get; internal set; }

        /// <summary>Fit 组残差统计；求解失败早期可能无值。</summary>
        public GeoResidualStatistics? FitMetrics { get; internal set; }

        /// <summary>Check 组残差统计；没有 Check 时无值。</summary>
        public GeoResidualStatistics? CheckMetrics { get; internal set; }

        /// <summary>逐点残差的只读列表。</summary>
        public IReadOnlyList<GeoControlPointResidual> Residuals { get; internal set; } = Array.AsReadOnly(new GeoControlPointResidual[0]);

        /// <summary>求解提示与警告，不代表测绘精度保证。</summary>
        public IReadOnlyList<GeoDiagnostic> Diagnostics { get; internal set; } = Array.AsReadOnly(new GeoDiagnostic[0]);

        /// <summary>失败原因；成功时为无错误。</summary>
        public GeoError Failure { get; internal set; }

        /// <summary>本次使用的水平转换算法及收敛信息。</summary>
        public GeoConversionMetadata ConversionMetadata { get; internal set; }

        /// <summary>模型局部坐标约定的身份标识，不是 EPSG 编号。</summary>
        public string LocalFrameId { get; internal set; }

        /// <summary>覆盖求解输入与设置的稳定追踪指纹，不是加密签名。</summary>
        public string DataFingerprint { get; internal set; }

        /// <summary>报告格式版本，当前为 1。</summary>
        public int SchemaVersion => 1;
    }

    /// <summary>成功求解的不可变局部映射及其报告，不包含 Unity Root。</summary>
    public sealed class GeoCalibrationSolution
    {
        /// <summary>不依赖 Unity 的双精度局部地理映射。</summary>
        public GeoReferenceFrame Frame { get; }

        /// <summary>此次求解或候选的报告。</summary>
        public GeoCalibrationReport Report { get; }

        internal GeoCalibrationSolution(GeoReferenceFrame frame, GeoCalibrationReport report)
        {
            Frame = frame;
            Report = report;
        }
    }
}
