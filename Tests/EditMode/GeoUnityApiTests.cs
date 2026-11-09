using System;
using NUnit.Framework;
using UnityEngine;

namespace SceneGeography.Tests
{
    public sealed class GeoUnityApiTests
    {
        GameObject host, rootObject;
        GeoReference reference;
        GeoCalibrationProfile profile;
        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Reference test");
            rootObject = new GameObject("Root test");
            reference = host.AddComponent<GeoReference>();
            profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            GeoTestData.FillProfile(profile, GeoCalibrationMode.Upright);
            Assert.That(reference.TrySetRoot(rootObject.transform, out _), Is.True);
            Assert.That(reference.TrySetLocalFrameId(GeoTestData.FrameId, out _), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(profile);
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

        [Test]
        public void UninitializedDoesNotReturnUsableZero()
        {
            Assert.That(reference.TryGeoToWorld(GeoTestData.Origin.ToCoordinate(), out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.NotInitialized));
        }

        [Test]
        public void RootRotationScaleAndWorldRoundTrip()
        {
            rootObject.transform.SetPositionAndRotation(new Vector3(100, 25, -40), Quaternion.Euler(8, 40, -12));
            rootObject.transform.localScale = Vector3.one * 2;
            Initialize();
            var input = GeoTestData.GeoAt(new GeoVector3d(20, 10, 30));
            Assert.That(reference.TryGeoToWorld(input, out var world, out var error), Is.True, error.ToString());
            var local = GeoTestData.ModelPosition(new GeoVector3d(20, 10, 30), GeoCalibrationMode.Upright);
            Assert.That(Vector3.Distance(world, rootObject.transform.TransformPoint(local)), Is.LessThan(.03));
            Assert.That(reference.TryWorldToWgs84(world, out var canonical, out error), Is.True);
            Assert.That(reference.TryWgs84ToWorld(canonical, out var restored, out error), Is.True);
            Assert.That(Vector3.Distance(restored, world), Is.LessThan(.001));
            Assert.That(reference.TryMetersToWorldUnits(3, out var units, out error), Is.True);
            Assert.That(units, Is.EqualTo(7.5).Within(.001));
        }

        [Test]
        public void InvalidRootFreezesWorldButRetainsLocalMath()
        {
            Initialize();
            rootObject.transform.localScale = new Vector3(1, 2, 1);
            Assert.That(reference.TryGeoToWorld(GeoTestData.Origin.ToCoordinate(), out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.InvalidRootTransform));
            Assert.That(reference.TryGeoToLocal(GeoTestData.Origin.ToCoordinate(), out _, out _), Is.True);
            rootObject.transform.localScale = Vector3.one;
            Assert.That(reference.TryCaptureSnapshot(out _, out _), Is.True);
        }

        [Test]
        public void DestroyedRootIsNotImplicitWorldMode()
        {
            Initialize();
            UnityEngine.Object.DestroyImmediate(rootObject);
            rootObject = null;
            Assert.That(reference.TryGeoToWorld(GeoTestData.Origin.ToCoordinate(), out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.RootDestroyed));
        }

        [Test]
        public void NullRootIsExplicitWorldMode()
        {
            Assert.That(reference.TrySetRoot(null, out _), Is.True);
            Initialize();
            Assert.That(reference.TryGeoToWorld(GeoTestData.Origin.ToCoordinate(), out var world, out _), Is.True);
            var expected = GeoTestData.ModelPosition(GeoVector3d.Zero, GeoCalibrationMode.Upright);
            Assert.That(Vector3.Distance(world, expected), Is.LessThan(.03));
        }

        [Test]
        public void ParentReflectionOrShearIsRejected()
        {
            Initialize();
            rootObject.transform.localScale = new Vector3(-1, 1, 1);
            Assert.That(reference.TryCaptureSnapshot(out _, out _), Is.False);
            rootObject.transform.localScale = Vector3.one;
            var parent = new GameObject("Sheared parent");
            try
            {
                rootObject.transform.SetParent(parent.transform, false);
                parent.transform.localScale = new Vector3(1, 2, 1);
                rootObject.transform.localRotation = Quaternion.Euler(0, 0, 30);
                Assert.That(reference.TryCaptureSnapshot(out _, out var error), Is.False);
                Assert.That(error.Code, Is.EqualTo(GeoErrorCode.InvalidRootTransform));
            }
            finally
            {
                rootObject.transform.SetParent(null, true);
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void FailedReinitializationRetainsVersionFrameAndReport()
        {
            Initialize();
            long version = reference.CalibrationVersion;
            var report = reference.CurrentReport;
            reference.TryGeoToWorld(GeoTestData.Origin.ToCoordinate(), out var before, out _);
            profile.points[1].coordinate = profile.points[0].coordinate;
            profile.points[2].coordinate = profile.points[0].coordinate;
            Assert.That(reference.TryInitialize(Request(), out var failed, out _), Is.False);
            Assert.That(failed.Success, Is.False);
            Assert.That(reference.CalibrationVersion, Is.EqualTo(version));
            Assert.That(reference.CurrentReport, Is.SameAs(report));
            reference.TryGeoToWorld(GeoTestData.Origin.ToCoordinate(), out var after, out _);
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        public void CandidatePreviewDoesNotCommitAndOldCandidateExpires()
        {
            var request = Request();
            Assert.That(reference.TryPrepareCalibration(request, out var a, out _, out _), Is.True);
            Assert.That(reference.TryPrepareCalibration(request, out var b, out _, out _), Is.True);
            Assert.That(reference.IsInitialized, Is.False);
            Assert.That(reference.TryApplyCalibration(a, out _), Is.True);
            Assert.That(reference.TryApplyCalibration(b, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.StaleCalibrationCandidate));
            Assert.That(reference.TrySetRoot(null, out error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.RootChangeRequiresClear));
        }

        [Test]
        public void CandidateExpiresWhenUninitializedConfigurationChanges()
        {
            reference.TryPrepareCalibration(Request(), out var candidate, out _, out _);
            reference.TrySetRoot(null, out _);
            Assert.That(reference.TryApplyCalibration(candidate, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.StaleCalibrationCandidate));
        }

        [Test]
        public void ProfileEditsAndCalculationDoNotReplaceMappingUntilApply()
        {
            Initialize();
            var coordinate = GeoTestData.Origin.ToCoordinate();
            Assert.That(reference.TryCaptureSnapshot(out var original, out _), Is.True);
            Assert.That(original.TryGeoToWorld(coordinate, out var before, out _), Is.True);
            long version = reference.CalibrationVersion;
            foreach (var point in profile.points)
            {
                point.localPosition.x += 8;
            }

            Assert.That(reference.TryGeoToWorld(coordinate, out var unchanged, out _), Is.True);
            Assert.That(unchanged, Is.EqualTo(before));
            Assert.That(reference.TryPrepareCalibration(Request(), out var candidate, out _, out _), Is.True);
            Assert.That(reference.CalibrationVersion, Is.EqualTo(version));
            Assert.That(reference.TryGeoToWorld(coordinate, out unchanged, out _), Is.True);
            Assert.That(unchanged, Is.EqualTo(before));
            Assert.That(reference.TryApplyCalibration(candidate, out _), Is.True);
            Assert.That(reference.TryGeoToWorld(coordinate, out var after, out _), Is.True);
            Assert.That(after.x - before.x, Is.EqualTo(8).Within(.001));
            Assert.That(reference.CalibrationVersion, Is.GreaterThan(version));
            Assert.That(original.TryGeoToWorld(coordinate, out var historical, out _), Is.True);
            Assert.That(historical, Is.EqualTo(before));
        }

        [Test]
        public void SnapshotIsReusedAndHistoricalAfterRootMoves()
        {
            Initialize();
            reference.TryCaptureSnapshot(out var old, out _);
            reference.TryCaptureSnapshot(out var same, out _);
            Assert.That(same, Is.SameAs(old));
            var input = GeoTestData.Origin.ToCoordinate();
            old.TryGeoToWorld(input, out var before, out _);
            rootObject.transform.position += Vector3.right * 10;
            reference.TryCaptureSnapshot(out var current, out _);
            old.TryGeoToWorld(input, out var historical, out _);
            current.TryGeoToWorld(input, out var after, out _);
            Assert.That(historical, Is.EqualTo(before));
            Assert.That(after.x - before.x, Is.EqualTo(10).Within(.001));
            Assert.That(current.RootPoseVersion, Is.GreaterThan(old.RootPoseVersion));
        }

        [Test]
        public void HeadingPitchRollSignsAndDirectionReadback()
        {
            Initialize();
            reference.TryGetWorldAxes(out var axes, out _);
            var request = GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(90));
            Assert.That(reference.TryGetWorldRotation(Vector3.zero, request, out var q, out _), Is.True);
            Assert.That(Vector3.Dot(q * Vector3.forward, axes.East), Is.GreaterThan(.9999));
            reference.TryWorldDirectionToHeading(q * Vector3.forward, out var heading, out _);
            Assert.That(heading.Degrees, Is.EqualTo(90).Within(.001));
            request = GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(0), new GeoRotationOffset(0, 30, 0));
            reference.TryGetWorldRotation(Vector3.zero, request, out q, out _);
            Assert.That(Vector3.Dot(q * Vector3.forward, axes.Up), Is.EqualTo(.5).Within(.001));
            request = GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(0), new GeoRotationOffset(0, 0, 30));
            reference.TryGetWorldRotation(Vector3.zero, request, out q, out _);
            Assert.That(Vector3.Dot(q * Vector3.up, axes.East), Is.EqualTo(.5).Within(.001));
            Assert.That(reference.TryGetWorldRotation(Vector3.zero, default, out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.InvalidOrientationRequest));
        }

        [Test]
        public void DegenerateLookDoesNotProduceRotation()
        {
            Initialize();
            reference.TryGetWorldAxes(out var axes, out _);
            Assert.That(
                reference.TryGetWorldRotation(
                    Vector3.zero,
                    GeoOrientationRequest.ForWorldTarget(axes.Up * 10, GeoLookMode.Full3D),
                    out _,
                    out var error),
                Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.DegenerateLookDirection));
            Assert.That(reference.TryWorldDirectionToHeading(axes.Up, out _, out _), Is.False);
        }

