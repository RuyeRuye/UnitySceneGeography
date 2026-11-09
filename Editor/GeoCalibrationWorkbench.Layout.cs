using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static SceneGeography.Editor.GeoCalibrationWorkbenchStyle;

namespace SceneGeography.Editor
{
    public sealed partial class GeoCalibrationWorkbench
    {
        void BuildUI()
        {
            bool bindOnSave = bind != null && bind.value;
            buildingUI = true;
            tick?.Pause();
            ReleaseBindings();
            rootVisualElement.Clear();
            detailPages.Clear(); detailTabs.Clear();
            rootVisualElement.style.paddingLeft = rootVisualElement.style.paddingRight = 6;
            rootVisualElement.style.paddingTop = rootVisualElement.style.paddingBottom = 4;
            inventory = GeoCalibrationSceneInventory.Capture();

            var toolbar = new Toolbar { name = "main-toolbar" };
            toolbar.style.flexShrink = 0;
            toolbar.style.flexWrap = Wrap.Wrap;
            toolbar.style.height = StyleKeyword.Auto;
            toolbar.Add(new ToolbarButton(() => ReadScene(true)) { text = GeoEditorLocalization.Tr("Read scene"), tooltip = GeoEditorLocalization.Tr("Read all loaded normal scenes and reset filters. Do not reload the draft, save, or apply.") });
            toolbar.Add(new ToolbarButton(() => ReadScene(false)) { text = GeoEditorLocalization.Tr("Refresh scene"), tooltip = GeoEditorLocalization.Tr("Refresh the list and status while preserving filters, the draft, and valid capture selections.") });
            AddLanguageSetting(toolbar);
            toolbar.Add(new ToolbarSpacer());
            calculate = Action(toolbar, GeoEditorLocalization.Tr("Calculate candidate"), () => Run(() => model.Calculate(), GeoEditorLocalization.Tr("Candidate calculated; no assets were saved and calibration was not applied.")));
            save = Action(toolbar, GeoEditorLocalization.Tr("Save to Profile"), () => Save(false));
            apply = Action(toolbar, GeoEditorLocalization.Tr("Apply candidate (preview only)"), () => Run(() => model.Apply(), GeoEditorLocalization.Tr("Candidate applied for preview; no assets were saved.")));
            saveApply = Action(toolbar, GeoEditorLocalization.Tr("Save and apply"), () => Save(true));
            saveAs = Action(toolbar, GeoEditorLocalization.Tr("Save as new Profile…"), SaveAs);
            saveApply.tooltip = GeoEditorLocalization.Tr("If the target Profile differs from the Reference's assigned Profile, enable the binding option first.");
            calculate.name = "calculate-candidate";
            save.name = "save-profile";
            apply.name = "apply-candidate";
            saveApply.name = "save-and-apply";
            saveAs.name = "save-as-profile";
            rootVisualElement.Add(toolbar);

            var split = new TwoPaneSplitView(0, 420, TwoPaneSplitViewOrientation.Horizontal) { name = "main-split" };
            split.style.flexGrow = 1; split.style.flexBasis = 0; split.style.minHeight = 0;
            var browser = new VisualElement { name = "scene-browser" };
            browser.style.minWidth = 300; browser.style.minHeight = 0;
            browser.style.paddingRight = 6;
            browser.Add(Heading(GeoEditorLocalization.Tr("Scene objects")));
            BuildFilters(browser);
            sceneSummary = Text("", true); browser.Add(sceneSummary);
            markers = new ScrollView(ScrollViewMode.Vertical) { name = "scene-tree" };
            markers.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            markers.style.flexGrow = 1; markers.style.minHeight = 0;
            browser.Add(markers);
            split.Add(browser);

            var details = new VisualElement { name = "work-details" };
            details.style.minWidth = 0; details.style.minHeight = 0;
            details.style.paddingLeft = 8;
            var tabs = new Toolbar(); tabs.style.flexShrink = 0;
            foreach (string title in new[] { GeoEditorLocalization.Tr("Calibration settings"), GeoEditorLocalization.Tr("Object details"), GeoEditorLocalization.Tr("Calculation report") })
            {
                int index = detailTabs.Count;
                var tab = new ToolbarToggle { text = title };
                tab.RegisterValueChangedCallback(_ => ShowPage(index));
                tabs.Add(tab); detailTabs.Add(tab);
                var page = new ScrollView(ScrollViewMode.Vertical);
                page.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                page.style.flexGrow = 1; page.style.minHeight = 0; page.style.minWidth = 0;
                detailPages.Add(page);
            }
            details.Add(tabs);
            foreach (var page in detailPages) details.Add(page);
            BuildConfiguration(detailPages[0]);
            bind.SetValueWithoutNotify(bindOnSave);
            markerEditor = new VisualElement { name = "scene-object-editor" };
            detailPages[1].Add(markerEditor);
            markerEditor.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
            {
                var changed = evt.changedProperty;
                if (changed != null && changed.propertyPath == "target" && GeoInspectorGUI.IsReporter(changed.serializedObject.targetObject) &&
                    reporterTargets.TryGetValue(changed.serializedObject.targetObject, out var previous) &&
                    !object.ReferenceEquals(previous, GeoInspectorGUI.ReporterTarget(changed.serializedObject.targetObject)))
                {
                    var reporter = changed.serializedObject.targetObject;
                    var target = GeoInspectorGUI.ReporterTarget(reporter);
                    reporterTargets[reporter] = target;
                    changed.serializedObject.FindProperty("hasExplicitTarget").boolValue = target != null;
                    changed.serializedObject.ApplyModifiedProperties();
                }
                UpdateObjectFieldRules();
                model.CandidateIsCurrent(); operationMessage = null; operationFailed = false;
                RefreshStatus();
            });
            BuildObjectEditor();
            detailPages[2].Add(Text(GeoEditorLocalization.Tr("Calculating does not move objects; applying updates bindings. Saving configuration is not applying calibration."), true));
            detailPages[2].Add(new IMGUIContainer(() =>
            {
                GeoReferenceEditor.DrawReport(model.Report, model.Candidate != null ? GeoEditorLocalization.Tr("Workbench candidate report (not applied)") : GeoEditorLocalization.Tr("Workbench last calculation report"));
                if (reference != null) GeoReferenceEditor.DrawReport(reference.CurrentReport, GeoEditorLocalization.Tr("Active calibration report"));
            }));
            split.Add(details);
            rootVisualElement.Add(split);
            status = new Label { name = "status-line" };
            status.style.flexShrink = 0; status.style.height = 25;
            status.style.unityTextAlign = TextAnchor.MiddleLeft;
            status.style.textOverflow = TextOverflow.Ellipsis; status.style.overflow = Overflow.Hidden;
            rootVisualElement.Add(status);
            RebuildMarkers(); ShowPage(detailPage);
            buildingUI = false;
            RefreshStatus();
            tick = rootVisualElement.schedule.Execute(Tick).Every(500);
        }

