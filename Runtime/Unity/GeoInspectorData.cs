using System;
using UnityEngine;

namespace SceneGeography
{
    /// <summary>供 Inspector 序列化的外部坐标 DTO；运行时业务使用不可变 GeoCoordinate。</summary>
    [Serializable]
    public sealed class GeoCoordinateSettings
    {
        /// <summary>输入水平坐标参考：WGS84、GCJ-02 或 BD-09；必须明确选择。</summary>
        [Tooltip("Input horizontal reference: WGS84, GCJ-02, or BD-09. An explicit selection is required.")]
        public GeoCoordinateType horizontalType;

        /// <summary>输入高度参考；椭球高与海拔不能混用。当前 Orthometric 无高程模型，会明确失败。</summary>
        [Tooltip("Input height reference. Do not mix ellipsoid and orthometric heights. Orthometric currently fails without a height model.")]
        public GeoHeightKind heightKind;

        /// <summary>纬度，单位度；北正南负，范围 [-90,90]。</summary>
        [Tooltip("Latitude in degrees. North positive, south negative; range [-90,90].")]
        public double latitudeDegrees;

        /// <summary>经度，单位度；东正西负，规范化到 [-180,180)。</summary>
        [Tooltip("Longitude in degrees. East positive, west negative; normalized to [-180,180).")]
        public double longitudeDegrees;

        /// <summary>所选高度参考下的高度，单位现实米；不是 Unity Y 坐标。</summary>
        [Tooltip("Height in real meters under the selected reference. Not Unity Y.")]
        public double heightMeters;

        /// <summary>相对高度零点对应的绝对椭球高，单位米，仅 RelativeToEllipsoidZero 生效。</summary>
        [Tooltip("Absolute ellipsoid height of the relative-height zero, in meters. Only used by RelativeToEllipsoidZero.")]
        public double ellipsoidZeroMeters;

        /// <summary>零点椭球高是否为假定值；报告会警告，不提升高程精度。</summary>
        [Tooltip("Whether the zero's ellipsoid height is assumed. Produces a warning, not improved height accuracy.")]
        public bool assumedHeightZero;

        /// <summary>正高模型标识，仅 Orthometric 使用；当前没有可用模型。</summary>
        [Tooltip("Height-model ID for Orthometric only. No model is currently installed.")]
        public string heightModelId;