        [Test]
        public void PositionIsAppliedBeforeDependentLookAndReporter()
        {
            var first = new GameObject("first");
            var second = new GameObject("second");
            try
            {
                var a = first.AddComponent<GeoAnchor>();
                a.Configure(reference, new GeoPlacement(GeoTestData.GeoAt(new GeoVector3d(-10, 0, 0))));
                var b = second.AddComponent<GeoAnchor>();
                b.Configure(reference, new GeoPlacement(GeoTestData.GeoAt(new GeoVector3d(10, 0, 0))));
                var orientation = first.AddComponent<GeoOrientation>();
                orientation.Configure(
                    reference,
                    GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(0)),
                    Quaternion.identity);
                orientation.SetTransformTarget(second.transform, GeoLookMode.Horizontal);
                var reporter = first.AddComponent<GeoPositionReporter>();
                reporter.Configure(reference, null, GeoCoordinateReference.Wgs84Ellipsoidal);
                Initialize();
                Assert.That(a.HasAppliedPosition, Is.True);
                Assert.That(b.HasAppliedPosition, Is.True);
                Assert.That(reporter.HasValue, Is.True);
                Assert.That(
                    Vector3.Dot(first.transform.forward, (second.transform.position - first.transform.position).normalized),
                    Is.GreaterThan(.9999));
                var before = first.transform.position;
                rootObject.transform.localScale = new Vector3(1, 2, 1);
                reference.SynchronizeBindings();
                Assert.That(first.transform.position, Is.EqualTo(before));
                Assert.That(a.LastError.Code, Is.EqualTo(GeoErrorCode.InvalidRootTransform));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void BindingsCannotWriteRootAndDefaultCoordinatesFail()
        {
            Initialize();
            var anchor = rootObject.AddComponent<GeoAnchor>();
            anchor.Configure(reference, new GeoPlacement(GeoTestData.Origin.ToCoordinate()));
            Assert.That(anchor.TryApply(out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.CyclicTransformBinding));
            Assert.Throws<ArgumentException>(() => anchor.SetPlacement(default));
        }

        [Test]
        public void ReentrantInitializationIsRejected()
        {
            var request = Request();
            bool rejected = false;
            reference.Changed += change =>
            {
                rejected = !reference.TryInitialize(request, out _, out var e) && e.Code == GeoErrorCode.ReentrantMutation;
            };
            Initialize();
            Assert.That(rejected, Is.True);
        }

        [Test]
        public void ClearPreservesObjectsButInvalidatesConversion()
        {
            Initialize();
            reference.Clear();
            Assert.That(reference.IsInitialized, Is.False);
            Assert.That(reference.CurrentReport, Is.Null);
            Assert.That(reference.TryCaptureSnapshot(out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(GeoErrorCode.NotInitialized));
        }

        [Test]
        public void SnapshotSuccessfulBatchHasNoPerPointAllocation()
        {
            Initialize();
            reference.TryCaptureSnapshot(out var snapshot, out _);
            var placement = new GeoPlacement(GeoTestData.Origin.ToCoordinate());
            for (int i = 0; i < 20; i++)
            {
                snapshot.TryGetWorldPosition(placement, out _, out _);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            bool success = true;
            for (int i = 0; i < 1000; i++)
            {
                success &= snapshot.TryGetWorldPosition(placement, out _, out _);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(success, Is.True);
            Assert.That(allocated, Is.EqualTo(0));
        }
    }
}
