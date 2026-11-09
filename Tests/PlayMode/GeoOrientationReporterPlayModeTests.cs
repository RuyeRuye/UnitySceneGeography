using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SceneGeography.Tests
{
    public sealed class GeoOrientationReporterPlayModeTests
    {
        [UnityTest]
        public IEnumerator ReporterSamplesAfterOrientationAndRespectsDisableAndInterval()
        {
            var host = new GameObject("Orientation reporter runtime");
            var poi = new GameObject("Reported POI");
            var root = new GameObject("Reporter root");
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                var reference = host.AddComponent<GeoReference>();
                reference.SynchronizeAutomatically = false;
                reference.TrySetRoot(root.transform, out _);
                GeoTestData.FillProfile(profile, GeoCalibrationMode.Spatial);
                reference.TrySetLocalFrameId(profile.localFrameId, out _);
                var facing = poi.AddComponent<GeoOrientation>();
                facing.Configure(reference, GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(42), new GeoRotationOffset(0, 20, 30)), Quaternion.Euler(0, 90, 0));
                var reporter = poi.AddComponent<GeoOrientationReporter>();
                reporter.Configure(reference, null, facing.modelCorrection);
                reporter.samplingIntervalSeconds = 100;
                int calls = 0;
                reporter.OrientationUpdated += _ => calls++;
                profile.TryBuildRequest(out var request, out _);
                Assert.That(reference.TryInitialize(request, out _, out var error), Is.True, error.ToString());
                Assert.That(reporter.HasValue, Is.True);
                Assert.That(reporter.LastValue.Heading.Degrees, Is.EqualTo(42).Within(.001));
                int first = calls;
                reference.SynchronizeBindings();
                Assert.That(calls, Is.EqualTo(first));
                Assert.That(reporter.TrySample(out _, out _), Is.True);
                Assert.That(calls, Is.EqualTo(first + 1));
                reporter.enabled = false;
                reporter.samplingIntervalSeconds = 0;
                yield return null;
                reference.SynchronizeBindings();
                Assert.That(calls, Is.EqualTo(first + 1));
                reporter.enabled = true;
                yield return null;
                root.transform.rotation = Quaternion.Euler(12, 80, -6);
                reference.SynchronizeBindings();
                Assert.That(reporter.LastValue.Heading.Degrees, Is.EqualTo(42).Within(.001));
                Assert.That(reporter.LastValue.PitchUpDegrees, Is.EqualTo(20).Within(.001));
                Assert.That(reporter.LastValue.RollClockwiseDegrees, Is.EqualTo(30).Within(.001));
                reference.Clear();
                Assert.That(reporter.HasValue, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(poi);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
