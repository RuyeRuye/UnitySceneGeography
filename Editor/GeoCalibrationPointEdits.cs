using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SceneGeography.Editor
{
    [Serializable]
    internal sealed class GeoPointReplacement
    {
        [SerializeField] internal string oldId;
        [SerializeField] internal string newId;
        [SerializeField] internal GeoControlPointMarker marker;
        [SerializeField] internal bool addedSelection;

        internal GeoPointReplacement Copy() => new GeoPointReplacement
        {
            oldId = oldId,
            newId = newId,
            marker = marker,
            addedSelection = addedSelection
        };
    }

    [Serializable]
    internal sealed class GeoPointEdits
    {
        [SerializeField] internal List<string> removals = new List<string>();
        [SerializeField] internal List<GeoPointReplacement> replacements = new List<GeoPointReplacement>();

        internal bool HasChanges => removals.Count > 0 || replacements.Count > 0;

        internal GeoPointEdits Copy() => new GeoPointEdits
        {
            removals = new List<string>(removals),
            replacements = replacements.Select(x => x.Copy()).ToList()
        };
    }

    internal sealed partial class GeoCalibrationWorkbenchModel
    {
        internal GeoPointEdits PointEdits { get; private set; } = new GeoPointEdits();

        internal void RestorePointEdits(GeoPointEdits edits)
        {
            PointEdits = edits == null ? new GeoPointEdits() : edits.Copy();
            Invalidate();
        }

        internal string PointChangeDetails()
        {
            var replaced = new HashSet<string>(PointEdits.replacements.Select(x => x.oldId), StringComparer.Ordinal);
            var lines = PointEdits.replacements.Select(x => GeoEditorLocalization.Tr("Replace: ") + x.oldId + " → " + x.newId)
                .Concat(Removed.Where(x => !replaced.Contains(x)).Select(x => GeoEditorLocalization.Tr("Remove: ") + x));
            return string.Join(Environment.NewLine, lines);
        }

        internal bool StageRemoval(string id)
        {
            if (Application.isPlaying || Draft == null || !(Draft.points ?? Array.Empty<GeoControlPointSettings>()).Any(x => x != null && x.id == id))
            {
                return Fail(GeoEditorLocalization.Tr("Only points in the current working configuration can be removed."));
            }

            if (PointEdits.replacements.Any(x => x.oldId == id))
            {
                return Fail(GeoEditorLocalization.Tr("Undo this point's replacement first."));
            }

            if (!PointEdits.removals.Contains(id))
            {
                PointEdits.removals.Add(id);
            }

            Invalidate();
            return true;
        }

        internal void UndoRemoval(string id)
        {
            PointEdits.removals.Remove(id);
            Invalidate();
        }

        internal bool StageReplacement(string id, GeoControlPointMarker marker)
        {
            if (Application.isPlaying || Draft == null ||
                !(Draft.points ?? Array.Empty<GeoControlPointSettings>()).Any(x => x != null && x.id == id) ||
                !GeoCalibrationWorkbenchData.IsSceneComponent(marker) || marker.reference != Reference ||
                string.IsNullOrWhiteSpace(marker.pointId))
            {
                return Fail(GeoEditorLocalization.Tr("Select a valid Marker in the current Reference and an existing configuration point."));
            }

            if (GeoCalibrationWorkbenchData.Markers().Any(x => x.reference == Reference && x.pointId == id))
            {
                return Fail(GeoEditorLocalization.Tr("The old ID still has a scene Marker. Check the objects; two scene points are never merged automatically."));
            }

            if ((Draft.points ?? Array.Empty<GeoControlPointSettings>()).Any(x => x != null && x.id == marker.pointId) ||
                PointEdits.removals.Contains(id) ||
                PointEdits.replacements.Any(x => x.oldId == id || x.marker == marker || x.newId == marker.pointId))
            {
                return Fail(GeoEditorLocalization.Tr("The replacement conflicts with an existing ID or pending operation. Undo the conflicting operation first."));
            }

            PointEdits.replacements.Add(new GeoPointReplacement
            {
                oldId = id,
                newId = marker.pointId,
                marker = marker,
                addedSelection = !Selected.Contains(marker)
            });
            if (!Selected.Contains(marker))
            {
                Selected.Add(marker);
            }

            Invalidate();
            return true;
        }

        internal void UndoReplacement(string id)
        {
            var edit = PointEdits.replacements.FirstOrDefault(x => x.oldId == id);
            if (edit == null)
            {
                return;
            }

            if (edit.addedSelection)
            {
                Selected.Remove(edit.marker);
            }

            PointEdits.replacements.Remove(edit);
            Invalidate();
        }

        // Revalidate recorded identities: renamed/deleted/rebound targets must not silently change intent.
        bool ValidatePointEdits(GeoControlPointSettings[] original, out HashSet<string> removed)
        {
            removed = new HashSet<string>(PointEdits.removals, StringComparer.Ordinal);
            var ids = new HashSet<string>(original.Select(x => x.id), StringComparer.Ordinal);
            if (removed.Count != PointEdits.removals.Count || removed.Any(x => !ids.Contains(x)))
            {
                return Fail(GeoEditorLocalization.Tr("A pending removal ID no longer exists in the working configuration. Undo it and select again."));
            }

            var destinations = new HashSet<string>(StringComparer.Ordinal);
            var sceneMarkers = GeoCalibrationWorkbenchData.Markers();
            foreach (var edit in PointEdits.replacements)
            {
                if (!ids.Contains(edit.oldId) || !removed.Add(edit.oldId) ||
                    ids.Contains(edit.newId) || !destinations.Add(edit.newId) ||
                    !GeoCalibrationWorkbenchData.IsSceneComponent(edit.marker) ||
                    edit.marker.reference != Reference || edit.marker.pointId != edit.newId ||
                    !Selected.Contains(edit.marker) ||
                    sceneMarkers.Any(x => x.reference == Reference && x.pointId == edit.oldId))
                {
                    return Fail(GeoEditorLocalization.Tr("Invalid or conflicting replacement: ") + edit.oldId + " → " + edit.newId + GeoEditorLocalization.Tr(". Undo the replacement and select again."));
                }
            }

            return true;
        }
    }
}
