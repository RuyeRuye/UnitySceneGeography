using System;
using UnityEngine;

namespace SceneGeography.Tests
{
    public static class GeoTestData
    {
        public const string FrameId = "synthetic-block-v1";
        public static readonly Wgs84Coordinate Origin = Wgs84Coordinate.FromDegrees(31.2304, 121.4737, 15);
        public static readonly GeoVector3d[] EnuPoints =
        {
            new GeoVector3d(-60, 0, -40),
            new GeoVector3d(80, 0, -35),
            new GeoVector3d(20, 0, 90),
            new GeoVector3d(-50, 8, 75),
            new GeoVector3d(70, 18, 50)
        };
        public static Quaternion ModelRotation(GeoCalibrationMode mode) =>
            mode == GeoCalibrationMode.Spatial ? Quaternion.Euler(12, 32, -6) : Quaternion.Euler(0, 32, 0);
        public static Vector3 ModelPosition(GeoVector3d enu, GeoCalibrationMode mode) =>
            new Vector3(20, 4, -15) + ModelRotation(mode) * new Vector3((float)enu.X, (float)enu.Y, (float)enu.Z) * 1.25f;
        public static GeoReferenceFrame IdentityFrame()
        {
            var p = new GeoControlPoint("origin", GeoVector3d.Zero, Origin.ToCoordinate());
            var options = GeoCalibrationOptions.CreateDefault();
            options.ReferenceOrigin = Origin;
            var request = GeoCalibrationRequest.SingleAnchor(
                p,
                new GeoSingleAnchorSettings(GeoVector3d.UnitY, GeoVector3d.UnitZ, GeoHeading.FromDegrees(0), 1, options));
            if (!GeoCalibrationEngine.TrySolve(request, GeoCoordinateConverter.CreateDefault(), out var solution, out _, out var error))
            {
                throw new InvalidOperationException(error.Message);
            }

            return solution.Frame;
        }

        public static GeoCoordinate GeoAt(GeoVector3d enu, GeoCoordinateType type = GeoCoordinateType.Wgs84)
        {
            if (!IdentityFrame().TryLocalToWgs84(enu, out var wgs, out var error))
            {
                throw new InvalidOperationException(error.Message);
            }

            var reference = new GeoCoordinateReference(type, GeoHeightReference.Ellipsoidal);
            if (!GeoCoordinateConverter.CreateDefault().TryFromWgs84(wgs, reference, out var result, out error))
            {
                throw new InvalidOperationException(error.Message);
            }

            return result;
        }

        public static void FillProfile(GeoCalibrationProfile profile, GeoCalibrationMode mode)
        {
            profile.schemaVersion = 1;
            profile.localFrameId = FrameId;
            profile.mode = mode;
            profile.localUp = Vector3.up;
            profile.localHeadingDirection = ModelRotation(mode) * Vector3.forward;
            profile.boundHeadingDegrees = 0;
            profile.localUnitsPerMeter = 1.25;
            profile.estimateScale = mode != GeoCalibrationMode.SingleAnchor;
            profile.options = new GeoCalibrationOptionsSettings
            {
                useExplicitOrigin = true,
                originLatitudeDegrees = Origin.LatitudeDegrees,
                originLongitudeDegrees = Origin.LongitudeDegrees,
                originEllipsoidHeightMeters = Origin.EllipsoidHeightMeters,
                limitFitRms = true,
                fitRmsMeters = .02,
                limitCheckMaximum = true,
                checkMaximumMeters = .03
            };
            profile.points = new GeoControlPointSettings[EnuPoints.Length];
            for (int i = 0; i < EnuPoints.Length; i++)
            {
                var local = ModelPosition(EnuPoints[i], mode);
                var type = (GeoCoordinateType)(1 + i % 3);
                bool fit = mode == GeoCalibrationMode.SingleAnchor ? i == 0 : i < 3;
                var point = new GeoControlPoint(
                    "Control-" + (i + 1),
                    new GeoVector3d(local.x, local.y, local.z),
                    GeoAt(EnuPoints[i], type),
                    role: fit ? GeoControlPointRole.Fit : GeoControlPointRole.Check);
                profile.points[i] = GeoControlPointSettings.From(point);
            }
        }
    }
}
