using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SceneGeography.Editor
{
    internal static class GeoCalibrationWorkbenchStyle
    {
        internal static Color Reference => EditorGUIUtility.isProSkin ? new Color(.32f, .68f, .94f) : new Color(.12f, .36f, .65f);
        internal static Color Muted => EditorGUIUtility.isProSkin ? new Color(.70f, .73f, .77f) : new Color(.32f, .35f, .39f);
        internal static Color Warning => EditorGUIUtility.isProSkin ? new Color(.98f, .74f, .36f) : new Color(.57f, .32f, .04f);
        internal static Color Danger => EditorGUIUtility.isProSkin ? new Color(1f, .48f, .45f) : new Color(.70f, .14f, .12f);
        internal static Color Success => EditorGUIUtility.isProSkin ? new Color(.48f, .82f, .61f) : new Color(.13f, .42f, .25f);

        internal static Color Component(GeoWorkbenchComponentType type)
        {
            bool dark = EditorGUIUtility.isProSkin;
            switch (type)
            {
                case GeoWorkbenchComponentType.Marker: return Warning;
                case GeoWorkbenchComponentType.Anchor: return dark ? new Color(.35f, .81f, .85f) : new Color(.03f, .40f, .45f);
                case GeoWorkbenchComponentType.Orientation: return dark ? new Color(.77f, .64f, .97f) : new Color(.42f, .25f, .65f);
                default: return Success;
            }
        }

        internal static void Card(VisualElement element, Color accent, bool raised = false)
        {
            element.style.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(raised ? .22f : .17f, raised ? .23f : .18f, raised ? .25f : .20f)
                : new Color(raised ? .91f : .81f, raised ? .93f : .83f, raised ? .96f : .86f);
            element.style.borderLeftColor = accent;
            element.style.borderLeftWidth = 3;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = 4;
            element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = 4;
            element.style.paddingLeft = element.style.paddingRight = 8;
            element.style.paddingTop = element.style.paddingBottom = 7;
            element.style.marginTop = element.style.marginBottom = 4;
        }

        internal static Label Text(string text, bool muted = false)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            if (muted) label.style.color = Muted;
            return label;
        }

        internal static Label Badge(string text, Color color)
        {
            var label = new Label(text);
            label.style.color = color;
            label.style.backgroundColor = new Color(color.r, color.g, color.b, .13f);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 11;
            label.style.paddingLeft = label.style.paddingRight = 5;
            label.style.paddingTop = label.style.paddingBottom = 2;
            label.style.marginRight = 5;
            label.style.marginBottom = 3;
            label.style.borderTopLeftRadius = label.style.borderTopRightRadius = 3;
            label.style.borderBottomLeftRadius = label.style.borderBottomRightRadius = 3;
            return label;
        }

        internal static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            return row;
        }

        internal static Label Heading(string text)
        {
            var label = Text(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 13;
            label.style.marginTop = 8;
            label.style.marginBottom = 5;
            return label;
        }
    }
}
