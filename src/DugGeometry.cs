using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killcraft
{
    // Clips and carves ULTRAKILL's level meshes and colliders when Minecraft digs cells out of them.
    // Listens to Proto.RenDug from Minecraft's WorldExporter.
    internal static class DugGeometry
    {
        private struct Vertex
        {
            public Vector3 Pos;
            public Vector3 Norm;
            public Vector2 UV;

            public static Vertex Lerp(Vertex a, Vertex b, float t)
            {
                return new Vertex
                {
                    Pos = Vector3.Lerp(a.Pos, b.Pos, t),
                    Norm = Vector3.Normalize(Vector3.Lerp(a.Norm, b.Norm, t)),
                    UV = Vector2.Lerp(a.UV, b.UV, t)
                };
            }
        }

        // Remember which blocks have already carved the geometry to avoid redundant cuts
        private static readonly HashSet<long> dugBlocks = new HashSet<long>();

        // Cache modified cloned meshes per GameObject ID so multiple blocks on the same wall carve sequentially
        private static readonly Dictionary<int, Mesh> modifiedMeshes = new Dictionary<int, Mesh>();

        public static void Clear()
        {
            dugBlocks.Clear();
            modifiedMeshes.Clear();
        }

        public static unsafe void OnDug(byte* p, int bytes)
        {
            if (Plugin.DigIntoLevels == null || !Plugin.DigIntoLevels.Value)
            {
                return;
            }
            if (bytes < 24)
            {
                return;
            }

            int sx = *(int*)(p + 0);
            int sy = *(int*)(p + 4);
            int sz = *(int*)(p + 8);
            int dugCount = *(int*)(p + 12);

            if (dugCount <= 0 || bytes < 24 + 512)
            {
                return;
            }

            ulong* words = (ulong*)(p + 24);
            float u = Coords.U;
            Vector3 blockSize = new Vector3(u, u, u);

            for (int w = 0; w < 64; w++)
            {
                ulong word = words[w];
                if (word == 0)
                {
                    continue;
                }

                for (int b = 0; b < 64; b++)
                {
                    if (((word >> b) & 1UL) == 0)
                    {
                        continue;
                    }

                    int bit = (w << 6) | b;
                    int lx = bit & 15;
                    int lz = (bit >> 4) & 15;
                    int ly = (bit >> 8) & 15;

                    int bx = sx * 16 + lx;
                    int by = sy * 16 + ly;
                    int bz = sz * 16 + lz;

                    long key = ((long)bx << 42) ^ ((long)by << 21) ^ (long)bz;
                    if (!dugBlocks.Add(key))
                    {
                        continue;
                    }

                    Vector3 center = Coords.ToUnity(bx + 0.5, by + 0.5, bz + 0.5);
                    CutBox(center, blockSize);
                }
            }
        }

        public static void CutBox(Vector3 center, Vector3 size)
        {
            Vector3 half = size * 0.5f;
            Vector3 bmin = center - half;
            Vector3 bmax = center + half;

            // Find all colliders overlapping this block in Unity world space
            Collider[] hits = Physics.OverlapBox(center, half, Quaternion.identity);
            if (hits == null || hits.Length == 0)
            {
                return;
            }

            var processedGos = new HashSet<int>();

            foreach (Collider c in hits)
            {
                if (c == null || !c.enabled || c.isTrigger || WorldRender.IsOurs(c))
                {
                    continue;
                }

                // Ignore player, enemies, bullets, items
                if (c.GetComponentInParent<NewMovement>() != null ||
                    c.GetComponentInParent<EnemyIdentifier>() != null ||
                    c.GetComponentInParent<Projectile>() != null)
                {
                    continue;
                }

                GameObject go = c.gameObject;
                if (!processedGos.Add(go.GetInstanceID()))
                {
                    continue;
                }

                if (c is BoxCollider bc)
                {
                    // Convert BoxCollider to MeshCollider so it can be physically sliced
                    Mesh boxMesh = CreateBoxMesh(bc.center, bc.size);
                    bc.enabled = false;
                    MeshCollider mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = boxMesh;
                    MeshFilter mf = go.GetComponent<MeshFilter>() ?? go.GetComponentInParent<MeshFilter>() ?? go.GetComponentInChildren<MeshFilter>();
                    CarveObject(go, mc, mf, bmin, bmax);
                }
                else if (c is MeshCollider mc)
                {
                    MeshFilter mf = go.GetComponent<MeshFilter>() ?? go.GetComponentInParent<MeshFilter>() ?? go.GetComponentInChildren<MeshFilter>();
                    CarveObject(go, mc, mf, bmin, bmax);
                }
            }
        }

        private static void CarveObject(GameObject go, MeshCollider mc, MeshFilter mf, Vector3 bmin, Vector3 bmax)
        {
            int goId = go.GetInstanceID();
            Mesh targetMesh = null;

            if (modifiedMeshes.TryGetValue(goId, out Mesh existing))
            {
                targetMesh = existing;
            }
            else
            {
                Mesh source = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh : (mc != null ? mc.sharedMesh : null);
                if (source == null)
                {
                    return;
                }
                try
                {
                    targetMesh = UnityEngine.Object.Instantiate(source);
                    targetMesh.name = source.name + "_Dug";
                    modifiedMeshes[goId] = targetMesh;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"DugGeometry: couldn't clone mesh '{source.name}': {e.Message}");
                    return;
                }
            }

            if (targetMesh == null)
            {
                return;
            }

            bool changed = CarveMesh(targetMesh, go.transform, bmin, bmax);
            if (!changed)
            {
                return;
            }

            // Apply carved mesh to both visual renderer and physical collider!
            if (mf != null)
            {
                mf.sharedMesh = targetMesh;
            }
            if (mc != null)
            {
                mc.sharedMesh = null;
                mc.sharedMesh = targetMesh;
            }
        }

        private static bool CarveMesh(Mesh mesh, Transform tr, Vector3 bmin, Vector3 bmax)
        {
            Vector3[] origVerts;
            try
            {
                origVerts = mesh.vertices;
            }
            catch
            {
                return false;
            }

            if (origVerts == null || origVerts.Length == 0)
            {
                return false;
            }

            Vector3[] origNorms = mesh.normals;
            Vector2[] origUVs = mesh.uv;
            bool hasNorms = origNorms != null && origNorms.Length == origVerts.Length;
            bool hasUVs = origUVs != null && origUVs.Length == origVerts.Length;

            Matrix4x4 l2w = tr.localToWorldMatrix;
            Matrix4x4 w2l = tr.worldToLocalMatrix;

            // Transform vertices to world space for exact AABB clipping
            Vertex[] worldVerts = new Vertex[origVerts.Length];
            for (int i = 0; i < origVerts.Length; i++)
            {
                worldVerts[i] = new Vertex
                {
                    Pos = l2w.MultiplyPoint3x4(origVerts[i]),
                    Norm = hasNorms ? l2w.MultiplyVector(origNorms[i]) : Vector3.up,
                    UV = hasUVs ? origUVs[i] : Vector2.zero
                };
            }

            var newVerts = new List<Vertex>();
            var submeshTris = new List<int[]>();
            bool anyChange = false;

            int subCount = mesh.subMeshCount;
            for (int s = 0; s < subCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                var keptIndices = new List<int>();

                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vertex v0 = worldVerts[tris[t]];
                    Vertex v1 = worldVerts[tris[t + 1]];
                    Vertex v2 = worldVerts[tris[t + 2]];

                    var keptPolys = new List<List<Vertex>>();
                    SubtractBox(v0, v1, v2, bmin, bmax, keptPolys);

                    if (keptPolys.Count == 1 && keptPolys[0].Count == 3 &&
                        keptPolys[0][0].Pos == v0.Pos &&
                        keptPolys[0][1].Pos == v1.Pos &&
                        keptPolys[0][2].Pos == v2.Pos)
                    {
                        // Completely outside and untouched
                        int i0 = AddVertex(newVerts, v0);
                        int i1 = AddVertex(newVerts, v1);
                        int i2 = AddVertex(newVerts, v2);
                        keptIndices.Add(i0);
                        keptIndices.Add(i1);
                        keptIndices.Add(i2);
                    }
                    else
                    {
                        // Triangle was clipped or removed!
                        anyChange = true;
                        foreach (var poly in keptPolys)
                        {
                            if (poly.Count < 3) continue;
                            int rootIdx = AddVertex(newVerts, poly[0]);
                            for (int k = 1; k < poly.Count - 1; k++)
                            {
                                int idx1 = AddVertex(newVerts, poly[k]);
                                int idx2 = AddVertex(newVerts, poly[k + 1]);
                                keptIndices.Add(rootIdx);
                                keptIndices.Add(idx1);
                                keptIndices.Add(idx2);
                            }
                        }
                    }
                }
                submeshTris.Add(keptIndices.ToArray());
            }

            if (!anyChange)
            {
                return false;
            }

            // Convert vertices back to local space
            Vector3[] finalVerts = new Vector3[newVerts.Count];
            Vector3[] finalNorms = hasNorms ? new Vector3[newVerts.Count] : null;
            Vector2[] finalUVs = hasUVs ? new Vector2[newVerts.Count] : null;

            for (int i = 0; i < newVerts.Count; i++)
            {
                finalVerts[i] = w2l.MultiplyPoint3x4(newVerts[i].Pos);
                if (hasNorms)
                {
                    finalNorms[i] = w2l.MultiplyVector(newVerts[i].Norm).normalized;
                }
                if (hasUVs)
                {
                    finalUVs[i] = newVerts[i].UV;
                }
            }

            mesh.Clear();
            mesh.vertices = finalVerts;
            if (hasNorms) mesh.normals = finalNorms;
            if (hasUVs) mesh.uv = finalUVs;
            mesh.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++)
            {
                mesh.SetTriangles(submeshTris[s], s);
            }
            mesh.RecalculateBounds();
            return true;
        }

        private static int AddVertex(List<Vertex> list, Vertex v)
        {
            int idx = list.Count;
            list.Add(v);
            return idx;
        }

        private static void SubtractBox(Vertex v0, Vertex v1, Vertex v2, Vector3 bmin, Vector3 bmax, List<List<Vertex>> keptPolys)
        {
            float minX = Mathf.Min(v0.Pos.x, Mathf.Min(v1.Pos.x, v2.Pos.x));
            float maxX = Mathf.Max(v0.Pos.x, Mathf.Max(v1.Pos.x, v2.Pos.x));
            float minY = Mathf.Min(v0.Pos.y, Mathf.Min(v1.Pos.y, v2.Pos.y));
            float maxY = Mathf.Max(v0.Pos.y, Mathf.Max(v1.Pos.y, v2.Pos.y));
            float minZ = Mathf.Min(v0.Pos.z, Mathf.Min(v1.Pos.z, v2.Pos.z));
            float maxZ = Mathf.Max(v0.Pos.z, Mathf.Max(v1.Pos.z, v2.Pos.z));

            if (maxX < bmin.x || minX > bmax.x || maxY < bmin.y || minY > bmax.y || maxZ < bmin.z || minZ > bmax.z)
            {
                keptPolys.Add(new List<Vertex> { v0, v1, v2 });
                return;
            }

            bool in0 = v0.Pos.x >= bmin.x && v0.Pos.x <= bmax.x && v0.Pos.y >= bmin.y && v0.Pos.y <= bmax.y && v0.Pos.z >= bmin.z && v0.Pos.z <= bmax.z;
            bool in1 = v1.Pos.x >= bmin.x && v1.Pos.x <= bmax.x && v1.Pos.y >= bmin.y && v1.Pos.y <= bmax.y && v1.Pos.z >= bmin.z && v1.Pos.z <= bmax.z;
            bool in2 = v2.Pos.x >= bmin.x && v2.Pos.x <= bmax.x && v2.Pos.y >= bmin.y && v2.Pos.y <= bmax.y && v2.Pos.z >= bmin.z && v2.Pos.z <= bmax.z;
            if (in0 && in1 && in2)
            {
                return;
            }

            var planes = new (int axis, float val, bool outGreater, bool inGreater)[]
            {
                (0, bmin.x, false, true),
                (0, bmax.x, true, false),
                (1, bmin.y, false, true),
                (1, bmax.y, true, false),
                (2, bmin.z, false, true),
                (2, bmax.z, true, false)
            };

            var activePolys = new List<List<Vertex>> { new List<Vertex> { v0, v1, v2 } };

            for (int p = 0; p < planes.Length; p++)
            {
                var nextActive = new List<List<Vertex>>();
                for (int i = 0; i < activePolys.Count; i++)
                {
                    var poly = activePolys[i];
                    var outPart = ClipPolyPlane(poly, planes[p].axis, planes[p].val, planes[p].outGreater);
                    if (outPart.Count >= 3)
                    {
                        keptPolys.Add(outPart);
                    }
                    var inPart = ClipPolyPlane(poly, planes[p].axis, planes[p].val, planes[p].inGreater);
                    if (inPart.Count >= 3)
                    {
                        nextActive.Add(inPart);
                    }
                }
                activePolys = nextActive;
                if (activePolys.Count == 0) break;
            }
        }

        private static List<Vertex> ClipPolyPlane(List<Vertex> poly, int axis, float val, bool keepGreater)
        {
            if (poly == null || poly.Count == 0) return new List<Vertex>();
            var outPoly = new List<Vertex>(poly.Count + 1);
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vertex v1 = poly[i];
                Vertex v2 = poly[(i + 1) % n];
                float v1Val = axis == 0 ? v1.Pos.x : (axis == 1 ? v1.Pos.y : v1.Pos.z);
                float v2Val = axis == 0 ? v2.Pos.x : (axis == 1 ? v2.Pos.y : v2.Pos.z);
                bool c1 = keepGreater ? (v1Val >= val) : (v1Val <= val);
                bool c2 = keepGreater ? (v2Val >= val) : (v2Val <= val);

                if (c1 && c2)
                {
                    outPoly.Add(v2);
                }
                else if (c1 && !c2)
                {
                    float denom = v2Val - v1Val;
                    float t = Mathf.Abs(denom) > 1e-6f ? (val - v1Val) / denom : 0.5f;
                    outPoly.Add(Vertex.Lerp(v1, v2, Mathf.Clamp01(t)));
                }
                else if (!c1 && c2)
                {
                    float denom = v2Val - v1Val;
                    float t = Mathf.Abs(denom) > 1e-6f ? (val - v1Val) / denom : 0.5f;
                    outPoly.Add(Vertex.Lerp(v1, v2, Mathf.Clamp01(t)));
                    outPoly.Add(v2);
                }
            }
            return outPoly;
        }

        private static Mesh CreateBoxMesh(Vector3 center, Vector3 size)
        {
            Mesh m = new Mesh();
            m.name = "BoxMesh_Carved";
            Vector3 h = size * 0.5f;
            Vector3[] v = new Vector3[]
            {
                center + new Vector3(-h.x, -h.y, -h.z),
                center + new Vector3( h.x, -h.y, -h.z),
                center + new Vector3( h.x,  h.y, -h.z),
                center + new Vector3(-h.x,  h.y, -h.z),
                center + new Vector3(-h.x, -h.y,  h.z),
                center + new Vector3( h.x, -h.y,  h.z),
                center + new Vector3( h.x,  h.y,  h.z),
                center + new Vector3(-h.x,  h.y,  h.z)
            };
            int[] tri = new int[]
            {
                0, 2, 1, 0, 3, 2,
                5, 6, 4, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                2, 3, 7, 2, 7, 6,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5
            };
            m.vertices = v;
            m.triangles = tri;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
