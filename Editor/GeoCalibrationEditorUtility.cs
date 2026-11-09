using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SceneGeography.Editor
{
    /// <summary>仅 Editor 可用的标定入口；记录绑定 Transform Undo，并按位置、朝向、读取顺序预览。</summary>
    public static class GeoCalibrationEditorUtility
    {
        /// <summary>构建 Profile、计算候选并提交；不保存运行时解，也不写入 Profile。失败保留旧解。</summary>
        /// <param name="reference">已配置 Root 与 LocalFrameId 的场景地理系统。</param>
        /// <param name="profile">与模型局部坐标约定一致的配置。</param>
        /// <param name="report">数学报告；构建请求前失败时可能为空。</param>
        /// <param name="error">失败原因；提交成功后仍需检查各绑定的 LastError。</param>
        public static bool TryInitializeFromProfile(
            GeoReference reference,
            GeoCalibrationProfile profile,
            out GeoCalibrationReport report,
            out GeoError error)
        {
            report = null;
            error = default;
            if (reference == null || profile == null)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, "Reference and Profile are required.");
                return false;
            }

            if (!profile.TryBuildRequest(out var request, out error) ||
                !reference.TryPrepareCalibration(request, out var candidate, out report, out error) ||
                !TryApplyCalibration(reference, candidate, out error)) return false;
            GeoCalibrationApplicationState.Record(reference, GeoCalibrationWorkbenchData.Json(profile));
            return true;
        }

        /// <summary>提交候选；编辑模式记录姿态 Undo、预览启用的绑定并标记场景待保存。PlayMode 直接使用运行时调度。</summary>
        /// <remarks>Undo 撤销物体姿态，不恢复内存标定解；撤销后应 Clear 或重新计算应用。成功仅表示标定提交成功。</remarks>
        public static bool TryApplyCalibration(GeoReference reference, GeoCalibrationCandidate candidate, out GeoError error)
        {
            if (reference == null)
            {
                error = new GeoError(GeoErrorCode.MissingReference, "Reference is required.");
                return false;
            }

            if (Application.isPlaying)
            {
                return reference.TryApplyCalibration(candidate, out error);
            }

            var bindings = GeoEditorCompatibility.FindSceneObjects<GeoBindingBehaviour>(false);
            foreach (var binding in bindings)
            {
                if (Enabled(binding, reference) && !GeoInspectorGUI.IsReporter(binding))
                {
                    Undo.RecordObject(binding.transform, "Apply geographic calibration");
                }
            }

            if (!reference.TryApplyCalibration(candidate, out error))
            {
                return false;
            }

            foreach (var binding in bindings)
            {
                if (Enabled(binding, reference) && binding is GeoAnchor anchor)
                {
                    anchor.TryApply(out _);
                }
            }

            foreach (var binding in bindings)
            {
                if (Enabled(binding, reference) && binding is GeoOrientation orientation)
                {
                    orientation.TryApply(out _);
                }
            }

            foreach (var binding in bindings)
            {
                if (Enabled(binding, reference) && binding is GeoPositionReporter reporter)
                {
                    reporter.TrySample(out _, out _);
                }
                else if (Enabled(binding, reference) && binding is GeoOrientationReporter facingReporter)
                {
                    facingReporter.TrySample(out _, out _);
                }
            }

            foreach (var binding in bindings)
            {
                if (Enabled(binding, reference) && !GeoInspectorGUI.IsReporter(binding))
                {
                    EditorUtility.SetDirty(binding.transform);
                    var scene = binding.gameObject.scene;
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                    }
                }
            }

            SceneView.RepaintAll();
            return true;
        }

        static bool Enabled(GeoBindingBehaviour binding, GeoReference reference) => binding.isActiveAndEnabled && binding.Reference == reference;
    }
}
