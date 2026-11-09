using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SceneGeography.Editor
{
    [CustomEditor(typeof(GeoReference))]
    public sealed class GeoReferenceEditor : UnityEditor.Editor
    {
        GeoCalibrationCandidate candidate;
        GeoCalibrationReport report;
        string candidateProfileJson;
        string error;
        public override void OnInspectorGUI()
        {
            var reference = (GeoReference)target;
            var previousRoot = reference.Root;
            serializedObject.Update();
            var property = serializedObject.GetIterator();
            bool enter = true;
            while (property.NextVisible(enter))
            {
                enter = false;
                using (new EditorGUI.DisabledScope(property.name == "m_Script" || !GeoInspectorRules.IsEditable(reference, property.name)))
                {
                    EditorGUILayout.PropertyField(property, GeoEditorLocalization.PropertyContent(property), true);
                }
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                if (!reference.IsInitialized && !object.ReferenceEquals(previousRoot, reference.Root))
                {
                    reference.TrySetRoot(reference.Root, out _);
                }

                candidate = null;
                report = null;
                error = GeoEditorLocalization.Tr("Configuration changed. Calculate a new candidate.");
            }

            EditorGUILayout.Space();
            var profile = serializedObject.FindProperty("profile").objectReferenceValue as GeoCalibrationProfile;
            if (candidate != null && (profile == null || candidateProfileJson != JsonUtility.ToJson(profile)))
            {
                candidate = null;
                report = null;
                error = GeoEditorLocalization.Tr("Profile changed. Calculate a new candidate.");
            }

            EditorGUILayout.LabelField(GeoEditorLocalization.Tr("Calibration status"), EditorStyles.boldLabel);
            GeoReportGUI.DrawRow(GeoReportGUI.CandidateReady, candidate != null ? GeoEditorLocalization.Tr("Yes") : GeoEditorLocalization.Tr("No"));
            GeoReportGUI.DrawRow(GeoReportGUI.ActiveCalibration, reference.IsInitialized ? GeoEditorLocalization.Tr("Yes") : GeoEditorLocalization.Tr("No"));
            GeoReportGUI.DrawRow(GeoReportGUI.CalibrationVersion, reference.CalibrationVersion.ToString());
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    GeoEditorLocalization.Tr("Calculate checks the Profile without moving objects. Apply activates the result and updates enabled bindings once. ") +
                    GeoEditorLocalization.Tr("Edit-mode preview does not follow later edits automatically. Calibration must be initialized again after a script reload or when entering Play mode."),
                    MessageType.Info);
            }

            if (GUILayout.Button(GeoEditorLocalization.Tr("Open Calibration Workbench"))) GeoCalibrationWorkbench.OpenReference(reference);
            EditorGUILayout.HelpBox(GeoCalibrationApplicationState.Describe(reference, profile), MessageType.Info);

            if (GUILayout.Button(GeoEditorLocalization.Tr("Calculate Candidate (no object writes)")))
            {
                candidate = null;
                report = null;
                if (profile == null)
                {
                    error = GeoEditorLocalization.Tr("Assign a Calibration Profile.");
                }
                else if (!profile.TryBuildRequest(out var request, out var failure) ||
                    !reference.TryPrepareCalibration(request, out candidate, out report, out failure))
                {
                    error = failure.ToString();
                }
                else
                {
                    error = null;
                    candidateProfileJson = JsonUtility.ToJson(profile);
                }

                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(candidate == null))
            {
                if (GUILayout.Button(GeoEditorLocalization.Tr("Apply Candidate")))
                {
                    if (!GeoCalibrationEditorUtility.TryApplyCalibration(reference, candidate, out var failure))
                    {
                        error = failure.ToString();
                    }
                    else
                    {
                        error = null;
                        GeoCalibrationApplicationState.Record(reference, candidateProfileJson);
                        candidate = null;
                        report = null;
                    }

                    SceneView.RepaintAll();
                }
            }

            if (GUILayout.Button(GeoEditorLocalization.Tr("Clear Calibration")))
            {
                reference.Clear();
                candidate = null;
                report = null;
                error = null;
            }

            if (!string.IsNullOrEmpty(error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            }

            if (report != null)
            {
                DrawReport(report, candidate != null ? GeoEditorLocalization.Tr("Candidate report (not applied)") : GeoEditorLocalization.Tr("Last calculation report"));
            }

            DrawReport(reference.CurrentReport, GeoEditorLocalization.Tr("Active calibration report"));
        }

        public static void DrawReport(GeoCalibrationReport value, string title = "Calibration report")
        {
            if (value == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GeoEditorLocalization.Content(title, GeoEditorLocalization.Tr("A candidate report is not an applied result. See Active calibration report for the mapping in use. All errors are measured in real meters.")), EditorStyles.boldLabel);
            GeoReportGUI.DrawRow(GeoReportGUI.Success, value.Success.ToString());
            GeoReportGUI.DrawRow(GeoReportGUI.ControlCounts, value.FitCount + " / " + value.CheckCount);
            if (value.LocalUnitsPerMeter.HasValue)
            {
                GeoReportGUI.DrawRow(GeoReportGUI.LocalUnitsPerMeter, value.LocalUnitsPerMeter.Value.ToString("G9"));
            }

            if (value.FitMetrics.HasValue)
            {
                GeoReportGUI.DrawRow(GeoReportGUI.FitRms, value.FitMetrics.Value.SpatialRmsMeters.ToString("G9"));
            }

            if (value.CheckMetrics.HasValue)
            {
                GeoReportGUI.DrawRow(GeoReportGUI.CheckMax, value.CheckMetrics.Value.MaximumResidualMeters.ToString("G9"));
            }

            if (!value.Failure.IsNone)
            {
                GeoReportGUI.DrawMessage(
                    value.Failure.ToString(),
                    GeoEditorLocalization.Tr("Solving or an enabled accuracy check failed. Do not use this result. Check configuration, enabled limits, and per-point residuals."),
                    MessageType.Error);
            }

            foreach (var warning in value.Diagnostics)
            {
                string explanation = GeoReportGUI.ExplainDiagnostic(warning);
                string subject = string.IsNullOrEmpty(warning.SubjectId) ? string.Empty : " [" + warning.SubjectId + "]";
                GeoReportGUI.DrawMessage(
                    warning.Code + subject + ": " + explanation,
                    explanation + GeoEditorLocalization.Tr("\nOriginal message: ") + warning.Message,
                    MessageType.Warning);
            }

            foreach (var residual in value.Residuals)
            {
                GeoReportGUI.DrawRow(
                    new GUIContent(residual.Id + " [" + residual.Role + "]", GeoReportGUI.ResidualTooltip),
                    $"E {residual.EnuErrorMeters.EastMeters:F4} / N {residual.EnuErrorMeters.NorthMeters:F4} / U {residual.EnuErrorMeters.UpMeters:F4} m");
            }
        }

        void OnSceneGUI()
        {
            var reference = (GeoReference)target;
            var value = report ?? reference.CurrentReport;
            if (value == null)
            {
                return;
            }

            foreach (var point in value.Residuals)
            {
                Vector3 a = new Vector3(
                    (float)point.ActualLocalPosition.X,
                    (float)point.ActualLocalPosition.Y,
                    (float)point.ActualLocalPosition.Z);
                Vector3 b = new Vector3(
                    (float)point.PredictedLocalPosition.X,
                    (float)point.PredictedLocalPosition.Y,
                    (float)point.PredictedLocalPosition.Z);
                if (reference.Root != null)
                {
                    a = reference.Root.TransformPoint(a);
                    b = reference.Root.TransformPoint(b);
                }

                Handles.color = point.Role == GeoControlPointRole.Fit ? Color.cyan : Color.yellow;
                Handles.DrawLine(a, b);
                Handles.Label(a, point.Id);
            }
        }
    }

    [CustomEditor(typeof(GeoCalibrationProfile))]
    public sealed class GeoCalibrationProfileEditor : UnityEditor.Editor
    {
        GeoCalibrationReport report;
        string error;
        public override void OnInspectorGUI()
        {
            if (GeoInspectorGUI.DrawFields(serializedObject))
            {
                report = null;
                error = null;
            }

            EditorGUILayout.HelpBox(
                GeoEditorLocalization.Tr("Disabled fields do not affect the selected mode. SingleAnchor requires exactly 1 Fit, Upright at least 2, and Spatial at least 3 non-collinear Fit points. Keep independent Check points."),
                MessageType.Info);
            if (GUILayout.Button(GeoEditorLocalization.Tr("Edit in Calibration Workbench"))) GeoCalibrationWorkbench.OpenProfile((GeoCalibrationProfile)target);
            if (GUILayout.Button(GeoEditorLocalization.Tr("Validate / Solve Profile")))
            {
                var p = (GeoCalibrationProfile)target;
                if (!p.TryBuildRequest(out var request, out var failure) ||
                    !GeoCalibrationEngine.TrySolve(request, GeoCoordinateConverter.CreateDefault(), out _, out report, out failure))
                {
                    error = failure.ToString();
                }
                else
                {
                    error = null;
                }
            }

            if (!string.IsNullOrEmpty(error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            GeoReferenceEditor.DrawReport(report);
        }
    }

    [CustomEditor(typeof(GeoControlPointMarker)), CanEditMultipleObjects]
    public sealed class GeoControlPointMarkerEditor : UnityEditor.Editor
    {
        GeoAutoFillResult autoFillResult;
        public override bool RequiresConstantRepaint() => true;

        public override void OnInspectorGUI()
        {
            GeoInspectorGUI.DrawFields(serializedObject);
            GeoInspectorAutoFill.DrawButton(targets, true, ref autoFillResult);
            var markers = targets.Cast<GeoControlPointMarker>().ToArray();
            bool changed = false;
            foreach (var marker in markers)
            {
                var profile = GeoCalibrationWorkbenchData.AssignedProfile(marker.reference);
                var status = GeoCalibrationWorkbenchData.GetMarkerStatus(marker, profile);
                changed |= !status.IsSynchronized;
                EditorGUILayout.HelpBox(marker.name + ": " + status.Message, status.IsSynchronized ? MessageType.Info : MessageType.Warning);
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button(changed ? GeoEditorLocalization.Tr("Update / configure in workbench…") : GeoEditorLocalization.Tr("View in workbench")))
                    GeoCalibrationWorkbench.OpenMarkers(markers);
            }
        }
    }

    [CustomEditor(typeof(GeoBindingBehaviour), true)]
    public sealed class GeoComponentEditor : UnityEditor.Editor
    {
        GeoAutoFillResult autoFillResult;

        public override void OnInspectorGUI()
        {
            GeoInspectorGUI.DrawFields(serializedObject);
            GeoInspectorAutoFill.DrawButton(targets, false, ref autoFillResult);
            var component = (GeoBindingBehaviour)target;
            if (component is GeoPositionReporter output && output.outputHeight == GeoHeightKind.Orthometric)
            {
                EditorGUILayout.HelpBox(GeoEditorLocalization.Tr("No height model is installed. Orthometric output will fail."), MessageType.Warning);
            }

            if (component is GeoOrientation facing && facing.useTransformTarget && facing.transformTarget == null)
            {
                EditorGUILayout.HelpBox(GeoEditorLocalization.Tr("Assign a Transform Target. The component will not fall back to Kind automatically."), MessageType.Warning);
            }

            bool ready = component.Reference != null && component.Reference.IsInitialized;
            if (!ready)
            {
                EditorGUILayout.HelpBox(GeoEditorLocalization.Tr("Assign a Reference and initialize calibration before applying or sampling."), MessageType.Info);
            }

            string label = component is GeoOrientationReporter ? GeoEditorLocalization.Tr("Sample Orientation Now") : component is GeoPositionReporter ? GeoEditorLocalization.Tr("Sample Coordinates Now") : component is GeoAnchor ? GeoEditorLocalization.Tr("Apply Position Now") : GeoEditorLocalization.Tr("Apply Orientation Now");
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button(label))
                {
                    if (!GeoInspectorGUI.IsReporter(component))
                    {
                        Undo.RecordObject(component.transform, "Apply geographic binding");
                    }

                    if (component is GeoAnchor anchor)
                    {
                        anchor.TryApply(out _);
                    }
                    else if (component is GeoOrientation orientation)
                    {
                        orientation.TryApply(out _);
                    }
                    else if (component is GeoPositionReporter reporter)
                    {
                        reporter.TrySample(out _, out _);
                    }
                    else if (component is GeoOrientationReporter facingReporter)
                    {
                        facingReporter.TrySample(out _, out _);
                    }
                }
            }

            if (component is GeoAnchor position)
            {
                GeoReportGUI.DrawRow(GeoReportGUI.AppliedPosition, position.HasAppliedPosition.ToString());
            }

            if (component is GeoPositionReporter source)
            {
                GeoReportGUI.DrawRow(GeoReportGUI.ValidCoordinates, source.HasValue.ToString());
                if (source.HasValue)
                {
                    GeoReportGUI.DrawRow(
                        GeoReportGUI.LastCoordinate,
                        source.LastValue.LatitudeDegrees.ToString("F7") + ", " + source.LastValue.LongitudeDegrees.ToString("F7") + ", " + source.LastValue.HeightMeters.ToString("F3") + " m");
                }
            }

            if (component is GeoOrientationReporter attitudeReporter)
            {
                GeoReportGUI.DrawRow(GeoReportGUI.ValidAttitude, attitudeReporter.HasValue.ToString());
                if (attitudeReporter.HasValue)
                {
                    var value = attitudeReporter.LastValue;
                    GeoReportGUI.DrawRow(GeoReportGUI.MeasuredHeading, value.HasHeading ? value.Heading.Degrees.ToString("F3") : GeoEditorLocalization.Tr("Undefined (vertical)"));
                    GeoReportGUI.DrawRow(GeoReportGUI.MeasuredPitch, value.PitchUpDegrees.ToString("F3"));
                    GeoReportGUI.DrawRow(GeoReportGUI.MeasuredRoll, value.HasRoll ? value.RollClockwiseDegrees.ToString("F3") : GeoEditorLocalization.Tr("Undefined (vertical)"));
                }
            }

            if (!component.LastError.IsNone)
            {
                EditorGUILayout.HelpBox(component.LastError.ToString(), MessageType.Warning);
            }
        }
    }
}
