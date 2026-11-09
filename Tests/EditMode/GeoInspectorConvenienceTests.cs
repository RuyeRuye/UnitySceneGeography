using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneGeography.Editor;
using Object = UnityEngine.Object;

namespace SceneGeography.Tests
{
    public sealed class GeoInspectorConvenienceTests
    {
        readonly List<GameObject> objects = new List<GameObject>();
        Scene previewScene;
        GeoEditorLanguageScope languageScope;

        [SetUp]
        public void SetUp()
        {
            languageScope = new GeoEditorLanguageScope(GeoEditorLanguage.SimplifiedChinese);
        }

        GameObject Create(string name)
        {
            var value = new GameObject(name);
            objects.Add(value);
            return value;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }

            objects.Clear();
            if (previewScene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }

            languageScope?.Dispose();
        }

        [Test]
        public void ReportLabelsExplainTheirMeaningAndUnits()
        {
            int labels = 0;
            foreach (var field in typeof(GeoReportGUI).GetProperties(BindingFlags.Static | BindingFlags.NonPublic))
            {
                if (field.PropertyType != typeof(GUIContent))
                {
                    continue;
                }

                var content = (GUIContent)field.GetValue(null);
                Assert.That(content.text, Is.Not.Null.And.Not.Empty, field.Name);
                Assert.That(content.tooltip, Is.Not.Null.And.Not.Empty, field.Name);
                labels++;
            }

            Assert.That(labels, Is.GreaterThanOrEqualTo(11));
            Assert.That(GeoReportGUI.CheckMax.tooltip, Does.Contain("现实米").And.Contain("不是允许"));
            Assert.That(GeoReportGUI.FitRms.tooltip, Does.Contain("均方根"));
            Assert.That(GeoReportGUI.ResidualTooltip, Does.Contain("减去").And.Contain("负值"));
        }

        [TestCase("AccuracyThresholdNotConfigured")]
        [TestCase("AssumedHeightZero")]
        [TestCase("FarFromOrigin")]
        [TestCase("HeadingVectorProjected")]
        [TestCase("ShortBaseline")]
        [TestCase("MinimalSpatialFit")]
        public void KnownDiagnosticsHaveReadableExplanations(string code)
        {
            string explanation = GeoReportGUI.ExplainDiagnostic(new GeoDiagnostic(code, "Raw diagnostic."));
            Assert.That(explanation, Is.Not.Null.And.Not.Empty.And.Not.EqualTo("Raw diagnostic."));
        }

        [Test]
        public void UnknownDiagnosticKeepsItsOriginalMessage()
        {
            Assert.That(
                GeoReportGUI.ExplainDiagnostic(new GeoDiagnostic("FutureDiagnostic", "Original message")),
                Is.EqualTo("Original message"));
        }

        [Test]
        public void UniqueInactiveReferenceAndEmptyIdAreFilledAndUndoable()
        {
            var host = Create("Only reference");
            var reference = host.AddComponent<GeoReference>();
            host.SetActive(false);
            var marker = Create("Control A").AddComponent<GeoControlPointMarker>();
            marker.pointId = "  ";

            var result = GeoInspectorAutoFill.FillMissing(new Object[] { marker }, true);
            Assert.That(marker.reference, Is.SameAs(reference));
            Assert.That(marker.pointId, Is.EqualTo("Control A"));
            Assert.That(result.ReferencesFilled, Is.EqualTo(1));
            Assert.That(result.PointIdsFilled, Is.EqualTo(1));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(reference.IsInitialized, Is.False);

            Undo.PerformUndo();
            Assert.That(marker.reference, Is.Null);
            Assert.That(string.IsNullOrWhiteSpace(marker.pointId), Is.True);
        }

        [Test]
        public void NoReferenceDoesNotPreventFillingMarkerId()
        {
            var marker = Create("Control A").AddComponent<GeoControlPointMarker>();
            var result = GeoInspectorAutoFill.FillMissing(new Object[] { marker }, true);
            Assert.That(marker.reference, Is.Null);
            Assert.That(marker.pointId, Is.EqualTo("Control A"));
            Assert.That(result.ReferencesFilled, Is.Zero);
            Assert.That(result.Warnings, Is.Not.Empty);
        }

        [Test]
        public void MultipleReferencesRemainUnassignedIncludingInactiveOnes()
        {
            Create("First reference").AddComponent<GeoReference>();
            var second = Create("Second reference");
            second.AddComponent<GeoReference>();
            second.SetActive(false);
            var marker = Create("Control A").AddComponent<GeoControlPointMarker>();
            var result = GeoInspectorAutoFill.FillMissing(new Object[] { marker }, true);
            Assert.That(marker.reference, Is.Null);
            Assert.That(marker.pointId, Is.EqualTo("Control A"));
            Assert.That(result.Warnings, Is.Not.Empty);
        }

