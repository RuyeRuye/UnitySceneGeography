using UnityEditor;
using UnityEngine;

namespace SceneGeography.Editor
{
    internal static class GeoReportGUI
    {
        internal static GUIContent CandidateReady => GeoEditorLocalization.Content(
            GeoEditorLocalization.Tr("Candidate ready to apply"),
            GeoEditorLocalization.Tr("Whether a calculated, unapplied candidate exists. Yes does not mean it is used for conversion; Apply Candidate activates it."));
        internal static GUIContent ActiveCalibration => GeoEditorLocalization.Content(
            GeoEditorLocalization.Tr("Active calibration"),
            GeoEditorLocalization.Tr("Whether an applied calibration exists. It may differ from an edited Profile, and does not guarantee that the current Root transform is valid."));
        internal static GUIContent CalibrationVersion => GeoEditorLocalization.Content(
            GeoEditorLocalization.Tr("Calibration version"),
            GeoEditorLocalization.Tr("Active-state version. Increments after successful apply or Clear, not after candidate calculation; it is not an apply count."));
        internal static GUIContent Success => GeoEditorLocalization.Content(
            GeoEditorLocalization.Tr("Success"),
            GeoEditorLocalization.Tr("Whether solving and enabled accuracy checks passed. Without configured limits, True only means mathematical success, not that application accuracy requirements were met."));
        internal static GUIContent ControlCounts => GeoEditorLocalization.Content(
            GeoEditorLocalization.Tr("Fit / Check"),
            GeoEditorLocalization.Tr("Fit points used for solving / Check points used only for independent validation. Check points do not affect the solution or automatic origin."));
        internal static GUIContent LocalUnitsPerMeter => GeoEditorLocalization.Content(
            GeoEditorLocalization.Tr("Local units / meter"),
            GeoEditorLocalization.Tr("Root-local units per real meter. For example, 0.05 means 20 real meters per local unit. Does not include Root's world scale."));
        internal static GUIContent FitRms => GeoEditorLocalization.Content(
            GeoEditorLocalization.Tr("Fit RMS (meters)"),
            GeoEditorLocalization.Tr("Weighted RMS of Fit-point 3D errors in real meters. Describes overall fitting error, not the maximum or accuracy at other locations."));
        internal static GUIContent CheckMax => GeoEditorLocalization.Content(
            GeoEditorLocalization.Tr("Check max (meters)"),
            GeoEditorLocalization.Tr("Largest Check-point 3D position error in real meters, not reduced by weight. Each error is sqrt(E²+N²+U²): measured model position minus predicted position. This is measured error, not the permitted limit."));
        internal static GUIContent AppliedPosition => GeoEditorLocalization.Content(
            GeoEditorLocalization.Tr("Has applied position"),
            GeoEditorLocalization.Tr("Whether the last positioning operation was valid. False preserves the last position instead of resetting the object to zero."));
        internal static GUIContent ValidCoordinates => GeoEditorLocalization.Content(
            GeoEditorLocalization.Tr("Has valid coordinates"),
            GeoEditorLocalization.Tr("Whether the last coordinate sample was valid. When False, LastValue may still hold a historical value and must not be used as a current coordinate."));
        internal static GUIContent LastCoordinate => GeoEditorLocalization.Content(
            GeoEditorLocalization.Tr("Last coordinate"),
            GeoEditorLocalization.Tr("Last successful latitude, longitude, and height, in degrees, degrees, and meters. Reporter Output Type / Output Height determine the reference."));
        internal static GUIContent ValidAttitude => GeoEditorLocalization.Content(GeoEditorLocalization.Tr("Has valid attitude"), GeoEditorLocalization.Tr("Whether the last attitude sample succeeded. When false, LastValue is historical. Vertical attitudes retain a valid quaternion but do not have uniquely separable heading and roll."));
        internal static GUIContent MeasuredHeading => GeoEditorLocalization.Content(GeoEditorLocalization.Tr("Heading (degrees)"), GeoEditorLocalization.Tr("Heading clockwise from reference north toward east, in [0,360). Undefined for vertical forward directions; no artificial zero is returned."));
        internal static GUIContent MeasuredPitch => GeoEditorLocalization.Content(GeoEditorLocalization.Tr("Pitch up (degrees)"), GeoEditorLocalization.Tr("Forward elevation relative to the reference horizontal plane. Positive means pitch up; range [-90,90] degrees."));
        internal static GUIContent MeasuredRoll => GeoEditorLocalization.Content(GeoEditorLocalization.Tr("Roll clockwise (degrees)"), GeoEditorLocalization.Tr("Clockwise roll using the GeoRotationOffset convention, in [-180,180). Heading and roll cannot be separated uniquely at vertical attitudes."));
        internal static string ResidualTooltip =>
            GeoEditorLocalization.Tr("Control-point residual: supplied model position minus the position predicted from geographic coordinates, converted to real meters. Signed E/N/U components point east/north/up; negative values point west/south/down. Fit participates in solving; Check only validates.");

        internal static void DrawRow(GUIContent label, string value)
        {
            EditorGUILayout.LabelField(label, new GUIContent(value, label.tooltip));
        }

        internal static void DrawMessage(string message, string tooltip, MessageType type)
        {
            EditorGUILayout.HelpBox(message, type);
            // HelpBox has no tooltip overload; a transparent label covers the same rect.
            GUI.Label(GUILayoutUtility.GetLastRect(), new GUIContent(string.Empty, tooltip), GUIStyle.none);
        }

        internal static string ExplainDiagnostic(GeoDiagnostic diagnostic)
        {
            switch (diagnostic.Code)
            {
                case "AccuracyThresholdNotConfigured":
                    return GeoEditorLocalization.Tr("No accuracy limits are enabled. Errors are calculated and displayed, but application accuracy is not assessed. This does not mean an error exceeded a limit. Enable Fit / Check limits in Profile > Options.");
                case "AssumedHeightZero":
                    return GeoEditorLocalization.Tr("The height zero uses an assumed ellipsoid height. Control points and POIs must use consistent conventions; this assumption cannot guarantee absolute height accuracy.");
                case "FarFromOrigin":
                    return GeoEditorLocalization.Tr("Fit points extend more than 5 km from the ENU origin. Check the origin, scene extent, and suitability of fixed geographic axes.");
                case "HeadingVectorProjected":
                    return GeoEditorLocalization.Tr("The known heading vector deviates more than 5° from the horizontal plane. Solving uses its projection onto the Up-horizontal plane. Check Local Up and Local Heading Direction.");
                case "ShortBaseline":
                    return GeoEditorLocalization.Tr("The control baseline is short and direction is sensitive to measurement error. Use reliable corresponding points farther apart.");
                case "MinimalSpatialFit":
                    return GeoEditorLocalization.Tr("Spatial uses only the minimum three Fit points. This cannot independently validate registration quality or all reflection ambiguities. Add distributed Fit points and independent Check points.");
                default:
                    return diagnostic.Message;
            }
        }
    }
}