        /// <summary>验证 DTO 参考和数值并创建不可变坐标；失败返回错误。</summary>
        public bool TryBuild(out GeoCoordinate coordinate, out GeoError error)
        {
            coordinate = default;
            if (horizontalType == GeoCoordinateType.Unspecified)
            {
                error = new GeoError(GeoErrorCode.CoordinateTypeUnspecified, "Select the input horizontal coordinate type.");
                return false;
            }

            if (heightKind == GeoHeightKind.Unspecified)
            {
                error = new GeoError(GeoErrorCode.HeightReferenceUnspecified, "Select the input height reference.");
                return false;
            }

            try
            {
                GeoHeightReference height;
                switch (heightKind)
                {
                    case GeoHeightKind.Ellipsoidal:
                        height = GeoHeightReference.Ellipsoidal;
                        break;
                    case GeoHeightKind.RelativeToEllipsoidZero:
                        height = GeoHeightReference.RelativeToEllipsoidZero(ellipsoidZeroMeters, assumedHeightZero);
                        break;
                    case GeoHeightKind.Orthometric:
                        height = GeoHeightReference.Orthometric(heightModelId);
                        break;
                    default:
                        error = new GeoError(GeoErrorCode.HeightReferenceUnspecified, "Unsupported height reference.");
                        return false;
                }

                return GeoCoordinate.TryCreate(
                    latitudeDegrees,
                    longitudeDegrees,
                    heightMeters,
                    new GeoCoordinateReference(horizontalType, height),
                    out coordinate,
                    out error);
            }
            catch (ArgumentException ex)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, ex.Message);
                return false;
            }
        }

        /// <summary>从不可变业务值创建可序列化 DTO 副本。</summary>
        public static GeoCoordinateSettings From(GeoCoordinate coordinate) => new GeoCoordinateSettings
        {
            horizontalType = coordinate.Reference.HorizontalType,
            heightKind = coordinate.Reference.HeightReference.Kind,
            latitudeDegrees = coordinate.LatitudeDegrees,
            longitudeDegrees = coordinate.LongitudeDegrees,
            heightMeters = coordinate.HeightMeters,
            ellipsoidZeroMeters = coordinate.Reference.HeightReference.EllipsoidZeroMeters,
            assumedHeightZero = coordinate.Reference.HeightReference.IsAssumed,
            heightModelId = coordinate.Reference.HeightReference.ModelId
        };
    }

    /// <summary>Inspector 使用的双精度三分量 DTO；控制点位置的单位为 Root 局部单位。</summary>
    [Serializable]
    public struct GeoDouble3Settings
    {
        /// <summary>向量 X 分量。控制点位置为 Root 局部单位，不是现实米偏移。</summary>
        [Tooltip("Vector X component. Control-point positions use Root-local units, not real-meter offsets.")]
        public double x;

        /// <summary>向量 Y 分量。控制点位置为 Root 局部单位。</summary>
        [Tooltip("Vector Y component. Control-point positions use Root-local units.")]
        public double y;

        /// <summary>向量 Z 分量。控制点位置为 Root 局部单位。</summary>
        [Tooltip("Vector Z component. Control-point positions use Root-local units.")]
        public double z;

        /// <summary>Inspector 使用的双精度三分量 DTO；控制点位置的单位为 Root 局部单位。</summary>
        public GeoDouble3Settings(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        /// <summary>将三个序列化分量转换为 Core 双精度向量。</summary>
        public GeoVector3d Value => new GeoVector3d(x, y, z);

        /// <summary>从不可变业务值创建可序列化 DTO 副本。</summary>
        public static GeoDouble3Settings From(GeoVector3d p) => new GeoDouble3Settings(p.X, p.Y, p.Z);
    }

    /// <summary>可序列化控制点 DTO，保存 Root 局部位置、明确参考的地理坐标及用途。</summary>
    [Serializable]
    public sealed class GeoControlPointSettings
    {
        /// <summary>控制点唯一且非空的标识；同一请求内不能重复。</summary>
        [Tooltip("Unique, nonempty control-point ID. Must not be duplicated within one request.")]
        public string id;

        /// <summary>Fit 参与拟合；Check 只做独立精度检查。</summary>
        [Tooltip("Fit participates in solving. Check independently validates accuracy.")]
        public GeoControlPointRole role;

        /// <summary>必须大于零。Fit 影响求解，Check 影响 RMS；最大误差不加权。</summary>
        [Tooltip("Must be positive. Weights affect Fit solving and Check RMS; maximum error is unweighted.")]
        public double weight = 1;

        /// <summary>模型 Root 局部坐标，不一定等于 Marker 直属父级的 localPosition。</summary>
        [Tooltip("Model Root-local coordinate, not necessarily the Marker's parent-local localPosition.")]
        public GeoDouble3Settings localPosition;

        /// <summary>该模型位置对应的现实经纬高，需明确水平和高度参考。</summary>
        [Tooltip("Geographic coordinate corresponding to this model-local position, with explicit horizontal and height references.")]
        public GeoCoordinateSettings coordinate = new GeoCoordinateSettings();

        /// <summary>从不可变业务值创建可序列化 DTO 副本。</summary>
        public static GeoControlPointSettings From(GeoControlPoint p) => new GeoControlPointSettings
        {
            id = p.Id,
            role = p.Role,
            weight = p.Weight,
            localPosition = GeoDouble3Settings.From(p.LocalPosition),
            coordinate = GeoCoordinateSettings.From(p.Coordinate)
        };
        internal bool TryBuild(out GeoControlPoint point, out GeoError error)
        {
            point = null;
            if (coordinate == null)
            {
                error = new GeoError(GeoErrorCode.InvalidControlPoint, "Coordinate data is missing.", id);
                return false;
            }

            if (!coordinate.TryBuild(out var geo, out error))
            {
                return false;
            }

            try
            {
                point = new GeoControlPoint(id, localPosition.Value, geo, weight, role);
                return true;
            }
            catch (ArgumentException ex)
            {
                error = new GeoError(GeoErrorCode.InvalidControlPoint, ex.Message, id);
                return false;
            }
        }
    }

    /// <summary>Inspector 标定选项 DTO；关闭的限制开关不参与求解验收。</summary>
    [Serializable]
    public sealed class GeoCalibrationOptionsSettings
    {
        /// <summary>固定 ENU 原点；关闭时使用 Fit 点的 ECEF 加权中心，Check 不参与。</summary>
        [Tooltip("Fixed ENU origin. When disabled, use the Fit points' ECEF weighted center; Check points do not contribute.")]
        public bool useExplicitOrigin;

        /// <summary>显式原点的 WGS84 纬度，单位度，不继承控制点的 GCJ/BD 类型。</summary>
        [Tooltip("Explicit origin WGS84 latitude in degrees. Does not inherit a control point's GCJ/BD reference.")]
        public double originLatitudeDegrees;

        /// <summary>显式原点的 WGS84 经度，单位度。</summary>
        [Tooltip("Explicit origin WGS84 longitude in degrees.")]
        public double originLongitudeDegrees;

        /// <summary>显式原点的绝对椭球高，单位米，不是海拔或相对高度。</summary>
        [Tooltip("Explicit origin absolute ellipsoid height in meters. Not orthometric or relative height.")]
        public double originEllipsoidHeightMeters;

        /// <summary>最小地理基线判定阈值，单位现实米；短基线容易导致方向不稳定。</summary>
        [Tooltip("Minimum geographic baseline in meters. Short baselines make direction unstable.")]
        public double minimumBaselineMeters = .01;

        /// <summary>Spatial 近共线判定的分布比阈值，越大越严格；默认 0.0001。</summary>
        [Tooltip("Spatial near-collinearity distribution ratio. Larger values reject more configurations; default 0.0001.")]
        public double nearCollinearRatio = 1e-4;

        /// <summary>允许的最小正比例，单位 Root 局部单位/现实米。</summary>
        [Tooltip("Minimum positive scale in Root-local units per real meter.")]
        public double minimumScale = 1e-6;

        /// <summary>允许的最大比例，单位 Root 局部单位/现实米。</summary>
        [Tooltip("Maximum positive scale in Root-local units per real meter.")]
        public double maximumScale = 1e6;

        /// <summary>是否拒绝 Fit 三维加权 RMS 超限的标定。</summary>
        [Tooltip("Reject calibration when weighted Fit 3D RMS exceeds its limit.")]
        public bool limitFitRms;

        /// <summary>是否拒绝 Fit 竖直加权 RMS 超限的标定。</summary>
        [Tooltip("Reject calibration when weighted Fit vertical RMS exceeds its limit.")]
        public bool limitVerticalRms;

        /// <summary>是否检查 Check 三维加权 RMS；启用后必须有独立 Check 点。</summary>
        [Tooltip("Check weighted Check-point 3D RMS. Requires independent Check points when enabled.")]
        public bool limitCheckRms;

        /// <summary>是否检查 Check 最大三维误差；启用后必须有独立 Check 点。</summary>
        [Tooltip("Check maximum Check-point 3D error. Requires independent Check points when enabled.")]
        public bool limitCheckMaximum;

        /// <summary>是否限制到 ENU 原点的三维距离；包含高度和最终放置偏移。</summary>
        [Tooltip("Limit 3D distance from the ENU origin, including height and final placement offsets.")]
        public bool limitDistance;

        /// <summary>Fit 三维加权 RMS 上限，单位现实米，仅对应开关启用时生效。</summary>
        [Tooltip("Weighted Fit 3D RMS limit in real meters; used only when the corresponding switch is enabled.")]
        public double fitRmsMeters = 1;

        /// <summary>Fit 竖直加权 RMS 上限，单位现实米。</summary>
        [Tooltip("Weighted Fit vertical RMS limit in real meters.")]
        public double verticalRmsMeters = .5;

        /// <summary>Check 三维加权 RMS 上限，单位现实米。</summary>
        [Tooltip("Weighted Check-point 3D RMS limit in real meters.")]
        public double checkRmsMeters = 1;

        /// <summary>单个 Check 点的最大三维残差上限，单位现实米，不加权。</summary>
        [Tooltip("Maximum single Check-point 3D residual in real meters; unweighted.")]
        public double checkMaximumMeters = 2;

        /// <summary>允许的 ENU 三维半径上限，单位现实米，不只是水平距离。</summary>
        [Tooltip("Maximum ENU 3D radius in real meters, not just horizontal distance.")]
        public double maximumDistanceMeters = 5000;
        internal GeoCalibrationOptions Build(string frameId) => new GeoCalibrationOptions
        {
            ReferenceOrigin = useExplicitOrigin
                ? (Wgs84Coordinate?)Wgs84Coordinate.FromDegrees(originLatitudeDegrees, originLongitudeDegrees, originEllipsoidHeightMeters)
                : null,
            ExpectedLocalFrameId = frameId,
            MinimumBaselineMeters = minimumBaselineMeters,
            NearCollinearRatioThreshold = nearCollinearRatio,
            MinimumScale = minimumScale,
            MaximumScale = maximumScale,
            MaximumFitRmsMeters = limitFitRms ? (double?)fitRmsMeters : null,
            MaximumFitVerticalRmsMeters = limitVerticalRms ? (double?)verticalRmsMeters : null,
            MaximumCheckRmsMeters = limitCheckRms ? (double?)checkRmsMeters : null,
            MaximumCheckResidualMeters = limitCheckMaximum ? (double?)checkMaximumMeters : null,
            MaximumDistanceFromOriginMeters = limitDistance ? (double?)maximumDistanceMeters : null
        };
    }
}
