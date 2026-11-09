using UnityEngine;
using SceneGeography;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoRuntimeExamples
    {
        public static bool Initialize(
            GeoReference reference,
            GeoCalibrationProfile profile,
            out GeoCalibrationReport report,
            out GeoError error)
        {
            report = null;
            if (reference == null || profile == null)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, "Reference/Profile required.");
                return false;
            }

            if (!profile.TryBuildRequest(out var request, out error))
            {
                return false;
            }

            return reference.TryInitialize(request, out report, out error);
        }

        public static bool PlacePoi(GeoReference reference, GeoCoordinate coordinate, Transform target, out GeoError error)
        {
            // Use one snapshot for position and rotation: 1 m east, 2 m up, heading 105 degrees.
            if (!reference.TryCaptureSnapshot(out var snapshot, out error))
            {
                return false;
            }

            var placement = new GeoPlacement(coordinate, new GeoEnuOffset(1, 0, 2));
            var orientation = GeoOrientationRequest.ForHeading(GeoHeading.FromCardinal(GeoCardinalDirection.East, 15));
            if (!snapshot.TryGetWorldPose(placement, orientation, out var pose, out error))
            {
                return false;
            }

            target.SetPositionAndRotation(pose.position, pose.rotation);
            return true;
        }

        public static bool GetVisitPose(GeoReference reference, GeoCoordinate poi, out Pose pose, out GeoError error)
        {
            // Keep the visit pose separate from the visual anchor: 22 m south and up, looking at the POI.
            var placement = new GeoPlacement(poi, new GeoEnuOffset(0, -22, 22));
            var look = GeoOrientationRequest.ForGeoTarget(poi, GeoLookMode.Full3D);
            return reference.TryGetWorldPose(placement, look, out pose, out error);
        }

        public static bool ConfigureAndApply(
            GeoReference reference,
            GeoAnchor anchor,
            GeoOrientation orientation,
            GeoCoordinate poi,
            out GeoError error)
        {
            anchor.Configure(reference, new GeoPlacement(poi, new GeoEnuOffset(0, 0, 2)));
            orientation.Configure(
                reference,
                GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(90)),
                Quaternion.identity);
            // Configure updates input only. Apply position before orientation for an immediate update.
            return anchor.TryApply(out error) && orientation.TryApply(out error);
        }

        public static bool ReadPlayer(GeoReference reference, Transform player, out GeoCoordinate coordinate, out GeoError error)
        {
            // Specify output references explicitly; RelativeToEllipsoidZero(20) uses a 20 m ellipsoid-height zero.
            return reference.TryWorldToCoordinate(player.position, GeoCoordinateReference.Wgs84Ellipsoidal, out coordinate, out error);
        }
    }
}
