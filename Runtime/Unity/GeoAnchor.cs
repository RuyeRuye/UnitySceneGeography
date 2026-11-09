using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace SceneGeography
{
    /// <summary>OnChange 在配置/映射变化时定位；Continuous 每次 Reference 同步定位，不适合与玩家移动共同控制位置。</summary>
    public enum GeoAnchorUpdatePolicy
    {
        OnChange,
        Continuous
    }

    /// <summary>把经纬高及 ENU 现实米偏移绑定到 Transform 世界位置；失效时保留最后姿态，不能写 Root 或祖先。</summary>
    [DisallowMultipleComponent]
    public sealed class GeoAnchor : GeoBindingBehaviour
    {
        /// <summary>锚点原始经纬高；必须明确水平类型和高度参考。运行时使用 SetPlacement 更新。</summary>
        [Tooltip("Original geographic coordinate with explicit horizontal and height references. At runtime, update through SetPlacement.")]
        public GeoCoordinateSettings coordinate = new GeoCoordinateSettings();

        /// <summary>相对原始坐标向东的偏移，单位现实米；负值向西，不是世界 X 偏移。</summary>
        [Tooltip("Eastward offset from the original coordinate in real meters. Negative means west; this is not world X.")]
        [FormerlySerializedAs("eastMeters")]
        public double eastOffsetMeters;

        /// <summary>相对原始坐标向北的偏移，单位现实米；负值向南，不是世界 Z 偏移。</summary>
        [Tooltip("Northward offset from the original coordinate in real meters. Negative means south; this is not world Z.")]
        [FormerlySerializedAs("northMeters")]
        public double northOffsetMeters;

        /// <summary>沿参考地理 Up 的偏移，单位现实米；负值向下，不改变源坐标高度。</summary>
        [Tooltip("Offset along reference geographic Up in real meters. Negative means down; does not modify the source height.")]
        [FormerlySerializedAs("upMeters")]
        public double upOffsetMeters;

        /// <summary>OnChange 仅在配置或映射变化时定位；Continuous 每次同步写入，勿与玩家移动脚本竞争。</summary>
        [Tooltip("OnChange positions only when inputs or mapping change. Continuous writes on each synchronization; do not combine with a player movement script.")]
        public GeoAnchorUpdatePolicy updatePolicy;

        /// <summary>是否自动启动位置跟随；关闭时手动成功 TryApply 一次后启动。停止自动跟随请禁用组件。</summary>
        [Tooltip("Start position following automatically. If disabled, one successful explicit TryApply starts it. Disable the component to stop automatic following.")]
        [FormerlySerializedAs("applyOnEnable")]
        public bool startAutomatically = true;
        GeoPlacement placement;
        bool hasApplied;
        long calibration = -1, rootVersion = -1;
        Transform previousParent;
        Matrix4x4 parentMatrix;

        /// <summary>读取并验证当前放置配置，不读取或写入 Transform；失败返回错误，避免暴露过期缓存。</summary>
        public bool TryGetPlacement(out GeoPlacement value, out GeoError error)
        {
            bool success = Build(out error);
            value = success ? placement : default;
            return success;
        }

        /// <summary>最近应用是否有效；失去可用映射后为 false，不表示应重置位置。</summary>
        public bool HasAppliedPosition { get; private set; }
        protected override int BindingPhase => 0;

        /// <summary>配置绑定的 Reference 与业务输入，标记下一次同步更新；非法参数抛出异常。</summary>
        public void Configure(GeoReference reference, GeoPlacement value)
        {
            ValidatePlacement(value);
            SetReference(reference);
            SetPlacement(value);
        }

        /// <summary>提交地理坐标和 ENU 现实米偏移，更新 Inspector DTO 并标记待同步；不立即写 Transform。</summary>
        public void SetPlacement(GeoPlacement value)
        {
            ValidatePlacement(value);
            placement = value;
            coordinate = GeoCoordinateSettings.From(value.Coordinate);
            eastOffsetMeters = value.Offset.EastMeters;
            northOffsetMeters = value.Offset.NorthMeters;
            upOffsetMeters = value.Offset.UpMeters;
            Dirty = true;
        }

        static void ValidatePlacement(GeoPlacement value)
        {
            if (!value.Coordinate.TryValidate(out var error))
            {
                throw new ArgumentException(error.Message);
            }

            if (!value.Offset.EngineVector.IsFinite)
            {
                throw new ArgumentException("Offset must be finite.");
            }
        }

        bool Build(out GeoError error)
        {
            error = default;
            if (coordinate == null)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, "Coordinate settings are missing.");
                return false;
            }

            if (!coordinate.TryBuild(out var value, out error))
            {
                return false;
            }

            if (!GeoMath.Finite(eastOffsetMeters) || !GeoMath.Finite(northOffsetMeters) ||
                !GeoMath.Finite(upOffsetMeters))
            {
                error = new GeoError(GeoErrorCode.InvalidNumber, "Offset must be finite.");
                return false;
            }

            placement = new GeoPlacement(value, new GeoEnuOffset(eastOffsetMeters, northOffsetMeters, upOffsetMeters));
            return true;
        }

        /// <summary>使用当前配置立即更新一次世界位置；失败保留原位置。组件禁用时也可显式调用。</summary>
        public bool TryApply(out GeoError error)
        {
            if (!TrySnapshot(out var s, out error))
            {
                HasAppliedPosition = false;
                return false;
            }

            return Apply(s, out error);
        }

        bool Apply(GeoReferenceSnapshot s, out GeoError error)
        {
            if (!Build(out error) || !GeoTransformUtility.Validate(transform, Reference, out error) ||
                !s.TryGetWorldPosition(placement, out var p, out error))
            {
                LastError = error;
                HasAppliedPosition = false;
                return false;
            }

            transform.position = p;
            LastError = default;
            HasAppliedPosition = true;
            hasApplied = true;
            Dirty = false;
            calibration = s.CalibrationVersion;
            rootVersion = s.RootPoseVersion;
            previousParent = transform.parent;
            parentMatrix = previousParent == null ? Matrix4x4.identity : previousParent.localToWorldMatrix;
            return true;
        }

        protected override void Refresh(GeoReferenceSnapshot s)
        {
            var parent = transform.parent;
            var matrix = parent == null ? Matrix4x4.identity : parent.localToWorldMatrix;
            if (!startAutomatically && !hasApplied)
            {
                return;
            }

            if (Dirty || updatePolicy == GeoAnchorUpdatePolicy.Continuous || calibration != s.CalibrationVersion ||
                rootVersion != s.RootPoseVersion ||
                previousParent != parent ||
                !GeoRootTransform.Same(matrix, parentMatrix))
            {
                Apply(s, out _);
            }
        }

        protected override void Unavailable(GeoError error)
        {
            base.Unavailable(error);
            HasAppliedPosition = false;
        }
    }
}
