using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using SceneGeography.Editor;
using Object = UnityEngine.Object;

namespace SceneGeography.Tests
{
    public sealed class GeoInspectorTests
    {
        [TestCase(GeoCalibrationMode.SingleAnchor, true, true, true, false)]
        [TestCase(GeoCalibrationMode.Upright, true, false, false, true)]
        [TestCase(GeoCalibrationMode.Spatial, false, false, false, true)]
        public void CalibrationModeControlsApplicableFields(GeoCalibrationMode mode, bool up, bool heading, bool fixedScale, bool estimate)
        {
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                profile.mode = mode;
                profile.estimateScale = true;
                Assert.That(GeoInspectorRules.IsEditable(profile, "schemaVersion"), Is.False);
                Assert.That(GeoInspectorRules.IsEditable(profile, "localUp"), Is.EqualTo(up));
                Assert.That(GeoInspectorRules.IsEditable(profile, "localHeadingDirection"), Is.EqualTo(heading));
                Assert.That(GeoInspectorRules.IsEditable(profile, "localUnitsPerMeter"), Is.EqualTo(fixedScale));
                Assert.That(GeoInspectorRules.IsEditable(profile, "estimateScale"), Is.EqualTo(estimate));
                Assert.That(
                    GeoInspectorRules.IsEditable(profile, "options.nearCollinearRatio"),
                    Is.EqualTo(mode == GeoCalibrationMode.Spatial));
                profile.estimateScale = false;
                Assert.That(GeoInspectorRules.IsEditable(profile, "localUnitsPerMeter"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [TestCase(GeoOrientationKind.Heading, false)]
        [TestCase(GeoOrientationKind.GeoTarget, false)]
        [TestCase(GeoOrientationKind.WorldTarget, false)]
        [TestCase(GeoOrientationKind.Heading, true)]
        public void OrientationEnablesOnlySelectedSource(GeoOrientationKind kind, bool transformTarget)
        {
            var go = new GameObject("Inspector orientation");
            try
            {
                var value = go.AddComponent<GeoOrientation>();
                value.kind = kind;
                value.useTransformTarget = transformTarget;
                Assert.That(GeoInspectorRules.IsEditable(value, "kind"), Is.EqualTo(!transformTarget));
                Assert.That(
                    GeoInspectorRules.IsEditable(value, "headingDegrees"),
                    Is.EqualTo(!transformTarget && kind == GeoOrientationKind.Heading));
                Assert.That(
                    GeoInspectorRules.IsEditable(value, "targetCoordinate"),
                    Is.EqualTo(!transformTarget && kind == GeoOrientationKind.GeoTarget));
                Assert.That(
                    GeoInspectorRules.IsEditable(value, "targetWorldPosition"),
                    Is.EqualTo(!transformTarget && kind == GeoOrientationKind.WorldTarget));
                Assert.That(GeoInspectorRules.IsEditable(value, "transformTarget"), Is.EqualTo(transformTarget));
                Assert.That(
                    GeoInspectorRules.IsEditable(value, "lookMode"),
                    Is.EqualTo(transformTarget || kind != GeoOrientationKind.Heading));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(GeoHeightKind.Ellipsoidal)]
        [TestCase(GeoHeightKind.RelativeToEllipsoidZero)]
        [TestCase(GeoHeightKind.Orthometric)]
        public void HeightReferenceControlsCoordinateAndReporterFields(GeoHeightKind kind)
        {
            var go = new GameObject("Inspector reporter");
            try
            {
                var reporter = go.AddComponent<GeoPositionReporter>();
                reporter.outputHeight = kind;
                foreach (string field in new[]
                {
                    "ellipsoidZeroMeters",
                    "assumedHeightZero",
                    "heightModelId"
                }

                )
                {
                    bool expected = field == "heightModelId" ? kind == GeoHeightKind.Orthometric : kind == GeoHeightKind.RelativeToEllipsoidZero;
                    Assert.That(GeoInspectorRules.IsCoordinateFieldEditable(kind, field), Is.EqualTo(expected));
                    Assert.That(GeoInspectorRules.IsEditable(reporter, field), Is.EqualTo(expected));
                }

                var flag = typeof(GeoPositionReporter).GetField("hasExplicitTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(flag.GetCustomAttribute<HideInInspector>(), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase("originLatitudeDegrees", "useExplicitOrigin")]
        [TestCase("originLongitudeDegrees", "useExplicitOrigin")]
        [TestCase("originEllipsoidHeightMeters", "useExplicitOrigin")]
        [TestCase("fitRmsMeters", "limitFitRms")]
        [TestCase("verticalRmsMeters", "limitVerticalRms")]
        [TestCase("checkRmsMeters", "limitCheckRms")]
        [TestCase("checkMaximumMeters", "limitCheckMaximum")]
        [TestCase("maximumDistanceMeters", "limitDistance")]
        public void OptionalValuesAreControlledByTheirSwitch(string field, string toggle)
        {
            Assert.That(GeoInspectorRules.OptionSwitch(field), Is.EqualTo(toggle));
        }

        [TestCase(typeof(GeoAnchor), "startAutomatically", "applyOnEnable")]
        [TestCase(typeof(GeoOrientation), "startAutomatically", "applyOnEnable")]
        [TestCase(typeof(GeoAnchor), "eastOffsetMeters", "eastMeters")]
        [TestCase(typeof(GeoAnchor), "northOffsetMeters", "northMeters")]
        [TestCase(typeof(GeoAnchor), "upOffsetMeters", "upMeters")]
        [TestCase(typeof(GeoOrientation), "yawOffsetDegrees", "yawDegrees")]
        [TestCase(typeof(GeoOrientation), "pitchUpOffsetDegrees", "pitchUpDegrees")]
        [TestCase(typeof(GeoOrientation), "rollClockwiseOffsetDegrees", "rollClockwiseDegrees")]
        [TestCase(typeof(GeoPositionReporter), "samplingIntervalSeconds", "samplingInterval")]
        public void RenamedFieldsDeclareMigrationAndRoundTripCurrentValues(Type type, string field, string oldName)
        {
            var go = new GameObject("Serialized migration");
            try
            {
                var component = go.AddComponent(type);
                var info = type.GetField(field);
                Assert.That(
                    info.GetCustomAttributes<FormerlySerializedAsAttribute>().Any(a => a.oldName == oldName),
                    Is.True);
                object expected = info.FieldType == typeof(bool) ? (object)false : Convert.ChangeType(12.5, info.FieldType);
                info.SetValue(component, expected);
                string json = EditorJsonUtility.ToJson(component);
                info.SetValue(
                    component,
                    info.FieldType == typeof(bool) ? (object)true : Convert.ChangeType(0, info.FieldType));
                EditorJsonUtility.FromJsonOverwrite(json, component);
                Assert.That(info.GetValue(component), Is.EqualTo(expected));
                Assert.That(type.GetProperty(oldName), Is.Null, "Old source aliases were intentionally removed.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void UserFacingSerializedFieldsHaveTooltips()
        {
            Type[] types =
            {
                typeof(GeoReference),
                typeof(GeoBindingBehaviour),
                typeof(GeoCalibrationProfile),
                typeof(GeoAnchor),
                typeof(GeoOrientation),
                typeof(GeoPositionReporter),
                typeof(GeoOrientationReporter),
                typeof(GeoControlPointMarker),
                typeof(GeoCoordinateSettings),
                typeof(GeoControlPointSettings),
                typeof(GeoDouble3Settings),
                typeof(GeoCalibrationOptionsSettings)
            };
            foreach (var type in types)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    bool serialized = !field.IsInitOnly && !field.IsLiteral &&
                        (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null);
                    if (!serialized || field.GetCustomAttribute<HideInInspector>() != null)
                    {
                        continue;
                    }

                    Assert.That(
                        field.GetCustomAttribute<TooltipAttribute>()?.tooltip,
                        Is.Not.Null.And.Not.Empty,
                        type.Name + "." + field.Name);
                }
            }
        }

        [Test]
        public void EditorHelperAppliesSerializedBindingsAndLocksIdentityUntilClear()
        {
            var scene = SceneManager.GetActiveScene();
            var host = new GameObject("Editor reference");
            var poi = new GameObject("Editor POI");
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                var reference = host.AddComponent<GeoReference>();
                GeoTestData.FillProfile(profile, GeoCalibrationMode.Upright);
                reference.TrySetLocalFrameId(profile.localFrameId, out _);
                var anchor = poi.AddComponent<GeoAnchor>();
                var serialized = new SerializedObject(anchor);
                serialized.FindProperty("reference").objectReferenceValue = reference;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                anchor.coordinate = GeoCoordinateSettings.From(GeoTestData.Origin.ToCoordinate());
                Assert.That(
                    GeoCalibrationEditorUtility.TryInitializeFromProfile(reference, profile, out _, out var error),
                    Is.True,
                    error.ToString());
                Assert.That(anchor.HasAppliedPosition, Is.True);
                Assert.That(anchor.LastError.IsNone, Is.True);
                Assert.That(scene.isDirty, Is.True);
                Assert.That(GeoInspectorRules.IsEditable(reference, "root"), Is.False);
                Assert.That(GeoInspectorRules.IsEditable(reference, "localFrameId"), Is.False);
                reference.Clear();
                Assert.That(GeoInspectorRules.IsEditable(reference, "root"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(poi);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void NestedSettingsDrawersReportExpandedHeightsWithoutSceneMutation()
        {
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                GeoTestData.FillProfile(profile, GeoCalibrationMode.Upright);
                var serialized = new SerializedObject(profile);
                var options = serialized.FindProperty("options");
                options.isExpanded = true;
                Assert.That(
                    new GeoCalibrationOptionsSettingsDrawer().GetPropertyHeight(options, GUIContent.none),
                    Is.GreaterThan(100));
                var coordinate = serialized.FindProperty("points").GetArrayElementAtIndex(0).FindPropertyRelative("coordinate");
                coordinate.isExpanded = true;
                Assert.That(
                    new GeoCoordinateSettingsDrawer().GetPropertyHeight(coordinate, GUIContent.none),
                    Is.GreaterThan(100));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
