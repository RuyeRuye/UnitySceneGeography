using System;
using System.Collections.Generic;
using UnityEngine;

namespace SceneGeography
{
    /// <summary>本轮同步合并的标定、Root 姿态、有效性或清除变化。</summary>
    [Flags]
    public enum GeoReferenceChangeFlags
    {
        None = 0,
        CalibrationChanged = 1,
        RootTransformChanged = 2,
        RootValidityChanged = 4,
        Cleared = 8
    }

    /// <summary>绑定三阶段刷新之后发出的状态快照；回调中不要重入变更。</summary>
    public readonly struct GeoReferenceChange
    {
        /// <summary>本次通知包含的变化标记，可同时包含多项。</summary>
        public GeoReferenceChangeFlags Flags { get; }

        /// <summary>捕获的标定版本；成功提交或 Clear 后变化，失败求解不改变。</summary>
        public long CalibrationVersion { get; }

        /// <summary>捕获时的 Root 世界姿态版本，不包含重新拟合。</summary>
        public long RootPoseVersion { get; }

        /// <summary>是否存在已生效的数学标定解；不等于 Root 当前世界映射一定有效。</summary>
        public bool IsInitialized { get; }

        /// <summary>最近捕获/同步时世界映射是否有效；查询实时变换请调用 TryCaptureSnapshot。</summary>
        public bool IsWorldMappingValid { get; }

        /// <summary>捕获时 Root 世界映射的错误。</summary>
        public GeoError RootError { get; }

        internal GeoReferenceChange(GeoReferenceChangeFlags flags, GeoReference reference, long rootVersion)
        {
            Flags = flags;
            CalibrationVersion = reference.CalibrationVersion;
            RootPoseVersion = rootVersion;
            IsInitialized = reference.IsInitialized;
            IsWorldMappingValid = reference.IsWorldMappingValid;
            RootError = reference.LastWorldMappingError;
        }
    }

    /// <summary>尚未提交的局部标定候选，绑定实例、配置身份和标定版本；不能跨 Reference 或旧版本复用。</summary>
    public sealed class GeoCalibrationCandidate
    {
        /// <summary>此次求解或候选的报告。</summary>
        public GeoCalibrationReport Report => Solution.Report;

        /// <summary>生成候选时 Reference 的标定版本，用于提交时拒绝旧候选。</summary>
        public long BaseCalibrationVersion { get; }
        internal GeoReference Owner { get; }
        internal int ConfigurationVersion { get; }
        internal GeoCalibrationSolution Solution { get; }
        internal Transform Root { get; }
        internal string LocalFrameId { get; }

        internal GeoCalibrationCandidate(GeoReference owner, int configuration, long version, GeoCalibrationSolution solution)
        {
            Owner = owner;
            ConfigurationVersion = configuration;
            BaseCalibrationVersion = version;
            Solution = solution;
            Root = owner.Root;
            LocalFrameId = owner.LocalFrameId;
        }
    }

    internal interface IGeoBinding
    {
        int Phase { get; }

        void Refresh(GeoReferenceSnapshot snapshot);
        void Unavailable(GeoError error);
    }

    /// <summary>场景地理系统门面；Unity 主线程上配置/捕获 Root，采用 WGS84→ECEF→固定 ENU→局部→世界链。</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(100)]
    public sealed partial class GeoReference : MonoBehaviour
    {
        /// <summary>模型局部坐标参考；留空使用世界坐标。完整祖先矩阵仅允许平移、旋转、正统一缩放；初始化后更换需先 Clear。</summary>
        [Tooltip("Model-local Root. Empty means world space. Its complete ancestor matrix must contain only translation, rotation, and positive uniform scale. Clear before changing after initialization.")]
        [SerializeField]
        Transform root;

        /// <summary>模型局部坐标版本标识，建议与 Profile 一致；不是 EPSG 编号。初始化后更换需先 Clear。</summary>
        [Tooltip("Model-local coordinate convention/version ID. Match the Profile; this is not an EPSG code. Clear before changing after initialization.")]
        [SerializeField]
        string localFrameId;
        [SerializeField, HideInInspector]
        bool hasTransformRoot;

