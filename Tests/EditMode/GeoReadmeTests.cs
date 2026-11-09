using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.PackageManager;
using SceneGeography.DocumentationExamples;
using Object = UnityEngine.Object;

namespace SceneGeography.Tests
{
    public sealed class GeoReadmeTests
    {
        [TestCase("README.md")]
        [TestCase("README.zh-CN.md")]
        public void ReadmeCodeBlocksMatchTheirCompiledSources(string readme)
        {
            var package = PackageInfo.FindForAssembly(typeof(GeoReference).Assembly);
            Assert.That(package, Is.Not.Null, "Scene Geography must resolve as a UPM package.");
            string root = package.resolvedPath;
            var blocks = Regex.Matches(
                File.ReadAllText(Path.Combine(root, readme)),
                @"```csharp\r?\n([\s\S]*?)``` ".TrimEnd());
            string[] files =
            {
                "GeoRuntimeExamples",
                "GeoCalibrationExamples",
                "GeoNumericExamples",
                "GeoEditorExamples"
            };
            Assert.That(blocks.Count, Is.EqualTo(files.Length));
            for (int i = 0; i < files.Length; i++)
            {
                string compiled = File.ReadAllText(Path.Combine(root, "Tests/EditMode/Documentation", files[i] + ".cs"));
                Assert.That(
                    compiled.Replace("\r\n", "\n").TrimEnd(),
                    Is.EqualTo(blocks[i].Groups[1].Value.Replace("\r\n", "\n").TrimEnd()));
            }
        }

        [Test]
        public void RuntimeExamplesInitializePlaceVisitReadAndBind()
        {
            var host = new GameObject("Readme reference");
            var poi = new GameObject("Readme POI");
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                var reference = host.AddComponent<GeoReference>();
                GeoTestData.FillProfile(profile, GeoCalibrationMode.Upright);
                reference.TrySetLocalFrameId(profile.localFrameId, out _);
                Assert.That(
                    GeoRuntimeExamples.Initialize(reference, profile, out _, out var error),
                    Is.True,
                    error.ToString());
                var coordinate = GeoTestData.Origin.ToCoordinate();
                Assert.That(
                    GeoRuntimeExamples.PlacePoi(reference, coordinate, poi.transform, out error),
                    Is.True,
                    error.ToString());
                Assert.That(
                    GeoRuntimeExamples.ReadPlayer(reference, poi.transform, out var read, out error),
                    Is.True,
                    error.ToString());
                Assert.That(read.Reference.HorizontalType, Is.EqualTo(GeoCoordinateType.Wgs84));
                Assert.That(
                    GeoRuntimeExamples.GetVisitPose(reference, coordinate, out var pose, out error),
                    Is.True,
                    error.ToString());
                Assert.That(Vector3.Distance(pose.position, poi.transform.position), Is.GreaterThan(1));
                var anchor = poi.AddComponent<GeoAnchor>();
                var orientation = poi.AddComponent<GeoOrientation>();
                Assert.That(
                    GeoRuntimeExamples.ConfigureAndApply(reference, anchor, orientation, coordinate, out error),
                    Is.True,
                    error.ToString());
                Assert.That(anchor.HasAppliedPosition, Is.True);
                Assert.That(orientation.LastError.IsNone, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(poi);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(profile);
            }
        }

