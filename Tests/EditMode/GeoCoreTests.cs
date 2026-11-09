using System;
using NUnit.Framework;
using UnityEngine;

namespace SceneGeography.Tests
{
    public sealed class GeodesyAndConversionTests
    {
        [Test]
        public void EquatorialEcefMatchesIndependentReference()
        {
            var e = Wgs84Geodesy.ToEcef(Wgs84Coordinate.FromDegrees(0, 0, 0));
            Assert.That(e.X, Is.EqualTo(6378137).Within(1e-8));
            Assert.That(e.Y, Is.EqualTo(0).Within(1e-8));
            Assert.That(e.Z, Is.EqualTo(0).Within(1e-8));
            e = Wgs84Geodesy.ToEcef(Wgs84Coordinate.FromDegrees(0, 90, 12));
            Assert.That(e.Y, Is.EqualTo(6378149).Within(1e-8));
            Assert.That(e.X, Is.EqualTo(0).Within(1e-7));
            e = Wgs84Geodesy.ToEcef(Wgs84Coordinate.FromDegrees(90, 0, 0));
            Assert.That(e.Z, Is.EqualTo(6356752.314245179).Within(1e-6));
        }

        [TestCase(31.23, 121.47, 15)]
        [TestCase(-45, -179.999, -100)]
        [TestCase(89.99, 45, 1000)]
        [TestCase(0, 180, 0)]
        [TestCase(-90, 0, 12)]
        public void WgsEcefRoundTrip(double latitude, double longitude, double height)
        {
            var input = Wgs84Coordinate.FromDegrees(latitude, longitude, height);
            Assert.That(
                Wgs84Geodesy.TryFromEcef(Wgs84Geodesy.ToEcef(input), out var output, out var error),
                Is.True,
                error.ToString());
            Assert.That(output.LatitudeDegrees, Is.EqualTo(input.LatitudeDegrees).Within(1e-9));
            Assert.That(output.LongitudeDegrees, Is.EqualTo(input.LongitudeDegrees).Within(1e-9));
            Assert.That(output.EllipsoidHeightMeters, Is.EqualTo(height).Within(1e-5));
        }

        [Test]
        public void EarthCenterAndMissingMetadataAreRejected()
        {
            Assert.That(Wgs84Geodesy.TryFromEcef(default, out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.UndefinedGeodeticPosition));
            Assert.That(GeoCoordinateConverter.CreateDefault().TryToWgs84(default, out _, out error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.CoordinateTypeUnspecified));
            Assert.That(Wgs84Coordinate.TryCreate(0, double.NaN, 0, out _, out _), Is.False);
            Assert.That(Wgs84Coordinate.TryCreate(91, 0, 0, out _, out _), Is.False);
        }

        [Test]
        public void EnuAxisOrderMatchesIndependentEquatorialFrame()
        {
            var tangent = new EnuTangentFrame(Wgs84Coordinate.FromDegrees(0, 0, 0));
            Assert.That(
                tangent.TryFromEngine(new GeoVector3d(10, 20, 30), out var geo, out var error),
                Is.True,
                error.ToString());
            var e = Wgs84Geodesy.ToEcef(geo);
            Assert.That(e.X, Is.EqualTo(6378157).Within(1e-6));
            Assert.That(e.Y, Is.EqualTo(10).Within(1e-6));
            Assert.That(e.Z, Is.EqualTo(30).Within(1e-6));
        }

