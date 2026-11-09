using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SceneGeography.Editor
{
    public sealed partial class GeoCalibrationWorkbench : EditorWindow
    {
        [SerializeField] GeoReference reference;
        [SerializeField] GeoCalibrationProfile profile;
        [SerializeField] List<GeoControlPointMarker> selection = new List<GeoControlPointMarker>();
        [SerializeField] string draftBackup;
        [SerializeField] string sourceBackup;
        [SerializeField] bool replaceBackup;
        GeoCalibrationWorkbenchModel model;
        ObjectField profileField;
        ScrollView markers;
        VisualElement settings, markerEditor;
        Label summary, application, profileOnly, status, sceneSummary;
        Toggle replace, bind;
        Button calculate, save, apply, saveApply, saveAs;
        readonly Dictionary<GeoControlPointMarker, Label> markerLabels = new Dictionary<GeoControlPointMarker, Label>();
        readonly Dictionary<string, PropertyField> settingFields = new Dictionary<string, PropertyField>();
        readonly List<SerializedObject> objectSerialized = new List<SerializedObject>();
        readonly Dictionary<PropertyField, UnityEngine.Component> objectFields = new Dictionary<PropertyField, UnityEngine.Component>();
        readonly Dictionary<UnityEngine.Object, Transform> reporterTargets = new Dictionary<UnityEngine.Object, Transform>();
        SerializedObject draftSerialized;
        GeoCalibrationSceneEntry editingEntry;
        GeoCalibrationSceneInventory inventory;
        [SerializeField] GeoWorkbenchComponentType componentFilter = GeoWorkbenchComponentType.All;
        [SerializeField] string search = "";
        readonly Dictionary<string, bool> expanded = new Dictionary<string, bool>();
        readonly List<VisualElement> detailPages = new List<VisualElement>();
        readonly List<ToolbarToggle> detailTabs = new List<ToolbarToggle>();
        int detailPage;
        string operationMessage;
        bool operationFailed;
        bool hierarchyDirty, buildingUI;
        IVisualElementScheduledItem tick;
        string lastDraftJson;

        [MenuItem("Window/Scene Geography/Calibration Workbench")]
        public static void Open()
        {
            if (Selection.activeGameObject != null)
            {
                var marker = Selection.activeGameObject.GetComponent<GeoControlPointMarker>();
                if (marker != null) { OpenMarkers(new[] { marker }); return; }
                var selectedReference = Selection.activeGameObject.GetComponent<GeoReference>();
                if (selectedReference != null) { OpenReference(selectedReference); return; }
            }
            if (Selection.activeObject is GeoCalibrationProfile selectedProfile) { OpenProfile(selectedProfile); return; }
            GetWindow<GeoCalibrationWorkbench>(GeoEditorLocalization.Tr("Calibration Workbench")).Show();
        }

        public static void OpenReference(GeoReference value) => ShowContext(value, GeoCalibrationWorkbenchData.AssignedProfile(value), null);

        public static void OpenProfile(GeoCalibrationProfile value)
        {
            var references = GeoEditorCompatibility.FindSceneObjects<GeoReference>()
                .Where(GeoCalibrationWorkbenchData.IsSceneComponent).Where(x => GeoCalibrationWorkbenchData.AssignedProfile(x) == value).ToArray();
            ShowContext(references.Length == 1 ? references[0] : null, value, null);
        }

        public static void OpenMarkers(IEnumerable<GeoControlPointMarker> values)
        {
            var selected = values.Where(x => x != null).Distinct().ToArray();
            var first = selected.FirstOrDefault();
            if (first == null) { Open(); return; }
            // 多 Reference 的选择只自动预选第一个分组，其他分组仍在工作台中可见。
            ShowContext(first.reference, GeoCalibrationWorkbenchData.AssignedProfile(first.reference),
                selected.Where(x => x.reference == first.reference).ToArray());
        }

        static void ShowContext(GeoReference value, GeoCalibrationProfile target, GeoControlPointMarker[] points)
        {
            var window = GetWindow<GeoCalibrationWorkbench>(GeoEditorLocalization.Tr("Calibration Workbench"));
            window.SetContext(value, target, points);
            window.Show();
            window.Focus();
        }

        void OnEnable()
        {
            minSize = new Vector2(920, 580);
            titleContent = new GUIContent(GeoEditorLocalization.Tr("Calibration Workbench"));
            model = new GeoCalibrationWorkbenchModel();
            model.Load(reference, profile);
            model.RestoreDraft(draftBackup, sourceBackup);
            model.ReplacePoints = replaceBackup;
            model.RestorePointEdits(pointEditsBackup);
            model.Selected.AddRange(selection.Where(x => x != null && x.reference == reference));
            GeoEditorLocalization.Changed += OnLanguageChanged;
            Undo.undoRedoPerformed += OnUndo;
            EditorApplication.hierarchyChanged += OnHierarchy;
            EditorApplication.projectChanged += OnHierarchy;
        }

        void OnDisable()
        {
            BackupDraft();
            tick?.Pause();
            GeoEditorLocalization.Changed -= OnLanguageChanged;
            Undo.undoRedoPerformed -= OnUndo;
            EditorApplication.hierarchyChanged -= OnHierarchy;
            EditorApplication.projectChanged -= OnHierarchy;
            ReleaseBindings();
            model?.Dispose();
        }

        // EditorWindow handles the close prompt; serialized input survives assembly reload.
        void BackupDraft()
        {
            if (model == null || model.Draft == null)
            {
                return;
            }

            hasUnsavedChanges = model.HasDraftEdits;
            saveChangesMessage = GeoEditorLocalization.Tr("The calibration working draft has unsaved parameter or point changes. Save, discard, or cancel closing the workbench.");
            draftBackup = hasUnsavedChanges ? GeoCalibrationWorkbenchData.Json(model.Draft) : null;
            sourceBackup = model.SourceJson;
            replaceBackup = model.ReplacePoints;
            pointEditsBackup = model.PointEdits.Copy();
            selection = model.Selected.ToList();
        }

        public override void SaveChanges()
        {
            if (model.Profile == null)
            {
                SaveAs();
            }
            else
            {
                Save(false);
            }

            BackupDraft();
            if (!hasUnsavedChanges)
            {
                base.SaveChanges();
            }
        }

        public override void DiscardChanges()
        {
            draftBackup = sourceBackup = null;
            replaceBackup = false;
            pointEditsBackup = null;
            if (model != null)
            {
                selection = model.Selected.Where(x => !model.PointEdits.replacements.Any(edit => edit.addedSelection && edit.marker == x)).ToList();
                ReleaseBindings();
                model.Load(reference, profile);
                model.Selected.AddRange(selection.Where(x => x != null && x.reference == reference));
                if (rootVisualElement.childCount > 0)
                {
                    BuildUI();
                }
            }

            base.DiscardChanges();
        }

        void OnUndo() { model?.Invalidate(); hierarchyDirty = true; }
        void OnHierarchy() { hierarchyDirty = true; }

        bool ConfirmDiscard() => model == null || !model.HasDraftEdits ||
            EditorUtility.DisplayDialog(GeoEditorLocalization.Tr("Switch working configuration"), GeoEditorLocalization.Tr("The working draft has unsaved parameter or point changes. Discard them? Scene Marker edits will not be undone."), GeoEditorLocalization.Tr("Discard draft"), GeoEditorLocalization.Tr("Cancel"));

        void SetContext(GeoReference value, GeoCalibrationProfile target, GeoControlPointMarker[] points)
        {
            if (!ConfirmDiscard()) return;
            ReleaseBindings();
            reference = value; profile = target;
            operationMessage = null; operationFailed = false;
            selection = points == null ? new List<GeoControlPointMarker>() : points.ToList();
            draftBackup = sourceBackup = null;
            replaceBackup = false;
            pointEditsBackup = null;
            model.Load(reference, profile);
            model.Selected.AddRange(selection);
            inventory = GeoCalibrationSceneInventory.Capture();
            editingEntry = inventory.Entries.FirstOrDefault(x => selection.Contains(x.Marker));
            detailPage = 0;
            if (rootVisualElement.childCount > 0) BuildUI();
        }

        public void CreateGUI() { BuildUI(); }

        void ReleaseBindings()
        {
            rootVisualElement.Unbind();
            draftSerialized?.Dispose(); draftSerialized = null;
            foreach (var serialized in objectSerialized) serialized.Dispose();
            objectSerialized.Clear(); objectFields.Clear(); reporterTargets.Clear();
        }

        static Button Action(VisualElement parent, string text, System.Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.style.minHeight = 28;
            parent.Add(button);
            return button;
        }

        void AddPreset(VisualElement parent, string text, GeoCalibrationMode mode)
        {
            parent.Add(new ToolbarButton(() =>
            {
                Undo.RecordObject(model.Draft, "Change calibration draft mode");
                model.Draft.mode = mode;
                model.Invalidate();
                draftSerialized.Update();
                UpdateFieldRules();
                RefreshStatus();
            }) { text = text });
        }

        void BuildSettings()
        {
            settingFields.Clear();
            draftSerialized = new SerializedObject(model.Draft);
            var iterator = draftSerialized.GetIterator();
            bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                if (iterator.name == "m_Script" || iterator.name == "points") continue;
                var property = new PropertyField(iterator.Copy()) { name = "draft-" + iterator.name };
                if (iterator.name == "options")
                {
                    var advanced = new Foldout { text = GeoEditorLocalization.Tr("Acceptance and solver options (advanced)"), value = false };
                    advanced.Add(property); settings.Add(advanced);
                }
                else settings.Add(property);
                GeoEditorLocalization.LocalizeProperty(property, iterator.Copy());
                property.BindProperty(iterator.Copy());
                settingFields[iterator.name] = property;
            }
            settings.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                operationMessage = null; operationFailed = false;
                model.CandidateIsCurrent(); UpdateFieldRules(); RefreshStatus();
            });
            UpdateFieldRules();
        }

        void UpdateFieldRules()
        {
            foreach (var pair in settingFields) pair.Value.SetEnabled(!Application.isPlaying && GeoInspectorRules.IsEditable(model.Draft, pair.Key));
        }

        void SelectionChanged()
        {
            operationMessage = null; operationFailed = false;
            selection = model.Selected.ToList();
            model.Invalidate(); RefreshStatus();
        }

        bool ConfirmRemovals()
        {
            if (!model.Build()) return false;
            if (model.Removed.Count == 0) return true;
            string message = GeoEditorLocalization.Tr("Update the target Profile (no scene objects will be deleted):") + Environment.NewLine +
                GeoEditorLocalization.Format("Add {0}, update {1}, replace {2}, remove {3}.", model.Added, model.Updated, model.PointEdits.replacements.Count, Math.Max(0, model.Removed.Count - model.PointEdits.replacements.Count)) + Environment.NewLine +
                model.PointChangeDetails() + Environment.NewLine + GeoEditorLocalization.Tr("Asset changes can be undone, but the active calibration must be calculated and applied again.");
            return EditorUtility.DisplayDialog(GeoEditorLocalization.Tr("Confirm control point changes"), message, GeoEditorLocalization.Tr("Confirm and save"), GeoEditorLocalization.Tr("Cancel"));
        }

        void Run(Func<bool> action, string successMessage = null)
        {
            bool success = action();
            operationFailed = !success;
            operationMessage = success ? successMessage ?? GeoEditorLocalization.Tr("Operation complete. Check asset synchronization and active calibration separately.") : model.Error;
            if (!success) EditorUtility.DisplayDialog(GeoEditorLocalization.Tr("Operation incomplete"), model.Error ?? GeoEditorLocalization.Tr("Check the workbench status."), GeoEditorLocalization.Tr("OK"));
            RefreshStatus(); SceneView.RepaintAll(); Repaint();
        }

        void Save(bool andApply)
        {
            if (!ConfirmRemovals()) { RefreshStatus(); return; }
            Run(() => andApply ? model.SaveAndApply(true, bind.value) : model.Save(true, bind.value),
                andApply ? GeoEditorLocalization.Tr("Profile saved and applied to the current Reference.") : GeoEditorLocalization.Tr("Profile saved; calibration was not applied."));
        }

        void SaveAs()
        {
            string path = EditorUtility.SaveFilePanelInProject(GeoEditorLocalization.Tr("Save calibration Profile as"), profile == null ? "Calibration" : profile.name + " Copy", "asset", GeoEditorLocalization.Tr("Choose a new asset path. Existing Profiles will not be overwritten."));
            if (string.IsNullOrEmpty(path)) return;
            var previous = profile;
            Run(() =>
            {
                if (!model.SaveAs(path, bind.value)) return false;
                profile = model.Profile;
                profileField.SetValueWithoutNotify(profile);
                return true;
            });
            if (profile != previous) BuildUI();
        }
    }
}
