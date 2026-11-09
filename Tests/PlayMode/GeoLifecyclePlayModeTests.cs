using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SceneGeography.Tests
{
    public sealed class GeoLifecyclePlayModeTests
    {
        GameObject host, root, poi;
        GeoReference reference;
        GeoCalibrationProfile profile;
        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Play reference");
            root = new GameObject("Play root");
            poi = new GameObject("Play POI");
            reference = host.AddComponent<GeoReference>();
            profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            GeoTestData.FillProfile(profile, GeoCalibrationMode.Upright);
            reference.TrySetRoot(root.transform, out _);
            reference.TrySetLocalFrameId(GeoTestData.FrameId, out _);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(poi);
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(profile);
        }

        void Initialize()
        {
            Assert.That(profile.TryBuildRequest(out var request, out var error), Is.True, error.ToString());
            Assert.That(reference.TryInitialize(request, out _, out error), Is.True, error.ToString());
        }

        [UnityTest]
        public IEnumerator AnchorWaitsForInitializationThenFollowsRoot()
        {
            var anchor = poi.AddComponent<GeoAnchor>();
            anchor.Configure(reference, new GeoPlacement(GeoTestData.Origin.ToCoordinate()));
            yield return null;
            Assert.That(anchor.HasAppliedPosition, Is.False);
            Initialize();
            Assert.That(anchor.HasAppliedPosition, Is.True);
            var before = poi.transform.position;
            root.transform.position += Vector3.right * 10;
            yield return null;
            yield return null;
            Assert.That(poi.transform.position.x - before.x, Is.EqualTo(10).Within(.001));
            root.transform.localScale = new Vector3(1, 2, 1);
            before = poi.transform.position;
            yield return null;
            yield return null;
            Assert.That(poi.transform.position, Is.EqualTo(before));
            Assert.That(anchor.LastError.Code, Is.EqualTo(GeoErrorCode.InvalidRootTransform));
            root.transform.localScale = Vector3.one;
            yield return null;
            yield return null;
            Assert.That(anchor.LastError.IsNone, Is.True);
        }

        [UnityTest]
        public IEnumerator DisabledBindingDoesNotCompeteWithMovement()
        {
            var anchor = poi.AddComponent<GeoAnchor>();
            anchor.Configure(reference, new GeoPlacement(GeoTestData.Origin.ToCoordinate()));
            Initialize();
            anchor.enabled = false;
            poi.transform.position = new Vector3(1, 2, 3);
            root.transform.position += Vector3.right * 20;
            yield return null;
            yield return null;
            Assert.That(poi.transform.position, Is.EqualTo(new Vector3(1, 2, 3)));
            anchor.enabled = true;
            yield return null;
            yield return null;
            Assert.That(poi.transform.position, Is.Not.EqualTo(new Vector3(1, 2, 3)));
        }

        [UnityTest]
        public IEnumerator DestroyedLookTargetKeepsLastRotation()
        {
            Initialize();
            var target = new GameObject("Look target");
            target.transform.position = Vector3.forward * 30;
            var orientation = poi.AddComponent<GeoOrientation>();
            orientation.Configure(
                reference,
                GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(0)),
                Quaternion.identity);
            orientation.SetTransformTarget(target.transform, GeoLookMode.Horizontal);
            reference.SynchronizeBindings();
            var before = poi.transform.rotation;
            Object.Destroy(target);
            yield return null;
            yield return null;
            Assert.That(Quaternion.Angle(before, poi.transform.rotation), Is.LessThan(.001));
            Assert.That(orientation.LastError.Code, Is.EqualTo(GeoErrorCode.MissingLookTarget));
        }

        [UnityTest]
        public IEnumerator ReporterNeverTakesPositionOwnership()
        {
            Initialize();
            var reporter = poi.AddComponent<GeoPositionReporter>();
            reporter.Configure(reference, null, GeoCoordinateReference.Wgs84Ellipsoidal);
            poi.transform.position = new Vector3(10, 2, 30);
            var before = poi.transform.position;
            Assert.That(reporter.TrySample(out _, out _), Is.True);
            yield return null;
            Assert.That(poi.transform.position, Is.EqualTo(before));
            reference.Clear();
            Assert.That(reporter.HasValue, Is.False);
            Assert.That(poi.transform.position, Is.EqualTo(before));
        }

    }
}
