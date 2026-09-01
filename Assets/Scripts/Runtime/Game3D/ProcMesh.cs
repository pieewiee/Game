using System.Collections.Generic;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// Procedural flat-shaded mesh building — the whole 15-mesh kit from
    /// docs/art-bible.md is boxes and cylinders with vertex colours, so it is
    /// generated in code and no mesh asset exists anywhere in the project.
    /// Vertices are duplicated per face for hard flat shading.
    /// </summary>
    public sealed class ProcMesh
    {
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _tris = new List<int>();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            // Unity is left-handed: with vertices a→b→c→d ordered counter-
            // clockwise as seen from OUTSIDE, the front face needs the
            // REVERSED triangle winding and the cross product flipped —
            // the naive version rendered the whole world inside-out.
            Vector3 n = Vector3.Cross(c - a, b - a).normalized;
            int i = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
            for (int k = 0; k < 4; k++) { _normals.Add(n); _colors.Add(color); }
            _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 1);
            _tris.Add(i); _tris.Add(i + 3); _tris.Add(i + 2);
        }

        /// <summary>Axis-aligned box; centre + full size. Top face slightly lighter,
        /// bottom darker — a cheap fake of sky ambient on flat shading.</summary>
        public void Box(Vector3 centre, Vector3 size, Color color)
        {
            Vector3 h = size * 0.5f;
            Vector3 p = centre;
            var c000 = p + new Vector3(-h.x, -h.y, -h.z);
            var c100 = p + new Vector3(h.x, -h.y, -h.z);
            var c110 = p + new Vector3(h.x, h.y, -h.z);
            var c010 = p + new Vector3(-h.x, h.y, -h.z);
            var c001 = p + new Vector3(-h.x, -h.y, h.z);
            var c101 = p + new Vector3(h.x, -h.y, h.z);
            var c111 = p + new Vector3(h.x, h.y, h.z);
            var c011 = p + new Vector3(-h.x, h.y, h.z);
            Color top = Color.Lerp(color, Color.white, 0.08f);
            Color bottom = Color.Lerp(color, Color.black, 0.25f);
            Quad(c010, c110, c111, c011, top);       // +y
            Quad(c001, c101, c100, c000, bottom);    // -y
            Quad(c000, c100, c110, c010, color);     // -z
            Quad(c101, c001, c011, c111, color);     // +z
            Quad(c100, c101, c111, c110, color);     // +x
            Quad(c001, c000, c010, c011, color);     // -x
        }

        /// <summary>A square-section bar from a to b in any direction — a sloped
        /// handrail. Same shading and winding as Box, in the bar's own frame.</summary>
        public void Beam(Vector3 a, Vector3 b, float t, Color color)
        {
            Vector3 u = (b - a).normalized;
            Vector3 v = Vector3.Cross(Vector3.up, u);
            v = v.sqrMagnitude < 1e-4f ? Vector3.right : v.normalized;
            Vector3 w = Vector3.Cross(u, v);
            float h = t * 0.5f;
            Vector3 c000 = a - v * h - w * h, c100 = a + v * h - w * h;
            Vector3 c110 = a + v * h + w * h, c010 = a - v * h + w * h;
            Vector3 c001 = b - v * h - w * h, c101 = b + v * h - w * h;
            Vector3 c111 = b + v * h + w * h, c011 = b - v * h + w * h;
            Color top = Color.Lerp(color, Color.white, 0.08f);
            Color bottom = Color.Lerp(color, Color.black, 0.25f);
            Quad(c010, c110, c111, c011, top);
            Quad(c001, c101, c100, c000, bottom);
            Quad(c000, c100, c110, c010, color);
            Quad(c101, c001, c011, c111, color);
            Quad(c100, c101, c111, c110, color);
            Quad(c001, c000, c010, c011, color);
        }

        /// <summary>Upright prism/cylinder around centre; low side count keeps it
        /// on-palette low-poly (turbine tower 6, tree cone 5...).</summary>
        public void Cylinder(Vector3 centre, float radius, float height, int sides,
            Color color, float topRadius = -1f)
        {
            if (topRadius < 0f) topRadius = radius;
            float y0 = centre.y - height * 0.5f;
            float y1 = centre.y + height * 0.5f;
            for (int s = 0; s < sides; s++)
            {
                float a0 = Mathf.PI * 2f * s / sides;
                float a1 = Mathf.PI * 2f * (s + 1) / sides;
                Vector3 b0 = centre + new Vector3(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius); b0.y = y0;
                Vector3 b1 = centre + new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius); b1.y = y0;
                Vector3 t0 = centre + new Vector3(Mathf.Cos(a0) * topRadius, 0, Mathf.Sin(a0) * topRadius); t0.y = y1;
                Vector3 t1 = centre + new Vector3(Mathf.Cos(a1) * topRadius, 0, Mathf.Sin(a1) * topRadius); t1.y = y1;
                Quad(b0, b1, t1, t0, color);
            }
            // caps (fan)
            Color top = Color.Lerp(color, Color.white, 0.08f);
            for (int s = 1; s < sides - 1; s++)
            {
                float a0 = Mathf.PI * 2f * s / sides;
                float a1 = Mathf.PI * 2f * (s + 1) / sides;
                Vector3 p0 = centre + new Vector3(Mathf.Cos(0) * topRadius, 0, Mathf.Sin(0) * topRadius); p0.y = y1;
                Vector3 p1 = centre + new Vector3(Mathf.Cos(a0) * topRadius, 0, Mathf.Sin(a0) * topRadius); p1.y = y1;
                Vector3 p2 = centre + new Vector3(Mathf.Cos(a1) * topRadius, 0, Mathf.Sin(a1) * topRadius); p2.y = y1;
                int i = _verts.Count;
                _verts.Add(p0); _verts.Add(p2); _verts.Add(p1);
                for (int k = 0; k < 3; k++) { _normals.Add(Vector3.up); _colors.Add(top); }
                _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
            }
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh();
            mesh.name = name;
            if (_verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_verts);
            mesh.SetNormals(_normals);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>The single opaque + single transparent material, and GameObject
    /// assembly helpers.</summary>
    public static class MatLib
    {
        private static Material _opaque;
        private static Material _transparent;

        public static Material Opaque
        {
            get
            {
                if (_opaque == null) _opaque = new Material(Shader.Find("GNP/VertexColor"));
                return _opaque;
            }
        }

        public static Material Transparent
        {
            get
            {
                if (_transparent == null) _transparent = new Material(Shader.Find("GNP/VertexColorTransparent"));
                return _transparent;
            }
        }

        public static GameObject Spawn(string name, Mesh mesh, Transform parent,
            Vector3 localPos, bool collider = true, bool transparent = false)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = transparent ? Transparent : Opaque;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (collider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }
            return go;
        }
    }
}
