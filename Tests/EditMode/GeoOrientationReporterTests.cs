using System;
using NUnit.Framework;
using SceneGeography.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SceneGeography.Tests
{
    public sealed class GeoOrientationReporterTests
    {
        GameObject host, poi, target;
        GeoCalibrationProfile profile;
        GeoReference reference;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Reporter reference");
            poi = new GameObject("Reporter object");
            target = new GameObject("Reporter explicit target");
            profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            reference = host.AddComponent<GeoReference>();
            GeoTestData.FillProfile(profile, GeoCalibrationMode.Spatial);
            reference.TrySetLocalFrameId(profile.localFrameId, out _);
            Assert.That(GeoCalibrationEditorUtility.TryInitializeFromProfile(reference, profile, out _, out var error), Is.True, error.ToString());
        }

        [TearDown]
        public void TearDown()
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
            Object.DestroyImmediate(poi);
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(profile);
        }

        [Test]
        public void SamplingRemovesExplicitModelCorrectionAndNeverWritesPose()
        {
            var reporter = poi.AddComponent<GeoOrientationReporter>();
            var correction = Quaternion.Euler(12, 90, 4);
            reporter.Configure(reference, target.transform, correction);
            Assert.That(reference.TryGetWorldRotation(GeoAttitude.FromDegrees(62, 27, -35), out var rotation, out _), Is.True);
            target.transform.SetPositionAndRotation(new Vector3(1, 2, 3), rotation * correction);
            var beforePosition = target.transform.position;
            var beforeRotation = target.transform.rotation;
            Assert.That(reporter.TrySample(out var value, out var error), Is.True, error.ToString());
            Assert.That(value.Heading.Degrees, Is.EqualTo(62).Within(.001));
            Assert.That(value.PitchUpDegrees, Is.EqualTo(27).Within(.001));
            Assert.That(value.RollClockwiseDegrees, Is.EqualTo(-35).Within(.001));
            Assert.That(target.transform.position, Is.EqualTo(beforePosition));
            Assert.That(target.transform.rotation, Is.EqualTo(beforeRotation));
        }

        [Test]
        public void DestroyedTargetDoesNotFallBackAndFailedConfigureIsAtomic()
        {
            var reporter = poi.AddComponent<GeoOrientationReporter>();
            reporter.Configure(reference, target.transform);
            Assert.That(reporter.TrySample(out var previous, out _), Is.True);
            Assert.Throws<ArgumentException>(() => reporter.Configure(reference, null, default));
            Assert.That(reporter.target, Is.SameAs(target.transform));
            Object.DestroyImmediate(target);
            Assert.That(reporter.TrySample(out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.MissingLookTarget));
            Assert.That(reporter.HasValue, Is.False);
            Assert.That(reporter.LastValue.GeographicRotation, Is.EqualTo(previous.GeographicRotation));
            reporter.Configure(reference, null);
            Assert.That(reporter.TrySample(out _, out _), Is.True);
        }

        [Test]
        public void EditorPreviewSamplesReporterAndDoesNotCreateTransformUndoOrDirtyWrites()
        {
            var reporter = poi.AddComponent<GeoOrientationReporter>();
            reporter.Configure(reference, null);
            Assert.That(reference.TryGetWorldRotation(GeoAttitude.FromDegrees(23, 10, 5), out var rotation, out _), Is.True);
            poi.transform.rotation = rotation;
            int notifications = 0;
            reporter.OrientationUpdated += _ => notifications++;
            Assert.That(GeoCalibrationEditorUtility.TryInitializeFromProfile(reference, profile, out _, out var error), Is.True, error.ToString());
            Assert.That(reporter.HasValue, Is.True);
            Assert.That(notifications, Is.GreaterThanOrEqualTo(1));
            Assert.That(reporter.LastValue.Heading.Degrees, Is.EqualTo(23).Within(.001));
            Assert.That(poi.transform.rotation, Is.EqualTo(rotation));
            Assert.That(GeoCalibrationSceneInventory.Capture().Entries, Has.Some.Matches<GeoCalibrationSceneEntry>(x =>
                x.Object == poi && x.Types == GeoWorkbenchComponentType.Reporter));
            Undo.ClearUndo(poi.transform);
        }

        [Test]
        public void VerticalSamplingSucceedsAndUnavailableReferenceInvalidatesValue()
        {
            var reporter = poi.AddComponent<GeoOrientationReporter>();
            reporter.Configure(reference, null);
            reference.TryGetWorldRotation(GeoAttitude.FromDegrees(34, 90, 20), out var rotation, out _);
            poi.transform.rotation = rotation;
            Assert.That(reporter.TrySample(out var value, out _), Is.True);
            Assert.That(value.HasHeading || value.HasRoll, Is.False);
            reference.Clear();
            Assert.That(reporter.TrySample(out _, out _), Is.False);
            Assert.That(reporter.HasValue, Is.False);
        }
    }
}