        [TestCase(51.5074, -0.1278)]
        [TestCase(40.7128, -74.0060)]
        [TestCase(-33.8688, 151.2093)]
        public void Wgs84OutsideGcjEnvelopeRemainsSupported(double latitude, double longitude)
        {
            var converter = GeoCoordinateConverter.CreateDefault();
            var input = GeoCoordinate.FromDegrees(latitude, longitude, 20, GeoCoordinateReference.Wgs84Ellipsoidal);
            Assert.That(converter.TryToWgs84(input, out var wgs84, out var error), Is.True, error.ToString());
            Assert.That(
                converter.TryFromWgs84(wgs84, GeoCoordinateReference.Wgs84Ellipsoidal, out var restored, out error),
                Is.True);
            Assert.That(restored.LatitudeDegrees, Is.EqualTo(latitude).Within(1e-10));
            Assert.That(restored.LongitudeDegrees, Is.EqualTo(longitude).Within(1e-10));
            foreach (var type in new[]
            {
                GeoCoordinateType.Gcj02,
                GeoCoordinateType.Bd09
            }

            )
            {
                var outputReference = new GeoCoordinateReference(type, GeoHeightReference.Ellipsoidal);
                Assert.That(converter.TryFromWgs84(wgs84, outputReference, out _, out error), Is.False);
                Assert.That(error.Code, Is.EqualTo(GeoErrorCode.UnsupportedCoordinateRegion));
            }
        }

        [Test]
        public void GcjForwardMatchesPublishedBeijingExample()
        {
            var converter = GeoCoordinateConverter.CreateDefault();
            var input = Wgs84Coordinate.FromDegrees(39.915, 116.404, 20);
            var reference = new GeoCoordinateReference(GeoCoordinateType.Gcj02, GeoHeightReference.Ellipsoidal);
            Assert.That(
                converter.TryFromWgs84(input, reference, out var result, out var error),
                Is.True,
                error.ToString());
            Assert.That(result.LatitudeDegrees, Is.EqualTo(39.91640428150164).Within(1e-10));
            Assert.That(result.LongitudeDegrees, Is.EqualTo(116.41024449916938).Within(1e-10));
        }

        [TestCase(GeoCoordinateType.Gcj02)]
        [TestCase(GeoCoordinateType.Bd09)]
        public void HorizontalConversionsPreserveExplicitRelativeHeight(GeoCoordinateType type)
        {
            var converter = GeoCoordinateConverter.CreateDefault();
            var input = Wgs84Coordinate.FromDegrees(31.2304, 121.4737, 27);
            var reference = new GeoCoordinateReference(type, GeoHeightReference.RelativeToEllipsoidZero(20));
            Assert.That(
                converter.TryFromWgs84(input, reference, out var converted, out var error),
                Is.True,
                error.ToString());
            Assert.That(converted.HeightMeters, Is.EqualTo(7).Within(1e-10));
            Assert.That(converter.TryToWgs84(converted, out var restored, out error), Is.True, error.ToString());
            Assert.That((Wgs84Geodesy.ToEcef(restored) - Wgs84Geodesy.ToEcef(input)).Magnitude, Is.LessThan(.011));
        }

        [Test]
        public void UnsupportedCoverageAndMissingHeightModelDoNotSilentlyPass()
        {
            var converter = GeoCoordinateConverter.CreateDefault();
            var gcj = GeoCoordinate.FromDegrees(
                51,
                -.1,
                0,
                new GeoCoordinateReference(GeoCoordinateType.Gcj02, GeoHeightReference.Ellipsoidal));
            Assert.That(converter.TryToWgs84(gcj, out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.UnsupportedCoordinateRegion));
            var sea = GeoCoordinate.FromDegrees(
                31,
                121,
                10,
                new GeoCoordinateReference(GeoCoordinateType.Wgs84, GeoHeightReference.Orthometric("not-installed")));
            Assert.That(converter.TryToWgs84(sea, out _, out error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.MissingHeightModel));
        }
    }

