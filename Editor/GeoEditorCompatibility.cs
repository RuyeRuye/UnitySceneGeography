using UnityEngine;

namespace SceneGeography.Editor
{
    // Keep version-specific object discovery in one place; 2021.3 remains supported.
    internal static class GeoEditorCompatibility
    {
        internal static T[] FindSceneObjects<T>(bool includeInactive = true) where T : Object
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>(includeInactive);
#endif
        }
    }
}