        /// <summary>Inspector 候选计算及启用时初始化所使用的标定资产。</summary>
        [Tooltip("Calibration asset used for Inspector candidate calculation and initialization on enable.")]
        [SerializeField]
        GeoCalibrationProfile profile;

        /// <summary>每次启用时尝试从 Profile 初始化。异步加载模型应关闭，并在资源就绪后调用 TryInitialize。</summary>
        [Tooltip("Try initializing from Profile each time this component is enabled. Disable for asynchronous loading and call TryInitialize after prerequisites are ready.")]
        [SerializeField]
        bool initializeOnEnable;

        /// <summary>在 LateUpdate 依次同步位置、朝向、坐标读取；关闭后由业务调用 SynchronizeBindings。</summary>
        [Tooltip("Synchronize positions, orientations, and Reporters in LateUpdate. When disabled, the application calls SynchronizeBindings.")]
        [SerializeField]
        bool synchronizeAutomatically = true;

        /// <summary>选中时绘制参考东、北、上轴的长度，单位现实米，不影响标定。</summary>
        [Tooltip("Length of selected reference east/north/up gizmos in real meters. Does not affect calibration.")]
        [SerializeField]
        float gizmoLengthMeters = 5;
        readonly GeoCoordinateConverter converter = GeoCoordinateConverter.CreateDefault();
        readonly List<IGeoBinding> bindings = new List<IGeoBinding>();
        readonly List<Action> pendingRegistrations = new List<Action>();
        GeoReferenceFrame frame;
        GeoReferenceSnapshot snapshot;
        Matrix4x4 rootMatrix;
        GeoRootPose rootPose;
        bool observedRoot, rootValid, busy;
        int configurationVersion;
        long rootVersion;
        GeoReferenceChangeFlags pendingChanges;

        /// <summary>模型局部空间的参考 Transform；显式 null 使用世界模式，已配置 Root 销毁不自动回退。</summary>
        public Transform Root => root;

        /// <summary>模型局部坐标约定的身份标识，不是 EPSG 编号。</summary>
        public string LocalFrameId => localFrameId;

        /// <summary>是否存在已生效的数学标定解；不等于 Root 当前世界映射一定有效。</summary>
        public bool IsInitialized => frame != null;

        /// <summary>最近捕获/同步时世界映射是否有效；查询实时变换请调用 TryCaptureSnapshot。</summary>
        public bool IsWorldMappingValid => IsInitialized && rootValid;

        /// <summary>捕获的标定版本；成功提交或 Clear 后变化，失败求解不改变。</summary>
        public long CalibrationVersion { get; private set; }

        /// <summary>当前已生效标定的报告，失败重标定不替换它；Clear 后为空。</summary>
        public GeoCalibrationReport CurrentReport { get; private set; }

        /// <summary>最近一次 Root 捕获或同步错误，不代表独立局部数学映射失效。</summary>
        public GeoError LastWorldMappingError { get; private set; }

        /// <summary>位置→朝向→读取完成后发出变化通知；不要在回调内初始化、Clear 或重入同步。</summary>
        public event Action<GeoReferenceChange> Changed;

        /// <summary>是否由 LateUpdate 自动同步绑定；关闭后业务需手动调用 SynchronizeBindings。</summary>
        public bool SynchronizeAutomatically { get => synchronizeAutomatically; set => synchronizeAutomatically = value; }

        void Awake()
        {
            hasTransformRoot |= !ReferenceEquals(root, null);
        }

        void OnEnable()
        {
            hasTransformRoot |= !ReferenceEquals(root, null);
            if (initializeOnEnable && profile != null)
            {
                if (!profile.TryBuildRequest(out var request, out var error) ||
                    !TryInitialize(request, out _, out error))
                {
                    Debug.LogError(error.ToString(), this);
                }
            }
        }

        void LateUpdate()
        {
            if (synchronizeAutomatically)
            {
                SynchronizeBindings();
            }
        }

        internal void Register(IGeoBinding binding)
        {
            if (busy)
            {
                pendingRegistrations.Add(() => Register(binding));
                return;
            }

            if (!bindings.Contains(binding))
            {
                bindings.Add(binding);
            }
        }

