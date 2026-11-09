using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneGeography.Editor
{
    internal sealed class GeoAutoFillResult
    {
        internal int ReferencesFilled;
        internal int PointIdsFilled;
        internal readonly List<string> Warnings = new List<string>();
        internal string Summary =>
            GeoEditorLocalization.Format("Assigned {0} References and {1} Point IDs. Existing values were preserved.", ReferencesFilled, PointIdsFilled) +
            (Warnings.Count == 0 ? string.Empty : "\n" + string.Join("\n", Warnings));
    }

    internal static class GeoInspectorAutoFill
    {
        internal static void DrawButton(Object[] targets, bool fillPointIds, ref GeoAutoFillResult result)
        {
            string label = fillPointIds ? GeoEditorLocalization.Tr("Auto Fill Reference & Point ID") : GeoEditorLocalization.Tr("Auto Assign Reference");
            string tooltip = GeoEditorLocalization.Tr("Fill an empty reference only when the component's own scene has exactly one GeoReference, including inactive References. Never choose across scenes or overwrite existing references.");
            if (fillPointIds)
            {
                tooltip += GeoEditorLocalization.Tr("Empty Point IDs use object names. Multiple selections are handled individually; duplicate names must be made unique manually.");
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button(new GUIContent(label, tooltip)))
                {
                    result = FillMissing(targets, fillPointIds);
                }
            }

            if (result != null)
            {
                EditorGUILayout.HelpBox(result.Summary, result.Warnings.Count == 0 ? MessageType.Info : MessageType.Warning);
            }
        }

        internal static GeoAutoFillResult FillMissing(Object[] targets, bool fillPointIds)
        {
            var result = new GeoAutoFillResult();
            if (Application.isPlaying)
            {
                result.Warnings.Add(GeoEditorLocalization.Tr("Auto-fill is available only in Edit mode. At runtime, use Configure or another configuration API."));
                return result;
            }

            var referencesByScene = new Dictionary<int, List<GeoReference>>();
            var filledMarkers = new List<GeoControlPointMarker>();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fill missing geographic configuration");
            try
            {
                foreach (var target in targets)
                {
                    if (!(target is Component component) || component == null || EditorUtility.IsPersistent(component))
                    {
                        result.Warnings.Add(GeoEditorLocalization.Tr("Only components in loaded scenes are supported. Prefab assets cannot be bound automatically."));
                        continue;
                    }

                    var scene = component.gameObject.scene;
                    if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene) ||
                        PrefabStageUtility.GetPrefabStage(component.gameObject) != null)
                    {
                        result.Warnings.Add(component.name + GeoEditorLocalization.Tr(": References are not assigned automatically in preview scenes or Prefab stages."));
                        continue;
                    }

                    var serialized = new SerializedObject(component);
                    var reference = serialized.FindProperty("reference");
                    if (reference != null && reference.objectReferenceValue == null)
                    {
                        if (!referencesByScene.TryGetValue(scene.handle, out var candidates))
                        {
                            candidates = FindReferences(scene);
                            referencesByScene.Add(scene.handle, candidates);
                        }

                        if (candidates.Count == 1)
                        {
                            reference.objectReferenceValue = candidates[0];
                            result.ReferencesFilled++;
                        }
                        else
                        {
                            result.Warnings.Add(component.name + GeoEditorLocalization.Tr(": the object's scene has ") + candidates.Count + GeoEditorLocalization.Tr(" GeoReferences; select one manually."));
                        }
                    }

                    if (fillPointIds && component is GeoControlPointMarker marker)
                    {
                        var id = serialized.FindProperty("pointId");
                        if (string.IsNullOrWhiteSpace(id.stringValue))
                        {
                            if (string.IsNullOrWhiteSpace(component.name))
                            {
                                result.Warnings.Add(GeoEditorLocalization.Tr("The object name is empty. Name it before using its name as a Point ID."));
                            }
                            else
                            {
                                id.stringValue = component.name;
                                result.PointIdsFilled++;
                                filledMarkers.Add(marker);
                            }
                        }
                    }

                    if (serialized.ApplyModifiedProperties())
                    {
                        EditorUtility.SetDirty(component);
                        EditorSceneManager.MarkSceneDirty(scene);
                    }
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            foreach (var marker in filledMarkers)
            {
                int matches = 0;
                foreach (var root in marker.gameObject.scene.GetRootGameObjects())
                {
                    foreach (var other in root.GetComponentsInChildren<GeoControlPointMarker>(true))
                    {
                        if (other.pointId == marker.pointId && other.reference == marker.reference)
                        {
                            matches++;
                        }
                    }
                }

                if (matches > 1)
                {
                    result.Warnings.Add(marker.name + "：Point Id '" + marker.pointId + GeoEditorLocalization.Tr("' duplicates another Marker. Assign a unique name manually."));
                }
            }

            return result;
        }

        static List<GeoReference> FindReferences(Scene scene)
        {
            var result = new List<GeoReference>();
            foreach (var root in scene.GetRootGameObjects())
            {
                result.AddRange(root.GetComponentsInChildren<GeoReference>(true));
            }

            return result;
        }
    }
}
