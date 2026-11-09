using System;
using UnityEngine;

namespace SceneGeography
{
    /// <summary>只读采样 Transform 的实际世界旋转，输出相对参考地理基底的姿态，不控制物体。</summary>
    public sealed class GeoOrientationReporter : GeoBindingBehaviour
    {
        /// <summary>采样目标；初始为空时读取自身，显式目标销毁后报错而不回退。</summary>
        [Tooltip("Initially empty reads self. A destroyed explicit target reports an error instead of falling back to self. Switch targets through Configure at runtime.")]
        public Transform target;

        /// <summary>从实际旋转右侧移除的模型轴修正，单位四元数；identity 表示直接测量 Transform 轴。</summary>
        [Tooltip("Unit quaternion removed before measurement. Use the same Model Correction as GeoOrientation to report its logical attitude; identity measures the Transform's own axes.")]
        public Quaternion modelCorrection = Quaternion.identity;

        /// <summary>自动采样间隔，秒；0 表示每次同步均采样，TrySample 不受此间隔限制。</summary>
        [Tooltip("Automatic sampling interval in seconds. Zero samples on every Reference synchronization. TrySample samples immediately.")]
        [Min(0)]
        public float samplingIntervalSeconds = .2f;

        [SerializeField, HideInInspector]
        bool hasExplicitTarget;
        double nextSample;

        /// <summary>最近一次采样是否有效；false 时 LastValue 可能仍为历史值。</summary>
        public bool HasValue { get; private set; }

        /// <summary>最后成功采样的姿态；先检查 HasValue，再检查其中的 HasHeading/HasRoll。</summary>
        public GeoAttitude LastValue { get; private set; }

        /// <summary>成功采样后通知；订阅者异常被隔离，不允许回调内重入 Reference 变更。</summary>
        public event Action<GeoAttitude> OrientationUpdated;
        protected override int BindingPhase => 2;

        /// <summary>绑定 Reference 和采样目标，直接读取其轴，不做模型修正。</summary>
        public void Configure(GeoReference reference, Transform source)
        {
            Configure(reference, source, Quaternion.identity);
        }

        /// <summary>绑定采样目标和要移除的单位模型修正；非法修正抛出异常且不改变既有配置。</summary>
        public void Configure(GeoReference reference, Transform source, Quaternion correction)
        {
            if (!GeoUnity.Unit(correction))
            {
                throw new ArgumentException("Model correction must be a finite unit quaternion.", nameof(correction));
            }

            SetReference(reference);
            target = source;
            hasExplicitTarget = !ReferenceEquals(source, null);
            modelCorrection = correction;
            nextSample = 0;
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

        /// <summary>立即采样，不受间隔限制；失败使 HasValue 为 false，保留旧 LastValue。</summary>
        public bool TrySample(out GeoAttitude attitude, out GeoError error)
        {
            attitude = default;
            if (!TrySnapshot(out var snapshot, out error))
            {
                HasValue = false;
                return false;
            }

            return Sample(snapshot, out attitude, out error);
        }

        bool Sample(GeoReferenceSnapshot snapshot, out GeoAttitude attitude, out GeoError error)
        {
            attitude = default;
            hasExplicitTarget |= !ReferenceEquals(target, null);
            if (hasExplicitTarget && target == null)
            {
                error = new GeoError(GeoErrorCode.MissingLookTarget, "Orientation reporting target was destroyed.");
            }
            else if (!GeoUnity.Unit(modelCorrection))
            {
                error = new GeoError(GeoErrorCode.InvalidOrientationRequest, "Model correction must be a finite unit quaternion.");
            }
            else
            {
                var source = hasExplicitTarget ? target : transform;
                var rotation = source.rotation * Quaternion.Inverse(modelCorrection);
                if (snapshot.TryWorldRotationToAttitude(rotation, out attitude, out error))
                {
                    HasValue = true;
                    LastValue = attitude;
                    LastError = default;
                    Notify(attitude);
                    return true;
                }
            }

            HasValue = false;
            LastError = error;
            return false;
        }

        void Notify(GeoAttitude attitude)
        {
            if (OrientationUpdated == null)
            {
                return;
            }

            foreach (Action<GeoAttitude> handler in OrientationUpdated.GetInvocationList())
            {
                try
                {
                    handler(attitude);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }
            }
        }

        protected override void Unavailable(GeoError error)
        {
            base.Unavailable(error);
            HasValue = false;
        }
    }
}
