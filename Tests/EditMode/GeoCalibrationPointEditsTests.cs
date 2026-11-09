using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SceneGeography.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SceneGeography.Tests
{
    public sealed partial class GeoCalibrationWorkbenchTests
    {
        [Test]
        public void ExplicitReplacementFixesSingleAnchorRenameWithoutDeletingObjects()
        {
            string oldId = marker.pointId;
            string original = GeoCalibrationWorkbenchData.Json(profile);
            string guid = AssetDatabase.AssetPathToGUID(path);
            marker.pointId = "Renamed";
            Assert.That(model.Calculate(), Is.False, "A plain rename still merges as a new point.");
            Assert.That(model.StageReplacement(oldId, marker), Is.True, model.Error);
            Assert.That(model.HasDraftEdits, Is.True);
            Assert.That(model.Calculate(), Is.True, model.Error);
            Assert.That(model.Proposal.points.Count(x => x.role == GeoControlPointRole.Fit), Is.EqualTo(1));
            Assert.That(model.Proposal.points.Select(x => x.id), Does.Contain("Renamed").And.Not.Contain(oldId));
            Assert.That(model.Proposal.points.Length, Is.EqualTo(profile.points.Length));
            Assert.That(model.Added, Is.Zero);
            Assert.That(model.Removed, Is.EqualTo(new[] { oldId }));
            Assert.That(model.PointChangeDetails(), Is.EqualTo("替换：" + oldId + " → Renamed"));
            Assert.That(GeoCalibrationWorkbenchData.Json(profile), Is.EqualTo(original));
            Assert.That(model.Save(false, false), Is.False, "Replacement must require confirmation.");
            Assert.That(model.SaveAndApply(true, false), Is.True, model.Error);
            Assert.That(model.HasDraftEdits, Is.False);
            Assert.That(model.PointEdits.HasChanges, Is.False);
            Assert.That(reference.IsInitialized, Is.True);
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(marker != null && marker.gameObject != null, Is.True);
            Assert.That(profile.points.Single(x => x.role == GeoControlPointRole.Fit).id, Is.EqualTo("Renamed"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void UndoReplacementRestoresPreviousSelectionAndOriginalPoint(bool selected)
        {
            string id = marker.pointId;
            marker.pointId = "Renamed";
            if (!selected) model.Selected.Clear();
            Assert.That(model.StageReplacement(id, marker), Is.True, model.Error);
            Assert.That(model.Selected, Does.Contain(marker));
            model.UndoReplacement(id);
            Assert.That(model.Selected.Contains(marker), Is.EqualTo(selected));
            Assert.That(model.HasDraftEdits, Is.False);
            Assert.That(model.Build(), Is.True, model.Error);
            Assert.That(model.Proposal.points.Select(x => x.id), Does.Contain(id));
        }

        [Test]
        public void IndividualRemovalPreservesOtherPointsAndDoesNotApply()
        {
            string id = profile.points[1].id;
            Assert.That(model.Calculate(), Is.True);
            Assert.That(model.StageRemoval(id), Is.True);
            Assert.That(model.Candidate, Is.Null);
            Assert.That(model.Calculate(), Is.True, model.Error);
            Assert.That(profile.points.Length, Is.EqualTo(5));
            Assert.That(model.Save(false, false), Is.False);
            Assert.That(model.HasDraftEdits, Is.True);
            model.UndoRemoval(id);
            Assert.That(model.HasDraftEdits, Is.False);
            Assert.That(model.Build(), Is.True);
            Assert.That(model.Proposal.points.Length, Is.EqualTo(5));
            Assert.That(model.StageRemoval(id), Is.True);
            Assert.That(model.Save(true, false), Is.True, model.Error);
            Assert.That(profile.points.Length, Is.EqualTo(4));
            Assert.That(profile.points.Select(x => x.id), Does.Not.Contain(id));
            Assert.That(reference.IsInitialized, Is.False);
            Assert.That(model.HasDraftEdits, Is.False);
        }

        [Test]
        public void RemovingOnlyFitCannotSaveOrDestroyThePreviousCalibration()
        {
            Assert.That(model.Calculate(), Is.True);
            Assert.That(model.Apply(), Is.True);
            long version = reference.CalibrationVersion;
            string original = GeoCalibrationWorkbenchData.Json(profile);
            Assert.That(model.StageRemoval(marker.pointId), Is.True);
            Assert.That(model.Save(true, false), Is.False);
            Assert.That(model.HasDraftEdits, Is.True);
            Assert.That(GeoCalibrationWorkbenchData.Json(profile), Is.EqualTo(original));
            Assert.That(reference.CalibrationVersion, Is.EqualTo(version));
        }

        [TestCase("renamed")]
        [TestCase("destroyed")]
        [TestCase("rebound")]
        [TestCase("old-id-returned")]
        public void ReplacementRevalidatesSceneIdentityAndKeepsDraftOnFailure(string change)
        {
            string id = marker.pointId;
            string original = GeoCalibrationWorkbenchData.Json(profile);
            marker.pointId = "Renamed";
            Assert.That(model.StageReplacement(id, marker), Is.True, model.Error);
            Assert.That(model.Calculate(), Is.True, model.Error);
            if (change == "renamed") marker.pointId = "RenamedAgain";
            else if (change == "destroyed") Object.DestroyImmediate(marker);
            else if (change == "rebound") marker.reference = root.AddComponent<GeoReference>();
            else CreateMarker(profile.points[0]);
            Assert.That(model.CandidateIsCurrent(), Is.False);
            Assert.That(model.Save(true, false), Is.False);
            Assert.That(model.HasDraftEdits, Is.True);
            Assert.That(GeoCalibrationWorkbenchData.Json(profile), Is.EqualTo(original));
            model.UndoReplacement(id);
            Assert.That(model.PointEdits.HasChanges, Is.False);
        }

        [Test]
        public void ConflictingReplacementAndRemovalRequestsAreRejectedWithoutPartialChanges()
        {
            string id = marker.pointId;
            Assert.That(model.StageReplacement(id, marker), Is.False, "An existing matching scene point is not an unmatched source.");
            marker.pointId = "Renamed";
            Assert.That(model.StageRemoval(id), Is.True);
            Assert.That(model.StageReplacement(id, marker), Is.False);
            model.UndoRemoval(id);
            Assert.That(model.StageReplacement(id, marker), Is.True);
            Assert.That(model.StageReplacement(profile.points[1].id, marker), Is.False);
            Assert.That(model.StageRemoval(id), Is.False);
            Assert.That(model.PointEdits.replacements.Count, Is.EqualTo(1));
            Assert.That(model.PointEdits.removals, Is.Empty);
            Assert.That(model.StageRemoval("not-a-point"), Is.False);
        }

        [Test]
        public void PendingPointEditsDoNotOverrideExternalAssetChanges()
        {
            marker.pointId = "Renamed";
            Assert.That(model.StageReplacement(profile.points[0].id, marker), Is.True);
            profile.boundHeadingDegrees = 42;
            Assert.That(model.Save(true, false), Is.False);
            Assert.That(profile.boundHeadingDegrees, Is.EqualTo(42));
            Assert.That(profile.points[0].id, Is.EqualTo("Control-1"));
            Assert.That(model.HasDraftEdits, Is.True);
        }

        static readonly BindingFlags PointEditFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        GeoCalibrationWorkbench OpenPointEditWindow()
        {
            var window = ScriptableObject.CreateInstance<GeoCalibrationWorkbench>();
            window.ShowUtility();
            typeof(GeoCalibrationWorkbench).GetMethod("SetContext", PointEditFlags)
                .Invoke(window, new object[] { reference, profile, new[] { marker } });
            return window;
        }

        static GeoCalibrationWorkbenchModel PointEditModel(GeoCalibrationWorkbench window) =>
            (GeoCalibrationWorkbenchModel)typeof(GeoCalibrationWorkbench).GetField("model", PointEditFlags).GetValue(window);

        static void InvokePointEditWindow(GeoCalibrationWorkbench window, string method) =>
            typeof(GeoCalibrationWorkbench).GetMethod(method, PointEditFlags).Invoke(window, null);

        [UnityTest]
        public IEnumerator ProfileOnlyRowsOfferDraftRemovalAndUndoWithoutSceneActions()
        {
            var window = OpenPointEditWindow();
            try
            {
                yield return null;
                string id = profile.points[1].id;
                string key = reference.GetInstanceID() + "-" + id;
                var ui = window.rootVisualElement;
                var row = ui.Q("profile-point-" + key);
                Assert.That(row, Is.Not.Null);
                Assert.That(row.Query<Toggle>().ToList().Any(x => x.name.StartsWith("capture-")), Is.False);
                Assert.That(row.Query<Button>().ToList().Any(x => x.text == "定位" || x.text == "预览"), Is.False);
                Assert.That(row.Query<Label>().ToList().Any(x => x.text.Contains("未找到场景 Marker")), Is.True);
                Submit(ui.Q<Button>("edit-profile-point-" + key));
                Assert.That(window.hasUnsavedChanges, Is.True);
                Assert.That(profile.points.Length, Is.EqualTo(5));
                Assert.That(ui.Q<Button>("edit-profile-point-" + key).text, Is.EqualTo("撤销移除"));
                Assert.That(PointEditModel(window).Proposal.points.Length, Is.EqualTo(4));
                Submit(ui.Q<Button>("edit-profile-point-" + key));
                Assert.That(window.hasUnsavedChanges, Is.False);
                Assert.That(PointEditModel(window).Proposal.points.Length, Is.EqualTo(5));

                var filter = ui.Q<UnityEditor.UIElements.ToolbarToggle>("filter-Marker");
                filter.value = false;
                Assert.That(ui.Q("profile-point-" + key), Is.Null);
                filter.value = true;
                Assert.That(ui.Q("profile-point-" + key), Is.Not.Null);
            }
            finally
            {
                window.DiscardChanges();
                window.Close();
            }
        }

        [UnityTest]
        public IEnumerator ReplacementAndRemovalSurviveWindowReloadAndDiscardTogether()
        {
            string oldId = marker.pointId;
            marker.pointId = "Renamed";
            var window = OpenPointEditWindow();
            try
            {
                yield return null;
                var ui = window.rootVisualElement;
                Assert.That(ui.Q<Button>("replace-point-" + marker.GetInstanceID()).enabledSelf, Is.True);
                var work = PointEditModel(window);
                work.Selected.Clear();
                Assert.That(work.StageReplacement(oldId, marker), Is.True, work.Error);
                Assert.That(work.StageRemoval(profile.points[1].id), Is.True);
                InvokePointEditWindow(window, "PointEditChanged");
                Assert.That(ui.Q<Toggle>("capture-" + marker.GetInstanceID()).enabledSelf, Is.False);
                Assert.That(work.Calculate(), Is.True, work.Error);
                Submit(ui.Query<Button>().ToList().Single(x => x.text == "取消可见 Marker"));
                Assert.That(work.Selected, Does.Contain(marker), "Bulk deselection cannot break a pending replacement.");
                Assert.That(window.hasUnsavedChanges, Is.True);
                InvokePointEditWindow(window, "OnDisable");
                InvokePointEditWindow(window, "OnEnable");
                window.CreateGUI();
                work = PointEditModel(window);
                Assert.That(work.PointEdits.replacements.Single().newId, Is.EqualTo("Renamed"));
                Assert.That(work.PointEdits.removals, Is.EqualTo(new[] { profile.points[1].id }));
                Assert.That(work.Candidate, Is.Null);
                Assert.That(window.hasUnsavedChanges, Is.True);
                Assert.That(work.Calculate(), Is.True, work.Error);
                Assert.That(work.Proposal.points.Length, Is.EqualTo(4));
                Assert.That(profile.points.Length, Is.EqualTo(5));
                window.DiscardChanges();
                Assert.That(window.hasUnsavedChanges, Is.False);
                Assert.That(PointEditModel(window).PointEdits.HasChanges, Is.False);
                Assert.That(PointEditModel(window).Selected.Contains(marker), Is.False, "Discard restores selection before an automatically selected replacement.");
                Assert.That(profile.points[0].id, Is.EqualTo(oldId));
                Assert.That(marker.pointId, Is.EqualTo("Renamed"), "Discard does not undo scene edits.");
            }
            finally
            {
                window.DiscardChanges();
                window.Close();
            }
        }

        [Test]
        public void IndividualEditsComposeWithFullReplacementAndSavedCandidateRemainsUsable()
        {
            marker.pointId = "Renamed";
            Assert.That(model.StageReplacement(profile.points[0].id, marker), Is.True);
            Assert.That(model.StageRemoval(profile.points[1].id), Is.True);
            model.ReplacePoints = true;
            Assert.That(model.Calculate(), Is.True, model.Error);
            Assert.That(model.Proposal.points.Length, Is.EqualTo(1));
            Assert.That(model.Removed.Count, Is.EqualTo(5));
            Assert.That(model.Save(true, false), Is.True, model.Error);
            Assert.That(model.CandidateIsCurrent(), Is.True, model.Error);
            Assert.That(model.Apply(), Is.True, model.Error);
            Assert.That(profile.points.Single().id, Is.EqualTo("Renamed"));
        }
    }
}
