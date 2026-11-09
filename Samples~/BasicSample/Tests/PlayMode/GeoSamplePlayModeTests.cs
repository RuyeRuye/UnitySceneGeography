using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SceneGeography.Samples.Tests
{
    public sealed class GeoSamplePlayModeTests
    {
        GameObject host, root;
        GeoReference reference;
        GeoCalibrationProfile profile;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Sample reference");
            root = new GameObject("Sample root");
            reference = host.AddComponent<GeoReference>();
            reference.TrySetRoot(root.transform, out _);
            reference.TrySetLocalFrameId(GeoDemoData.FrameId, out _);
            profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(profile);
        }

        [UnityTest]
        public IEnumerator GeneratedGeometryUsesPackagedShader()
        {
            GeoDemoData.FillProfile(profile, GeoCalibrationMode.Upright);
            var demo = host.AddComponent<GeoReferenceDemo>();
            demo.reference = reference;
            demo.sceneRoot = root.transform;
            demo.profile = profile;
            demo.showControls = false;
            yield return null;
            Assert.That(demo.Initialized, Is.True, demo.Status);
            var renderers = root.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThan(5));
            foreach (var renderer in renderers)
            {
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("SceneGeography/Samples/Unlit"));
                Assert.That(renderer.sharedMaterial.shader.isSupported, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator InteractiveDemoBootstrapsAndRejectsBadCalibration()
        {
            foreach (GeoCalibrationMode mode in new[]
            {
                GeoCalibrationMode.SingleAnchor,
                GeoCalibrationMode.Upright,
                GeoCalibrationMode.Spatial
            })
            {
                GeoDemoData.FillProfile(profile, mode);
                reference.Clear();
                var demo = host.AddComponent<GeoReferenceDemo>();
                demo.reference = reference;
                demo.sceneRoot = root.transform;
                demo.profile = profile;
                demo.createProxyGeometry = false;
                demo.showControls = false;
                var camera = new GameObject("Test viewer").AddComponent<GeoDemoCamera>();
                camera.reference = reference;
                camera.enabled = false;
                demo.viewer = camera;
                yield return null;
                Assert.That(demo.Initialized, Is.True, demo.Status);
                Assert.That(demo.PoiTransforms.Count, Is.EqualTo(5));
                demo.VisitPoi(0);
                Assert.That(camera.transform.position.sqrMagnitude, Is.GreaterThan(1));
                long version = reference.CalibrationVersion;
                Assert.That(demo.Reinitialize(true), Is.False);
                Assert.That(reference.CalibrationVersion, Is.EqualTo(version));
                Assert.That(demo.Reinitialize(false), Is.True);
                Assert.That(reference.CalibrationVersion, Is.GreaterThan(version));
                Object.Destroy(camera.gameObject);
                Object.Destroy(demo);
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    Object.Destroy(root.transform.GetChild(i).gameObject);
                }

                yield return null;
            }
        }
    }
}
