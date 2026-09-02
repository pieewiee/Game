using System.Collections.Generic;
using System.IO;
using Game.Runtime.World;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Import rules for everything under Assets/ThirdParty (art-bible.md:
    /// no textures, no UV unwrapping, one vertex-colour material).
    ///
    /// Kenney-style kits carry no vertex colours: their UVs point into a
    /// flat-swatch colormap.png. At import each mesh's UVs are baked into
    /// vertex colours, quantised to Palette.Town (the palette minus the
    /// three colours that carry meaning), and the UVs are dropped. The
    /// model is scaled from the kit's 1-unit grid to metres, materials are
    /// not imported at all, and the mesh stays readable so the town can be
    /// statically batched at runtime.
    ///
    /// Bump GetVersion after changing the bake: other machines keep stale
    /// vertex colours in their Library until the importer version moves.
    /// </summary>
    public sealed class KitImport : AssetPostprocessor
    {
        private const string Root = "Assets/ThirdParty/";
        private const string KitAssetPath = "Assets/Settings/Resources/" + AssetKit.ResourceName + ".asset";

        public override uint GetVersion() { return 5; }

        /// <summary>The kits' window glass is the one saturated light-blue
        /// swatch column (#D0E8FF and its gradient). It is tagged in the
        /// vertex ALPHA (0 = glass, 1 = anything else) — the opaque shader
        /// ignores alpha, and the town's window pass copies exactly those
        /// triangles as night glow. Pale trims quantise to the same palette
        /// entry, so the palette colour alone could not tell them apart.</summary>
        private static bool IsGlass(Color src)
        {
            return src.b > 0.9f && src.r < 0.9f && src.b - src.r > 0.12f;
        }

        /// <summary>Kit grid unit → metres, read off the bounds KitImport logs
        /// on import. City kits: a road tile is exactly 1 unit and the town's
        /// carriageway is 7 m, so ×8 (a house then spans 10 × 8 m, two
        /// storeys). The car kit is authored larger and chunkier: a sedan is
        /// 2.55 × 1.5 units, and ×1.6 lands it at 4.1 m long and 2.4 m wide,
        /// which is what the 7 m carriageway and the visitor bays allow.</summary>
        private static float KitScale(string path)
        {
            if (path.Contains("/city-kit-")) return 8f;
            if (path.Contains("/car-kit/")) return 1.6f;
            // A Blocky Character stands 2.70 units to the crown of its
            // oversized head; ×0.65 makes that a 1.75 m adult, the same
            // height as the procedural figures it replaces.
            if (path.Contains("/blocky-characters/")) return 0.65f;
            return 1f;
        }

        /// <summary>Blocky Characters are the one animated kit: six rigid
        /// parts (root, torso, head, two arms, two legs) with authored takes
        /// — idle, walk, sit, interact and more. They import with a Legacy
        /// rig, which needs no AnimatorController asset: the runtime just
        /// plays a clip by name on the model's Animation component.</summary>
        private static bool IsCharacter(string path)
        {
            return path.Contains("/blocky-characters/");
        }

        private static bool IsKit(string path)
        {
            return path.StartsWith(Root, System.StringComparison.Ordinal);
        }

        private void OnPreprocessModel()
        {
            if (!IsKit(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importConstraints = false;
            bool character = IsCharacter(assetPath);
            mi.importAnimation = character;
            mi.animationType = character ? ModelImporterAnimationType.Legacy : ModelImporterAnimationType.None;
            if (character) mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.weldVertices = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.isReadable = true;
            mi.globalScale = KitScale(assetPath);
        }

        private void OnPreprocessAudio()
        {
            if (!IsKit(assetPath)) return;
            var ai = (AudioImporter)assetImporter;
            ai.forceToMono = false;
            ai.loadInBackground = false;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            ai.defaultSampleSettings = s;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsKit(assetPath)) return;
            Texture2D map = LoadColormap(assetPath);
            var hist = new int[Palette.Town.Length];
            int tris = 0, parts = 0, unmapped = 0;
            Bounds bounds = default;
            bool any = false;
            var empties = new List<string>();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                var mf = t.GetComponent<MeshFilter>();
                var smr = t.GetComponent<SkinnedMeshRenderer>();
                Mesh m = mf != null ? mf.sharedMesh : smr != null ? smr.sharedMesh : null;
                if (m == null) { empties.Add(t.name); continue; }
                Vector2[] uv = m.uv;
                var cols = new Color[m.vertexCount];
                bool mapped = map != null && uv != null && uv.Length == m.vertexCount;
                if (!mapped) unmapped++;
                for (int i = 0; i < cols.Length; i++)
                {
                    Color src = mapped ? Sample(map, uv[i]) : Palette.Concrete;
                    int k = Palette.NearestIndex(src, Palette.Town);
                    hist[k]++;
                    cols[i] = Palette.Town[k];
                    cols[i].a = mapped && IsGlass(src) ? 0f : 1f;
                }
                m.colors = cols;
                m.uv = null;
                m.uv2 = null;
                m.RecalculateBounds();
                tris += m.triangles.Length / 3;
                parts++;
                Bounds wb = Transformed(m.bounds, root.transform.worldToLocalMatrix * t.localToWorldMatrix);
                if (!any) { bounds = wb; any = true; } else bounds.Encapsulate(wb);
            }
            // Also the root itself, when the model is a single mesh at the root.
            var rootMf = root.GetComponent<MeshFilter>();
            if (rootMf != null && rootMf.sharedMesh != null)
            {
                Mesh m = rootMf.sharedMesh;
                Vector2[] uv = m.uv;
                var cols = new Color[m.vertexCount];
                bool mapped = map != null && uv != null && uv.Length == m.vertexCount;
                if (!mapped) unmapped++;
                for (int i = 0; i < cols.Length; i++)
                {
                    Color src = mapped ? Sample(map, uv[i]) : Palette.Concrete;
                    int k = Palette.NearestIndex(src, Palette.Town);
                    hist[k]++;
                    cols[i] = Palette.Town[k];
                    cols[i].a = mapped && IsGlass(src) ? 0f : 1f;
                }
                m.colors = cols;
                m.uv = null;
                m.uv2 = null;
                m.RecalculateBounds();
                tris += m.triangles.Length / 3;
                parts++;
                if (!any) { bounds = m.bounds; any = true; } else bounds.Encapsulate(m.bounds);
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("KitImport ").Append(KeyFor(assetPath))
              .Append(" parts=").Append(parts).Append(" tris=").Append(tris)
              .Append(" size=").Append(bounds.size.ToString("F2"))
              .Append(" min=").Append(bounds.min.ToString("F2"))
              .Append(map == null ? " NO-COLORMAP" : "")
              .Append(unmapped > 0 ? " UNMAPPED=" + unmapped : "")
              .Append(" colours:");
            int total = 0;
            foreach (int h in hist) total += h;
            for (int i = 0; i < hist.Length; i++)
            {
                if (hist[i] == 0) continue;
                sb.Append(' ').Append(PaletteName(Palette.Town[i])).Append('=')
                  .Append(Mathf.RoundToInt(100f * hist[i] / Mathf.Max(1, total))).Append('%');
            }
            if (empties.Count > 0) sb.Append(" empties:").Append(string.Join(",", empties));
            Debug.Log(sb.ToString());
        }

        private static string PaletteName(Color c)
        {
            for (int i = 0; i < Palette.All.Length; i++)
                if (Palette.All[i] == c) return Palette.Names[i];
            return "?";
        }

        /// <summary>Nearest-texel sample: the colormap is a swatch atlas and a
        /// bilinear read at a swatch edge would mix two swatches.</summary>
        private static Color Sample(Texture2D map, Vector2 uv)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * map.width), 0, map.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * map.height), 0, map.height - 1);
            return map.GetPixel(x, y);
        }

        private static Bounds Transformed(Bounds b, Matrix4x4 m)
        {
            Vector3 mn = b.min, mx = b.max;
            var r = new Bounds(m.MultiplyPoint3x4(mn), Vector3.zero);
            for (int i = 1; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
                r.Encapsulate(m.MultiplyPoint3x4(c));
            }
            return r;
        }

        /// <summary>The swatch sheet a model's UVs point into, from the
        /// Textures folder beside it or in any parent up to the kit root:
        /// the kit-wide `colormap.png`, else the model's own sheet
        /// (`character-a.fbx` → `texture-a.png`), else the only PNG there.
        /// Read from disk, so the import order of the PNG does not matter.</summary>
        private static Texture2D LoadColormap(string modelPath)
        {
            string name = Path.GetFileNameWithoutExtension(modelPath);
            int dash = name.LastIndexOf('-');
            string dir = Path.GetDirectoryName(modelPath)?.Replace('\\', '/');
            for (int depth = 0; depth < 4 && !string.IsNullOrEmpty(dir) && dir.StartsWith("Assets/ThirdParty"); depth++)
            {
                string textures = dir + "/Textures";
                if (Directory.Exists(textures))
                {
                    string candidate = textures + "/colormap.png";
                    if (!File.Exists(candidate) && dash >= 0) candidate = textures + "/texture" + name.Substring(dash) + ".png";
                    if (!File.Exists(candidate))
                    {
                        string[] pngs = Directory.GetFiles(textures, "*.png");
                        candidate = pngs.Length == 1 ? pngs[0] : null;
                    }
                    if (candidate == null) return null;
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    return tex.LoadImage(File.ReadAllBytes(candidate)) ? tex : null;
                }
                dir = Path.GetDirectoryName(dir)?.Replace('\\', '/');
            }
            return null;
        }

        /// <summary>Path below Assets/ThirdParty, lower-cased, without the
        /// vendor's format folders and the extension.</summary>
        public static string KeyFor(string assetPath)
        {
            string rel = assetPath.Substring(Root.Length);
            rel = rel.Substring(0, rel.Length - Path.GetExtension(rel).Length);
            var kept = new List<string>();
            foreach (string seg in rel.Split('/'))
            {
                string s = seg.ToLowerInvariant();
                if (s == "models" || s == "fbx format" || s == "audio" || s == "textures") continue;
                kept.Add(s);
            }
            return string.Join("/", kept);
        }

        // ------------------------------------------------------------------
        // The catalogue
        // ------------------------------------------------------------------

        [MenuItem("GNP/Rebuild Asset Kit")]
        public static void RebuildAssetKit()
        {
            var meshes = new List<AssetKit.MeshEntry>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var entry = new AssetKit.MeshEntry { Key = KeyFor(path), Model = go };
                var parts = new List<AssetKit.Part>();
                var atts = new List<AssetKit.Attachment>();
                Bounds b = default;
                bool any = false;
                int tris = 0;
                Matrix4x4 rootInv = go.transform.worldToLocalMatrix;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    Matrix4x4 rel = rootInv * t.localToWorldMatrix;
                    var mf = t.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        parts.Add(new AssetKit.Part
                        {
                            Mesh = mf.sharedMesh,
                            LocalPosition = rel.GetColumn(3),
                            LocalRotation = rel.rotation,
                            LocalScale = rel.lossyScale
                        });
                        tris += mf.sharedMesh.triangles.Length / 3;
                        Bounds wb = Transformed(mf.sharedMesh.bounds, rel);
                        if (!any) { b = wb; any = true; } else b.Encapsulate(wb);
                    }
                    else if (t != go.transform)
                    {
                        atts.Add(new AssetKit.Attachment
                        {
                            Name = t.name,
                            LocalPosition = rel.GetColumn(3),
                            LocalRotation = rel.rotation
                        });
                    }
                }
                if (parts.Count == 0) continue;
                entry.Parts = parts.ToArray();
                entry.Attachments = atts.ToArray();
                entry.Bounds = b;
                entry.Triangles = tris;
                meshes.Add(entry);
            }

            var clips = new List<AssetKit.ClipEntry>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/ThirdParty" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                clips.Add(new AssetKit.ClipEntry { Key = KeyFor(path), Clip = clip });
            }

            meshes.Sort((a, c) => string.CompareOrdinal(a.Key, c.Key));
            clips.Sort((a, c) => string.CompareOrdinal(a.Key, c.Key));

            var kit = AssetDatabase.LoadAssetAtPath<AssetKit>(KitAssetPath);
            if (kit == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(KitAssetPath));
                kit = ScriptableObject.CreateInstance<AssetKit>();
                AssetDatabase.CreateAsset(kit, KitAssetPath);
            }
            kit.Meshes = meshes.ToArray();
            kit.Clips = clips.ToArray();
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            Debug.Log("AssetKit: " + meshes.Count + " models, " + clips.Count + " clips -> " + KitAssetPath);
        }
    }
}
