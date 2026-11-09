using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using SceneGeography.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SceneGeography.Tests
{
    public sealed partial class GeoCalibrationWorkbenchTests
    {
        GameObject host, root;
        GeoReference reference;
        GeoCalibrationProfile profile;
        GeoControlPointMarker marker;
        GeoCalibrationWorkbenchModel model;
        string folder, path;
        GeoEditorLanguageScope languageScope;

        [SetUp]
        public void SetUp()
        {
            languageScope = new GeoEditorLanguageScope(GeoEditorLanguage.SimplifiedChinese);
            string name = "__GeoWorkbenchTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            folder = "Assets/" + name;
            path = folder + "/Calibration.asset";
            host = new GameObject("Workbench reference");
            root = new GameObject("Workbench root");
            reference = host.AddComponent<GeoReference>();
            reference.TrySetRoot(root.transform, out _);
            reference.TrySetLocalFrameId(GeoTestData.FrameId, out _);
            profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            GeoTestData.FillProfile(profile, GeoCalibrationMode.SingleAnchor);
            profile.options.limitCheckMaximum = false;
            AssetDatabase.CreateAsset(profile, path);
            GeoCalibrationWorkbenchData.Bind(reference, profile);
            marker = CreateMarker(profile.points[0]);
            model = new GeoCalibrationWorkbenchModel();
            model.Load(reference, profile);
            model.Selected.Add(marker);
        }

        GeoControlPointMarker CreateMarker(GeoControlPointSettings point)
        {
            var go = new GameObject(point.id);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3((float)point.localPosition.x, (float)point.localPosition.y, (float)point.localPosition.z);
            var value = go.AddComponent<GeoControlPointMarker>();
            value.reference = reference;
            value.pointId = point.id;
            value.coordinate = JsonUtility.FromJson<GeoCoordinateSettings>(JsonUtility.ToJson(point.coordinate));
            value.role = point.role;
            value.weight = point.weight;
            return value;
        }

        [TearDown]
        public void TearDown()
        {
            model?.Dispose();
            if (profile != null) Undo.ClearUndo(profile);
            if (reference != null) Undo.ClearUndo(reference);
            if (root != null) Object.DestroyImmediate(root);
            if (host != null) Object.DestroyImmediate(host);
            if (!string.IsNullOrEmpty(folder)) AssetDatabase.DeleteAsset(folder);
            languageScope?.Dispose();
        }

        [Test]
        public void DirtyStatusUsesCapturedRootLocalPositionNotSceneDirtyFlag()
        {
            Assert.That(GeoCalibrationWorkbenchData.MarkerStatus(marker, profile), Is.EqualTo("已同步"));
            root.transform.SetPositionAndRotation(new Vector3(10, 2, 3), Quaternion.Euler(0, 30, 0));
            root.transform.localScale = Vector3.one * 2;
            Assert.That(GeoCalibrationWorkbenchData.MarkerStatus(marker, profile), Is.EqualTo("已同步"));
            marker.transform.localPosition += Vector3.right;
            Assert.That(GeoCalibrationWorkbenchData.MarkerStatus(marker, profile), Does.StartWith("已修改"));
        }

        [TestCase("coordinate")]
        [TestCase("role")]
        [TestCase("weight")]
        public void EffectivePointEditsAreReported(string field)
        {
            if (field == "coordinate") marker.coordinate.longitudeDegrees += 0.00001;
            if (field == "role") marker.role = GeoControlPointRole.Check;
            if (field == "weight") marker.weight = 2;
            Assert.That(GeoCalibrationWorkbenchData.MarkerStatus(marker, profile), Does.StartWith("已修改"));
        }

        [Test]
        public void RenameIsAnAdditionAndDoesNotDeleteOldPointDuringMerge()
        {
            marker.pointId = "Renamed";
            Assert.That(model.Build(), Is.True, model.Error);
            Assert.That(model.Added, Is.EqualTo(1));
            Assert.That(model.Removed, Is.Empty);
            Assert.That(model.Proposal.points.Select(x => x.id), Does.Contain("Renamed").And.Contain("Control-1"));
        }

        [Test]
        public void MergePreservesUnselectedPointsAllSettingsAndSourceAsset()
        {
            string original = GeoCalibrationWorkbenchData.Json(profile);
            marker.transform.localPosition += Vector3.right;
            Assert.That(model.Build(), Is.True, model.Error);
            Assert.That(model.Updated, Is.EqualTo(1));
            Assert.That(model.Proposal.points.Length, Is.EqualTo(5));
            Assert.That(model.Proposal.mode, Is.EqualTo(profile.mode));
            Assert.That(model.Proposal.boundHeadingDegrees, Is.EqualTo(profile.boundHeadingDegrees));
            Assert.That(model.Proposal.localUnitsPerMeter, Is.EqualTo(profile.localUnitsPerMeter));
            Assert.That(JsonUtility.ToJson(model.Proposal.options), Is.EqualTo(JsonUtility.ToJson(profile.options)));
            Assert.That(GeoCalibrationWorkbenchData.Json(profile), Is.EqualTo(original));
            model.Proposal.points[1].coordinate.latitudeDegrees = 0;
            Assert.That(profile.points[1].coordinate.latitudeDegrees, Is.Not.Zero);
        }

        [Test]
        public void ReplaceRequiresExplicitRemovalConfirmationAndPreservesGuid()
        {
            model.ReplacePoints = true;
            string original = GeoCalibrationWorkbenchData.Json(profile);
            string guid = AssetDatabase.AssetPathToGUID(path);
            Assert.That(model.Save(false, false), Is.False);
            Assert.That(model.Removed.Count, Is.EqualTo(4));
            Assert.That(GeoCalibrationWorkbenchData.Json(profile), Is.EqualTo(original));
            Assert.That(model.Save(true, false), Is.True, model.Error);
            Assert.That(profile.points.Length, Is.EqualTo(1));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(profile.hideFlags, Is.EqualTo(HideFlags.None));
        }

        [Test]
        public void SaveDoesNotApplyAndRebasesMergeAfterDeselectingMarker()
        {
            marker.weight = 2;
            Assert.That(model.Save(false, false), Is.True, model.Error);
            Assert.That(reference.IsInitialized, Is.False);
            Assert.That(profile.points[0].weight, Is.EqualTo(2));
            Assert.That(model.HasDraftEdits, Is.False);
            model.Selected.Clear();
            Assert.That(model.Save(false, false), Is.True, model.Error);
            Assert.That(profile.points[0].weight, Is.EqualTo(2), "取消勾选不能撤回上一次已经保存的数据。");
        }

        [Test]
        public void CandidatePreviewDoesNotWriteAssetAndApplicationStatusSeparatesBothStates()
        {
            string original = GeoCalibrationWorkbenchData.Json(profile);
            marker.weight = 2;
            Assert.That(model.Calculate(), Is.True, model.Error);
            Assert.That(reference.IsInitialized, Is.False);
            Assert.That(model.Apply(), Is.True, model.Error);
            Assert.That(reference.IsInitialized, Is.True);
            Assert.That(GeoCalibrationWorkbenchData.Json(profile), Is.EqualTo(original));
            Assert.That(GeoCalibrationApplicationState.Describe(reference, profile), Does.Contain("不同"));
            Assert.That(model.Save(false, false), Is.True, model.Error);
            Assert.That(GeoCalibrationApplicationState.Describe(reference, profile), Does.Contain("一致"));
        }

        [Test]
        public void SaveAndApplyCandidateUsesSavedConfiguration()
        {
            marker.weight = 2;
            Assert.That(model.Save(false, true), Is.True, model.Error);
            Assert.That(model.Apply(), Is.True, model.Error);
            Assert.That(GeoCalibrationApplicationState.Describe(reference, profile), Does.Contain("一致"));
            profile.boundHeadingDegrees += 1;
            Assert.That(GeoCalibrationApplicationState.Describe(reference, profile), Does.Contain("不同"));
        }

        [TestCase("position")]
        [TestCase("coordinate")]
        [TestCase("weight")]
        [TestCase("selection")]
        [TestCase("mode")]
        [TestCase("externalAsset")]
        [TestCase("activeVersion")]
        public void RelevantInputChangesInvalidateCandidate(string change)
        {
            Assert.That(model.Calculate(), Is.True, model.Error);
            if (change == "position") marker.transform.localPosition += Vector3.right;
            if (change == "coordinate") marker.coordinate.longitudeDegrees += 0.00001;
            if (change == "weight") marker.weight = 2;
            if (change == "selection") model.Selected.Clear();
            if (change == "mode") model.Draft.mode = GeoCalibrationMode.Upright;
            if (change == "externalAsset") profile.boundHeadingDegrees = 33;
            if (change == "activeVersion") reference.Clear();
            Assert.That(model.CandidateIsCurrent(), Is.False);
            Assert.That(model.Candidate, Is.Null);
        }

        [Test]
        public void FailedSolveDoesNotOverwriteAssetOrActiveCalibration()
        {
            Assert.That(model.Calculate(), Is.True, model.Error);
            Assert.That(model.Apply(), Is.True, model.Error);
            long version = reference.CalibrationVersion;
            string original = GeoCalibrationWorkbenchData.Json(profile);
            marker.role = GeoControlPointRole.Check;
            Assert.That(model.Save(true, false), Is.False);
            Assert.That(reference.CalibrationVersion, Is.EqualTo(version));
            Assert.That(GeoCalibrationWorkbenchData.Json(profile), Is.EqualTo(original));
        }

        [Test]
        public void ExternalAssetEditCannotBeSilentlyOverwrittenByOldDraft()
        {
            profile.boundHeadingDegrees = 123;
            Assert.That(model.SourceChanged, Is.True);
            Assert.That(model.Save(true, false), Is.False);
            Assert.That(model.Error, Does.Contain("重新加载"));
            Assert.That(profile.boundHeadingDegrees, Is.EqualTo(123));
        }

        [Test]
        public void DuplicateSceneIdsAreRejectedEvenWhenOnlyOneIsSelected()
        {
            CreateMarker(profile.points[0]);
            Assert.That(model.Build(), Is.False);
            Assert.That(model.Error, Does.Contain("重复"));
            Assert.That(GeoCalibrationWorkbenchData.MarkerStatus(marker, profile), Does.Contain("重复"));
        }

        [Test]
        public void OtherReferencesCannotBeMixedInOneCapture()
        {
            var other = root.AddComponent<GeoReference>();
            marker.reference = other;
            Assert.That(model.Build(), Is.False);
            Assert.That(model.Error, Does.Contain("其他 Reference"));
        }

        [Test]
        public void InactiveMarkersAreListedButPreviewSceneMarkersAreExcluded()
        {
            marker.gameObject.SetActive(false);
            Assert.That(GeoCalibrationWorkbenchData.Markers(), Does.Contain(marker));
            Scene preview = EditorSceneManager.NewPreviewScene();
            var point = new GameObject("Preview marker");
            try
            {
                SceneManager.MoveGameObjectToScene(point, preview);
                var component = point.AddComponent<GeoControlPointMarker>();
                Assert.That(GeoCalibrationWorkbenchData.Markers(), Has.No.Member(component));
            }
            finally { Object.DestroyImmediate(point); EditorSceneManager.ClosePreviewScene(preview); }
        }

        [Test]
        public void FrameIdMismatchAndEmptyIdBlockCapture()
        {
            model.Draft.localFrameId = "wrong-frame";
            Assert.That(model.Build(), Is.False);
            model.Draft.localFrameId = reference.LocalFrameId;
            marker.pointId = "";
            Assert.That(model.Build(), Is.False);
        }

        [Test]
        public void SaveAsDoesNotOverwriteExistingAssetAndBindingIsExplicit()
        {
            string originalGuid = AssetDatabase.AssetPathToGUID(path);
            Assert.That(model.SaveAs(path, true), Is.False);
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(originalGuid));
            Assert.That(model.SaveAs(folder + "/Copy.asset", false), Is.True, model.Error);
            Assert.That(GeoCalibrationWorkbenchData.AssignedProfile(reference), Is.SameAs(profile));
            Assert.That(model.Profile, Is.Not.SameAs(profile));
            Assert.That(model.Profile.mode, Is.EqualTo(profile.mode));
            Assert.That(model.Profile.localUnitsPerMeter, Is.EqualTo(profile.localUnitsPerMeter));
            Assert.That(model.SaveAs(folder + "/Bound.asset", true), Is.True, model.Error);
            Assert.That(GeoCalibrationWorkbenchData.AssignedProfile(reference), Is.SameAs(model.Profile));
        }

        [Test]
        public void ProfileUpdateIsUndoableButDoesNotPretendToRestoreActiveSolution()
        {
            Undo.IncrementCurrentGroup();
            marker.weight = 2;
            Assert.That(model.Save(false, false), Is.True, model.Error);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(profile.points[0].weight, Is.EqualTo(1));
            Assert.That(model.SourceChanged, Is.True);
        }
        [Test]
        public void NoOpCapturePreservesStoredDataAndDoesNotCreateFalsePreviewDifference()
        {
            string json = GeoCalibrationWorkbenchData.Json(profile);
            Assert.That(model.Calculate(), Is.True, model.Error);
            Assert.That(model.Updated, Is.Zero, GeoCalibrationWorkbenchData.Json(profile) + Environment.NewLine + GeoCalibrationWorkbenchData.Json(model.Proposal));
            Assert.That(GeoCalibrationWorkbenchData.Json(model.Proposal), Is.EqualTo(json));
            Assert.That(model.Apply(), Is.True, model.Error);
            Assert.That(GeoCalibrationApplicationState.Describe(reference, profile), Does.Contain("一致"));
        }

        [TestCase(0.001)]
        [TestCase(0.000001)]
        public void SmallModelUnitEditsAreNotDiscarded(double scale)
        {
            profile.localUnitsPerMeter = scale;
            model.Load(reference, profile);
            model.Selected.Add(marker);
            marker.transform.localPosition += Vector3.right * .00005f;
            Assert.That(model.Build(), Is.True, model.Error);
            Assert.That(model.Updated, Is.EqualTo(1));
            Assert.That(model.Proposal.points[0].localPosition.x, Is.Not.EqualTo(profile.points[0].localPosition.x));
        }

        [Test]
        public void DescendantCaptureIsStableAcrossLargeRootMotionAndKeepsTinyCoordinateEdits()
        {
            Assert.That(marker.TryCapture(out var before, out _), Is.True);
            root.transform.SetPositionAndRotation(new Vector3(1000000, 2000000, -1000000), Quaternion.Euler(12, 70, 8));
            root.transform.localScale = Vector3.one * 7;
            Assert.That(marker.TryCapture(out var after, out _), Is.True);
            Assert.That(after.LocalPosition.X, Is.EqualTo(before.LocalPosition.X));
            Assert.That(after.LocalPosition.Y, Is.EqualTo(before.LocalPosition.Y));
            Assert.That(after.LocalPosition.Z, Is.EqualTo(before.LocalPosition.Z));
            Assert.That(GeoCalibrationWorkbenchData.MarkerStatus(marker, profile), Is.EqualTo("已同步"));
            marker.coordinate.heightMeters += 0.0000005;
            Assert.That(model.Build(), Is.True, model.Error);
            Assert.That(model.Updated, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DraftChangesEnableCloseProtectionAndSurviveWindowReload()
        {
            var window = ScriptableObject.CreateInstance<GeoCalibrationWorkbench>();
            try
            {
                window.ShowUtility();
                yield return null;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(GeoCalibrationWorkbench).GetMethod("SetContext", flags).Invoke(window, new object[] { reference, profile, new[] { marker } });
                var modelField = typeof(GeoCalibrationWorkbench).GetField("model", flags);
                var work = (GeoCalibrationWorkbenchModel)modelField.GetValue(window);
                work.Draft.boundHeadingDegrees = 17;
                typeof(GeoCalibrationWorkbench).GetMethod("RefreshStatus", flags).Invoke(window, null);
                Assert.That(window.hasUnsavedChanges, Is.True);
                Assert.That(work.Calculate(), Is.True, work.Error);
                typeof(GeoCalibrationWorkbench).GetMethod("OnDisable", flags).Invoke(window, null);
                typeof(GeoCalibrationWorkbench).GetMethod("OnEnable", flags).Invoke(window, null);
                window.CreateGUI();
                work = (GeoCalibrationWorkbenchModel)modelField.GetValue(window);
                Assert.That(work.Draft.boundHeadingDegrees, Is.EqualTo(17));
                Assert.That(work.HasDraftEdits, Is.True);
                Assert.That(work.Candidate, Is.Null);
                Assert.That(window.hasUnsavedChanges, Is.True);
                Assert.That(profile.boundHeadingDegrees, Is.EqualTo(0));
            }
            finally
            {
                window.DiscardChanges();
                window.Close();
            }
        }

        [UnityTest]
        public IEnumerator DraftSaveClearsCloseProtectionAndFailedSaveKeepsIt()
        {
            var window = ScriptableObject.CreateInstance<GeoCalibrationWorkbench>();
            try
            {
                window.ShowUtility();
                yield return null;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(GeoCalibrationWorkbench).GetMethod("SetContext", flags).Invoke(window, new object[] { reference, profile, new[] { marker } });
                var work = (GeoCalibrationWorkbenchModel)typeof(GeoCalibrationWorkbench).GetField("model", flags).GetValue(window);
                var refresh = typeof(GeoCalibrationWorkbench).GetMethod("RefreshStatus", flags);
                work.Draft.boundHeadingDegrees = 17;
                refresh.Invoke(window, null);
                Assert.That(window.hasUnsavedChanges, Is.True);
                window.SaveChanges();
                Assert.That(window.hasUnsavedChanges, Is.False);
                Assert.That(profile.boundHeadingDegrees, Is.EqualTo(17));
                Assert.That(work.HasDraftEdits, Is.False);

                work.Draft.boundHeadingDegrees = 23;
                profile.boundHeadingDegrees = 31;
                Assert.That(work.Save(false, false), Is.False, "External asset edits must prevent overwriting.");
                refresh.Invoke(window, null);
                Assert.That(window.hasUnsavedChanges, Is.True, "A failed save must leave close protection active.");
                Assert.That(work.Draft.boundHeadingDegrees, Is.EqualTo(23));
                Assert.That(profile.boundHeadingDegrees, Is.EqualTo(31));
                window.DiscardChanges();
                work = (GeoCalibrationWorkbenchModel)typeof(GeoCalibrationWorkbench).GetField("model", flags).GetValue(window);
                Assert.That(window.hasUnsavedChanges, Is.False);
                Assert.That(work.Draft.boundHeadingDegrees, Is.EqualTo(31));
            }
            finally
            {
                window.DiscardChanges();
                window.Close();
            }
        }

        static void SetBindingReference(GeoBindingBehaviour binding, GeoReference owner)
        {
            using (var serialized = new SerializedObject(binding))
            {
                serialized.FindProperty("reference").objectReferenceValue = owner;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void InventoryMergesRelatedComponentsOnOneObjectAndIncludesInactiveBindings()
        {
            SetBindingReference(marker.gameObject.AddComponent<GeoAnchor>(), reference);
            SetBindingReference(marker.gameObject.AddComponent<GeoOrientation>(), reference);
            SetBindingReference(marker.gameObject.AddComponent<GeoPositionReporter>(), reference);
            marker.gameObject.SetActive(false);
            var entries = GeoCalibrationSceneInventory.Capture().Entries.Where(x => x.Object == marker.gameObject).ToArray();
            Assert.That(entries.Length, Is.EqualTo(1));
            Assert.That(entries[0].Reference, Is.SameAs(reference));
            Assert.That(entries[0].Types, Is.EqualTo(GeoWorkbenchComponentType.All));
            Assert.That(entries[0].Components.Length, Is.EqualTo(4));
            Assert.That(entries[0].Marker, Is.SameAs(marker));
        }

        [Test]
        public void InventoryUsesEachComponentsReferenceNotItsTransformParent()
        {
            var other = root.AddComponent<GeoReference>();
            SetBindingReference(marker.gameObject.AddComponent<GeoAnchor>(), other);
            marker.gameObject.AddComponent<GeoOrientation>();
            var entries = GeoCalibrationSceneInventory.Capture().Entries.Where(x => x.Object == marker.gameObject).ToArray();
            Assert.That(entries.Length, Is.EqualTo(3));
            Assert.That(entries.Single(x => x.Reference == reference).Types, Is.EqualTo(GeoWorkbenchComponentType.Marker));
            Assert.That(entries.Single(x => x.Reference == other).Types, Is.EqualTo(GeoWorkbenchComponentType.Anchor));
            Assert.That(entries.Single(x => x.Reference == null).Types, Is.EqualTo(GeoWorkbenchComponentType.Orientation));
        }

        [Test]
        public void InventoryFiltersByTypeUnionPathOrPointIdWithoutMutatingCapture()
        {
            SetBindingReference(marker.gameObject.AddComponent<GeoAnchor>(), reference);
            var entry = GeoCalibrationSceneInventory.Capture().Entries.Single(x => x.Object == marker.gameObject);
            Assert.That(entry.Matches(GeoWorkbenchComponentType.Marker, marker.pointId.ToLowerInvariant()), Is.True);
            Assert.That(entry.Matches(GeoWorkbenchComponentType.Anchor, root.name), Is.True);
            Assert.That(entry.Matches(GeoWorkbenchComponentType.Anchor | GeoWorkbenchComponentType.Orientation, ""), Is.True);
            Assert.That(entry.Matches(GeoWorkbenchComponentType.Orientation, ""), Is.False);
            Assert.That(entry.Matches(GeoWorkbenchComponentType.None, ""), Is.False);
            Assert.That(entry.Matches(GeoWorkbenchComponentType.All, "missing-object"), Is.False);
            Assert.That(model.Selected, Is.EqualTo(new[] { marker }));
        }

        [Test]
        public void InventoryExcludesPreviewBindingsAndTracksReferenceChanges()
        {
            var anchor = marker.gameObject.AddComponent<GeoAnchor>();
            string before = GeoCalibrationSceneInventory.Capture().Signature;
            SetBindingReference(anchor, reference);
            Assert.That(GeoCalibrationSceneInventory.Capture().Signature, Is.Not.EqualTo(before));
            Scene preview = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Preview bindings");
            try
            {
                SceneManager.MoveGameObjectToScene(go, preview);
                go.AddComponent<GeoAnchor>(); go.AddComponent<GeoOrientation>(); go.AddComponent<GeoPositionReporter>();
                Assert.That(GeoCalibrationSceneInventory.Capture().Entries.Any(x => x.Object == go), Is.False);
            }
            finally { Object.DestroyImmediate(go); EditorSceneManager.ClosePreviewScene(preview); }
        }

        static void Submit(Button button)
        {
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        [UnityTest]
        public IEnumerator FilteringAndBrowsingBindingsPreserveCaptureAndRefreshPreservesDraft()
        {
            var go = new GameObject("Workbench anchor preview"); go.transform.SetParent(root.transform, false);
            SetBindingReference(go.AddComponent<GeoAnchor>(), reference);
            var orphan = CreateMarker(profile.points[1]); orphan.reference = null;
            var window = ScriptableObject.CreateInstance<GeoCalibrationWorkbench>();
            try
            {
                window.ShowUtility(); yield return null;
                typeof(GeoCalibrationWorkbench).GetMethod("SetContext", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { reference, profile, new[] { marker } });
                yield return null;
                var ui = window.rootVisualElement;
                var work = (GeoCalibrationWorkbenchModel)typeof(GeoCalibrationWorkbench).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                var tree = ui.Q<ScrollView>("scene-tree");
                Assert.That(tree.Q("scene-group-" + reference.gameObject.scene.handle).Q("reference-group-" + reference.GetInstanceID()), Is.Not.Null);
                Assert.That(tree.Q("unbound-group-" + orphan.gameObject.scene.handle).Q<Toggle>("capture-" + orphan.GetInstanceID()).enabledSelf, Is.False);
                Assert.That(work.Calculate(), Is.True, work.Error);
                foreach (var type in new[] { "Marker", "Orientation", "Reporter" }) ui.Q<ToolbarToggle>("filter-" + type).value = false;
                yield return null;
                Assert.That(tree.Q<Toggle>("capture-" + marker.GetInstanceID()), Is.Null);
                Assert.That(work.Selected, Is.EqualTo(new[] { marker }), "隐藏的勾选不可被筛选清空。");
                Assert.That(work.CandidateIsCurrent(), Is.True, "纯显示筛选不能使候选失效。");
                Assert.That(ui.Q<Label>("status-line").tooltip, Does.Contain("隐藏 1"));
                Submit(tree.Query<Button>().ToList().Single(x => x.text == go.name));
                yield return null;
                Assert.That(ui.Q("scene-object-editor").Query<Foldout>().ToList().Any(x => x.text == "GeoAnchor"), Is.True);
                Assert.That(work.Selected, Is.EqualTo(new[] { marker }));
                Assert.That(work.CandidateIsCurrent(), Is.True, "浏览 Anchor 不能改变采集内容。");
                work.Draft.boundHeadingDegrees = 17;
                Submit(ui.Q<Toolbar>("main-toolbar").Query<Button>().ToList().Single(x => x.text == "刷新场景"));
                yield return null;
                Assert.That(work.Draft.boundHeadingDegrees, Is.EqualTo(17));
                Assert.That(ui.Q<ToolbarToggle>("filter-Marker").value, Is.False);
                Assert.That(work.Selected, Is.EqualTo(new[] { marker }));
                Assert.That(profile.boundHeadingDegrees, Is.Zero);
                Submit(ui.Q<Toolbar>("main-toolbar").Query<Button>().ToList().Single(x => x.text == "读取当前场景"));
                yield return null;
                Assert.That(work.Draft.boundHeadingDegrees, Is.EqualTo(17));
                Assert.That(ui.Q<ToolbarToggle>("filter-Marker").value, Is.True);
                Assert.That(work.Selected, Is.EqualTo(new[] { marker }));
            }
            finally { window.DiscardChanges(); window.Close(); }
        }

        [UnityTest]
        public IEnumerator VisibleMarkerSelectionDoesNotClearHiddenControlPoints()
        {
            var other = CreateMarker(profile.points[1]);
            var window = ScriptableObject.CreateInstance<GeoCalibrationWorkbench>();
            try
            {
                window.ShowUtility(); yield return null;
                typeof(GeoCalibrationWorkbench).GetMethod("SetContext", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { reference, profile, new[] { marker } });
                yield return null;
                var ui = window.rootVisualElement;
                var work = (GeoCalibrationWorkbenchModel)typeof(GeoCalibrationWorkbench).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                ui.Q<ToolbarSearchField>("scene-search").value = other.pointId;
                yield return null;
                Submit(ui.Q("reference-group-" + reference.GetInstanceID()).Query<Button>().ToList().Single(x => x.text == "勾选可见 Marker"));
                yield return null;
                Assert.That(work.Selected, Is.EquivalentTo(new[] { marker, other }));
                Submit(ui.Q("reference-group-" + reference.GetInstanceID()).Query<Button>().ToList().Single(x => x.text == "取消可见 Marker"));
                yield return null;
                Assert.That(work.Selected, Is.EqualTo(new[] { marker }));
                Assert.That(work.Removed, Is.Empty);
                Assert.That(work.Proposal.points.Length, Is.EqualTo(profile.points.Length));
            }
            finally { window.DiscardChanges(); window.Close(); }
        }

        [UnityTest]
        public IEnumerator RebuildingUiDoesNotInvalidateCandidateThroughInitialBindingNotifications()
        {
            var window = ScriptableObject.CreateInstance<GeoCalibrationWorkbench>();
            try
            {
                window.ShowUtility(); yield return null;
                typeof(GeoCalibrationWorkbench).GetMethod("SetContext", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { reference, profile, new[] { marker } });
                var work = (GeoCalibrationWorkbenchModel)typeof(GeoCalibrationWorkbench).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                Assert.That(work.Calculate(), Is.True, work.Error);
                yield return null; yield return null;
                Assert.That(work.CandidateIsCurrent(), Is.True, "初次绑定属性字段不能作废未改变的候选。");
                Submit(window.rootVisualElement.Q<Toolbar>("main-toolbar").Query<Button>().ToList().Single(x => x.text == "读取当前场景"));
                yield return null; yield return null;
                Assert.That(work.CandidateIsCurrent(), Is.True, "读取/重建 UI 不改变输入时应保留候选。");
            }
            finally { window.DiscardChanges(); window.Close(); }
        }

        [UnityTest]
        public IEnumerator BindingDetailsRespectFieldRulesAndReporterExplicitTargetState()
        {
            var go = new GameObject("Workbench binding rules"); go.transform.SetParent(root.transform, false);
            var orientation = go.AddComponent<GeoOrientation>(); SetBindingReference(orientation, reference);
            var reporter = go.AddComponent<GeoPositionReporter>(); SetBindingReference(reporter, reference);
            var facingReporter = go.AddComponent<GeoOrientationReporter>();
            SetBindingReference(facingReporter, reference);
            foreach (var value in new GeoBindingBehaviour[] { reporter, facingReporter })
            using (var serialized = new SerializedObject(value))
            {
                serialized.FindProperty("hasExplicitTarget").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var window = ScriptableObject.CreateInstance<GeoCalibrationWorkbench>();
            try
            {
                window.ShowUtility(); yield return null;
                typeof(GeoCalibrationWorkbench).GetMethod("SetContext", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { reference, profile, new[] { marker } });
                yield return null;
                var ui = window.rootVisualElement;
                Submit(ui.Q("reference-group-" + reference.GetInstanceID()).Query<Button>().ToList().Single(x => x.text == go.name));
                yield return null; yield return null;
                Assert.That(ui.Q<PropertyField>("component-" + orientation.GetInstanceID() + "-targetCoordinate").enabledSelf, Is.False);
                Assert.That(ui.Q<PropertyField>("component-" + orientation.GetInstanceID() + "-headingDegrees").enabledSelf, Is.True);
                foreach (var value in new GeoBindingBehaviour[] { reporter, facingReporter })
                {
                    using (var serialized = new SerializedObject(value))
                        Assert.That(serialized.FindProperty("hasExplicitTarget").boolValue, Is.True, "仅浏览不能清除丢失显式目标状态。");
                    var target = ui.Q<PropertyField>("component-" + value.GetInstanceID() + "-target").Q<ObjectField>();
                    target.value = root.transform;
                    yield return null; yield return null;
                    Assert.That(GeoInspectorGUI.ReporterTarget(value), Is.SameAs(root.transform));
                    target.value = null;
                    yield return null; yield return null;
                    using (var serialized = new SerializedObject(value)) Assert.That(serialized.FindProperty("hasExplicitTarget").boolValue, Is.False);
                    target.value = host.transform;
                    yield return null; yield return null;
                    using (var serialized = new SerializedObject(value)) Assert.That(serialized.FindProperty("hasExplicitTarget").boolValue, Is.True);
                }
                Assert.That(model.Selected, Is.EqualTo(new[] { marker }));
            }
            finally { window.DiscardChanges(); window.Close(); Undo.ClearUndo(reporter); Undo.ClearUndo(facingReporter); }
        }

        [UnityTest]
        public IEnumerator WorkbenchDisplaysLinkedAssetAndComputesThroughItsUiButton()
        {
            var window = ScriptableObject.CreateInstance<GeoCalibrationWorkbench>();
            try
            {
                window.ShowUtility();
                yield return null;
                typeof(GeoCalibrationWorkbench).GetMethod("SetContext", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { reference, profile, new[] { marker } });
                yield return null;
                var rootElement = window.rootVisualElement;
                Assert.That((int)(model.Draft.hideFlags & HideFlags.NotEditable), Is.Zero);
                Assert.That(rootElement.Query<InspectorElement>().ToList(), Is.Empty, "工作台只显示工作草稿，不再重复嵌入资产 Inspector。");
                Assert.That(rootElement.Query<ObjectField>().ToList().Count(x => x.value == profile), Is.GreaterThanOrEqualTo(2));
                Assert.That(rootElement.Query<Label>().ToList().Any(x => x.text == path), Is.True);
                var toolbar = rootElement.Q<Toolbar>("main-toolbar");
                Assert.That(toolbar, Is.Not.Null);
                Assert.That(toolbar.Query<ObjectField>().ToList(), Is.Empty, "顶部只放操作，不再放 Reference 选择框。");
                Assert.That(toolbar.Query<Button>().ToList().Select(x => x.text), Does.Contain("读取当前场景").And.Contain("刷新场景").And.Contain("保存并应用"));
                Assert.That(rootElement.Query<Button>().ToList().Any(x => x.text == "查看／定位配置资产"), Is.False);
                Assert.That(rootElement.Q("scene-browser"), Is.Not.Null);
                Assert.That(rootElement.Q("work-details"), Is.Not.Null);
                var button = toolbar.Query<Button>().ToList().Single(x => x.text == "计算候选");
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = button;
                    button.SendEvent(submit);
                }
                yield return null;
                var work = (GeoCalibrationWorkbenchModel)typeof(GeoCalibrationWorkbench)
                    .GetField("model", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                Assert.That(work.Candidate, Is.Not.Null, work.Error);
                Assert.That(reference.IsInitialized, Is.False);
                Assert.That(rootElement.Query<Button>().ToList().Single(x => x.text == "应用候选（仅预览）").enabledSelf, Is.True);
                var heading = rootElement.Q<PropertyField>("draft-boundHeadingDegrees").Q<DoubleField>();
                Assert.That(heading, Is.Not.Null);
                Assert.That(heading.enabledInHierarchy, Is.True);
                heading.value = 12;
                yield return null;
                yield return null;
                Assert.That(work.Draft.boundHeadingDegrees, Is.EqualTo(12));
                Assert.That(profile.boundHeadingDegrees, Is.Zero);
                Assert.That(work.Candidate, Is.Null, "修改草稿必须作废候选，不能写入资产。");
                rootElement.Query<Foldout>().ToList().First(x => x.text.StartsWith("验收与求解选项")).value = true;
                yield return null;
                var saveButton = rootElement.Query<Button>().ToList().Single(x => x.text == "保存并应用");
                Assert.That(saveButton.worldBound.yMax, Is.LessThanOrEqualTo(rootElement.worldBound.yMax));
                Assert.That(saveButton.layout.height, Is.GreaterThan(20));
                Assert.That(saveButton.worldBound.yMax, Is.LessThanOrEqualTo(rootElement.Q("main-split").worldBound.yMin));
                Assert.That(rootElement.Q("status-line").worldBound.yMin, Is.GreaterThanOrEqualTo(rootElement.Q("main-split").worldBound.yMax - 1));
                window.position = new Rect(60, 60, 920, 580);
                yield return null;
                yield return null;
                Assert.That(toolbar.Query<Button>().ToList().All(x => x.worldBound.xMax <= rootElement.worldBound.xMax + 1), Is.True);
                Assert.That(rootElement.Q("status-line").worldBound.yMax, Is.LessThanOrEqualTo(rootElement.worldBound.yMax + 1));
            }
            finally { window.DiscardChanges(); window.Close(); }
        }
    }
}
