using System;
using UnityEngine;

namespace SceneGeography
{
    /// <summary>采集 Transform 世界位置相对 Reference.Root 的局部控制点；移动 Marker 不会自动更新已有 Profile。</summary>
    public sealed class GeoControlPointMarker : MonoBehaviour
    {
        /// <summary>采集时使用的坐标系；世界位置会换算到它的 Root 局部空间。</summary>
        [Tooltip("Reference used for capture; world position is converted into its Root-local space.")]
        public GeoReference reference;

        /// <summary>控制点唯一且非空的标识，用于残差报告。</summary>
        [Tooltip("Unique, nonempty control-point ID used in residual reports.")]
        public string pointId;

        /// <summary>Fit 参与标定；Check 仅独立验证，不影响拟合和自动原点。</summary>
        [Tooltip("Fit participates in solving. Check independently validates without affecting the fit or automatic origin.")]
        public GeoControlPointRole role;

        /// <summary>大于零的相对权重；最大残差不加权，通常保持 1。</summary>
        [Tooltip("Positive relative weight. Maximum residual is not weighted. Usually leave at 1.")]
        public double weight = 1;

        /// <summary>Marker 当前位置对应的现实经纬高，必须声明水平类型和高度参考。</summary>
        [Tooltip("Real-world coordinate corresponding to the Marker's current position, with explicit horizontal and height references.")]
        public GeoCoordinateSettings coordinate = new GeoCoordinateSettings();

        /// <summary>将当前世界位置换算为 Reference.Root 局部位置并创建控制点，未初始化也可采集；不直接保存 Profile。</summary>
        public bool TryCapture(out GeoControlPoint point, out GeoError error)
        {
            point = null;
            error = default;
            if (reference == null)
            {
                error = new GeoError(GeoErrorCode.MissingReference, "Assign a geographic reference.");
                return false;
            }

            if (coordinate == null || !coordinate.TryBuild(out var geo, out error))
            {
                return false;
            }

            if (!reference.TryCaptureRootPosition(transform, out var local, out error))
            {
                return false;
            }

            try
            {
                point = new GeoControlPoint(pointId, local, geo, weight, role);
                return true;
            }
            catch (ArgumentException ex)
            {
                error = new GeoError(GeoErrorCode.InvalidControlPoint, ex.Message, pointId);
                return false;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = role == GeoControlPointRole.Fit ? Color.cyan : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, .3f);
        }
    }
}
