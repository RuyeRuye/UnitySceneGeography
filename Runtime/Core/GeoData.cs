using System;

namespace SceneGeography
{
    /// <summary>外部水平坐标类型；Unspecified 无效，调用方必须明确 WGS84、GCJ-02 或 BD-09。</summary>
    public enum GeoCoordinateType
    {
        Unspecified,
        Wgs84,
        Gcj02,
        Bd09
    }

    /// <summary>高度参考类别；椭球高、相对椭球零点高度与正高互不等价。</summary>
    public enum GeoHeightKind
    {
        Unspecified,
        Ellipsoidal,
        RelativeToEllipsoidZero,
        Orthometric
    }

    /// <summary>固定 ENU 参考原点的东、北、上及相反方向，并非逐点真北/天顶。</summary>
    public enum GeoCardinalDirection
    {
        North,
        East,
        South,
        West,
        Up,
        Down
    }

    /// <summary>可供业务分支处理的地理转换、标定、Root 和生命周期错误码。</summary>
    public enum GeoErrorCode
    {
        None,
        InvalidNumber,
        InvalidLatitude,
        CoordinateTypeUnspecified,
        HeightReferenceUnspecified,
        UnsupportedCoordinateType,
        UnsupportedCoordinateRegion,
        MissingHeightModel,
        ConversionDidNotConverge,
        UndefinedGeodeticPosition,
        InvalidControlPoint,
        DuplicateControlPointId,
        InsufficientFitPoints,
        DegenerateBaseline,
        NearCollinearPoints,
        InvalidUp,
        InvalidHeadingDirection,
        ScaleOutOfRange,
        ResidualLimitExceeded,
        CheckPointsRequired,
        InvalidConfiguration,
        NotInitialized,
        RootDestroyed,
        InvalidRootTransform,
        InvalidTargetParentTransform,
        OutOfConfiguredRange,
        LocalFrameIdMismatch,
        DegenerateLookDirection,
        MissingLookTarget,
        CyclicTransformBinding,
        InvalidModelCorrection,
        InvalidOrientationRequest,
        MissingReference,
        RootChangeRequiresClear,
        StaleCalibrationCandidate,
        LocalFrameIdChangeRequiresClear,
        ReentrantMutation
    }

    /// <summary>不抛异常的 Try API 返回的错误；成功时为默认值，失败输出不可作为有效坐标使用。</summary>
    public readonly struct GeoError
    {
        /// <summary>机器可读错误码或诊断标识。</summary>
        public GeoErrorCode Code { get; }

        /// <summary>用于人工诊断的错误或提示文本，不应作为稳定业务分支条件。</summary>
        public string Message { get; }

        /// <summary>关联控制点的标识；与具体点无关时可为空。</summary>
        public string SubjectId { get; }

        /// <summary>是否没有错误。</summary>
        public bool IsNone => Code == GeoErrorCode.None;

        /// <summary>不抛异常的 Try API 返回的错误；成功时为默认值，失败输出不可作为有效坐标使用。</summary>
        public GeoError(GeoErrorCode code, string message, string subjectId = null)
        {
            Code = code;
            Message = message;
            SubjectId = subjectId;
        }

        /// <summary>返回便于日志诊断的值描述。</summary>
        public override string ToString() => IsNone ? "None" : $"{Code}: {Message}";
        internal static bool Fail(GeoErrorCode code, string message, out GeoError error, string id = null)
        {
            error = new GeoError(code, message, id);
            return false;
        }
    }

    /// <summary>不阻止求解的诊断严重程度。</summary>
    public enum GeoDiagnosticSeverity
    {
        Info,
        Warning
    }

    /// <summary>标定过程的提示或警告，包含代码、说明及关联点。</summary>
    public readonly struct GeoDiagnostic
    {
        /// <summary>提示或警告的严重程度。</summary>
        public GeoDiagnosticSeverity Severity { get; }

        /// <summary>机器可读错误码或诊断标识。</summary>
        public string Code { get; }

        /// <summary>关联控制点的标识；与具体点无关时可为空。</summary>
        public string SubjectId { get; }

        /// <summary>用于人工诊断的错误或提示文本，不应作为稳定业务分支条件。</summary>
        public string Message { get; }

        /// <summary>标定过程的提示或警告，包含代码、说明及关联点。</summary>
        public GeoDiagnostic(
            string code,
            string message,
            string subjectId = null,
            GeoDiagnosticSeverity severity = GeoDiagnosticSeverity.Warning)
        {
            Severity = severity;
            Code = code;
            Message = message;
            SubjectId = subjectId;
        }
    }

