using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SceneGeography.Editor;
using Object = UnityEngine.Object;

namespace SceneGeography.Samples.Tests
{
    public sealed class GeoSampleInspectorTests
    {
        [TestCase(typeof(GeoDemoCamera))]
        [TestCase(typeof(GeoReferenceDemo))]
        public void SampleInspectorsProvideReferenceAssignment(Type type)
        {
            var host = new GameObject("Sample consumer");
            UnityEditor.Editor inspector = null;
            try
            {
                var component = host.AddComponent(type);
                inspector = UnityEditor.Editor.CreateEditor(component);
                Assert.That(inspector, Is.InstanceOf<GeoReferenceConsumerEditor>());
                Assert.That(new SerializedObject(component).FindProperty("reference"), Is.Not.Null);
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(typeof(GeoDemoCamera))]
        [TestCase(typeof(GeoReferenceDemo))]
        public void SerializedSampleFieldsHaveTooltips(Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                bool serialized = !field.IsInitOnly && !field.IsLiteral &&
                    (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null);
                if (serialized && field.GetCustomAttribute<HideInInspector>() == null)
                {
                    Assert.That(field.GetCustomAttribute<TooltipAttribute>()?.tooltip, Is.Not.Null.And.Not.Empty, type.Name + "." + field.Name);
                }
            }
        }
    }
}