        void BuildConfiguration(VisualElement details)
        {
            details.Add(Heading(reference == null ? GeoEditorLocalization.Tr("Select a Reference on the left") : reference.name + GeoEditorLocalization.Tr(" · Calibration settings")));
            details.Add(Text(reference == null ? GeoEditorLocalization.Tr("Read the scene, then click Edit calibration in a Reference group.") : GeoCalibrationSceneEntry.HierarchyPath(reference.transform), true));
            summary = Text(""); details.Add(summary);
            application = Text("", true); details.Add(application);
            profileField = new ObjectField(GeoEditorLocalization.Tr("Target Profile")) { objectType = typeof(GeoCalibrationProfile), allowSceneObjects = false, value = profile };
            profileField.RegisterValueChangedCallback(evt =>
            {
                SetContext(reference, evt.newValue as GeoCalibrationProfile, selection.ToArray());
                profileField.SetValueWithoutNotify(profile);
            });
            details.Add(profileField);
            details.Add(Text(GeoEditorLocalization.Tr("These settings are a working draft. The Profile asset is unchanged until saved."), true));
            details.Add(new Button(() => SetContext(reference, profile, selection.ToArray())) { text = GeoEditorLocalization.Tr("Reload target Profile (discard draft)") });
            bind = new Toggle(GeoEditorLocalization.Tr("Bind the target Profile to the current Reference when saving"));
            bind.RegisterValueChangedCallback(_ => RefreshStatus());
            details.Add(bind);
            var presets = new Toolbar(); presets.style.flexWrap = Wrap.Wrap; presets.style.height = StyleKeyword.Auto;
            presets.Add(new Label(GeoEditorLocalization.Tr("Mode presets:")));
            AddPreset(presets, GeoEditorLocalization.Tr("Single anchor + heading"), GeoCalibrationMode.SingleAnchor);
            AddPreset(presets, GeoEditorLocalization.Tr("Upright block"), GeoCalibrationMode.Upright);
            AddPreset(presets, GeoEditorLocalization.Tr("Full spatial"), GeoCalibrationMode.Spatial);
            details.Add(presets);
            details.Add(Text(GeoEditorLocalization.Tr("Presets only change the mode. SingleAnchor: exactly 1 Fit; Upright: at least 2 with a horizontal baseline; Spatial: at least 3 non-collinear Fit points."), true));
            settings = new VisualElement(); details.Add(settings); BuildSettings();
            replace = new Toggle(GeoEditorLocalization.Tr("Replace the entire point set (default: merge by ID and preserve unselected points)"));
            replace.SetValueWithoutNotify(model.ReplacePoints);
            replace.RegisterValueChangedCallback(evt => { model.ReplacePoints = evt.newValue; model.Invalidate(); RefreshStatus(); });
            details.Add(replace);
            profileOnly = Text("", true); details.Add(profileOnly);
            details.Add(Text(GeoEditorLocalization.Tr("Saving assets is not applying calibration. After Undo or script reload, calculate and apply again."), true));
        }