    /// <summary>不可变高度约定；正高需外部高程模型，当前默认转换器不提供该模型。</summary>
    public readonly struct GeoHeightReference
    {
        /// <summary>高度的参考类型：椭球高、相对椭球零点高度或正高。</summary>
        public GeoHeightKind Kind { get; }

        /// <summary>相对高度零点对应的绝对椭球高，单位现实米。</summary>
        public double EllipsoidZeroMeters { get; }

        /// <summary>正高模型标识；仅声明名称不代表已安装模型。</summary>
        public string ModelId { get; }

        /// <summary>相对高度的绝对零点是否为假定值，不代表具有测量精度。</summary>
        public bool IsAssumed { get; }

        GeoHeightReference(GeoHeightKind kind, double zero, string id, bool assumed)
        {
            Kind = kind;
            EllipsoidZeroMeters = zero;
            ModelId = id;
            IsAssumed = assumed;
        }

        /// <summary>使用绝对椭球高。</summary>
        public static GeoHeightReference Ellipsoidal => new GeoHeightReference(GeoHeightKind.Ellipsoidal, 0, null, false);

        /// <summary>声明相对椭球零点高度；绝对椭球高等于 zeroMeters 加输入高度，可显式标记为假定零点。</summary>
        public static GeoHeightReference RelativeToEllipsoidZero(double zeroMeters, bool isAssumed = false)
        {
            GeoMath.RequireFinite(zeroMeters, nameof(zeroMeters));
            return new GeoHeightReference(GeoHeightKind.RelativeToEllipsoidZero, zeroMeters, null, isAssumed);
        }

        /// <summary>声明正高及模型标识；缺少模型时转换明确失败，不当作椭球高处理。</summary>
        public static GeoHeightReference Orthometric(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                throw new ArgumentException("A height model ID is required.", nameof(modelId));
            }

            return new GeoHeightReference(GeoHeightKind.Orthometric, 0, modelId, false);
        }

