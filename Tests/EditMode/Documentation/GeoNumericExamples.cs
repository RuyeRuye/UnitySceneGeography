using UnityEngine;
using SceneGeography;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoNumericExamples
    {
        public static bool ConvertCoordinate(
            GeoCoordinate input,
            GeoCoordinateReference outputReference,
            out GeoCoordinate output,
            out GeoError error)
        {
            // Coordinate-standard conversion needs no calibration or scene.
            return GeoCoordinateConverter.CreateDefault().TryConvert(input, outputReference, out output, out error);
        }

        public static bool CreateMapping(
            GeoCalibrationRequest request,
            out GeoLocalMapping mapping,
            out GeoError error)
        {
            mapping = null;
            var converter = GeoCoordinateConverter.CreateDefault();
            if (!GeoCalibrationEngine.TrySolve(request, converter, out var solution, out _, out error))
            {
                return false;
            }

            // No GeoReference, GameObject or Transform is required.
            mapping = new GeoLocalMapping(solution.Frame, converter);
            return true;
        }

        public static bool LocalPoseRoundTrip(
            GeoLocalMapping mapping,
            GeoCoordinate coordinate,
            GeoCoordinateReference outputReference,
            out GeoLocalPose localPose,
            out GeoGeographicPose geographicPose,
            out GeoError error)
        {
            geographicPose = default;
            var attitude = GeoAttitude.FromDegrees(90, 10, -5);
            return mapping.TryGeoToLocalPose(coordinate, attitude, out localPose, out error) &&
                mapping.TryLocalPoseToGeo(localPose, outputReference, out geographicPose, out error);
        }

        public static bool FromActiveReference(
            GeoReference reference,
            GeoCoordinate coordinate,
            out GeoLocalPose localPose,
            out GeoError error)
        {
            // Uses the active calibration, but never reads or writes a target Transform.
            return reference.TryGeoToLocalPose(coordinate, GeoAttitude.FromDegrees(90), out localPose, out error);
        }

        public static bool MeasureWorldRotation(
            GeoReference reference,
            Quaternion actualRotation,
            Quaternion modelCorrection,
            out GeoAttitude attitude,
            out GeoError error)
        {
            attitude = default;
            if (!GeoAttitude.TryFromRotation(modelCorrection, out _, out error))
            {
                return false;
            }

            var geographicWorldRotation = actualRotation * Quaternion.Inverse(modelCorrection);
            return reference.TryWorldRotationToAttitude(geographicWorldRotation, out attitude, out error);
        }
    }
}
