using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SceneGeography.Tests
{
    public sealed class GeoApiDesignTests
    {
        [Test]
        public void PlacementReadValidatesCurrentConfigurationWithoutInitialization()
        {
            var go = new GameObject("Placement read");
            try
            {
                var anchor = go.AddComponent<GeoAnchor>();
                Assert.That(anchor.TryGetPlacement(out _, out var error), Is.False);
                Assert.That(error.Code, Is.EqualTo(GeoErrorCode.CoordinateTypeUnspecified));
                anchor.SetPlacement(new GeoPlacement(GeoTestData.Origin.ToCoordinate(), new GeoEnuOffset(1, 2, 3)));
                anchor.eastOffsetMeters = 7;
                Assert.That(anchor.TryGetPlacement(out var placement, out error), Is.True, error.ToString());
                Assert.That(placement.Offset.EastMeters, Is.EqualTo(7));
                Assert.That(anchor.HasAppliedPosition, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void InvalidConfigureDoesNotChangeExistingReferenceOrRequest()
        {
            var first = new GameObject("First reference");
            var second = new GameObject("Second reference");
            var poi = new GameObject("POI");
            try
            {
                var a = first.AddComponent<GeoReference>();
                var b = second.AddComponent<GeoReference>();
                var anchor = poi.AddComponent<GeoAnchor>();
                anchor.Configure(a, new GeoPlacement(GeoTestData.Origin.ToCoordinate()));
                Assert.Throws<ArgumentException>(() => anchor.Configure(b, default));
                Assert.That(anchor.Reference, Is.SameAs(a));
                var orientation = poi.AddComponent<GeoOrientation>();
                var request = GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(30));
                orientation.Configure(a, request, Quaternion.identity);
                Assert.Throws<ArgumentException>(() => orientation.Configure(b, request, default));
                Assert.That(orientation.Reference, Is.SameAs(a));
                Assert.Throws<ArgumentException>(() => orientation.Configure(b, default, Quaternion.identity));
                Assert.That(orientation.Reference, Is.SameAs(a));
                Assert.That(orientation.headingDegrees, Is.EqualTo(30));
            }
            finally
            {
                Object.DestroyImmediate(poi);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void ExplicitOrientationApplyUsesCurrentSerializedFieldsNotOldRequestCache()
        {
            var host = new GameObject("Reference");
            var poi = new GameObject("POI");
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                var reference = host.AddComponent<GeoReference>();
                GeoTestData.FillProfile(profile, GeoCalibrationMode.Upright);
                reference.TrySetLocalFrameId(profile.localFrameId, out _);
                Assert.That(profile.TryBuildRequest(out var request, out _), Is.True);
                Assert.That(reference.TryInitialize(request, out _, out _), Is.True);
                var orientation = poi.AddComponent<GeoOrientation>();
                orientation.Configure(
                    reference,
                    GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(0)),
                    Quaternion.identity);
                orientation.headingDegrees = 90;
                Assert.That(orientation.TryApply(out var error), Is.True, error.ToString());
                reference.TryGetWorldAxes(out var axes, out _);
                Assert.That(Vector3.Dot(poi.transform.forward, axes.East), Is.GreaterThan(.9999));
            }
            finally
            {
                Object.DestroyImmediate(poi);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
