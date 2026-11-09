using UnityEditor;

namespace SceneGeography.Editor
{
    /// <summary>Base Inspector for components with a serialized field named reference of type GeoReference.</summary>
    /// <remarks>Draws the default fields and an explicit scene-local reference assignment button.</remarks>
    public abstract class GeoReferenceConsumerEditor : UnityEditor.Editor
    {
        GeoAutoFillResult autoFillResult;

        public override void OnInspectorGUI()
        {
            GeoInspectorGUI.DrawFields(serializedObject);
            GeoInspectorAutoFill.DrawButton(targets, false, ref autoFillResult);
        }
    }
}