        void ShowPage(int index)
        {
            detailPage = Mathf.Clamp(index, 0, detailPages.Count - 1);
            for (int i = 0; i < detailPages.Count; i++)
            {
                detailPages[i].style.display = i == detailPage ? DisplayStyle.Flex : DisplayStyle.None;
                detailTabs[i].SetValueWithoutNotify(i == detailPage);
            }
        }

        void BuildFilters(VisualElement browser)
        {
            var field = new ToolbarSearchField { name = "scene-search", value = search };
            field.style.flexShrink = 0; field.style.width = StyleKeyword.Auto; field.style.alignSelf = Align.Stretch;
            field.RegisterValueChangedCallback(evt => { search = evt.newValue; RebuildMarkers(); RefreshStatus(); });
            browser.Add(field);
            var filters = Row(); filters.name = "component-filters"; filters.style.flexShrink = 0;
            foreach (var type in new[] { GeoWorkbenchComponentType.Marker, GeoWorkbenchComponentType.Anchor, GeoWorkbenchComponentType.Orientation, GeoWorkbenchComponentType.Reporter })
            {
                var toggle = new ToolbarToggle { text = type.ToString(), name = "filter-" + type, value = (componentFilter & type) != 0 };
                toggle.style.color = Component(type);
                toggle.tooltip = GeoEditorLocalization.Tr("Filter by component type using union semantics. Filtering does not change capture selection.");
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) componentFilter |= type; else componentFilter &= ~type;
                    RebuildMarkers(); RefreshStatus();
                });
                filters.Add(toggle);
            }
            browser.Add(filters);
            browser.Add(Text(GeoEditorLocalization.Tr("Filtering only changes visibility. Only Marker checkboxes contribute calibration inputs."), true));
        }

        void ReadScene(bool resetFilters)
        {
            inventory = GeoCalibrationSceneInventory.Capture();
            if (resetFilters)
            {
                componentFilter = GeoWorkbenchComponentType.All; search = "";
            }
            if (!GeoCalibrationWorkbenchData.IsSceneComponent(reference))
            {
                var next = inventory.References.FirstOrDefault(x => x.gameObject.scene == SceneManager.GetActiveScene()) ?? inventory.References.FirstOrDefault();
                if (next != null) SetContext(next, GeoCalibrationWorkbenchData.AssignedProfile(next), null);
            }
            if (model.Selected.RemoveAll(x => !GeoCalibrationWorkbenchData.IsSceneComponent(x) || x.reference != reference) > 0) SelectionChanged();
            operationMessage = resetFilters ? GeoEditorLocalization.Tr("Loaded scenes read; filters reset. Calibration was not saved or applied.") : GeoEditorLocalization.Tr("Scene list refreshed; draft, filters, and valid selections preserved.");
            operationFailed = false;
            if (resetFilters) BuildUI(); else { RebuildMarkers(); RefreshStatus(); }
        }

        Foldout Group(string key, string title, bool initial)
        {
            var group = new Foldout { text = title, value = expanded.TryGetValue(key, out bool value) ? value : initial };
            group.RegisterValueChangedCallback(evt => { if (evt.target == group) expanded[key] = evt.newValue; });
            return group;
        }

        void RebuildMarkers()
        {
            if (markers == null) return;
            float scroll = markers.scrollOffset.y;
            markers.Clear(); markerLabels.Clear();
            var visible = inventory.Entries.Where(x => x.Matches(componentFilter, search)).ToArray();
            sceneSummary.text = GeoEditorLocalization.Format("{0} scenes · {1} References · showing {2}/{3} object entries", inventory.Scenes.Length, inventory.References.Length, visible.Length, inventory.Entries.Length);
            foreach (var scene in inventory.Scenes)
            {
                var owners = inventory.References.Where(x => x.gameObject.scene == scene).ToArray();
                var orphans = visible.Where(x => x.Object.scene == scene && !GeoCalibrationWorkbenchData.IsSceneComponent(x.Reference)).ToArray();
                var sceneGroup = Group("scene-" + scene.handle, scene.name + "  ·  " + owners.Length + GeoEditorLocalization.Tr(" References"), true);
                sceneGroup.name = "scene-group-" + scene.handle;
                sceneGroup.style.marginBottom = 8;
                var title = sceneGroup.Q<Toggle>(); title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.backgroundColor = EditorGUIUtility.isProSkin ? new Color(.12f, .14f, .17f) : new Color(.72f, .76f, .82f);
                title.style.paddingTop = title.style.paddingBottom = 5;
                foreach (var owner in owners)
                {
                    var all = inventory.Entries.Where(x => x.Reference == owner).ToArray();
                    var filtered = visible.Where(x => x.Reference == owner).ToArray();
                    var group = Group("ref-" + owner.GetInstanceID(), owner.name + GeoEditorLocalization.Format("  ·  {0}/{1} entries", filtered.Length, all.Length), true);
                    group.name = "reference-group-" + owner.GetInstanceID();
                    Card(group, owner == reference ? Success : Reference);
                    var heading = Row();
                    heading.Add(Badge("Reference", Reference));
                    if (owner == reference) heading.Add(Badge(GeoEditorLocalization.Tr("Editing"), Success));
                    heading.Add(new Button(() => SetContext(owner, GeoCalibrationWorkbenchData.AssignedProfile(owner), null)) { text = GeoEditorLocalization.Tr("Edit calibration") });
                    group.Add(heading);
                    AddReadOnlyObject(group, GeoEditorLocalization.Tr("Scene reference"), owner, typeof(GeoReference));
                    var linked = GeoCalibrationWorkbenchData.AssignedProfile(owner);
                    AddReadOnlyObject(group, GeoEditorLocalization.Tr("Assigned Profile"), linked, typeof(GeoCalibrationProfile));
                    var path = Text(linked == null ? GeoEditorLocalization.Tr("No Profile assigned") : AssetDatabase.GetAssetPath(linked), true);
                    path.style.fontSize = 10; group.Add(path);
                    group.Add(Text(Counts(all), true));
                    var points = filtered.Select(x => x.Marker).Where(x => x != null).ToArray();
                    if (owner == reference)
                    {
                        var actions = Row();
                        var select = Action(actions, GeoEditorLocalization.Tr("Select visible Markers"), () =>
                        {
                            foreach (var point in points) if (!model.Selected.Contains(point)) model.Selected.Add(point);
                            SelectionChanged(); RebuildMarkers();
                        });
                        var clear = Action(actions, GeoEditorLocalization.Tr("Deselect visible Markers"), () =>
                        {
                            foreach (var point in points)
                                if (!model.PointEdits.replacements.Any(x => x.marker == point)) model.Selected.Remove(point);
                            SelectionChanged(); RebuildMarkers();
                        });
                        select.name = "select-visible-" + owner.GetInstanceID();
                        clear.name = "deselect-visible-" + owner.GetInstanceID();
                        select.SetEnabled(points.Length > 0 && !Application.isPlaying);
                        clear.SetEnabled(points.Length > 0 && !Application.isPlaying);
                        clear.tooltip = GeoEditorLocalization.Tr("Replacement targets remain selected until their replacement is undone. Other visible Markers can be deselected.");
                        group.Add(actions);
                    }
                    foreach (var entry in filtered) AddSceneRow(group, entry);
                    int configRows = AddProfileRows(group, owner);
                    if (filtered.Length == 0 && configRows == 0) group.Add(Text(all.Length == 0 ? GeoEditorLocalization.Tr("This Reference has no related component objects.") : GeoEditorLocalization.Tr("No objects match the current filters."), true));
                    sceneGroup.Add(group);
                }
                if (orphans.Length > 0)
                {
                    var group = Group("unbound-" + scene.handle, GeoEditorLocalization.Tr("Unassigned / invalid Reference · ") + orphans.Length + GeoEditorLocalization.Tr(" entries"), true);
                    group.name = "unbound-group-" + scene.handle;
                    Card(group, Warning);
                    group.Add(Text(GeoEditorLocalization.Tr("These components have no valid scene Reference. Inspect and repair their references; they cannot contribute to the current group's calibration."), true));
                    foreach (var entry in orphans) AddSceneRow(group, entry);
                    sceneGroup.Add(group);
                }
                if (owners.Length == 0 && orphans.Length == 0) sceneGroup.Add(Text(GeoEditorLocalization.Tr("No matching geography components."), true));
                markers.Add(sceneGroup);
            }
            if (inventory.Scenes.Length == 0) markers.Add(Text(GeoEditorLocalization.Tr("No normal scenes are loaded. Prefab stages and preview scenes are excluded."), true));
            var tree = markers;
            tree.schedule.Execute(() => tree.scrollOffset = new Vector2(0, scroll));
            hierarchyDirty = false;
            pointRowsKey = PointRowsKey();
        }

        static string Counts(IEnumerable<GeoCalibrationSceneEntry> entries)
        {
            var components = entries.SelectMany(x => x.Components).ToArray();
            return $"Marker {components.OfType<GeoControlPointMarker>().Count()}（Fit {components.OfType<GeoControlPointMarker>().Count(x => x.role == GeoControlPointRole.Fit)} / Check {components.OfType<GeoControlPointMarker>().Count(x => x.role == GeoControlPointRole.Check)}）· Anchor {components.OfType<GeoAnchor>().Count()} · Orientation {components.OfType<GeoOrientation>().Count()} · Reporter {components.Count(GeoInspectorGUI.IsReporter)}";
        }

        static void AddReadOnlyObject(VisualElement parent, string label, UnityEngine.Object value, Type type)
        {
            var field = new ObjectField(label) { objectType = type, allowSceneObjects = type == typeof(GameObject) || typeof(UnityEngine.Component).IsAssignableFrom(type), value = value };
            // 保留对象字段的原生定位能力，但不允许通过分组展示字段改变真实配置。
            field.RegisterValueChangedCallback(_ => field.SetValueWithoutNotify(value));
            parent.Add(field);
        }

        void AddSceneRow(VisualElement parent, GeoCalibrationSceneEntry entry)
        {
            var row = new VisualElement { name = "scene-entry-" + entry.Object.GetInstanceID() + "-" + (entry.Reference == null ? 0 : entry.Reference.GetInstanceID()), userData = entry };
            Card(row, Component(entry.Marker != null ? GeoWorkbenchComponentType.Marker : GeoCalibrationSceneEntry.TypeOf(entry.Components[0])), true);
            row.style.marginLeft = 5;
            var badges = Row();
            foreach (var type in new[] { GeoWorkbenchComponentType.Marker, GeoWorkbenchComponentType.Anchor, GeoWorkbenchComponentType.Orientation, GeoWorkbenchComponentType.Reporter })
                if ((entry.Types & type) != 0) badges.Add(Badge(type.ToString(), Component(type)));
            if (!entry.Object.activeInHierarchy) badges.Add(Badge(GeoEditorLocalization.Tr("Inactive object"), Muted));
            if (entry.Components.OfType<Behaviour>().Any(x => !x.enabled)) badges.Add(Badge(GeoEditorLocalization.Tr("Contains disabled components"), Muted));
            row.Add(badges);
            var line = Row();
            var marker = entry.Marker;
            if (marker != null)
            {
                var selected = new Toggle { value = model.Selected.Contains(marker), name = "capture-" + marker.GetInstanceID(), tooltip = GeoEditorLocalization.Tr("Capture only this Marker into the current Reference's working configuration. Browsing an object does not select it for capture.") };
                selected.SetEnabled(entry.Reference == reference && GeoCalibrationWorkbenchData.IsSceneComponent(reference) && !Application.isPlaying &&
                    !model.PointEdits.replacements.Any(x => x.marker == marker));
                selected.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue && !model.Selected.Contains(marker)) model.Selected.Add(marker);
                    else if (!evt.newValue) model.Selected.Remove(marker);
                    SelectionChanged();
                });
                line.Add(selected);
            }
            var open = new Button(() => EditEntry(entry)) { text = entry.Object.name, tooltip = GeoEditorLocalization.Tr("Inspect this object and its geography components") };
            open.style.flexGrow = 1; open.style.unityTextAlign = TextAnchor.MiddleLeft;
            line.Add(open);
            line.Add(new Button(() => { EditorGUIUtility.PingObject(entry.Object); Selection.activeGameObject = entry.Object; }) { text = GeoEditorLocalization.Tr("Locate") });
            line.Add(new Button(() =>
            {
                Selection.activeGameObject = entry.Object;
                if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            }) { text = GeoEditorLocalization.Tr("Frame"), tooltip = GeoEditorLocalization.Tr("Frame the object in Scene view without moving it.") });
            row.Add(line);
            var path = Text(entry.Path, true); path.style.fontSize = 10; row.Add(path);
            if (marker != null)
            {
                var label = Text(""); row.Add(label); markerLabels[marker] = label;
                AddReplacementAction(row, marker);
            }
            else
            {
                string description = string.Join(" · ", entry.Components.Select(x =>
                    x is GeoAnchor ? GeoEditorLocalization.Tr("Geographic coordinate → world position") : x is GeoOrientation ? GeoEditorLocalization.Tr("Geographic direction → world rotation") : x is GeoPositionReporter ? GeoEditorLocalization.Tr("World position → geographic coordinate") : GeoEditorLocalization.Tr("World rotation → geographic attitude")));
                row.Add(Text(description + GeoEditorLocalization.Tr(" (not a calibration input)"), true));
            }
            parent.Add(row);
        }

        void EditEntry(GeoCalibrationSceneEntry entry)
        {
            if (entry.Object == null) return;
            var next = GeoCalibrationWorkbenchData.IsSceneComponent(entry.Reference) ? entry.Reference : null;
            if (next != reference)
            {
                SetContext(next, GeoCalibrationWorkbenchData.AssignedProfile(next), null);
                if (reference != next) return;
            }
            editingEntry = entry;
            BuildObjectEditor(); ShowPage(1);
        }

        void BuildObjectEditor()
        {
            markerEditor.Unbind(); markerEditor.Clear();
            foreach (var serialized in objectSerialized) serialized.Dispose();
            objectSerialized.Clear(); objectFields.Clear(); reporterTargets.Clear();
            if (editingEntry == null || editingEntry.Object == null)
            {
                markerEditor.Add(Heading(GeoEditorLocalization.Tr("Object details")));
                markerEditor.Add(Text(GeoEditorLocalization.Tr("Click an object name on the left to inspect its components. Frame does not add it to calibration inputs."), true));
                return;
            }
            var entry = editingEntry;
            markerEditor.Add(Heading(entry.Object.name));
            markerEditor.Add(Text(entry.Path, true));
            AddReadOnlyObject(markerEditor, GeoEditorLocalization.Tr("Scene objects"), entry.Object, typeof(GameObject));
            markerEditor.Add(Text(GeoEditorLocalization.Tr("This page edits scene components directly with Undo support. It does not save Profiles or apply bindings automatically."), true));
            foreach (var component in entry.Components.Where(x => x != null))
            {
                var type = GeoCalibrationSceneEntry.TypeOf(component);
                var section = new Foldout { text = component.GetType().Name, value = true };
                Card(section, Component(type));
                var serialized = new SerializedObject(component); objectSerialized.Add(serialized);
                if (GeoInspectorGUI.IsReporter(component)) reporterTargets[component] = GeoInspectorGUI.ReporterTarget(component);
                var iterator = serialized.GetIterator();
                bool enter = true;
                while (iterator.NextVisible(enter))
                {
                    enter = false;
                    if (iterator.name == "m_Script") continue;
                    var field = new PropertyField(iterator.Copy()) { name = "component-" + component.GetInstanceID() + "-" + iterator.name, bindingPath = iterator.propertyPath };
                    objectFields[field] = component;
                    GeoEditorLocalization.LocalizeProperty(field, iterator.Copy());
                    section.Add(field); field.BindProperty(iterator.Copy());
                }
                markerEditor.Add(section);
            }
            var transform = new SerializedObject(entry.Object.transform); objectSerialized.Add(transform);
            var position = new PropertyField(transform.FindProperty("m_LocalPosition"), GeoEditorLocalization.Tr("Transform position (relative to parent)"));
            markerEditor.Add(position); position.BindProperty(transform.FindProperty("m_LocalPosition"));
            markerEditor.SetEnabled(!Application.isPlaying);
            UpdateObjectFieldRules();
        }

        void UpdateObjectFieldRules()
        {
            foreach (var pair in objectFields)
                pair.Key.SetEnabled(!Application.isPlaying && pair.Value != null && GeoInspectorRules.IsEditable(pair.Value, pair.Key.bindingPath));
        }

        void Tick()
        {
            if (model == null || status == null) return;
            var current = GeoCalibrationSceneInventory.Capture();
            if (hierarchyDirty || inventory == null || inventory.Signature != current.Signature)
            {
                inventory = current;
                if (editingEntry != null)
                {
                    editingEntry = inventory.Entries.FirstOrDefault(x => x.Object == editingEntry.Object && x.Reference == editingEntry.Reference);
                    BuildObjectEditor();
                }
                RebuildMarkers();
            }
            RefreshStatus();
        }

        void RefreshStatus()
        {
            if (buildingUI || status == null) return;
            BackupDraft();
            if (pointRowsKey != PointRowsKey()) RebuildMarkers();
            bool valid = model.Build();
            bool ready = model.CandidateIsCurrent();
            string json = GeoCalibrationWorkbenchData.Json(model.Draft);
            if (json != lastDraftJson) { lastDraftJson = json; UpdateFieldRules(); }
            foreach (var pair in markerLabels)
            {
                if (pair.Key == null) { hierarchyDirty = true; continue; }
                var marker = pair.Key;
                var target = marker.reference == reference ? profile : GeoCalibrationWorkbenchData.AssignedProfile(marker.reference);
                var state = GeoCalibrationWorkbenchData.GetMarkerStatus(marker, target);
                pair.Value.text = GeoEditorLocalization.Format("{0} · {1} · weight {2}\n{3}", marker.pointId, marker.role, marker.weight, state.Message);
                pair.Value.style.color = state.IsSynchronized ? Success : Warning;
            }
            var visible = new HashSet<GeoControlPointMarker>(inventory.Entries.Where(x => x.Reference == reference && x.Matches(componentFilter, search)).Select(x => x.Marker).Where(x => x != null));
            int hidden = model.Selected.Count(x => !visible.Contains(x));
            summary.text = GeoEditorLocalization.Format("Capture selection: {0} Markers ({1} hidden by filters); add {2}, update {3}, replace {4}, remove {5}", model.Selected.Count, hidden, model.Added, model.Updated, model.PointEdits.replacements.Count, Math.Max(0, model.Removed.Count - model.PointEdits.replacements.Count)) +
                (model.Removed.Count == 0 ? "" : Environment.NewLine + model.PointChangeDetails());
            var sceneIds = new HashSet<string>(inventory.Entries.Where(x => x.Reference == reference && x.Marker != null).Select(x => x.Marker.pointId));
            var absent = model.Draft == null ? Array.Empty<string>() : (model.Draft.points ?? Array.Empty<GeoControlPointSettings>()).Where(x => x != null && !sceneIds.Contains(x.id)).Select(x => x.id).ToArray();
            profileOnly.text = absent.Length == 0 ? "" : GeoEditorLocalization.Tr("Configuration-only points (inspect, remove, or undo in the left list): ") + string.Join(", ", absent);
            application.text = GeoCalibrationApplicationState.Describe(reference, profile) + (ready ? GeoEditorLocalization.Tr("\nCandidate calculated, not applied.") : GeoEditorLocalization.Tr("\nNo applicable candidate.")) +
                (model.HasDraftEdits ? GeoEditorLocalization.Tr("\nThe working draft has unsaved parameter or point changes.") : "");
            string hint = Application.isPlaying ? GeoEditorLocalization.Tr("Play mode is read-only; no capture or asset writes.") :
                GeoEditorLocalization.Format("Current: {0} · {1} selected Markers ({2} hidden) · merge by ID by default; browsing and filtering do not change capture selection.", (reference == null ? GeoEditorLocalization.Tr("No Reference selected") : reference.name), model.Selected.Count, hidden);
            status.text = (model.Error ?? operationMessage ?? hint).Replace("\n", " ");
            status.tooltip = status.text + "\n" + application.text + "\n" + summary.text;
            status.style.color = !string.IsNullOrEmpty(model.Error) || operationFailed ? Warning : Muted;
            calculate.SetEnabled(valid); save.SetEnabled(valid && profile != null); saveAs.SetEnabled(valid);
            apply.SetEnabled(ready);
            saveApply.SetEnabled(valid && profile != null && (bind.value || GeoCalibrationWorkbenchData.AssignedProfile(reference) == profile));
            bind.SetEnabled(!Application.isPlaying && reference != null);
            replace.SetEnabled(!Application.isPlaying);
            markerEditor.SetEnabled(!Application.isPlaying);
            UpdateObjectFieldRules(); UpdateFieldRules();
        }
    }
}
