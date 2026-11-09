using System;
using UnityEngine;

namespace SceneGeography
{
    /// <summary>地理绑定共有生命周期；启用时注册，禁用时停止自动写入，异常与错误不强制归零。</summary>
    public abstract class GeoBindingBehaviour : MonoBehaviour, IGeoBinding
    {
        /// <summary>绑定的地理坐标系；运行时通过 Configure 切换。未初始化或 Root 无效时保留最后姿态并报告错误。</summary>
        [Tooltip("Bound geographic Reference; switch through Configure at runtime. Missing calibration or invalid Root preserves the last pose and reports an error.")]
        [SerializeField]
        GeoReference reference;
        GeoReference registered;
        protected bool Dirty = true;

        /// <summary>此组件绑定的地理坐标系。</summary>
        public GeoReference Reference => reference;

        /// <summary>最近一次绑定/采样的错误，成功应用后清除。</summary>
        public GeoError LastError { get; protected set; }
        protected abstract int BindingPhase { get; }

        int IGeoBinding.Phase => BindingPhase;

        protected virtual void OnEnable()
        {
            Dirty = true;
            Register();
        }

        protected virtual void OnDisable()
        {
            if (registered != null)
            {
                registered.Unregister(this);
            }

            registered = null;
        }

        protected virtual void OnValidate()
        {
            Dirty = true;
        }

        protected virtual void Update()
        {
            if (registered != reference)
            {
                if (registered != null)
                {
                    registered.Unregister(this);
                }

                registered = null;
                Register();
                Dirty = true;
            }

            if (reference == null)
            {
                Unavailable(new GeoError(GeoErrorCode.MissingReference, "Assign a geographic reference."));
            }
        }

        void Register()
        {
            if (isActiveAndEnabled && reference != null)
            {
                registered = reference;
                registered.Register(this);
            }
        }

        protected void SetReference(GeoReference value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (registered != null)
            {
                registered.Unregister(this);
            }

            registered = null;
            reference = value;
            Register();
            Dirty = true;
        }

        void IGeoBinding.Refresh(GeoReferenceSnapshot snapshot)
        {
            if (isActiveAndEnabled && reference == registered)
            {
                Refresh(snapshot);
            }
        }

        protected abstract void Refresh(GeoReferenceSnapshot snapshot);
        void IGeoBinding.Unavailable(GeoError error)
        {
            if (isActiveAndEnabled)
            {
                Unavailable(error);
            }
        }

        protected virtual void Unavailable(GeoError error)
        {
            LastError = error;
            Dirty = true;
        }

        protected bool TrySnapshot(out GeoReferenceSnapshot snapshot, out GeoError error)
        {
            snapshot = null;
            if (reference == null)
            {
                error = new GeoError(GeoErrorCode.MissingReference, "Assign a geographic reference.");
                LastError = error;
                return false;
            }

            if (reference.TryCaptureSnapshot(out snapshot, out error))
            {
                return true;
            }

            LastError = error;
            return false;
        }
    }

    internal static class GeoTransformUtility
    {
        internal static bool Validate(Transform target, GeoReference reference, out GeoError error)
        {
            error = default;
            if (reference.Root != null && reference.Root.IsChildOf(target))
            {
                error = new GeoError(
                    GeoErrorCode.CyclicTransformBinding,
                    "A geographic binding cannot write Root or its ancestors.");
                return false;
            }

            if (target.parent != null &&
                !GeoRootTransform.TryMatrix(target.parent.localToWorldMatrix, out _, out var parentError))
            {
                error = new GeoError(GeoErrorCode.InvalidTargetParentTransform, parentError.Message);
                return false;
            }

            return true;
        }
    }
}
