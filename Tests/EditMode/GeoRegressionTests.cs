using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SceneGeography.Tests
{
    public sealed class GeoRegressionTests
    {
        GameObject host, root, other;
        GeoReference reference;
        GeoCalibrationProfile profile;
        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Regression reference");
            root = new GameObject("Regression root");
            other = new GameObject("Regression target");
            reference = host.AddComponent<GeoReference>();
            reference.TrySetRoot(root.transform, out _);
            reference.TrySetLocalFrameId(GeoTestData.FrameId, out _);
            profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            GeoTestData.FillProfile(profile, GeoCalibrationMode.Upright);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(other);
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(profile);
        }

        GeoCalibrationRequest Request()
        {
            Assert.That(profile.TryBuildRequest(out var request, out var error), Is.True, error.ToString());
            return request;
        }

        void Initialize()
        {
            Assert.That(reference.TryInitialize(Request(), out _, out var error), Is.True, error.ToString());
        }

        [TestCase("root")]
        [TestCase("localFrameId")]
        public void InspectorIdentityChangesExpireCandidate(string field)
        {
            Assert.That(reference.TryPrepareCalibration(Request(), out var candidate, out _, out _), Is.True);
            var serialized = new SerializedObject(reference);
            if (field == "root")
            {
                serialized.FindProperty(field).objectReferenceValue = other.transform;
            }
            else
            {
                serialized.FindProperty(field).stringValue = "different-frame";
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(reference.TryApplyCalibration(candidate, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.StaleCalibrationCandidate));
            Assert.That(reference.IsInitialized, Is.False);
        }

        [Test]
        public void MarkerCapturesRootLocalRatherThanImmediateParentLocal()
        {
            root.transform.SetPositionAndRotation(new Vector3(30, 10, -20), Quaternion.Euler(12, 25, -8));
            root.transform.localScale = Vector3.one * 2;
            var marker = other.AddComponent<GeoControlPointMarker>();
            marker.reference = reference;
            marker.pointId = "marker";
            marker.coordinate = GeoCoordinateSettings.From(GeoTestData.Origin.ToCoordinate());
            var expected = new Vector3(8, 4, 12);
            other.transform.position = root.transform.TransformPoint(expected);
            Assert.That(marker.TryCapture(out var point, out var error), Is.True, error.ToString());
            Assert.That((point.LocalPosition - new GeoVector3d(8, 4, 12)).Magnitude, Is.LessThan(1e-4));
        }

        [Test]
        public void ManuallyStartedAnchorRecoversAfterInvalidRoot()
        {
            Initialize();
            var anchor = other.AddComponent<GeoAnchor>();
            anchor.startAutomatically = false;
            anchor.Configure(reference, new GeoPlacement(GeoTestData.Origin.ToCoordinate()));
            reference.SynchronizeBindings();
            Assert.That(anchor.HasAppliedPosition, Is.False);
            Assert.That(anchor.TryApply(out _), Is.True);
            root.transform.localScale = new Vector3(1, 2, 1);
            reference.SynchronizeBindings();
            Assert.That(anchor.HasAppliedPosition, Is.False);
            root.transform.localScale = Vector3.one;
            reference.SynchronizeBindings();
            Assert.That(anchor.HasAppliedPosition, Is.True);
        }

        [Test]
        public void PlacementOffsetCannotBypassRangeLimit()
        {
            var options = GeoCalibrationOptions.CreateDefault();
            options.ReferenceOrigin = GeoTestData.Origin;
            options.MaximumDistanceFromOriginMeters = 100;
            var point = new GeoControlPoint("A", GeoVector3d.Zero, GeoTestData.Origin.ToCoordinate());
            var request = GeoCalibrationRequest.SingleAnchor(
                point,
                new GeoSingleAnchorSettings(GeoVector3d.UnitY, GeoVector3d.UnitZ, GeoHeading.FromDegrees(0), 1, options));
            Assert.That(reference.TryInitialize(request, out _, out _), Is.True);
            Assert.That(
                reference.TryGetWorldPosition(
                    new GeoPlacement(GeoTestData.Origin.ToCoordinate(), new GeoEnuOffset(101, 0, 0)),
                    out _,
                    out var error),
                Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.OutOfConfiguredRange));
        }

        [Test]
        public void FingerprintIncludesSettingsNotOnlyPoints()
        {
            var converter = GeoCoordinateConverter.CreateDefault();
            Assert.That(GeoCalibrationEngine.TrySolve(Request(), converter, out _, out var first, out _), Is.True);
            Assert.That(
                GeoCalibrationEngine.TrySolve(Request(), converter, out _, out var identical, out _),
                Is.True);
            Assert.That(first.DataFingerprint, Is.EqualTo(identical.DataFingerprint));
            profile.options.minimumBaselineMeters = .02;
            Assert.That(
                GeoCalibrationEngine.TrySolve(Request(), converter, out _, out var changed, out _),
                Is.True);
            Assert.That(first.DataFingerprint, Is.Not.EqualTo(changed.DataFingerprint));
        }
    }
}