        internal bool IsValid => Kind == GeoHeightKind.Ellipsoidal ||
        (Kind == GeoHeightKind.RelativeToEllipsoidZero && GeoMath.Finite(EllipsoidZeroMeters)) ||
        (Kind == GeoHeightKind.Orthometric && !string.IsNullOrWhiteSpace(ModelId));
    }

    /// <summary>外部坐标的水平与高度参考组合；构造时拒绝未声明类型。</summary>
    public readonly struct GeoCoordinateReference
    {
        /// <summary>明确的 WGS84、GCJ-02 或 BD-09 水平类型。</summary>
        public GeoCoordinateType HorizontalType { get; }

        /// <summary>高度的参考约定，不隐式继承地图海拔语义。</summary>
        public GeoHeightReference HeightReference { get; }

        /// <summary>外部坐标的水平与高度参考组合；构造时拒绝未声明类型。</summary>
        public GeoCoordinateReference(GeoCoordinateType horizontalType, GeoHeightReference heightReference)
        {
            if (horizontalType < GeoCoordinateType.Wgs84 || horizontalType > GeoCoordinateType.Bd09)
            {
                throw new ArgumentException("An explicit supported horizontal coordinate type is required.");
            }

            if (!heightReference.IsValid)
            {
                throw new ArgumentException("An explicit height reference is required.");
            }

            HorizontalType = horizontalType;
            HeightReference = heightReference;
        }

        /// <summary>标准 WGS84 水平坐标与绝对椭球高参考。</summary>
        public static GeoCoordinateReference Wgs84Ellipsoidal => new GeoCoordinateReference(GeoCoordinateType.Wgs84, GeoHeightReference.Ellipsoidal);

        internal bool TryValidate(out GeoError error)
        {
            error = default;
            if (HorizontalType == GeoCoordinateType.Unspecified)
            {
                return GeoError.Fail(GeoErrorCode.CoordinateTypeUnspecified, "Specify WGS84, GCJ-02 or BD-09.", out error);
            }

            if (HorizontalType < GeoCoordinateType.Wgs84 || HorizontalType > GeoCoordinateType.Bd09)
            {
                return GeoError.Fail(GeoErrorCode.UnsupportedCoordinateType, "Unsupported horizontal type.", out error);
            }

            return HeightReference.IsValid ||
                GeoError.Fail(GeoErrorCode.HeightReferenceUnspecified, "Specify the height reference.", out error);
        }
    }

    /// <summary>带显式水平和高度参考的双精度经纬高；default 未声明参考，不能用于转换。</summary>
    public readonly struct GeoCoordinate
    {
        /// <summary>纬度，单位度，北正南负，范围 [-90,90]。</summary>
        public double LatitudeDegrees { get; }

        /// <summary>经度，单位度，东正西负，规范化为 [-180,180)。</summary>
        public double LongitudeDegrees { get; }

        /// <summary>在明确高度参考下的高度，单位现实米。</summary>
        public double HeightMeters { get; }

        /// <summary>该坐标使用的水平类型和高度参考。</summary>
        public GeoCoordinateReference Reference { get; }

        GeoCoordinate(double lat, double lon, double h, GeoCoordinateReference reference)
        {
            LatitudeDegrees = lat;
            LongitudeDegrees = lon;
            HeightMeters = h;
            Reference = reference;
        }

        /// <summary>以度构造角度或经纬高，检查有限性并规范化角度；非法输入抛出参数异常。</summary>
        public static GeoCoordinate FromDegrees(double latitude, double longitude, double heightMeters, GeoCoordinateReference reference)
        {
            if (!TryCreate(latitude, longitude, heightMeters, reference, out var value, out var error))
            {
                throw new ArgumentException(error.Message);
            }

            return value;
        }

        /// <summary>检查输入数值和参考系并构造值；失败返回 false 和具体错误，勿使用失败输出。</summary>
        public static bool TryCreate(
            double latitude,
            double longitude,
            double heightMeters,
            GeoCoordinateReference reference,
            out GeoCoordinate coordinate,
            out GeoError error)
        {
            coordinate = default;
            if (!reference.TryValidate(out error) ||
                !GeoMath.ValidateCoordinate(latitude, longitude, heightMeters, out error))
            {
                return false;
            }

            coordinate = new GeoCoordinate(latitude, GeoMath.Longitude(longitude), heightMeters, reference);
            return true;
        }

        internal bool TryValidate(out GeoError error) => Reference.TryValidate(out error) &&
        GeoMath.ValidateCoordinate(LatitudeDegrees, LongitudeDegrees, HeightMeters, out error);
    }

    /// <summary>标准双精度 WGS84 经纬度与绝对椭球高；default 的 (0,0,0) 合法，不是无值标记。</summary>
    public readonly struct Wgs84Coordinate
    {
        /// <summary>纬度，单位度，北正南负，范围 [-90,90]。</summary>
        public double LatitudeDegrees { get; }

        /// <summary>经度，单位度，东正西负，规范化为 [-180,180)。</summary>
        public double LongitudeDegrees { get; }

        /// <summary>绝对椭球高，单位现实米，不是正高/海拔。</summary>
        public double EllipsoidHeightMeters { get; }

        Wgs84Coordinate(double lat, double lon, double h)
        {
            LatitudeDegrees = lat;
            LongitudeDegrees = lon;
            EllipsoidHeightMeters = h;
        }

        /// <summary>以度构造角度或经纬高，检查有限性并规范化角度；非法输入抛出参数异常。</summary>
        public static Wgs84Coordinate FromDegrees(double latitude, double longitude, double ellipsoidHeightMeters)
        {
            if (!TryCreate(latitude, longitude, ellipsoidHeightMeters, out var value, out var error))
            {
                throw new ArgumentException(error.Message);
            }

            return value;
        }

        /// <summary>检查输入数值和参考系并构造值；失败返回 false 和具体错误，勿使用失败输出。</summary>
        public static bool TryCreate(
            double latitude,
            double longitude,
            double ellipsoidHeightMeters,
            out Wgs84Coordinate coordinate,
            out GeoError error)
        {
            coordinate = default;
            if (!GeoMath.ValidateCoordinate(latitude, longitude, ellipsoidHeightMeters, out error))
            {
                return false;
            }

            coordinate = new Wgs84Coordinate(latitude, GeoMath.Longitude(longitude), ellipsoidHeightMeters);
            return true;
        }

        /// <summary>转换为显式 WGS84 与椭球高参考的外部坐标。</summary>
        public GeoCoordinate ToCoordinate() => GeoCoordinate.FromDegrees(
            LatitudeDegrees,
            LongitudeDegrees,
            EllipsoidHeightMeters,
            GeoCoordinateReference.Wgs84Ellipsoidal);
    }

    /// <summary>不依赖 Unity 的双精度三维向量；具体空间和单位由 API 契约决定。</summary>
    public readonly struct GeoVector3d
    {
        /// <summary>向量 X 分量。</summary>
        public double X { get; }

        /// <summary>向量 Y 分量。</summary>
        public double Y { get; }

        /// <summary>向量 Z 分量。</summary>
        public double Z { get; }

        /// <summary>不依赖 Unity 的双精度三维向量；具体空间和单位由 API 契约决定。</summary>
        public GeoVector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>零向量或零偏移。</summary>
        public static GeoVector3d Zero => default;

        /// <summary>X 轴单位向量。</summary>
        public static GeoVector3d UnitX => new GeoVector3d(1, 0, 0);

        /// <summary>Y 轴单位向量。</summary>
        public static GeoVector3d UnitY => new GeoVector3d(0, 1, 0);

        /// <summary>Z 轴单位向量。</summary>
        public static GeoVector3d UnitZ => new GeoVector3d(0, 0, 1);

        /// <summary>全部向量分量是否均为有限数值。</summary>
        public bool IsFinite => GeoMath.Finite(X) && GeoMath.Finite(Y) && GeoMath.Finite(Z);

        /// <summary>向量长度的平方。</summary>
        public double SqrMagnitude => X * X + Y * Y + Z * Z;

        /// <summary>向量长度。</summary>
        public double Magnitude => Math.Sqrt(SqrMagnitude);

        /// <summary>归一化向量；零长输入返回零向量。</summary>
        public GeoVector3d Normalized => Magnitude > 1e-15 ? this / Magnitude : Zero;

        /// <summary>执行双精度向量分量运算；不隐式改变空间或单位。</summary>
        public static GeoVector3d operator +(GeoVector3d a, GeoVector3d b) => new GeoVector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        /// <summary>执行双精度向量分量运算；不隐式改变空间或单位。</summary>
        public static GeoVector3d operator -(GeoVector3d a, GeoVector3d b) => new GeoVector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        /// <summary>执行双精度向量分量运算；不隐式改变空间或单位。</summary>
        public static GeoVector3d operator -(GeoVector3d a) => a * -1;

        /// <summary>执行双精度向量分量运算；不隐式改变空间或单位。</summary>
        public static GeoVector3d operator *(GeoVector3d a, double s) => new GeoVector3d(a.X * s, a.Y * s, a.Z * s);

        /// <summary>执行双精度向量分量运算；不隐式改变空间或单位。</summary>
        public static GeoVector3d operator /(GeoVector3d a, double s) => a * (1 / s);

        /// <summary>两个向量的点积。</summary>
        public static double Dot(GeoVector3d a, GeoVector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>两个向量的叉积。</summary>
        public static GeoVector3d Cross(GeoVector3d a, GeoVector3d b) =>
            new GeoVector3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        /// <summary>返回便于日志诊断的值描述。</summary>
        public override string ToString() => $"({X:G8}, {Y:G8}, {Z:G8})";
    }

    /// <summary>固定参考 ENU 中的现实米制偏移；构造参数顺序为东、北、上，不是 Unity 的 X/Y/Z 顺序。</summary>
    public readonly struct GeoEnuOffset
    {
        /// <summary>向东的现实米数，负值向西。</summary>
        public double EastMeters { get; }

        /// <summary>向北的现实米数，负值向南。</summary>
        public double NorthMeters { get; }

        /// <summary>向参考 Up 的现实米数，负值向下。</summary>
        public double UpMeters { get; }

        /// <summary>固定参考 ENU 中的现实米制偏移；构造参数顺序为东、北、上，不是 Unity 的 X/Y/Z 顺序。</summary>
        public GeoEnuOffset(double eastMeters, double northMeters, double upMeters)
        {
            EastMeters = eastMeters;
            NorthMeters = northMeters;
            UpMeters = upMeters;
        }

        /// <summary>零向量或零偏移。</summary>
        public static GeoEnuOffset Zero => default;
        internal GeoVector3d EngineVector => new GeoVector3d(EastMeters, UpMeters, NorthMeters);
    }

    /// <summary>Root 局部空间中的固定参考东、北、上单位方向。</summary>
    public readonly struct GeoAxes3d
    {
        /// <summary>东向单位方向；具体为局部空间或世界空间由所属类型决定。</summary>
        public GeoVector3d East { get; }

        /// <summary>北向单位方向，不是逐点重新计算的真北。</summary>
        public GeoVector3d North { get; }

        /// <summary>参考 Up 单位方向，不强制等于世界 Y。</summary>
        public GeoVector3d Up { get; }

        internal GeoAxes3d(GeoVector3d east, GeoVector3d north, GeoVector3d up)
        {
            East = east;
            North = north;
            Up = up;
        }

        /// <summary>获取东、西、南、北、上、下单位方向；无效枚举抛出参数异常。</summary>
        public GeoVector3d GetDirection(GeoCardinalDirection direction)
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

    /// <summary>从参考北向东顺时针为正的航向角，规范化到 [0,360)。</summary>
    public readonly struct GeoHeading
    {
        /// <summary>航向角，北 0、东 90、南 180、西 270，单位度。</summary>
        public double Degrees { get; }

        GeoHeading(double degrees)
        {
            Degrees = degrees;
        }

        /// <summary>以度构造角度或经纬高，检查有限性并规范化角度；非法输入抛出参数异常。</summary>
        public static GeoHeading FromDegrees(double degrees)
        {
            GeoMath.RequireFinite(degrees, nameof(degrees));
            return new GeoHeading((degrees % 360 + 360) % 360);
        }

        /// <summary>由水平基准方位及顺时针偏角构造航向；Up/Down 不能作为水平航向。</summary>
        public static GeoHeading FromCardinal(GeoCardinalDirection direction, double clockwiseOffsetDegrees = 0)
        {
            double angle;
            switch (direction)
            {
                case GeoCardinalDirection.North:
                    angle = 0;
                    break;
                case GeoCardinalDirection.East:
                    angle = 90;
                    break;
                case GeoCardinalDirection.South:
                    angle = 180;
                    break;
                case GeoCardinalDirection.West:
                    angle = 270;
                    break;
                default:
                    throw new ArgumentException("Heading requires a horizontal cardinal direction.");
            }

            return FromDegrees(angle + clockwiseOffsetDegrees);
        }
    }

    internal static class GeoMath
    {
        internal const double Deg = Math.PI / 180;
        internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        internal static void RequireFinite(double x, string name)
        {
            if (!Finite(x))
            {
                throw new ArgumentOutOfRangeException(name, "Value must be finite.");
            }
        }

        internal static double Longitude(double lon) => ((lon % 360 + 540) % 360) - 180;
        internal static bool ValidateCoordinate(double lat, double lon, double h, out GeoError error)
        {
            error = default;
            if (!Finite(lat) || !Finite(lon) || !Finite(h))
            {
                return GeoError.Fail(GeoErrorCode.InvalidNumber, "Coordinates must be finite.", out error);
            }

            return Math.Abs(lat) <= 90 ||
                GeoError.Fail(GeoErrorCode.InvalidLatitude, "Latitude must be in [-90,90].", out error);
        }
    }
}