        internal void Unregister(IGeoBinding binding)
        {
            if (busy)
            {
                pendingRegistrations.Add(() => Unregister(binding));
                return;
            }

            bindings.Remove(binding);
        }

        bool MutationAllowed(out GeoError error)
        {
            error = default;
            return !busy ||
                GeoError.Fail(
                GeoErrorCode.ReentrantMutation,
                "Do not mutate the reference from binding/event callbacks.",
                out error);
        }

        /// <summary>仅在未初始化时显式选择 Root 或世界模式；已初始化需先 Clear，不移动模型。</summary>
        public bool TrySetRoot(Transform value, out GeoError error)
        {
            if (!MutationAllowed(out error))
            {
                return false;
            }

            if (IsInitialized)
            {
                return GeoError.Fail(GeoErrorCode.RootChangeRequiresClear, "Clear before changing Root.", out error);
            }

            bool has = !ReferenceEquals(value, null);
            if (!GeoRootTransform.TryCapture(value, has, out _, out _, out error))
            {
                return false;
            }

            root = value;
            hasTransformRoot = has;
            configurationVersion++;
            observedRoot = false;
            snapshot = null;
            return true;
        }

        /// <summary>仅在未初始化时更改模型局部坐标身份；更换前需 Clear，待提交旧候选随之失效。</summary>
        public bool TrySetLocalFrameId(string id, out GeoError error)
        {
            if (!MutationAllowed(out error))
            {
                return false;
            }

            if (IsInitialized)
            {
                return GeoError.Fail(
                    GeoErrorCode.LocalFrameIdChangeRequiresClear,
                    "Clear before changing LocalFrameId.",
                    out error);
            }

            localFrameId = id;
            configurationVersion++;
            return true;
        }

        /// <summary>复制输入求解候选并检查 Root/FrameId，不提交标定、不写物体；失败保留当前解。</summary>
        public bool TryPrepareCalibration(
            GeoCalibrationRequest request,
            out GeoCalibrationCandidate candidate,
            out GeoCalibrationReport report,
            out GeoError error)
        {
            candidate = null;
            report = new GeoCalibrationReport();
            if (!MutationAllowed(out error))
            {
                report.Failure = error;
                return false;
            }

            if (request == null)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, "A calibration request is required.");
                report.Failure = error;
                return false;
            }

            if (!string.IsNullOrEmpty(request.Options.ExpectedLocalFrameId) &&
                !string.Equals(request.Options.ExpectedLocalFrameId, localFrameId, StringComparison.Ordinal))
            {
                error = new GeoError(GeoErrorCode.LocalFrameIdMismatch, "Profile and model local frame IDs do not match.");
                report.Failure = error;
                return false;
            }

            hasTransformRoot |= !ReferenceEquals(root, null);
            if (!CaptureRoot(out error))
            {
                report.Failure = error;
                return false;
            }

            if (!GeoCalibrationEngine.TrySolve(request, converter, out var solution, out report, out error))
            {
                return false;
            }

