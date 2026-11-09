using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SceneGeography.Editor
{
    [Flags]
    internal enum GeoWorkbenchComponentType
    {
        None = 0, Marker = 1, Anchor = 2, Orientation = 4, Reporter = 8,
        All = Marker | Anchor | Orientation | Reporter
    }

    internal sealed class GeoCalibrationSceneEntry
    {
        internal GameObject Object { get; }
        internal GeoReference Reference { get; }
        internal Component[] Components { get; }
        internal GeoControlPointMarker Marker => Components.OfType<GeoControlPointMarker>().FirstOrDefault();
        internal GeoWorkbenchComponentType Types { get; }
        internal string Path => HierarchyPath(Object.transform);

        internal GeoCalibrationSceneEntry(GameObject value, GeoReference reference, Component[] components)
        {
            Object = value; Reference = reference;
            Components = components.OrderBy(x => (int)TypeOf(x)).ThenBy(x => x.GetInstanceID()).ToArray();
            foreach (var component in components) Types |= TypeOf(component);
        }

        internal bool Matches(GeoWorkbenchComponentType types, string search)
        {
            if ((Types & types) == 0) return false;
            if (string.IsNullOrWhiteSpace(search)) return true;
            string text = Path + " " + (Reference == null ? "" : Reference.name) + " " +
                (Marker == null ? "" : Marker.pointId) + " " + Types;
            return text.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static GeoWorkbenchComponentType TypeOf(Component component)
        {
            if (component is GeoControlPointMarker) return GeoWorkbenchComponentType.Marker;
            if (component is GeoAnchor) return GeoWorkbenchComponentType.Anchor;
            if (component is GeoOrientation) return GeoWorkbenchComponentType.Orientation;
            if (GeoInspectorGUI.IsReporter(component)) return GeoWorkbenchComponentType.Reporter;
            return GeoWorkbenchComponentType.None;
        }

        internal static string HierarchyPath(Transform value) => value.parent == null ? value.name : HierarchyPath(value.parent) + "/" + value.name;
    }

    internal sealed class GeoCalibrationSceneInventory
    {
        internal Scene[] Scenes { get; private set; }
        internal GeoReference[] References { get; private set; }
        internal GeoCalibrationSceneEntry[] Entries { get; private set; }
        internal string Signature { get; private set; }

        internal static GeoCalibrationSceneInventory Capture()
        {
            var scenes = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene)) scenes.Add(scene);
            }
            var references = GeoEditorCompatibility.FindSceneObjects<GeoReference>()
                .Where(GeoCalibrationWorkbenchData.IsSceneComponent).OrderBy(x => scenes.IndexOf(x.gameObject.scene))
                .ThenBy(x => GeoCalibrationSceneEntry.HierarchyPath(x.transform), StringComparer.Ordinal).ToArray();
            var components = GeoEditorCompatibility.FindSceneObjects<GeoControlPointMarker>().Cast<Component>()
                .Concat(GeoEditorCompatibility.FindSceneObjects<GeoBindingBehaviour>()
                    .Where(x => GeoCalibrationSceneEntry.TypeOf(x) != GeoWorkbenchComponentType.None))
                .Where(GeoCalibrationWorkbenchData.IsSceneComponent);
            // 同对象、同 Reference 的组件合为一条；跨 Reference 的组件分别归组，不推断父子关系。
            var entries = components.GroupBy(x => new { x.gameObject, reference = Owner(x) })
                .Select(x => new GeoCalibrationSceneEntry(x.Key.gameObject, x.Key.reference, x.ToArray()))
                .OrderBy(x => scenes.IndexOf(x.Object.scene)).ThenBy(x => x.Path, StringComparer.Ordinal).ToArray();
            return new GeoCalibrationSceneInventory
            {
                Scenes = scenes.ToArray(), References = references, Entries = entries,
                Signature = string.Join("|", scenes.Select(x => x.handle + ":" + x.name + ":" + x.path)) + ";" +
                    string.Join("|", references.Select(x => x.GetInstanceID() + ":" + GeoCalibrationSceneEntry.HierarchyPath(x.transform) + ":" +
                        (GeoCalibrationWorkbenchData.AssignedProfile(x) == null ? 0 : GeoCalibrationWorkbenchData.AssignedProfile(x).GetInstanceID()))) + ";" +
                    string.Join("|", entries.Select(x => x.Object.GetInstanceID() + ":" + x.Path + ":" + x.Object.activeInHierarchy + ":" +
                        (x.Reference == null ? 0 : x.Reference.GetInstanceID()) + ":" + (x.Marker == null ? "" : x.Marker.pointId + "/" + x.Marker.role) + ":" +
                        string.Join(",", x.Components.Select(c => c.GetInstanceID() + "/" + ((Behaviour)c).enabled))))
            };
        }

        static GeoReference Owner(Component component) => component is GeoControlPointMarker marker ? marker.reference : ((GeoBindingBehaviour)component).Reference;
    }
}
