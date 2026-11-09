using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SceneGeography.Editor
{
    internal static class GeoCalibrationWorkbenchData
    {
        internal static GeoCalibrationProfile AssignedProfile(GeoReference reference) => reference == null ? null :
            new SerializedObject(reference).FindProperty("profile").objectReferenceValue as GeoCalibrationProfile;

        internal static bool IsSceneComponent(Component component)
        {
            if (component == null || EditorUtility.IsPersistent(component)) return false;
            var scene = component.gameObject.scene;
            return scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene) &&
                PrefabStageUtility.GetPrefabStage(component.gameObject) == null;
        }

        internal static GeoControlPointMarker[] Markers() => GeoEditorCompatibility.FindSceneObjects<GeoControlPointMarker>().Where(IsSceneComponent)
            .OrderBy(x => x.gameObject.scene.path, StringComparer.Ordinal)
            .ThenBy(x => x.reference == null ? "" : x.reference.name, StringComparer.Ordinal)
            .ThenBy(x => x.pointId, StringComparer.Ordinal).ToArray();

        internal static string Json(GeoCalibrationProfile profile) => profile == null ? "" : JsonUtility.ToJson(profile);

        // Only absorb double serialization round-off, never a fixed tolerance in model units.
        static bool SameNumber(double a, double b)
        {
            return a == b || Math.Abs(a - b) <= Math.Max(Math.Abs(a), Math.Abs(b)) * 1.7763568394002505e-15;
        }

        internal static bool SamePoint(GeoControlPointSettings a, GeoControlPointSettings b)
        {
            if (a == null || b == null || a.coordinate == null || b.coordinate == null) return false;
            if (!a.coordinate.TryBuild(out var first, out _) || !b.coordinate.TryBuild(out var second, out _)) return false;
            return a.id == b.id && a.role == b.role && a.weight == b.weight &&
                SameNumber(a.localPosition.x, b.localPosition.x) &&
                SameNumber(a.localPosition.y, b.localPosition.y) &&
                SameNumber(a.localPosition.z, b.localPosition.z) &&
                first.Reference.Equals(second.Reference) &&
                SameNumber(first.LatitudeDegrees, second.LatitudeDegrees) &&
                SameNumber(first.LongitudeDegrees, second.LongitudeDegrees) &&
                SameNumber(first.HeightMeters, second.HeightMeters);
        }

        internal static GeoControlPointSettings CopyPoint(GeoControlPointSettings point)
        {
            var coordinate = point.coordinate;
            return new GeoControlPointSettings
            {
                id = point.id, role = point.role, weight = point.weight, localPosition = point.localPosition,
                coordinate = coordinate == null ? null : new GeoCoordinateSettings
                {
                    horizontalType = coordinate.horizontalType, heightKind = coordinate.heightKind,
                    latitudeDegrees = coordinate.latitudeDegrees, longitudeDegrees = coordinate.longitudeDegrees,
                    heightMeters = coordinate.heightMeters, ellipsoidZeroMeters = coordinate.ellipsoidZeroMeters,
                    assumedHeightZero = coordinate.assumedHeightZero, heightModelId = coordinate.heightModelId
                }
            };
        }

        internal static string MarkerStatus(GeoControlPointMarker marker, GeoCalibrationProfile profile) =>
            GetMarkerStatus(marker, profile).Message;

        internal static GeoMarkerStatus GetMarkerStatus(GeoControlPointMarker marker, GeoCalibrationProfile profile)
        {
            if (marker == null) return new GeoMarkerStatus(GeoMarkerState.Deleted, GeoEditorLocalization.Tr("Object deleted"));
            if (!IsSceneComponent(marker)) return new GeoMarkerStatus(GeoMarkerState.InvalidScene, GeoEditorLocalization.Tr("Invalid: only Markers in normal scenes are supported"));
            if (!marker.TryCapture(out var point, out var error)) return new GeoMarkerStatus(GeoMarkerState.InvalidCapture, GeoEditorLocalization.Tr("Invalid: ") + error);
            if (Markers().Count(x => x.reference == marker.reference && x.pointId == marker.pointId) > 1) return new GeoMarkerStatus(GeoMarkerState.DuplicateSceneId, GeoEditorLocalization.Tr("Invalid: duplicate scene ID"));
            if (profile == null) return new GeoMarkerStatus(GeoMarkerState.NoProfile, GeoEditorLocalization.Tr("No target Profile selected"));
            var matches = (profile.points ?? Array.Empty<GeoControlPointSettings>()).Where(x => x != null && x.id == point.Id).ToArray();
            if (matches.Length > 1) return new GeoMarkerStatus(GeoMarkerState.DuplicateProfileId, GeoEditorLocalization.Tr("Invalid: duplicate ID in Profile"));
            if (matches.Length == 0) return new GeoMarkerStatus(GeoMarkerState.New, GeoEditorLocalization.Tr("New: not in configuration"));
            return SamePoint(matches[0], GeoControlPointSettings.From(point))
                ? new GeoMarkerStatus(GeoMarkerState.Synchronized, GeoEditorLocalization.Tr("Synchronized"))
                : new GeoMarkerStatus(GeoMarkerState.Modified, GeoEditorLocalization.Tr("Modified: not saved"));
        }

        internal static void Bind(GeoReference reference, GeoCalibrationProfile profile)
        {
            var serialized = new SerializedObject(reference);
            serialized.FindProperty("profile").objectReferenceValue = profile;
            serialized.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(reference.gameObject.scene);
        }
    }

    [InitializeOnLoad]
    internal static class GeoCalibrationApplicationState
    {
        sealed class Applied
        {
            internal long version;
            internal string json;
        }
        static readonly Dictionary<GeoReference, Applied> applied = new Dictionary<GeoReference, Applied>();

        static GeoCalibrationApplicationState() { Undo.undoRedoPerformed += () => applied.Clear(); }

        internal static void Record(GeoReference reference, string json) =>
            applied[reference] = new Applied { version = reference.CalibrationVersion, json = json };

        internal static string Describe(GeoReference reference, GeoCalibrationProfile profile)
        {
            if (reference == null) return GeoEditorLocalization.Tr("Select a Reference");
            if (!reference.IsInitialized) return GeoEditorLocalization.Tr("The current Reference has no active calibration");
            if (!applied.TryGetValue(reference, out var value) || value.version != reference.CalibrationVersion)
                return GeoEditorLocalization.Tr("An active calibration exists, but its source is unconfirmed. Calculate and apply again after reload or Undo.");
            return value.json == GeoCalibrationWorkbenchData.Json(profile)
                ? GeoEditorLocalization.Tr("Active calibration matches the target Profile")
                : GeoEditorLocalization.Tr("Active calibration differs from the target Profile (unsaved preview or configuration not yet applied)");
        }
    }

    internal sealed partial class GeoCalibrationWorkbenchModel : IDisposable
    {
        internal GeoReference Reference { get; private set; }
        internal GeoCalibrationProfile Profile { get; private set; }
        internal GeoCalibrationProfile Draft { get; private set; }
        internal GeoCalibrationProfile Proposal { get; private set; }
        internal readonly List<GeoControlPointMarker> Selected = new List<GeoControlPointMarker>();
        internal bool ReplacePoints;
        internal GeoCalibrationCandidate Candidate { get; private set; }
        internal GeoCalibrationReport Report { get; private set; }
        internal string Error { get; private set; }
        internal int Added { get; private set; }
        internal int Updated { get; private set; }
        internal readonly List<string> Removed = new List<string>();
        string sourceJson, draftJson, candidateKey;

        internal void Load(GeoReference reference, GeoCalibrationProfile profile)
        {
            Dispose();
            Reference = reference;
            Profile = profile;
            Draft = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            Proposal = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            Draft.hideFlags = Proposal.hideFlags = HideFlags.HideAndDontSave;
            if (profile != null) EditorUtility.CopySerialized(profile, Draft);
            else if (reference != null) Draft.localFrameId = reference.LocalFrameId;
            Draft.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            sourceJson = GeoCalibrationWorkbenchData.Json(profile);
            draftJson = GeoCalibrationWorkbenchData.Json(Draft);
            Selected.Clear();
            PointEdits = new GeoPointEdits();
            ReplacePoints = false;
            Error = null;
        }

        internal bool HasDraftEdits => Draft != null && (GeoCalibrationWorkbenchData.Json(Draft) != draftJson || PointEdits.HasChanges);
        internal string SourceJson => sourceJson;

        internal void RestoreDraft(string json, string baseline)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            JsonUtility.FromJsonOverwrite(json, Draft);
            Draft.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            sourceJson = baseline;
            Invalidate();
        }

        internal bool SourceChanged => Profile != null && GeoCalibrationWorkbenchData.Json(Profile) != sourceJson;

        internal void Invalidate()
        {
            Candidate = null;
            candidateKey = null;
            Report = null;
        }

        internal bool Build()
        {
            Error = null;
            Added = Updated = 0;
            Removed.Clear();
            if (Application.isPlaying) return Fail(GeoEditorLocalization.Tr("The workbench captures and saves only in Edit mode. At runtime, calculate and apply through Reference."));
            if (!GeoCalibrationWorkbenchData.IsSceneComponent(Reference)) return Fail(GeoEditorLocalization.Tr("Select a Reference in a loaded normal scene."));
            if (Draft == null || Proposal == null) return Fail(GeoEditorLocalization.Tr("Reload the configuration."));
            if (SourceChanged) return Fail(GeoEditorLocalization.Tr("The target Profile was modified externally. Reload it before saving to avoid overwriting external changes."));
            if (!string.Equals(Draft.localFrameId ?? "", Reference.LocalFrameId ?? "", StringComparison.Ordinal)) return Fail(GeoEditorLocalization.Tr("Profile and Reference Local Frame IDs differ. Coordinate conventions are never overwritten automatically."));
            if (Selected.Any(x => x == null)) return Fail(GeoEditorLocalization.Tr("A selected Marker was deleted. Refresh the list and select again."));
            var duplicates = GeoCalibrationWorkbenchData.Markers().Where(x => x.reference == Reference && !string.IsNullOrWhiteSpace(x.pointId))
                .GroupBy(x => x.pointId, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
            if (duplicates != null) return Fail(GeoEditorLocalization.Tr("Duplicate scene Point ID: ") + duplicates.Key);
            var original = Draft.points ?? Array.Empty<GeoControlPointSettings>();
            if (original.Any(x => x == null || string.IsNullOrWhiteSpace(x.id)) ||
                original.GroupBy(x => x.id, StringComparer.Ordinal).Any(x => x.Count() > 1))
                return Fail(GeoEditorLocalization.Tr("The Profile contains null points, empty IDs, or duplicate IDs. Fix the configuration first."));
            if (!ValidatePointEdits(original, out var removedIds)) return false;
            var captured = new List<GeoControlPointSettings>();
            var selectedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var marker in Selected)
            {
                if (!GeoCalibrationWorkbenchData.IsSceneComponent(marker) || marker.reference != Reference)
                    return Fail(GeoEditorLocalization.Tr("Markers from other References, Prefabs, or preview scenes cannot be mixed."));
                if (!marker.TryCapture(out var point, out var error)) return Fail(error.ToString());
                if (!selectedIds.Add(point.Id)) return Fail(GeoEditorLocalization.Tr("Duplicate selected Marker ID: ") + point.Id);
                if (PointEdits.removals.Contains(point.Id)) continue;
                captured.Add(GeoControlPointSettings.From(point));
            }
            var before = original.ToDictionary(x => x.id, StringComparer.Ordinal);
            foreach (var point in captured)
            {
                if (!before.TryGetValue(point.id, out var old))
                {
                    if (!PointEdits.replacements.Any(x => x.newId == point.id)) Added++;
                }
                else if (!GeoCalibrationWorkbenchData.SamePoint(old, point)) Updated++;
            }
            if (ReplacePoints) removedIds.UnionWith(original.Where(x => !selectedIds.Contains(x.id)).Select(x => x.id));
            Removed.AddRange(original.Where(x => removedIds.Contains(x.id)).Select(x => x.id));
            captured = captured.Select(x => before.TryGetValue(x.id, out var old) && GeoCalibrationWorkbenchData.SamePoint(old, x) ? old : x).ToList();
            var points = ReplacePoints ? captured : original.Where(x => !removedIds.Contains(x.id)).ToList();
            if (!ReplacePoints)
                foreach (var point in captured)
                {
                    int index = points.FindIndex(x => x.id == point.id);
                    if (index < 0) points.Add(point); else points[index] = point;
                }
            EditorUtility.CopySerialized(Draft, Proposal);
            Proposal.hideFlags = HideFlags.HideAndDontSave;
            // DTO 深复制，候选和工作草稿不能共享可编辑点对象。
            Proposal.points = points.Select(GeoCalibrationWorkbenchData.CopyPoint).ToArray();
            return true;
        }

        string Key() => GeoCalibrationWorkbenchData.Json(Proposal) + "|" + Reference.GetInstanceID() + "|" +
            (Reference.Root == null ? 0 : Reference.Root.GetInstanceID()) + "|" + Reference.LocalFrameId + "|" +
            Reference.CalibrationVersion + "|" + ReplacePoints + "|" + JsonUtility.ToJson(PointEdits) + "|" +
            string.Join(",", Selected.Select(x => x == null ? 0 : x.GetInstanceID()));

        internal bool CandidateIsCurrent()
        {
            if (Candidate == null) return false;
            if (!Build() || candidateKey != Key())
            {
                Invalidate();
                if (string.IsNullOrEmpty(Error)) Error = GeoEditorLocalization.Tr("Inputs or active calibration changed. Calculate a new candidate.");
                return false;
            }
            return true;
        }

        internal bool Calculate()
        {
            Invalidate();
            if (!Build()) return false;
            if (!Proposal.TryBuildRequest(out var request, out var error)) return Fail(error.ToString());
            bool success = Reference.TryPrepareCalibration(request, out var candidate, out var report, out error);
            Report = report;
            if (!success) return Fail(error.ToString());
            Candidate = candidate;
            candidateKey = Key();
            return true;
        }

        internal bool Save(bool confirmRemovals, bool bind)
        {
            if (Profile == null) return Fail(GeoEditorLocalization.Tr("No target asset. Use Save as new Profile."));
            if (!Calculate()) return false;
            if (Removed.Count > 0 && !confirmRemovals) return Fail(GeoEditorLocalization.Tr("Saving removes or replaces control points. Explicit confirmation of the removal list is required."));
            if (!AssetDatabase.Contains(Profile) || !AssetDatabase.IsOpenForEdit(Profile)) return Fail(GeoEditorLocalization.Tr("The target is not a writable Profile asset."));
            Undo.RecordObject(Profile, "Update geographic calibration profile");
            string name = Profile.name;
            var flags = Profile.hideFlags;
            EditorUtility.CopySerialized(Proposal, Profile);
            Profile.name = name;
            Profile.hideFlags = flags;
            EditorUtility.SetDirty(Profile);
            AssetDatabase.SaveAssetIfDirty(Profile);
            if (bind) GeoCalibrationWorkbenchData.Bind(Reference, Profile);
            AcceptSavedProfile();
            return true;
        }

        internal bool SaveAndApply(bool confirmRemovals, bool bind)
        {
            if (!Save(confirmRemovals, bind)) return false;
            return Apply() || Fail(GeoEditorLocalization.Tr("Configuration saved, but applying to the current Reference failed: ") + Error);
        }

        internal bool SaveAs(string path, bool bind)
        {
            if (!Calculate()) return false;
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || AssetDatabase.LoadMainAssetAtPath(path) != null ||
                System.IO.File.Exists(path)) return Fail(GeoEditorLocalization.Tr("Choose an unused .asset path under Assets. Existing assets will not be overwritten."));
            var asset = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            EditorUtility.CopySerialized(Proposal, asset);
            asset.hideFlags = HideFlags.None;
            asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            Undo.RegisterCreatedObjectUndo(asset, "Create geographic calibration profile");
            Profile = asset;
            if (bind) GeoCalibrationWorkbenchData.Bind(Reference, asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            AcceptSavedProfile();
            return true;
        }

        void AcceptSavedProfile()
        {
            sourceJson = GeoCalibrationWorkbenchData.Json(Profile);
            EditorUtility.CopySerialized(Profile, Draft);
            Draft.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            draftJson = GeoCalibrationWorkbenchData.Json(Draft);
            PointEdits = new GeoPointEdits();
            // The saved proposal is still the candidate, even after pending edits become the baseline.
            candidateKey = Candidate == null ? null : Key();
        }

        internal bool Apply()
        {
            if (!CandidateIsCurrent()) return Fail(Error ?? GeoEditorLocalization.Tr("Calculate a valid candidate first."));
            string json = GeoCalibrationWorkbenchData.Json(Proposal);
            if (!GeoCalibrationEditorUtility.TryApplyCalibration(Reference, Candidate, out var error)) return Fail(error.ToString());
            GeoCalibrationApplicationState.Record(Reference, json);
            Candidate = null;
            candidateKey = null;
            return true;
        }

        bool Fail(string message) { Error = message; return false; }

        public void Dispose()
        {
            Invalidate();
            if (Draft != null) { Undo.ClearUndo(Draft); Object.DestroyImmediate(Draft); }
            if (Proposal != null) Object.DestroyImmediate(Proposal);
            Draft = Proposal = null;
        }
    }
}