            report.LocalFrameId = localFrameId;
            candidate = new GeoCalibrationCandidate(this, configurationVersion, CalibrationVersion, solution);
            return true;
        }

        /// <summary>检查候选归属、版本、配置身份和 Root 后提交并同步绑定；失败保留旧解。</summary>
        public bool TryApplyCalibration(GeoCalibrationCandidate candidate, out GeoError error)
        {
            if (!MutationAllowed(out error))
            {
                return false;
            }

            if (candidate == null || !ReferenceEquals(candidate.Owner, this) ||
                candidate.BaseCalibrationVersion != CalibrationVersion ||
                candidate.ConfigurationVersion != configurationVersion ||
                !ReferenceEquals(candidate.Root, root) ||
                !string.Equals(candidate.LocalFrameId, localFrameId, StringComparison.Ordinal))
            {
                return GeoError.Fail(
                    GeoErrorCode.StaleCalibrationCandidate,
                    "Candidate belongs to another or older reference configuration.",
                    out error);
            }

            if (!CaptureRoot(out error))
            {
                return false;
            }

            // 前面的检查均完成后才替换生效解：失败路径不得破坏旧报告、版本或绑定姿态。
            frame = candidate.Solution.Frame;
            CurrentReport = candidate.Report;
            CalibrationVersion++;
            snapshot = null;
            pendingChanges |= GeoReferenceChangeFlags.CalibrationChanged;
            SynchronizeBindings();
            return true;
        }

        /// <summary>求解并提交请求；成功刷新绑定，失败保留旧解和 CurrentReport。</summary>
        public bool TryInitialize(GeoCalibrationRequest request, out GeoCalibrationReport report, out GeoError error)
        {
            if (!TryPrepareCalibration(request, out var candidate, out report, out error))
            {
                return false;
            }

            return TryApplyCalibration(candidate, out error);
        }

        /// <summary>清除数学标定并通知绑定失效，保留物体最后姿态；回调内重入会抛异常。</summary>
        public void Clear()
        {
            if (busy)
            {
                throw new InvalidOperationException("Do not clear from binding/event callbacks.");
            }

            frame = null;
            CurrentReport = null;
            snapshot = null;
            CalibrationVersion++;
            pendingChanges |= GeoReferenceChangeFlags.Cleared;
            SynchronizeBindings();
        }

        bool CaptureRoot(out GeoError error)
        {
            hasTransformRoot |= !ReferenceEquals(root, null);
            bool valid = GeoRootTransform.TryCapture(root, hasTransformRoot, out var pose, out var matrix, out error);
            if (!observedRoot || !GeoRootTransform.Same(rootMatrix, matrix))
            {
                rootMatrix = matrix;
                rootVersion++;
                snapshot = null;
                pendingChanges |= GeoReferenceChangeFlags.RootTransformChanged;
            }

            if (!observedRoot || valid != rootValid || error.Code != LastWorldMappingError.Code)
            {
                pendingChanges |= GeoReferenceChangeFlags.RootValidityChanged;
                snapshot = null;
            }

            observedRoot = true;
            rootValid = valid;
            rootPose = pose;
            LastWorldMappingError = error;
            return valid;
        }

        internal bool TryCaptureRootPosition(Vector3 worldPosition, out GeoVector3d local, out GeoError error)
        {
            local = default;
            if (!GeoUnity.Finite(worldPosition))
            {
                error = new GeoError(GeoErrorCode.InvalidNumber, "Position must be finite.");
                return false;
            }

            if (!CaptureRoot(out error))
            {
                return false;
            }

            local = rootPose.ToLocal(GeoUnity.D(worldPosition));
            return true;
        }

        // Descendant markers can be captured without a lossy round trip through world floats.
        internal bool TryCaptureRootPosition(Transform source, out GeoVector3d local, out GeoError error)
        {
            local = default;
            if (source == null)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, "Capture source is required.");
                return false;
            }

            if (!TryCaptureRootPosition(source.position, out local, out error))
            {
                return false;
            }

            if (root != null && source.IsChildOf(root))
            {
                local = GeoVector3d.Zero;
                for (var current = source; current != root; current = current.parent)
                {
                    var scale = current.localScale;
                    var rotation = current.localRotation;
                    var scaled = new GeoVector3d(local.X * scale.x, local.Y * scale.y, local.Z * scale.z);
                    local = GeoUnity.D(current.localPosition) +
                        Matrix3d.FromQuaternion(rotation.w, rotation.x, rotation.y, rotation.z).Apply(scaled);
                }
            }

            if (!local.IsFinite)
            {
                return GeoError.Fail(GeoErrorCode.InvalidNumber, "Captured position must be finite.", out error);
            }

            return true;
        }

        bool RequireFrame(out GeoError error)
        {
            error = default;
            return IsInitialized ||
                GeoError.Fail(GeoErrorCode.NotInitialized, "Initialize the geographic reference first.", out error);
        }

        /// <summary>捕获同一时刻的标定与 Root 映射；状态未变复用快照，失败返回错误。</summary>
        public bool TryCaptureSnapshot(out GeoReferenceSnapshot result, out GeoError error)
        {
            result = null;
            if (!RequireFrame(out error) || !CaptureRoot(out error))
            {
                return false;
            }

            if (snapshot == null)
            {
                snapshot = new GeoReferenceSnapshot(frame, converter, rootPose, CalibrationVersion, rootVersion);
            }

            result = snapshot;
            return true;
        }

        /// <summary>按位置→朝向→Reporter→Changed 同步；同步或回调内重入会抛异常。</summary>
        public void SynchronizeBindings()
        {
            if (busy)
            {
                throw new InvalidOperationException("Binding synchronization cannot be re-entered.");
            }

            busy = true;
            try
            {
                bool available = TryCaptureSnapshot(out var current, out var error);
                // 先定位全部 POI，再让朝向读取新目标位置，最后采样坐标；不是任意父子依赖图求解。
                for (int phase = 0; phase < 3; phase++)
                {
                    foreach (var binding in bindings)
                    {
                        if (binding is UnityEngine.Object obj && obj == null)
                        {
                            continue;
                        }

                        if (binding.Phase != phase)
                        {
                            continue;
                        }

                        try
                        {
                            if (available)
                            {
                                binding.Refresh(current);
                            }
                            else
                            {
                                binding.Unavailable(error);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex, this);
                        }
                    }
                }

                var flags = pendingChanges;
                pendingChanges = GeoReferenceChangeFlags.None;
                if (flags != GeoReferenceChangeFlags.None && Changed != null)
                {
                    var change = new GeoReferenceChange(flags, this, rootVersion);
                    foreach (Action<GeoReferenceChange> handler in Changed.GetInvocationList())
                    {
                        try
                        {
                            handler(change);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex, this);
                        }
                    }
                }
            }
            finally
            {
                // 刷新/事件期间的注册变更延迟处理，避免回调修改正在遍历的集合。
                busy = false;
                if (pendingRegistrations.Count > 0)
                {
                    var pending = pendingRegistrations.ToArray();
                    pendingRegistrations.Clear();
                    foreach (var action in pending)
                    {
                        action();
                    }
                }
            }
        }

        /// <summary>标准化输入后转换为 Root 局部双精度位置；不要求 Root 世界矩阵合法。</summary>
        public bool TryGeoToLocal(in GeoCoordinate coordinate, out GeoVector3d localPosition, out GeoError error)
        {
            localPosition = default;
            return RequireFrame(out error) && converter.TryToWgs84(coordinate, out var wgs, out error) &&
                frame.TryWgs84ToLocal(wgs, out localPosition, out error);
        }

        /// <summary>将 WGS84 椭球高转换为 Root 局部双精度位置，并检查范围；不读取 Root 世界姿态。</summary>
        public bool TryWgs84ToLocal(in Wgs84Coordinate coordinate, out GeoVector3d localPosition, out GeoError error)
        {
            localPosition = default;
            return RequireFrame(out error) && frame.TryWgs84ToLocal(coordinate, out localPosition, out error);
        }

        /// <summary>将 Root 局部双精度位置反算为 WGS84 椭球高，并检查范围。</summary>
        public bool TryLocalToWgs84(in GeoVector3d localPosition, out Wgs84Coordinate coordinate, out GeoError error)
        {
            coordinate = default;
            return RequireFrame(out error) && frame.TryLocalToWgs84(localPosition, out coordinate, out error);
        }

        /// <summary>获取 Root 局部参考东、北、上单位方向；不要求 Root 世界矩阵合法。</summary>
        public bool TryGetLocalAxes(out GeoAxes3d axes, out GeoError error)
        {
            axes = default;
            if (!RequireFrame(out error))
            {
                return false;
            }

            axes = frame.LocalAxes;
            return true;
        }

        void OnDrawGizmosSelected()
        {
            if (!TryCaptureSnapshot(out var s, out _))
            {
                return;
            }

            if (!s.TryWgs84ToWorld(frame.Origin, out var p, out _))
            {
                return;
            }

            float length = (float)(s.WorldUnitsPerMeter * Math.Max(.1, gizmoLengthMeters));
            Gizmos.color = Color.red;
            Gizmos.DrawRay(p, s.WorldAxes.East * length);
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(p, s.WorldAxes.North * length);
            Gizmos.color = Color.green;
            Gizmos.DrawRay(p, s.WorldAxes.Up * length);
        }
    }
}
