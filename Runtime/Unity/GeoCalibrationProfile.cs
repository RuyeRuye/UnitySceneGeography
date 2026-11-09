using System;
using System.Collections.Generic;
using UnityEngine;

namespace SceneGeography
{
    /// <summary>可序列化的标定输入资产，不保存旧求解矩阵或场景 Root 引用；运行时仍需初始化。</summary>
    [CreateAssetMenu(menuName = "Scene Geography/Calibration Profile")]
    public sealed class GeoCalibrationProfile : ScriptableObject
    {
        /// <summary>配置格式版本，当前仅支持 1，请勿手动更改。</summary>
        [Tooltip("Configuration schema version. Only version 1 is supported; do not edit manually.")]
        public int schemaVersion = 1;

        /// <summary>模型局部坐标约定标识，建议必填且与 GeoReference 一致。</summary>
        [Tooltip("Model-local coordinate convention ID. Set it explicitly and match the Reference.")]
        public string localFrameId;

        /// <summary>SingleAnchor：单点方向与固定比例；Upright：已知 Up；Spatial：完整三维相似变换。</summary>
        [Tooltip("SingleAnchor: known direction and fixed scale; Upright: known Up; Spatial: full 3D similarity transform.")]
        public GeoCalibrationMode mode = GeoCalibrationMode.Upright;

        /// <summary>Root 局部位置与现实坐标的对应点。Id 唯一；Fit 求解，Check 独立验收。</summary>
        [Tooltip("Corresponding Root-local model positions and geographic coordinates. IDs must be unique. Fit solves; Check independently validates.")]
        public GeoControlPointSettings[] points = Array.Empty<GeoControlPointSettings>();

        /// <summary>Root 局部空间中的现实 Up，通常为 (0,1,0)。SingleAnchor/Upright 使用，Spatial 忽略。</summary>
        [Tooltip("Real-world Up in Root-local space, usually (0,1,0). Used by SingleAnchor/Upright; ignored by Spatial.")]
        public Vector3 localUp = Vector3.up;

        /// <summary>单点模式中已知地理航向的 Root 局部方向向量；先投影到 Up 水平面，不能与 Up 平行。</summary>
        [Tooltip("Root-local direction with a known geographic heading in SingleAnchor mode. Projected onto the Up-horizontal plane; must not be parallel to Up.")]
        public Vector3 localHeadingDirection = Vector3.forward;

        /// <summary>单点方向向量对应的航向角，北 0、东 90、南 180、西 270 度。</summary>
        [Tooltip("Geographic heading for the SingleAnchor direction: north 0, east 90, south 180, west 270 degrees.")]
        public double boundHeadingDegrees;

        /// <summary>Upright/Spatial 是否估算正统一比例；SingleAnchor 始终使用固定比例。</summary>
        [Tooltip("Estimate positive uniform scale in Upright/Spatial. SingleAnchor always uses fixed scale.")]
        public bool estimateScale = true;

        /// <summary>每现实米对应的 Root 局部单位数，必须正数；单点或关闭 Estimate Scale 时生效。</summary>
        [Tooltip("Positive Root-local units per real meter. Used by SingleAnchor or when Estimate Scale is disabled.")]
        public double localUnitsPerMeter = 1;

        /// <summary>原点、退化判定、比例范围及 Fit/Check 误差验收；误差和距离单位均为现实米。</summary>
        [Tooltip("Origin, degeneracy checks, scale bounds, and Fit/Check acceptance. Errors and distances use real meters.")]
        public GeoCalibrationOptionsSettings options = new GeoCalibrationOptionsSettings();

        /// <summary>验证配置格式并复制生成不可变请求，不求解、不移动物体；失败返回错误。</summary>
        public bool TryBuildRequest(out GeoCalibrationRequest request, out GeoError error)
        {
            request = null;
            error = default;
            if (schemaVersion != 1 || points == null || options == null)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, "Unsupported profile schema or missing data.");
                return false;
            }

            try
            {
                var list = new List<GeoControlPoint>();
                foreach (var p in points)
                {
                    if (p == null)
                    {
                        error = new GeoError(GeoErrorCode.InvalidControlPoint, "Null point in profile.");
                        return false;
                    }

                    if (!p.TryBuild(out var point, out error))
                    {
                        return false;
                    }

                    list.Add(point);
                }

                var o = options.Build(localFrameId);
                var scale = estimateScale ? GeoScalePolicy.Estimate : GeoScalePolicy.Fixed(localUnitsPerMeter);
                switch (mode)
                {
                    case GeoCalibrationMode.SingleAnchor:
                        GeoControlPoint anchor = null;
                        var checks = new List<GeoControlPoint>();
                        foreach (var p in list)
                        {
                            if (p.Role == GeoControlPointRole.Check)
                            {
                                checks.Add(p);
                            }
                            else if (anchor == null)
                            {
                                anchor = p;
                            }
                            else
                            {
                                throw new ArgumentException("SingleAnchor requires exactly one fit point.");
                            }
                        }

                        if (anchor == null)
                        {
                            throw new ArgumentException("SingleAnchor requires a fit point.");
                        }

                        request = GeoCalibrationRequest.SingleAnchor(
                    anchor,
                    new GeoSingleAnchorSettings(
                        GeoUnity.D(localUp),
                        GeoUnity.D(localHeadingDirection),
                        GeoHeading.FromDegrees(boundHeadingDegrees),
                        localUnitsPerMeter,
                        o),
                    checks);
                        break;
                    case GeoCalibrationMode.Upright:
                        request = GeoCalibrationRequest.Upright(list, new GeoUprightSettings(GeoUnity.D(localUp), scale, o));
                        break;
                    case GeoCalibrationMode.Spatial:
                        request = GeoCalibrationRequest.Spatial(list, new GeoSpatialSettings(scale, o));
                        break;
                    default:
                        throw new ArgumentException("Unsupported calibration mode.");
                }

                return true;
            }
            catch (ArgumentException ex)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, ex.Message);
                return false;
            }
        }
    }
}
