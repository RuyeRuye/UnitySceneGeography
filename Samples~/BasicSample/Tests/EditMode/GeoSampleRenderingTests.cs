using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SceneGeography.Samples.Tests
{
    public sealed class GeoSampleRenderingTests
    {
        [Test]
        public void SampleShaderIsAnIncludedResourceAndRendersItsColor()
        {
            var shader = Resources.Load<Shader>("SceneGeographySample/Unlit");
            Assert.That(shader, Is.Not.Null, "Import the complete Basic Sample, including Resources.");
            Assert.That(shader.name, Is.EqualTo("SceneGeography/Samples/Unlit"));
            Assert.That(shader.isSupported, Is.True);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            var material = new Material(shader) { color = Color.green };
            var target = RenderTexture.GetTemporary(8, 8, 0);
            var pixels = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(Texture2D.whiteTexture, target, material);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                var color = pixels.GetPixel(4, 4);
                Assert.That(color.r, Is.LessThan(.02f));
                Assert.That(color.g, Is.GreaterThan(.98f));
                Assert.That(color.b, Is.LessThan(.02f));
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(pixels);
            }
        }
    }
}
