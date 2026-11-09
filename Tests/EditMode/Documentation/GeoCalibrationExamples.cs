using System.Collections.Generic;
using SceneGeography;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoCalibrationExamples
    {
        public static GeoCalibrationRequest Single(
            GeoControlPoint anchor,
            GeoVector3d localUp,
            GeoVector3d knownEast,
            double localUnitsPerMeter)
        {
            var options = GeoCalibrationOptions.CreateDefault();
            // knownEast is geographic east in Root-local space, so its bound heading is 90 degrees.
            return GeoCalibrationRequest.SingleAnchor(
                anchor,
                new GeoSingleAnchorSettings(localUp, knownEast, GeoHeading.FromDegrees(90), localUnitsPerMeter, options));
        }

        public static GeoCalibrationRequest Upright(IReadOnlyList<GeoControlPoint> controls, GeoVector3d localUp)
        {
            var options = GeoCalibrationOptions.CreateDefault();
            options.MaximumFitRmsMeters = 0.5;
            // Example acceptance threshold; choose it from measurement quality and application requirements.
            return GeoCalibrationRequest.Upright(controls, new GeoUprightSettings(localUp, GeoScalePolicy.Estimate, options));
        }

        public static GeoCalibrationRequest Spatial(IReadOnlyList<GeoControlPoint> controls)
        {
            var options = GeoCalibrationOptions.CreateDefault();
            return GeoCalibrationRequest.Spatial(controls, new GeoSpatialSettings(GeoScalePolicy.Estimate, options));
        }

        public static bool SolveWithoutUnity(
            GeoCalibrationRequest request,
            Wgs84Coordinate coordinate,
            out GeoVector3d localPosition,
            out GeoError error)
        {
            localPosition = default;
            if (!GeoCalibrationEngine.TrySolve(request, GeoCoordinateConverter.CreateDefault(), out var solution, out _, out error))
            {
                return false;
            }

            return solution.Frame.TryWgs84ToLocal(coordinate, out localPosition, out error);
        }
    }
}