        [Test]
        public void ExistingReferenceAndIdAreNeverOverwritten()
        {
            Create("First reference").AddComponent<GeoReference>();
            var chosen = Create("Chosen reference").AddComponent<GeoReference>();
            var marker = Create("Control A").AddComponent<GeoControlPointMarker>();
            marker.reference = chosen;
            marker.pointId = "Measured-A";
            var result = GeoInspectorAutoFill.FillMissing(new Object[] { marker }, true);
            Assert.That(marker.reference, Is.SameAs(chosen));
            Assert.That(marker.pointId, Is.EqualTo("Measured-A"));
            Assert.That(result.ReferencesFilled, Is.Zero);
            Assert.That(result.PointIdsFilled, Is.Zero);
            Assert.That(result.Warnings, Is.Empty);
        }

        [Test]
        public void ReferencesInOtherScenesAreNotCandidates()
        {
            var local = Create("Local reference").AddComponent<GeoReference>();
            previewScene = EditorSceneManager.NewPreviewScene();
            var other = Create("Other scene reference");
            SceneManager.MoveGameObjectToScene(other, previewScene);
            other.AddComponent<GeoReference>();
            var marker = Create("Control A").AddComponent<GeoControlPointMarker>();
            var result = GeoInspectorAutoFill.FillMissing(new Object[] { marker }, true);
            Assert.That(marker.reference, Is.SameAs(local));
            Assert.That(result.Warnings, Is.Empty);
        }

        [Test]
        public void PreviewSceneObjectsAreNotModified()
        {
            previewScene = EditorSceneManager.NewPreviewScene();
            var host = Create("Preview reference");
            SceneManager.MoveGameObjectToScene(host, previewScene);
            host.AddComponent<GeoReference>();
            var point = Create("Preview control");
            SceneManager.MoveGameObjectToScene(point, previewScene);
            var marker = point.AddComponent<GeoControlPointMarker>();
            var result = GeoInspectorAutoFill.FillMissing(new Object[] { marker }, true);
            Assert.That(marker.reference, Is.Null);
            Assert.That(marker.pointId, Is.Null.Or.Empty);
            Assert.That(result.Warnings, Is.Not.Empty);
        }

        [Test]
        public void BatchFillUsesEachObjectNameAndFormsOneUndoStep()
        {
            var reference = Create("Only reference").AddComponent<GeoReference>();
            var a = Create("Control A").AddComponent<GeoControlPointMarker>();
            var b = Create("Control B").AddComponent<GeoControlPointMarker>();
            var result = GeoInspectorAutoFill.FillMissing(new Object[] { a, b }, true);
            Assert.That(a.reference, Is.SameAs(reference));
            Assert.That(b.reference, Is.SameAs(reference));
            Assert.That(a.pointId, Is.EqualTo("Control A"));
            Assert.That(b.pointId, Is.EqualTo("Control B"));
            Assert.That(result.PointIdsFilled, Is.EqualTo(2));
            Assert.That(result.Warnings, Is.Empty);

            Undo.PerformUndo();
            Assert.That(a.reference, Is.Null);
            Assert.That(b.reference, Is.Null);
            Assert.That(a.pointId, Is.Null.Or.Empty);
            Assert.That(b.pointId, Is.Null.Or.Empty);
        }

        [Test]
        public void DuplicateObjectNamesWarnWithoutInventingIds()
        {
            Create("Only reference").AddComponent<GeoReference>();
            var a = Create("Control").AddComponent<GeoControlPointMarker>();
            var b = Create("Control").AddComponent<GeoControlPointMarker>();
            var result = GeoInspectorAutoFill.FillMissing(new Object[] { a, b }, true);
            Assert.That(a.pointId, Is.EqualTo("Control"));
            Assert.That(b.pointId, Is.EqualTo("Control"));
            Assert.That(result.Warnings, Is.Not.Empty);
        }

        [Test]
        public void SameIdUnderDifferentReferencesDoesNotProduceDuplicateWarning()
        {
            var first = Create("First reference").AddComponent<GeoReference>();
            var second = Create("Second reference").AddComponent<GeoReference>();
            var a = Create("Control").AddComponent<GeoControlPointMarker>();
            var b = Create("Other control").AddComponent<GeoControlPointMarker>();
            a.reference = first;
            b.reference = second;
            b.pointId = "Control";

            var result = GeoInspectorAutoFill.FillMissing(new Object[] { a }, true);
            Assert.That(a.pointId, Is.EqualTo("Control"));
            Assert.That(a.reference, Is.SameAs(first));
            Assert.That(result.Warnings, Is.Empty);
        }

        [TestCase(typeof(GeoAnchor))]
        [TestCase(typeof(GeoOrientation))]
        [TestCase(typeof(GeoPositionReporter))]
        [TestCase(typeof(GeoOrientationReporter))]
        public void OtherReferenceConsumersCanFillTheirEmptyReference(Type type)
        {
            var reference = Create("Only reference").AddComponent<GeoReference>();
            var component = Create("Reference consumer").AddComponent(type);
            var result = GeoInspectorAutoFill.FillMissing(new Object[] { component }, false);
            var serialized = new SerializedObject(component);
            Assert.That(serialized.FindProperty("reference").objectReferenceValue, Is.SameAs(reference));
            Assert.That(result.ReferencesFilled, Is.EqualTo(1));
            Assert.That(result.PointIdsFilled, Is.Zero);
        }
    }
}