    public sealed class CalibrationTests
    {
        [TestCase(GeoCalibrationMode.SingleAnchor)]
        [TestCase(GeoCalibrationMode.Upright)]
        [TestCase(GeoCalibrationMode.Spatial)]
        public void SyntheticMixedStandardsRecoverKnownScaleAndCheckPoints(GeoCalibrationMode mode)
        {
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                GeoTestData.FillProfile(profile, mode);
                Assert.That(profile.TryBuildRequest(out var request, out var error), Is.True, error.ToString());
                Assert.That(
                    GeoCalibrationEngine.TrySolve(
                        request,
                        GeoCoordinateConverter.CreateDefault(),
                        out var solution,
                        out var report,
                        out error),
                    Is.True,
                    error.ToString());
                Assert.That(report.LocalUnitsPerMeter.Value, Is.EqualTo(1.25).Within(2e-4));
                Assert.That(report.CheckMetrics.Value.MaximumResidualMeters, Is.LessThan(.03));
                var geo = GeoTestData.GeoAt(new GeoVector3d(25, 13, -17));
                var expected = GeoTestData.ModelPosition(new GeoVector3d(25, 13, -17), mode);
                Assert.That(
                    GeoCoordinateConverter.CreateDefault().TryToWgs84(geo, out var canonical, out error),
                    Is.True);
                Assert.That(solution.Frame.TryWgs84ToLocal(canonical, out var actual, out error), Is.True);
                Assert.That((actual - new GeoVector3d(expected.x, expected.y, expected.z)).Magnitude, Is.LessThan(.03));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SpatialAcceptsThreeCoplanarNoncollinearPoints()
        {
            var profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            try
            {
                GeoTestData.FillProfile(profile, GeoCalibrationMode.Spatial);
                profile.points = System.Array.FindAll(profile.points, p => p.role == GeoControlPointRole.Fit);
                Assert.That(profile.TryBuildRequest(out var r, out var e), Is.True);
                Assert.That(
                    GeoCalibrationEngine.TrySolve(r, GeoCoordinateConverter.CreateDefault(), out _, out _, out e),
                    Is.False);
                Assert.That(e.Code, Is.EqualTo(GeoErrorCode.CheckPointsRequired));
                profile.options.limitCheckMaximum = false;
                Assert.That(profile.TryBuildRequest(out r, out e), Is.True);
                Assert.That(
                    GeoCalibrationEngine.TrySolve(r, GeoCoordinateConverter.CreateDefault(), out _, out _, out e),
                    Is.True,
                    e.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void UprightReportsHeightMismatchWithoutTilting()
        {
            var options = GeoCalibrationOptions.CreateDefault();
            options.ReferenceOrigin = GeoTestData.Origin;
            var points = new[]
            {
                new GeoControlPoint("A", new GeoVector3d(0, 0, 0), GeoTestData.GeoAt(new GeoVector3d(0, 0, 0))),
                new GeoControlPoint("B", new GeoVector3d(100, 5, 0), GeoTestData.GeoAt(new GeoVector3d(100, 0, 0)))
            };
            var request = GeoCalibrationRequest.Upright(points, new GeoUprightSettings(GeoVector3d.UnitY, GeoScalePolicy.Estimate, options));
            Assert.That(
                GeoCalibrationEngine.TrySolve(
                    request,
                    GeoCoordinateConverter.CreateDefault(),
                    out var result,
                    out var report,
                    out var error),
                Is.True,
                error.ToString());
            Assert.That(report.FitMetrics.Value.VerticalRmsMeters, Is.EqualTo(2.5).Within(1e-5));
            Assert.That(result.Frame.LocalAxes.Up.Y, Is.EqualTo(1).Within(1e-12));
        }

        [Test]
        public void RequestsCopyMutableOptionsAndLists()
        {
            var options = GeoCalibrationOptions.CreateDefault();
            var p = new GeoControlPoint("A", GeoVector3d.Zero, GeoTestData.Origin.ToCoordinate());
            var settings = new GeoSingleAnchorSettings(GeoVector3d.UnitY, GeoVector3d.UnitZ, GeoHeading.FromDegrees(0), 1, options);
            options.MinimumScale = 10;
            var request = GeoCalibrationRequest.SingleAnchor(p, settings);
            Assert.That(
                GeoCalibrationEngine.TrySolve(request, GeoCoordinateConverter.CreateDefault(), out _, out _, out var error),
                Is.True,
                error.ToString());
        }

        [Test]
        public void DegenerateInputAndParallelHeadingAreRejected()
        {
            var point = new GeoControlPoint("A", GeoVector3d.Zero, GeoTestData.Origin.ToCoordinate());
            var request = GeoCalibrationRequest.SingleAnchor(
                point,
                new GeoSingleAnchorSettings(
                    GeoVector3d.UnitY,
                    GeoVector3d.UnitY,
                    GeoHeading.FromDegrees(0),
                    1,
                    GeoCalibrationOptions.CreateDefault()));
            Assert.That(
                GeoCalibrationEngine.TrySolve(request, GeoCoordinateConverter.CreateDefault(), out _, out _, out var error),
                Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.InvalidHeadingDirection));
            var points = new[]
            {
                point,
                new GeoControlPoint("B", GeoVector3d.UnitY, point.Coordinate)
            };
            request = GeoCalibrationRequest.Upright(
                points,
                new GeoUprightSettings(GeoVector3d.UnitY, GeoScalePolicy.Estimate, GeoCalibrationOptions.CreateDefault()));
            Assert.That(
                GeoCalibrationEngine.TrySolve(request, GeoCoordinateConverter.CreateDefault(), out _, out _, out error),
                Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.DegenerateBaseline));
        }

        [Test]
        public void RangeChecksOffsetAndInverse()
        {
            var options = GeoCalibrationOptions.CreateDefault();
            options.ReferenceOrigin = GeoTestData.Origin;
            options.MaximumDistanceFromOriginMeters = 100;
            var r = GeoCalibrationRequest.SingleAnchor(
                new GeoControlPoint("A", GeoVector3d.Zero, GeoTestData.Origin.ToCoordinate()),
                new GeoSingleAnchorSettings(GeoVector3d.UnitY, GeoVector3d.UnitZ, GeoHeading.FromDegrees(0), 1, options));
            Assert.That(
                GeoCalibrationEngine.TrySolve(r, GeoCoordinateConverter.CreateDefault(), out var solution, out _, out _),
                Is.True);
            Assert.That(solution.Frame.TryLocalToWgs84(new GeoVector3d(101, 0, 0), out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.OutOfConfiguredRange));
        }

        [Test]
        public void SpatialWeightedRandomRotationsRecoverProperTransform()
        {
            var random = new System.Random(1234);
            var identity = GeoTestData.IdentityFrame();
            for (int trial = 0; trial < 24; trial++)
            {
                var rotation = Matrix3d.FromQuaternion(
                    random.NextDouble(),
                    random.NextDouble() - .5,
                    random.NextDouble() - .5,
                    random.NextDouble() - .5);
                double scale = .1 + random.NextDouble() * 3;
                var translation = new GeoVector3d(50, -20, 100);
                var points = new GeoControlPoint[7];
                for (int i = 0; i < points.Length; i++)
                {
                    var q = new GeoVector3d(random.NextDouble() * 200 - 100, random.NextDouble() * 50, random.NextDouble() * 200 - 100);
                    Assert.That(identity.TryLocalToWgs84(q, out var gps, out _), Is.True);
                    points[i] = new GeoControlPoint(i.ToString(), translation + rotation.Apply(q) * scale, gps.ToCoordinate(), 1 + i);
                }

                var options = GeoCalibrationOptions.CreateDefault();
                options.ReferenceOrigin = GeoTestData.Origin;
                var request = GeoCalibrationRequest.Spatial(points, new GeoSpatialSettings(GeoScalePolicy.Estimate, options));
                Assert.That(
                    GeoCalibrationEngine.TrySolve(request, GeoCoordinateConverter.CreateDefault(), out _, out var report, out var error),
                    Is.True,
                    error.ToString());
                Assert.That(report.FitMetrics.Value.SpatialRmsMeters, Is.LessThan(1e-5));
                Assert.That(report.LocalUnitsPerMeter.Value, Is.EqualTo(scale).Within(1e-6));
            }
        }
    }
}
