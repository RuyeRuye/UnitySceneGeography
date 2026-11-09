using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace SceneGeography
{
    /// <summary>只读地把指定 Transform 世界位置采样为明确参考的地理坐标，不修改位置和旋转。</summary>
    public sealed class GeoPositionReporter : GeoBindingBehaviour
    {
        /// <summary>读取的 Transform；未指定时读取自身。已指定目标销毁后报错，不自动回退。运行时切换用 Configure。</summary>
        [Tooltip("Transform to read; initially empty reads self. Destruction of an assigned target reports an error without fallback. Switch through Configure at runtime.")]
        public Transform target;

        /// <summary>输出水平坐标类型；必须明确选择。GCJ/BD 范围外转换失败。</summary>
        [Tooltip("Output horizontal coordinate type; explicit selection required. GCJ/BD conversion fails outside the supported region.")]
        public GeoCoordinateType outputType = GeoCoordinateType.Wgs84;

        /// <summary>输出高度参考；Orthometric 当前没有高程模型，转换会失败。</summary>
        [Tooltip("Output height reference. Orthometric currently fails because no height model is installed.")]
        public GeoHeightKind outputHeight = GeoHeightKind.Ellipsoidal;

        /// <summary>相对高度输出的零点绝对椭球高，单位米，仅 RelativeToEllipsoidZero 使用。</summary>
        [Tooltip("Absolute ellipsoid height of the relative-height output zero, in meters. Only used by RelativeToEllipsoidZero.")]
        public double ellipsoidZeroMeters;

        /// <summary>相对高度零点是否为假定值；不增加实际高程精度。</summary>
        [Tooltip("Whether the relative-height zero is assumed. Does not improve actual height accuracy.")]
        public bool assumedHeightZero;

        /// <summary>Orthometric 高程模型标识；当前未安装模型，仅填写名称不能启用转换。</summary>
        [Tooltip("Orthometric height-model ID. No model is installed; entering a name alone does not enable conversion.")]
        public string heightModelId;

        /// <summary>自动采样间隔，单位秒；0 表示每次 Reference 同步均采样。TrySample 不受此间隔限制。</summary>
        [Tooltip("Automatic sampling interval in seconds. Zero samples on every Reference synchronization. TrySample bypasses this interval.")]
        [FormerlySerializedAs("samplingInterval"), Min(0)]
        public float samplingIntervalSeconds = .2f;
        [SerializeField, HideInInspector]
        bool hasExplicitTarget;
        double nextSample;

        /// <summary>最近一次采样是否有效；false 时 LastValue 可能仍为旧值。</summary>
        public bool HasValue { get; private set; }

        /// <summary>最后成功采样的坐标；读取前检查 HasValue。</summary>
        public GeoCoordinate LastValue { get; private set; }

        /// <summary>成功采样后通知，订阅者异常被隔离；不能在回调中重入 Reference 变更。</summary>
        public event Action<GeoCoordinate> PositionUpdated;
        protected override int BindingPhase => 2;

        /// <summary>配置绑定的 Reference 与业务输入，标记下一次同步更新；非法参数抛出异常。</summary>
        public void Configure(GeoReference reference, Transform source, GeoCoordinateReference output)
        {
            if (!output.TryValidate(out var error))
            {
                throw new ArgumentException(error.Message);
            }

            SetReference(reference);
            target = source;
            hasExplicitTarget = !ReferenceEquals(source, null);
            outputType = output.HorizontalType;
            outputHeight = output.HeightReference.Kind;
            ellipsoidZeroMeters = output.HeightReference.EllipsoidZeroMeters;
            assumedHeightZero = output.HeightReference.IsAssumed;
            heightModelId = output.HeightReference.ModelId;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            hasExplicitTarget |= !ReferenceEquals(target, null);
            nextSample = 0;
        }

        protected override void Refresh(GeoReferenceSnapshot snapshot)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!Dirty && now < nextSample)
            {
                return;
            }

            Sample(snapshot, out _, out _);
            nextSample = now + Math.Max(0, samplingIntervalSeconds);
            Dirty = false;
        }

        /// <summary>忽略采样间隔立即读取一次地理坐标；失败清除 HasValue，但保留旧 LastValue。</summary>
        public bool TrySample(out GeoCoordinate coordinate, out GeoError error)
        {
            coordinate = default;
            if (!TrySnapshot(out var s, out error))
            {
                HasValue = false;
                return false;
            }

            return Sample(s, out coordinate, out error);
        }

        bool Sample(GeoReferenceSnapshot snapshot, out GeoCoordinate coordinate, out GeoError error)
        {
            coordinate = default;
            error = default;
            hasExplicitTarget |= !ReferenceEquals(target, null);
            if (hasExplicitTarget && target == null)
            {
                error = new GeoError(GeoErrorCode.MissingLookTarget, "Reporting target was destroyed.");
                LastError = error;
                HasValue = false;
                return false;
            }

            try
            {
                GeoHeightReference height;
                switch (outputHeight)
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
                        throw new ArgumentException("Select the output height reference.");
                }

                var output = new GeoCoordinateReference(outputType, height);
                if (!snapshot.TryWorldToCoordinate((hasExplicitTarget ? target : transform).position, output, out coordinate, out error))
                {
                    HasValue = false;
                    LastError = error;
                    return false;
                }
            }
            catch (ArgumentException ex)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, ex.Message);
                HasValue = false;
                LastError = error;
                return false;
            }

            HasValue = true;
            LastValue = coordinate;
            LastError = default;
            if (PositionUpdated != null)
            {
                foreach (Action<GeoCoordinate> handler in PositionUpdated.GetInvocationList())
                {
                    try
                    {
                        handler(coordinate);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex, this);
                    }
                }
            }

            return true;
        }

        protected override void Unavailable(GeoError error)
        {
            base.Unavailable(error);
            HasValue = false;
        }
    }
}
