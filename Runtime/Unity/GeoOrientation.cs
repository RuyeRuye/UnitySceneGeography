using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace SceneGeography
{
    /// <summary>根据地理航向或目标绑定 Transform 世界旋转；默认 +Z 前、+Y 上，先地理旋转再乘模型修正。</summary>
    [DisallowMultipleComponent]
    public sealed class GeoOrientation : GeoBindingBehaviour
    {
        /// <summary>朝向来源。勾选 Use Transform Target 时由 Transform 目标覆盖。</summary>
        [Tooltip("Orientation source. Use Transform Target overrides this when enabled.")]
        public GeoOrientationKind kind = GeoOrientationKind.Heading;

        /// <summary>航向角：北 0、东 90、南 180、西 270 度，仅 Heading 模式使用。</summary>
        [Tooltip("Heading in degrees: north 0, east 90, south 180, west 270. Used only in Heading mode.")]
        public double headingDegrees;

        /// <summary>GeoTarget 模式的目标经纬高；必须明确参考系。</summary>
        [Tooltip("Target geographic coordinate for GeoTarget mode, with an explicit reference.")]
        public GeoCoordinateSettings targetCoordinate = new GeoCoordinateSettings();

        /// <summary>WorldTarget 模式的固定世界目标位置，单位 Unity 世界单位。</summary>
        [Tooltip("Fixed world target position for WorldTarget mode, in Unity world units.")]
        public Vector3 targetWorldPosition;

        /// <summary>持续追踪的场景目标；销毁后保留最后旋转并报错，不回退为其他目标。</summary>
        [Tooltip("Continuously tracked scene target. Destruction preserves the last rotation and reports an error; no fallback target is selected.")]
        public Transform transformTarget;

        /// <summary>启用时优先使用 Transform Target，Kind 和其他来源暂不生效。</summary>
        [Tooltip("Use Transform Target instead of Kind and other sources while enabled.")]
        public bool useTransformTarget;

        /// <summary>Horizontal 投影到参考 Up 的水平面；Full3D 保留高度差。固定航向模式不使用。</summary>
        [Tooltip("Horizontal projects onto the reference Up-horizontal plane. Full3D retains height differences. Not used for fixed heading.")]
        public GeoLookMode lookMode;

        /// <summary>基础朝向上的偏航角，正值向右/顺时针，单位度。</summary>
        [Tooltip("Yaw offset from the base orientation in degrees. Positive turns right/clockwise.")]
        [FormerlySerializedAs("yawDegrees")]
        public double yawOffsetDegrees;

        /// <summary>基础朝向上的俯仰偏角，正值抬头，单位度。</summary>
        [Tooltip("Pitch offset from the base orientation in degrees. Positive means pitch up.")]
        [FormerlySerializedAs("pitchUpDegrees")]
        public double pitchUpOffsetDegrees;

        /// <summary>沿前向轴的侧倾偏角，正值顺时针，单位度。</summary>
        [Tooltip("Roll offset around the forward axis in degrees. Positive means clockwise.")]
        [FormerlySerializedAs("rollClockwiseDegrees")]
        public double rollClockwiseOffsetDegrees;

        /// <summary>地理旋转之后应用的模型局部轴修正，必须是单位四元数；默认 +Z 前、+Y 上。也可旋转视觉子节点。</summary>
        [Tooltip("Model-local axis correction applied after geographic rotation. Must be a unit quaternion; default +Z forward, +Y up. Alternatively rotate the visual child.")]
        public Quaternion modelCorrection = Quaternion.identity;

        /// <summary>是否自动启动朝向跟随；关闭时手动成功 TryApply 一次后启动。停止自动跟随请禁用组件。</summary>
        [Tooltip("Start orientation following automatically. If disabled, one successful explicit TryApply starts it. Disable the component to stop automatic following.")]
        [FormerlySerializedAs("applyOnEnable")]
        public bool startAutomatically = true;
        bool hasApplied;
        long calibration = -1, rootVersion = -1;
        Vector3 previousSource, previousTarget;
        Matrix4x4 previousParent;
        protected override int BindingPhase => 1;

        /// <summary>配置绑定的 Reference 与业务输入，标记下一次同步更新；非法参数抛出异常。</summary>
        public void Configure(GeoReference reference, GeoOrientationRequest value, Quaternion correction)
        {
            if (value.Kind == GeoOrientationKind.Unspecified)
            {
                throw new ArgumentException("Specify an orientation request.");
            }

            if (!GeoUnity.Unit(correction))
            {
                throw new ArgumentException("Model correction must be a unit quaternion.");
            }

            SetReference(reference);
            modelCorrection = correction;
            SetRequest(value);
        }

        /// <summary>提交固定航向或地理/世界目标请求，关闭 Transform 目标模式并标记待同步，不立即写旋转。</summary>
        public void SetRequest(GeoOrientationRequest value)
        {
            if (value.Kind == GeoOrientationKind.Unspecified)
            {
                throw new ArgumentException("Specify an orientation request.");
            }

            kind = value.Kind;
            headingDegrees = value.Heading.Degrees;
            if (kind == GeoOrientationKind.GeoTarget)
            {
                targetCoordinate = GeoCoordinateSettings.From(value.TargetGeo);
            }

            targetWorldPosition = value.TargetWorld;
            lookMode = value.LookMode;
            yawOffsetDegrees = value.Offset.YawDegrees;
            pitchUpOffsetDegrees = value.Offset.PitchUpDegrees;
            rollClockwiseOffsetDegrees = value.Offset.RollClockwiseDegrees;
            useTransformTarget = false;
            transformTarget = null;
            Dirty = true;
        }

        /// <summary>切换为追踪 Transform 目标；目标销毁后明确失败，不回退到固定位置。</summary>
        public void SetTransformTarget(Transform target, GeoLookMode mode, GeoRotationOffset offset = default)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            transformTarget = target;
            useTransformTarget = true;
            lookMode = mode;
            yawOffsetDegrees = offset.YawDegrees;
            pitchUpOffsetDegrees = offset.PitchUpDegrees;
            rollClockwiseOffsetDegrees = offset.RollClockwiseDegrees;
            Dirty = true;
        }

        bool Build(out GeoOrientationRequest value, out GeoError error)
        {
            value = default;
            error = default;
            if (!GeoUnity.Unit(modelCorrection))
            {
                error = new GeoError(GeoErrorCode.InvalidModelCorrection, "Model correction must be a unit quaternion.");
                return false;
            }

            try
            {
                var offset = new GeoRotationOffset(yawOffsetDegrees, pitchUpOffsetDegrees, rollClockwiseOffsetDegrees);
                if (useTransformTarget)
                {
                    if (transformTarget == null)
                    {
                        error = new GeoError(GeoErrorCode.MissingLookTarget, "Transform target is missing or destroyed.");
                        return false;
                    }

                    value = GeoOrientationRequest.ForWorldTarget(transformTarget.position, lookMode, offset);
                    return true;
                }

                switch (kind)
                {
                    case GeoOrientationKind.Heading:
                        value = GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(headingDegrees), offset);
                        break;
                    case GeoOrientationKind.WorldTarget:
                        value = GeoOrientationRequest.ForWorldTarget(targetWorldPosition, lookMode, offset);
                        break;
                    case GeoOrientationKind.GeoTarget:
                        if (targetCoordinate == null)
                        {
                            error = new GeoError(GeoErrorCode.InvalidConfiguration, "Target coordinate is missing.");
                            return false;
                        }

                        if (!targetCoordinate.TryBuild(out var target, out error))
                        {
                            return false;
                        }

                        value = GeoOrientationRequest.ForGeoTarget(target, lookMode, offset);
                        break;
                    default:
                        error = new GeoError(GeoErrorCode.InvalidOrientationRequest, "Select an orientation mode.");
                        return false;
                }

                return true;
            }
            catch (ArgumentException ex)
            {
                error = new GeoError(GeoErrorCode.InvalidOrientationRequest, ex.Message);
                return false;
            }
        }

        /// <summary>使用当前配置立即更新一次世界旋转；失败保留原旋转。组件禁用时也可显式调用。</summary>
        public bool TryApply(out GeoError error) => TrySnapshot(out var s, out error) && Apply(s, out error);
        bool Apply(GeoReferenceSnapshot s, out GeoError error)
        {
            if (!Build(out var value, out error) || !GeoTransformUtility.Validate(transform, Reference, out error) ||
                !s.TryGetWorldRotation(transform.position, value, out var rotation, out error))
            {
                LastError = error;
                return false;
            }

            transform.rotation = rotation * modelCorrection;
            LastError = default;
            Dirty = false;
            hasApplied = true;
            calibration = s.CalibrationVersion;
            rootVersion = s.RootPoseVersion;
            previousSource = transform.position;
            previousTarget = transformTarget == null ? default : transformTarget.position;
            previousParent = transform.parent == null ? Matrix4x4.identity : transform.parent.localToWorldMatrix;
            return true;
        }

        protected override void Refresh(GeoReferenceSnapshot s)
        {
            if (!startAutomatically && !hasApplied)
            {
                return;
            }

            var parent = transform.parent == null ? Matrix4x4.identity : transform.parent.localToWorldMatrix;
            bool moving = kind != GeoOrientationKind.Heading || useTransformTarget;
            if (Dirty || calibration != s.CalibrationVersion || rootVersion != s.RootPoseVersion ||
                !GeoRootTransform.Same(parent, previousParent) ||
                (moving &&
                (transform.position != previousSource ||
                (transformTarget != null && transformTarget.position != previousTarget) ||
                (useTransformTarget && transformTarget == null))))
            {
                Apply(s, out _);
            }
        }
    }
}
