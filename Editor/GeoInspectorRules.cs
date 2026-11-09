using UnityEngine;

namespace SceneGeography.Editor
{
    // Shared by Inspectors, nested drawers and EditMode policy tests.
    internal static class GeoInspectorRules
    {
        internal static bool IsEditable(Object target, string field)
        {
            if (target is GeoReference reference)
            {
                return !reference.IsInitialized || (field != "root" && field != "localFrameId");
            }

            if (target is GeoCalibrationProfile profile)
            {
                switch (field)
                {
                    case "schemaVersion":
                        return false;
                    case "localUp":
                        return profile.mode != GeoCalibrationMode.Spatial;
                    case "localHeadingDirection":
                    case "boundHeadingDegrees":
                        return profile.mode == GeoCalibrationMode.SingleAnchor;
                    case "estimateScale":
                        return profile.mode != GeoCalibrationMode.SingleAnchor;
                    case "localUnitsPerMeter":
                        return profile.mode == GeoCalibrationMode.SingleAnchor || !profile.estimateScale;
                    case "options.minimumBaselineMeters":
                        return profile.mode != GeoCalibrationMode.SingleAnchor;
                    case "options.nearCollinearRatio":
                        return profile.mode == GeoCalibrationMode.Spatial;
                }
            }

            if (target is GeoOrientation orientation)
            {
                bool transform = orientation.useTransformTarget;
                switch (field)
                {
                    case "kind":
                        return !transform;
                    case "headingDegrees":
                        return !transform && orientation.kind == GeoOrientationKind.Heading;
                    case "targetCoordinate":
                        return !transform && orientation.kind == GeoOrientationKind.GeoTarget;
                    case "targetWorldPosition":
                        return !transform && orientation.kind == GeoOrientationKind.WorldTarget;
                    case "transformTarget":
                        return transform;
                    case "lookMode":
                        return transform || orientation.kind == GeoOrientationKind.GeoTarget ||
                    orientation.kind == GeoOrientationKind.WorldTarget;
                }
            }

            if (target is GeoPositionReporter reporter)
            {
                if (field == "ellipsoidZeroMeters" || field == "assumedHeightZero")
                {
                    return reporter.outputHeight == GeoHeightKind.RelativeToEllipsoidZero;
                }

                if (field == "heightModelId")
                {
                    return reporter.outputHeight == GeoHeightKind.Orthometric;
                }
            }

            return true;
        }

        internal static bool IsCoordinateFieldEditable(GeoHeightKind kind, string field)
        {
            if (field == "ellipsoidZeroMeters" || field == "assumedHeightZero")
            {
                return kind == GeoHeightKind.RelativeToEllipsoidZero;
            }

            if (field == "heightModelId")
            {
                return kind == GeoHeightKind.Orthometric;
            }

            return true;
        }

        internal static string OptionSwitch(string field)
        {
            switch (field)
            {
                case "originLatitudeDegrees":
                case "originLongitudeDegrees":
                case "originEllipsoidHeightMeters":
                    return "useExplicitOrigin";
                case "fitRmsMeters":
                    return "limitFitRms";
                case "verticalRmsMeters":
                    return "limitVerticalRms";
                case "checkRmsMeters":
                    return "limitCheckRms";
                case "checkMaximumMeters":
                    return "limitCheckMaximum";
                case "maximumDistanceMeters":
                    return "limitDistance";
                default:
                    return null;
            }
        }
    }
}