        [TestCase(GeoCalibrationMode.SingleAnchor)]
        [TestCase(GeoCalibrationMode.Upright)]
        [TestCase(GeoCalibrationMode.Spatial)]
        public void CalibrationExamplesSolveAllModesWithoutReference(GeoCalibrationMode mode)
        {
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                GeoTestData.FillProfile(profile, mode);
                Assert.That(profile.TryBuildRequest(out var source, out _), Is.True);
                GeoCalibrationRequest request;
                if (mode == GeoCalibrationMode.SingleAnchor)
                {
                    var east = GeoTestData.ModelRotation(mode) * Vector3.right;
                    request = GeoCalibrationExamples.Single(source.Points[0], GeoVector3d.UnitY, new GeoVector3d(east.x, east.y, east.z), 1.25);
                }
                else if (mode == GeoCalibrationMode.Upright)
                {
                    request = GeoCalibrationExamples.Upright(source.Points, GeoVector3d.UnitY);
                }
                else
                {
                    request = GeoCalibrationExamples.Spatial(source.Points);
                }

                Assert.That(
                    GeoCalibrationExamples.SolveWithoutUnity(request, GeoTestData.Origin, out var local, out var error),
                    Is.True,
                    error.ToString());
                Assert.That(local.IsFinite, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void NumericExamplesConvertRoundTripAndMeasureWithoutBindings()
        {
            var host = new GameObject("Readme numeric reference");
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                GeoTestData.FillProfile(profile, GeoCalibrationMode.Spatial);
                Assert.That(profile.TryBuildRequest(out var request, out _), Is.True);
                Assert.That(GeoNumericExamples.CreateMapping(request, out var mapping, out var error), Is.True, error.ToString());
                var coordinate = GeoTestData.Origin.ToCoordinate();
                var output = new GeoCoordinateReference(GeoCoordinateType.Gcj02, GeoHeightReference.RelativeToEllipsoidZero(10));
                Assert.That(GeoNumericExamples.ConvertCoordinate(coordinate, output, out var converted, out error), Is.True, error.ToString());
                Assert.That(converted.HeightMeters, Is.EqualTo(5).Within(.001));
                Assert.That(GeoNumericExamples.LocalPoseRoundTrip(mapping, coordinate, output, out var local, out var back, out error), Is.True, error.ToString());
                Assert.That(back.Coordinate.LatitudeDegrees, Is.EqualTo(converted.LatitudeDegrees).Within(1e-7));
                Assert.That(back.Attitude.Heading.Degrees, Is.EqualTo(90).Within(.001));
                Assert.That(local.Position.IsFinite, Is.True);

                var reference = host.AddComponent<GeoReference>();
                reference.TrySetLocalFrameId(profile.localFrameId, out _);
                Assert.That(reference.TryInitialize(request, out _, out error), Is.True, error.ToString());
                Assert.That(GeoNumericExamples.FromActiveReference(reference, coordinate, out var active, out error), Is.True, error.ToString());
                Assert.That((active.Position - local.Position).Magnitude, Is.LessThan(.001));
                reference.TryGetWorldRotation(GeoAttitude.FromDegrees(42, 12, -7), out var rotation, out _);
                var correction = Quaternion.Euler(0, 90, 0);
                Assert.That(GeoNumericExamples.MeasureWorldRotation(reference, rotation * correction, correction, out var attitude, out error), Is.True, error.ToString());
                Assert.That(attitude.Heading.Degrees, Is.EqualTo(42).Within(.001));
                Assert.That(GeoNumericExamples.MeasureWorldRotation(reference, rotation, default, out _, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void EditorExamplesCaptureAndPreviewWithoutSavingAssets()
        {
            var host = new GameObject("Readme editor reference");
            var point = new GameObject("Readme marker");
            GeoCalibrationProfile profile = null;
            try
            {
                var reference = host.AddComponent<GeoReference>();
                var marker = point.AddComponent<GeoControlPointMarker>();
                marker.reference = reference;
                marker.pointId = "A";
                marker.coordinate = GeoCoordinateSettings.From(GeoTestData.Origin.ToCoordinate());
                Assert.That(
                    GeoEditorExamples.Capture(new[] { marker }, GeoCalibrationMode.SingleAnchor, out profile, out var error),
                    Is.True,
                    error.ToString());
                Assert.That(
                    GeoEditorExamples.Preview(reference, profile, out var report, out error),
                    Is.True,
                    error.ToString());
                Assert.That(report.Success, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(point);
                Object.DestroyImmediate(host);
                if (profile != null)
                {
                    Object.DestroyImmediate(profile);
                }
            }
        }
    }
}
