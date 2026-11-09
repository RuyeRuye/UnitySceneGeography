using UnityEditor;
using SceneGeography.Editor;

namespace SceneGeography.Samples.Editor
{
    [CustomEditor(typeof(GeoDemoCamera)), CanEditMultipleObjects]
    internal sealed class GeoDemoCameraEditor : GeoReferenceConsumerEditor
    {
    }

    [CustomEditor(typeof(GeoReferenceDemo)), CanEditMultipleObjects]
    internal sealed class GeoReferenceDemoEditor : GeoReferenceConsumerEditor
    {
    }
}
