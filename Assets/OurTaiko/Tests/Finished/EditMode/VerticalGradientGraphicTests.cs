using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Tests
{
    // The textureless gradient behind the practice progress bar's fill: an edge at every key, coloured
    // from the gradient, top (time 0) at the rect's top.
    public sealed class VerticalGradientGraphicTests
    {
        [Test]
        public void EachKeyIsAnEdgeColouredFromTheGradientTopToBottom()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.red, 0), new GradientColorKey(Color.white, .25f), new GradientColorKey(Color.black, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            var mesh = new VertexHelper();
            VerticalGradientGraphic.Fill(mesh, new Rect(10, -20, 100, 40), gradient, Color.white);

            var vertices = new List<UIVertex>();
            mesh.GetUIVertexStream(vertices);
            Assert.That(mesh.currentVertCount, Is.EqualTo(6), "Three edges, two vertices each.");
            Assert.That(vertices, Has.Count.EqualTo(12), "Two bands of two triangles.");
            // Gradient keeps key times to 1/65535, so an edge can sit a hair off its exact place.
            var edges = vertices.GroupBy(v => Mathf.Round(v.position.y * 100) / 100).OrderByDescending(e => e.Key)
                .Select(e => (y: e.Key, color: e.First().color, xs: e.Select(v => v.position.x).Distinct().OrderBy(x => x).ToArray())).ToArray();
            Assert.That(edges.Select(e => e.y), Is.EqualTo(new[] { 20f, 10f, -20f }).Within(.01f));
            Assert.That(edges.Select(e => e.color), Is.EqualTo(new Color32[] { Color.red, Color.white, Color.black }));
            foreach (var edge in edges) Assert.That(edge.xs, Is.EqualTo(new[] { 10f, 110f }), "Each edge spans the rect.");
            mesh.Dispose();
        }

        [Test]
        public void TheGraphicsColourTintsTheGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            var mesh = new VertexHelper();
            VerticalGradientGraphic.Fill(mesh, new Rect(0, 0, 10, 10), gradient, new Color(1, 0, 0, .5f));
            var vertex = new UIVertex();
            mesh.PopulateUIVertex(ref vertex, 0);
            Assert.That(vertex.color, Is.EqualTo(new Color32(255, 0, 0, 128)));
            mesh.Dispose();
        }
    }
}
