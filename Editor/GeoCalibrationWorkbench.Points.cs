using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static SceneGeography.Editor.GeoCalibrationWorkbenchStyle;

namespace SceneGeography.Editor
{
    public sealed partial class GeoCalibrationWorkbench
    {
        [SerializeField] GeoPointEdits pointEditsBackup;
        string pointRowsKey;

        GeoCalibrationProfile WorkingProfile(GeoReference owner) => owner == reference
            ? model.Draft : GeoCalibrationWorkbenchData.AssignedProfile(owner);

        IEnumerable<GeoControlPointSettings> UnmatchedPoints(GeoReference owner)
        {
            var working = WorkingProfile(owner);
            var sceneIds = new HashSet<string>(inventory.Entries.Where(x => x.Reference == owner)
                .SelectMany(x => x.Components).OfType<GeoControlPointMarker>().Select(x => x.pointId), StringComparer.Ordinal);
            return (working == null ? Array.Empty<GeoControlPointSettings>() : working.points ?? Array.Empty<GeoControlPointSettings>())
                .Where(x => x != null && (!sceneIds.Contains(x.id) || owner == reference &&
                    (model.PointEdits.removals.Contains(x.id) || model.PointEdits.replacements.Any(r => r.oldId == x.id))));
        }

        string PointRowsKey() => JsonUtility.ToJson(model.PointEdits) + ";" +
            string.Join(";", inventory.References.Select(x => x.GetInstanceID() + ":" + GeoCalibrationWorkbenchData.Json(WorkingProfile(x))));

        int AddProfileRows(VisualElement parent, GeoReference owner)
        {
            if ((componentFilter & GeoWorkbenchComponentType.Marker) == 0)
            {
                return 0;
            }

            int count = 0;
            foreach (var point in UnmatchedPoints(owner))
            {
                bool current = owner == reference;
                var replacement = current ? model.PointEdits.replacements.FirstOrDefault(x => x.oldId == point.id) : null;
                bool removed = current && model.PointEdits.removals.Contains(point.id);
                string state = replacement != null ? GeoEditorLocalization.Tr("Pending replacement: ") + replacement.oldId + " → " + replacement.newId :
                    removed ? GeoEditorLocalization.Tr("Pending removal") : GeoEditorLocalization.Tr("Configuration only: no scene Marker found");
                string text = owner.name + " " + point.id + " " + point.role + " " + state;
                if (!string.IsNullOrWhiteSpace(search) && text.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var row = new VisualElement { name = "profile-point-" + owner.GetInstanceID() + "-" + point.id };
                Card(row, removed ? Danger : Warning, true);
                row.style.marginLeft = 5;
                row.Add(Badge(GeoEditorLocalization.Tr("Configuration point"), Muted));
                row.Add(Text(point.id + " · " + point.role + GeoEditorLocalization.Tr(" · weight ") + point.weight));
                row.Add(Text(state, true));
                var details = new Foldout { text = GeoEditorLocalization.Tr("Inspect configuration values"), value = false };
                details.Add(Text(GeoEditorLocalization.Format("Root-local position: {0:G17}, {1:G17}, {2:G17}", point.localPosition.x, point.localPosition.y, point.localPosition.z), true));
                var coordinate = point.coordinate;
                details.Add(Text(coordinate == null ? GeoEditorLocalization.Tr("Invalid geographic coordinate") :
                    GeoEditorLocalization.Format("{0} · {1}\nLatitude {2:G17} · longitude {3:G17} · height {4:G17} m", coordinate.horizontalType, coordinate.heightKind, coordinate.latitudeDegrees, coordinate.longitudeDegrees, coordinate.heightMeters), true));
                row.Add(details);
                row.Add(Text(GeoEditorLocalization.Tr("Matches currently loaded scenes only. Absence does not imply deletion; points are never cleaned up automatically."), true));
                var button = new Button(() =>
                {
                    if (replacement != null) model.UndoReplacement(point.id);
                    else if (removed) model.UndoRemoval(point.id);
                    else if (!model.StageRemoval(point.id)) { Run(() => false); return; }
                    PointEditChanged();
                })
                {
                    text = replacement != null ? GeoEditorLocalization.Tr("Undo replacement") : removed ? GeoEditorLocalization.Tr("Undo removal") : GeoEditorLocalization.Tr("Remove from configuration"),
                    name = "edit-profile-point-" + owner.GetInstanceID() + "-" + point.id,
                    tooltip = current ? GeoEditorLocalization.Tr("Edit the working draft only; the Profile changes when saved. No scene objects are deleted.") : GeoEditorLocalization.Tr("Click Edit calibration for this Reference before maintaining its configuration points.")
                };
                button.SetEnabled(current && !Application.isPlaying);
                if (!removed && replacement == null) button.style.color = Danger;
                row.Add(button);
                parent.Add(row);
                count++;
            }

            return count;
        }

        void AddReplacementAction(VisualElement parent, GeoControlPointMarker marker)
        {
            if (marker.reference != reference || model.Draft == null)
            {
                return;
            }

            var edit = model.PointEdits.replacements.FirstOrDefault(x => x.marker == marker);
            if (edit != null)
            {
                parent.Add(Text(GeoEditorLocalization.Tr("Pending replacement: ") + edit.oldId + " → " + edit.newId, true));
                var undo = new Button(() => { model.UndoReplacement(edit.oldId); PointEditChanged(); }) { text = GeoEditorLocalization.Tr("Undo replacement") };
                undo.SetEnabled(!Application.isPlaying);
                parent.Add(undo);
                return;
            }

            if ((model.Draft.points ?? Array.Empty<GeoControlPointSettings>()).Any(x => x != null && x.id == marker.pointId))
            {
                return;
            }

            var choices = UnmatchedPoints(reference).Where(x => !model.PointEdits.removals.Contains(x.id) &&
                !model.PointEdits.replacements.Any(r => r.oldId == x.id)).ToArray();
            var button = new Button(() =>
            {
                var menu = new GenericMenu();
                foreach (var point in choices)
                {
                    string oldId = point.id;
                    menu.AddItem(new GUIContent(oldId.Replace("/", "／") + " → " + marker.pointId.Replace("/", "／")), false, () =>
                    {
                        if (model.StageReplacement(oldId, marker)) PointEditChanged();
                        else Run(() => false);
                    });
                }
                menu.ShowAsContext();
            })
            {
                text = GeoEditorLocalization.Tr("Replace existing configuration point…"),
                name = "replace-point-" + marker.GetInstanceID(),
                tooltip = GeoEditorLocalization.Tr("Explicitly select the old ID. One draft operation removes the old point and captures this Marker; renames are not inferred automatically.")
            };
            button.SetEnabled(!Application.isPlaying && choices.Length > 0);
            parent.Add(button);
        }

        void PointEditChanged()
        {
            selection = model.Selected.ToList();
            operationMessage = GeoEditorLocalization.Tr("Control point operation staged in the draft. The Profile is not saved and calibration is not applied.");
            operationFailed = false;
            RebuildMarkers();
            RefreshStatus();
        }
    }
}
