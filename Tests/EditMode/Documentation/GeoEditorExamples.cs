using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SceneGeography;
using SceneGeography.Editor;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoEditorExamples
    {
        public static bool Preview(
            GeoReference reference,
            GeoCalibrationProfile profile,
            out GeoCalibrationReport report,
            out GeoError error)
        {
            return GeoCalibrationEditorUtility.TryInitializeFromProfile(reference, profile, out report, out error);
        }

        public static bool Capture(
            IReadOnlyList<GeoControlPointMarker> markers,
            GeoCalibrationMode mode,
            out GeoCalibrationProfile profile,
            out GeoError error)
        {
            profile = null;
            error = default;
            if (markers == null || markers.Count == 0 || markers[0] == null)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, "Markers required.");
                return false;
            }

            var reference = markers[0].reference;
            var points = new List<GeoControlPointSettings>();
            foreach (var marker in markers)
            {
                if (marker == null || marker.reference != reference)
                {
                    error = new GeoError(GeoErrorCode.InvalidConfiguration, "Use one Reference.");
                    return false;
                }

                if (!marker.TryCapture(out var point, out error))
                {
                    return false;
                }

                points.Add(GeoControlPointSettings.From(point));
            }

            profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            profile.localFrameId = reference.LocalFrameId;
            profile.mode = mode;
            profile.points = points.ToArray();
            // Capture numbers only; configure Up, direction, scale and limits, then solve and review.
            return true;
        }

        public static string SaveNewProfile(GeoCalibrationProfile profile, string assetPath)
        {
            // For example Assets/Calibration/BlockA.asset; the parent folder must already exist.
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(profile, uniquePath);
            AssetDatabase.SaveAssets();
            return uniquePath;
        }
    }
}
