using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SceneGeography.Tests
{
    public sealed class GeoNumericPoseTests
    {
        static GeoLocalMapping Mapping(GeoCalibrationMode mode)
        {
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                GeoTestData.FillProfile(profile, mode);
                Assert.That(profile.TryBuildRequest(out var request, out var error), Is.True, error.ToString());
                Assert.That(GeoCalibrationEngine.TrySolve(request, GeoCoordinateConverter.CreateDefault(), out var solution, out _, out error), Is.True, error.ToString());
                return new GeoLocalMapping(solution.Frame);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [TestCase(0, 0, 0)]
        [TestCase(90, 20, 30)]
        [TestCase(359, -80, -170)]
        [TestCase(240, 89, 135)]
        public void AttitudeMeasuresThePublishedAngleConvention(double heading, double pitch, double roll)
        {
            var value = GeoAttitude.FromDegrees(heading, pitch, roll);
            Assert.That(value.IsValid && value.HasHeading && value.HasRoll, Is.True);
            Assert.That(Mathf.DeltaAngle((float)value.Heading.Degrees, (float)heading), Is.EqualTo(0).Within(.001));
            Assert.That(value.PitchUpDegrees, Is.EqualTo(pitch).Within(.001));
            Assert.That(Mathf.DeltaAngle((float)value.RollClockwiseDegrees, (float)roll), Is.EqualTo(0).Within(.001));
        }

        [TestCase(90)]
        [TestCase(-90)]
        public void VerticalAttitudePreservesRotationWithoutInventingHeadingOrRoll(double pitch)
        {
            var value = GeoAttitude.FromDegrees(123, pitch, 47);
            Assert.That(value.IsValid, Is.True);
            Assert.That(value.HasHeading || value.HasRoll, Is.False);
            Assert.That(value.PitchUpDegrees, Is.EqualTo(pitch).Within(.001));
            Assert.That(GeoAttitude.TryFromRotation(value.GeographicRotation, out var read, out _), Is.True);
            Assert.That(Quaternion.Angle(read.GeographicRotation, value.GeographicRotation), Is.LessThan(.05));
        }

        [Test]
        public void ArbitraryRotationsDecomposeAndRecomposeWithoutEulerOrderChanges()
        {
            var random = new System.Random(823);
            for (int i = 0; i < 200; i++)
            {
                var rotation = Quaternion.Euler((float)random.NextDouble() * 360, (float)random.NextDouble() * 360, (float)random.NextDouble() * 360);
                Assert.That(GeoAttitude.TryFromRotation(rotation, out var read, out var error), Is.True, error.ToString());
                if (!read.HasHeading)
                {
                    continue;
                }

                var rebuilt = GeoAttitude.FromDegrees(read.Heading.Degrees, read.PitchUpDegrees, read.RollClockwiseDegrees);
                Assert.That(Quaternion.Angle(rotation, rebuilt.GeographicRotation), Is.LessThan(.06));
            }
        }

        [Test]
        public void InvalidQuaternionsAndDefaultPoseAreRejected()
        {
            Assert.That(GeoAttitude.TryFromRotation(default, out _, out _), Is.False);
            Assert.That(GeoAttitude.TryFromRotation(new Quaternion(0, 0, 0, 2), out _, out _), Is.False);
            Assert.That(GeoAttitude.TryFromRotation(new Quaternion(float.NaN, 0, 0, 1), out _, out _), Is.False);
            var mapping = new GeoLocalMapping(GeoTestData.IdentityFrame());
            Assert.That(mapping.TryGetLocalRotation(default, out _, out _), Is.False);
            Assert.That(mapping.TryLocalPoseToGeo(default, GeoCoordinateReference.Wgs84Ellipsoidal, out _, out _), Is.False);
        }

        [TestCase(GeoCalibrationMode.SingleAnchor, GeoCoordinateType.Wgs84)]
        [TestCase(GeoCalibrationMode.SingleAnchor, GeoCoordinateType.Gcj02)]
        [TestCase(GeoCalibrationMode.SingleAnchor, GeoCoordinateType.Bd09)]
        [TestCase(GeoCalibrationMode.Upright, GeoCoordinateType.Wgs84)]
        [TestCase(GeoCalibrationMode.Upright, GeoCoordinateType.Gcj02)]
        [TestCase(GeoCalibrationMode.Upright, GeoCoordinateType.Bd09)]
        [TestCase(GeoCalibrationMode.Spatial, GeoCoordinateType.Wgs84)]
        [TestCase(GeoCalibrationMode.Spatial, GeoCoordinateType.Gcj02)]
        [TestCase(GeoCalibrationMode.Spatial, GeoCoordinateType.Bd09)]
        public void StandaloneMappingRoundTripsPoseWithoutAnyGameObject(GeoCalibrationMode mode, GeoCoordinateType type)
        {
            var mapping = Mapping(mode);
            var source = GeoTestData.GeoAt(new GeoVector3d(25, 13, -17), type);
            var attitude = GeoAttitude.FromDegrees(235, 28, -35);
            Assert.That(mapping.TryGeoToLocalPose(source, attitude, out var local, out var error), Is.True, error.ToString());
            var expected = GeoTestData.ModelPosition(new GeoVector3d(25, 13, -17), mode);
            Assert.That((local.Position - new GeoVector3d(expected.x, expected.y, expected.z)).Magnitude, Is.LessThan(.02));
            var output = new GeoCoordinateReference(type, GeoHeightReference.RelativeToEllipsoidZero(10));
            Assert.That(mapping.TryLocalPoseToGeo(local, output, out var read, out error), Is.True, error.ToString());
            Assert.That(read.Coordinate.LatitudeDegrees, Is.EqualTo(source.LatitudeDegrees).Within(1e-7));
            Assert.That(read.Coordinate.LongitudeDegrees, Is.EqualTo(source.LongitudeDegrees).Within(1e-7));
            Assert.That(read.Coordinate.HeightMeters + 10, Is.EqualTo(source.HeightMeters).Within(.001));
            Assert.That(Quaternion.Angle(read.Attitude.GeographicRotation, attitude.GeographicRotation), Is.LessThan(.06));
        }

        [Test]
        public void PureCoordinateConversionDoesNotRequireCalibration()
        {
            var converter = GeoCoordinateConverter.CreateDefault();
            var output = new GeoCoordinateReference(GeoCoordinateType.Bd09, GeoHeightReference.RelativeToEllipsoidZero(10));
            Assert.That(converter.TryConvert(GeoTestData.Origin.ToCoordinate(), output, out var converted, out var error), Is.True, error.ToString());
            Assert.That(converted.HeightMeters, Is.EqualTo(5));
            Assert.That(converter.TryConvert(converted, GeoCoordinateReference.Wgs84Ellipsoidal, out var back, out error), Is.True, error.ToString());
            Assert.That(back.LatitudeDegrees, Is.EqualTo(GeoTestData.Origin.LatitudeDegrees).Within(1e-7));
        }

        [Test]
        public void LocalPlacementChecksOffsetEndpointAndUnsupportedHeight()
        {
            var options = GeoCalibrationOptions.CreateDefault();
            options.ReferenceOrigin = GeoTestData.Origin;
            options.MaximumDistanceFromOriginMeters = 50;
            var point = new GeoControlPoint("A", GeoVector3d.Zero, GeoTestData.Origin.ToCoordinate());
            var request = GeoCalibrationRequest.SingleAnchor(point,
                new GeoSingleAnchorSettings(GeoVector3d.UnitY, GeoVector3d.UnitZ, GeoHeading.FromDegrees(0), 1, options));
            Assert.That(GeoCalibrationEngine.TrySolve(request, GeoCoordinateConverter.CreateDefault(), out var solution, out _, out _), Is.True);
            var mapping = new GeoLocalMapping(solution.Frame);
            var placement = new GeoPlacement(GeoTestData.Origin.ToCoordinate(), new GeoEnuOffset(51, 0, 0));
            Assert.That(mapping.TryGetLocalPose(placement, GeoAttitude.FromDegrees(0), out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.OutOfConfiguredRange));
            var unsupported = new GeoCoordinateReference(GeoCoordinateType.Wgs84, GeoHeightReference.Orthometric("not-installed"));
            Assert.That(mapping.TryLocalToCoordinate(GeoVector3d.Zero, unsupported, out _, out error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.MissingHeightModel));
        }

        [Test]
        public void ReferenceLocalMappingSurvivesInvalidDestroyedRootAndClear()
        {
            var host = new GameObject("Numeric reference");
            var root = new GameObject("Numeric root");
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                var reference = host.AddComponent<GeoReference>();
                GeoTestData.FillProfile(profile, GeoCalibrationMode.Spatial);
                reference.TrySetLocalFrameId(profile.localFrameId, out _);
                reference.TrySetRoot(root.transform, out _);
                Assert.That(profile.TryBuildRequest(out var request, out _), Is.True);
                Assert.That(reference.TryInitialize(request, out _, out var error), Is.True, error.ToString());
                Assert.That(reference.TryCaptureLocalMapping(out var mapping, out _), Is.True);
                root.transform.localScale = new Vector3(1, 2, 1);
                Assert.That(reference.TryGeoToLocalPose(GeoTestData.Origin.ToCoordinate(), GeoAttitude.FromDegrees(90), out var pose, out error), Is.True, error.ToString());
                Assert.That(reference.TryGeoToWorldPose(GeoTestData.Origin.ToCoordinate(), GeoAttitude.FromDegrees(90), out _, out _), Is.False);
                Assert.That(reference.TryLocalPoseToGeo(pose, GeoCoordinateReference.Wgs84Ellipsoidal, out _, out _), Is.True);
                Object.DestroyImmediate(root);
                Assert.That(reference.TryCaptureLocalMapping(out var same, out _), Is.True);
                Assert.That(same, Is.SameAs(mapping));
                reference.Clear();
                Assert.That(reference.TryCaptureLocalMapping(out _, out _), Is.False);
                Assert.That(mapping.TryLocalPoseToGeo(pose, GeoCoordinateReference.Wgs84Ellipsoidal, out _, out _), Is.True);
            }
            finally
            {
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void WorldPoseMeasurementMatchesRequestAnglesUnderRotatedScaledRoot()
        {
            var host = new GameObject("World numeric reference");
            var root = new GameObject("World numeric root");
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                var reference = host.AddComponent<GeoReference>();
                GeoTestData.FillProfile(profile, GeoCalibrationMode.Spatial);
                reference.TrySetLocalFrameId(profile.localFrameId, out _);
                reference.TrySetRoot(root.transform, out _);
                root.transform.SetPositionAndRotation(new Vector3(20, -4, 40), Quaternion.Euler(14, 70, -9));
                root.transform.localScale = Vector3.one * 2;
                profile.TryBuildRequest(out var request, out _);
                Assert.That(reference.TryInitialize(request, out _, out var error), Is.True, error.ToString());
                var input = GeoTestData.Origin.ToCoordinate();
                var attitude = GeoAttitude.FromDegrees(62, 27, -35);
                Assert.That(reference.TryGeoToWorldPose(input, attitude, out var world, out error), Is.True, error.ToString());
                Assert.That(reference.TryWorldPoseToGeo(world, GeoCoordinateReference.Wgs84Ellipsoidal, out var back, out error), Is.True, error.ToString());
                Assert.That(back.Coordinate.HeightMeters, Is.EqualTo(input.HeightMeters).Within(.001));
                Assert.That(Quaternion.Angle(back.Attitude.GeographicRotation, attitude.GeographicRotation), Is.LessThan(.06));
                var orientation = GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(62), new GeoRotationOffset(0, 27, -35));
                Assert.That(reference.TryGetWorldRotation(world.position, orientation, out var rotation, out _), Is.True);
                Assert.That(Quaternion.Angle(rotation, world.rotation), Is.LessThan(.06));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
