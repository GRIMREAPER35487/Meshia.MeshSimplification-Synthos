#nullable enable
#if ENABLE_VRCFURY

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Meshia.MeshSimplification.Editor
{
    /// <summary>
    /// Distributes a triangle budget between cascading entries so that the total visible damage is minimal.
    /// Each mesh is simplified to a few sample sizes and the actual surface deviation from the original is measured
    /// (mean distance x surface area, in world units). The resulting error curves are cached and merged greedily:
    /// triangles are removed first from whichever mesh loses the least surface accuracy per removed triangle.
    /// </summary>
    internal static class MeshiaSmartAllocator
    {
        static readonly float[] SampleFractions = { 0.75f, 0.5f, 0.35f, 0.25f, 0.15f, 0.1f, 0.05f };
        const int MaxReferencePoints = 4000;

        /// <summary>Lower convex hull of (removed triangles, error in world units).</summary>
        sealed class ErrorCurve
        {
            public int OriginalTriangleCount;
            public double[] Removed = Array.Empty<double>();
            public double[] Error = Array.Empty<double>();
            public double MaxRemoved => Removed.Length == 0 ? 0 : Removed[^1];
        }

        static readonly Dictionary<string, ErrorCurve> Cache = new();

        public static void ClearCache() => Cache.Clear();

        /// <summary>
        /// Sets <see cref="MeshiaCascadingAvatarMeshSimplifierRendererEntry.TargetTriangleCount"/> of the adjustable entries so that
        /// they sum to <paramref name="adjustableBudget"/> with minimal weighted error.
        /// </summary>
        /// <returns>False if the error curves could not be computed; entries are left untouched.</returns>
        public static bool Allocate(MeshiaCascadingAvatarMeshSimplifier component, IReadOnlyList<int> adjustableIndices, int adjustableBudget)
        {
            var entries = component.Entries;
            var curves = new ErrorCurve[adjustableIndices.Count];
            try
            {
                if (!TryGetCurves(component, adjustableIndices, curves))
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e, component);
                return false;
            }

            var weights = new double[curves.Length];
            long totalOriginal = 0;
            double totalMaxRemoved = 0;
            for (int i = 0; i < curves.Length; i++)
            {
                weights[i] = Math.Max(0f, entries[adjustableIndices[i]].Priority);
                totalOriginal += curves[i].OriginalTriangleCount;
                totalMaxRemoved += curves[i].MaxRemoved;
            }

            var removed = new double[curves.Length];
            var toRemove = totalOriginal - Math.Max(0, adjustableBudget);
            if (toRemove >= totalMaxRemoved)
            {
                for (int i = 0; i < curves.Length; i++) removed[i] = curves[i].MaxRemoved;
            }
            else if (toRemove > 0)
            {
                GreedyRemove(curves, weights, toRemove, removed);
            }

            for (int i = 0; i < curves.Length; i++)
            {
                var curve = curves[i];
                var target = curve.OriginalTriangleCount - (int)Math.Ceiling(removed[i] - 1e-6);
                entries[adjustableIndices[i]].TargetTriangleCount = Mathf.Clamp(target, 0, curve.OriginalTriangleCount);
            }
            return true;
        }

        static void GreedyRemove(ErrorCurve[] curves, double[] weights, double toRemove, double[] removed)
        {
            // Every hull has increasing slopes, so always taking the cheapest next segment is optimal.
            var segment = new int[curves.Length];
            var remaining = toRemove;
            while (remaining > 0)
            {
                var best = -1;
                var bestSlope = double.PositiveInfinity;
                for (int i = 0; i < curves.Length; i++)
                {
                    var curve = curves[i];
                    var s = segment[i];
                    if (s + 1 >= curve.Removed.Length) continue;
                    var dr = curve.Removed[s + 1] - curve.Removed[s];
                    if (dr <= 0) continue;
                    var slope = weights[i] * (curve.Error[s + 1] - curve.Error[s]) / dr;
                    if (slope < bestSlope)
                    {
                        bestSlope = slope;
                        best = i;
                    }
                }
                if (best < 0) break;

                var bestCurve = curves[best];
                var bs = segment[best];
                var length = bestCurve.Removed[bs + 1] - removed[best];
                if (length <= remaining)
                {
                    removed[best] = bestCurve.Removed[bs + 1];
                    remaining -= length;
                    segment[best]++;
                }
                else
                {
                    removed[best] += remaining;
                    remaining = 0;
                }
            }
        }

        /// <summary>Linear scale from mesh space to world space, so meshes with different import scales are comparable.</summary>
        static double GetWorldScale(Renderer renderer, Mesh mesh)
        {
            var local = mesh.bounds.size.magnitude;
            var world = renderer.bounds.size.magnitude;
            if (!(local > 1e-8f) || !(world > 1e-8f)) return 1;
            return world / local;
        }

        static bool TryGetCurves(MeshiaCascadingAvatarMeshSimplifier component, IReadOnlyList<int> adjustableIndices, ErrorCurve[] curves)
        {
            var entries = component.Entries;
            var animator = component.GetComponentInParent<Animator>();
            var avatarRoot = animator != null ? animator.gameObject : component.transform.root.gameObject;

            var missing = new List<(int CurveIndex, string Key, Mesh Mesh, double Scale, MeshSimplifierOptions Options, BitArray? Bones, float[]? BlendShapeWeights)>();
            MeshiaBlendShapeUsage? blendShapeUsage = null;
            for (int i = 0; i < adjustableIndices.Count; i++)
            {
                var entry = entries[adjustableIndices[i]];
                var renderer = entry.TargetRenderer;
                if (renderer == null) return false;
                var mesh = RendererUtility.GetMesh(renderer);
                if (mesh == null) return false;

                var scale = GetWorldScale(renderer, mesh);
                var key = GetKey(entry, mesh, scale);
                if (Cache.TryGetValue(key, out var curve))
                {
                    curves[i] = curve;
                    continue;
                }
                float[]? blendShapeWeights = null;
                if (entry.Options.PreserveBlendShapes)
                {
                    blendShapeUsage ??= new MeshiaBlendShapeUsage(avatarRoot);
                    blendShapeWeights = blendShapeUsage.GetBlendShapeWeights(renderer, mesh, entry.Options, entry.BlendShapeSettings);
                }
                var bones = MeshiaCascadingAvatarMeshSimplifier.GetPreserveBorderEdgesBoneIndices(avatarRoot, component, entry);
                missing.Add((i, key, mesh, scale, entry.Options, bones, blendShapeWeights));
            }

            if (missing.Count == 0) return true;

            var destinations = new List<Mesh>();
            try
            {
                EditorUtility.DisplayProgressBar("Meshia Smart Allocation", $"Simplifying {missing.Count} mesh(es) at {SampleFractions.Length} sample sizes...", 0.1f);
                var parameters = new List<(Mesh, MeshSimplificationTarget, MeshSimplifierOptions, BitArray?, float[]?, Mesh)>();
                foreach (var m in missing)
                {
                    var triangleCount = m.Mesh.GetTriangleCount();
                    foreach (var fraction in SampleFractions)
                    {
                        var destination = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                        destinations.Add(destination);
                        var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.AbsoluteTriangleCount, Value = Mathf.Floor(triangleCount * fraction) };
                        parameters.Add((m.Mesh, target, m.Options, m.Bones, m.BlendShapeWeights, destination));
                    }
                }
                MeshSimplifier.SimplifyBatch(parameters);

                for (int m = 0; m < missing.Count; m++)
                {
                    EditorUtility.DisplayProgressBar("Meshia Smart Allocation", $"Measuring {missing[m].Mesh.name}...", 0.5f + 0.5f * m / missing.Count);
                    var samples = destinations.Skip(m * SampleFractions.Length).Take(SampleFractions.Length).ToList();
                    var curve = BuildCurve(missing[m].Mesh, missing[m].Scale, samples);
                    Cache[missing[m].Key] = curve;
                    curves[missing[m].CurveIndex] = curve;
                }
            }
            finally
            {
                foreach (var destination in destinations)
                {
                    UnityEngine.Object.DestroyImmediate(destination);
                }
                EditorUtility.ClearProgressBar();
            }
            return true;
        }

        static string GetKey(MeshiaCascadingAvatarMeshSimplifierRendererEntry entry, Mesh mesh, double scale)
        {
            var settings = entry.Options.PreserveBlendShapes
                ? string.Join(";", entry.BlendShapeSettings.Select(s => $"{s.Name}:{s.Mode}:{s.Weight}"))
                : "";
            return $"{mesh.GetInstanceID()}|{mesh.GetTriangleCount()}|{mesh.vertexCount}|{scale:G6}|{JsonUtility.ToJson(entry.Options)}|{entry.PreserveBorderEdgesBones}|{settings}";
        }

        static ErrorCurve BuildCurve(Mesh original, double scale, List<Mesh> samples)
        {
            var originalTriangleCount = original.GetTriangleCount();
            var originalVertices = original.vertices;
            var originalTriangles = original.triangles;
            var area = SurfaceArea(originalVertices, originalTriangles) * scale * scale;

            // Reference points: an evenly strided subset of the original vertices.
            var stride = Math.Max(1, originalVertices.Length / MaxReferencePoints);
            var referencePoints = new List<Vector3>(originalVertices.Length / stride + 1);
            for (int i = 0; i < originalVertices.Length; i += stride) referencePoints.Add(originalVertices[i]);

            var points = new List<(double Removed, double Error)> { (0, 0) };
            foreach (var sample in samples)
            {
                var sampleTriangles = sample.triangles;
                var removedCount = originalTriangleCount - sampleTriangles.Length / 3;
                if (removedCount <= 0) continue;
                var meanDistance = MeanDistance(referencePoints, sample.vertices, sampleTriangles, original.bounds) * scale;
                points.Add((removedCount, meanDistance * area));
            }
            points.Sort((a, b) => a.Removed.CompareTo(b.Removed));

            // Lower convex hull (Andrew's monotone chain), keeping the cumulative error non-decreasing.
            var hull = new List<(double Removed, double Error)>();
            var maxError = 0.0;
            foreach (var raw in points)
            {
                if (hull.Count != 0 && raw.Removed <= hull[^1].Removed) continue;
                maxError = Math.Max(maxError, raw.Error);
                var p = (raw.Removed, maxError);
                while (hull.Count >= 2)
                {
                    var a = hull[^2]; var b = hull[^1];
                    var cross = (b.Removed - a.Removed) * (p.Item2 - a.Error) - (b.Error - a.Error) * (p.Removed - a.Removed);
                    if (cross <= 0) hull.RemoveAt(hull.Count - 1);
                    else break;
                }
                hull.Add(p);
            }

            return new ErrorCurve
            {
                OriginalTriangleCount = originalTriangleCount,
                Removed = hull.Select(h => h.Removed).ToArray(),
                Error = hull.Select(h => h.Error).ToArray(),
            };
        }

        static double SurfaceArea(Vector3[] vertices, int[] triangles)
        {
            double area = 0;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                area += Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]).magnitude * 0.5;
            }
            return area;
        }

        /// <summary>Mean distance (mesh space) from the reference points to the closest point on the simplified surface.</summary>
        static double MeanDistance(List<Vector3> referencePoints, Vector3[] vertices, int[] triangles, Bounds bounds)
        {
            if (referencePoints.Count == 0) return 0;
            var cell = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) / 48f;
            if (!(cell > 0)) return 0;
            const int maxRing = 4;
            var cap = cell * (maxRing + 1);

            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                var a = vertices[triangles[t]]; var b = vertices[triangles[t + 1]]; var c = vertices[triangles[t + 2]];
                var min = Vector3Int.FloorToInt(Vector3.Min(a, Vector3.Min(b, c)) / cell);
                var max = Vector3Int.FloorToInt(Vector3.Max(a, Vector3.Max(b, c)) / cell);
                for (int x = min.x; x <= max.x; x++)
                    for (int y = min.y; y <= max.y; y++)
                        for (int z = min.z; z <= max.z; z++)
                        {
                            var key = new Vector3Int(x, y, z);
                            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                            list.Add(t);
                        }
            }

            double sum = 0;
            foreach (var p in referencePoints)
            {
                var k = Vector3Int.FloorToInt(p / cell);
                var best = float.PositiveInfinity;
                // Grow the search ring until something is found; one extra ring covers closer triangles in the next shell.
                for (int ring = 0; ring <= maxRing; ring++)
                {
                    for (int x = -ring; x <= ring; x++)
                        for (int y = -ring; y <= ring; y++)
                            for (int z = -ring; z <= ring; z++)
                            {
                                if (Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) != ring) continue;
                                if (!grid.TryGetValue(k + new Vector3Int(x, y, z), out var list)) continue;
                                foreach (var t in list)
                                {
                                    var d = (ClosestPointOnTriangle(p, vertices[triangles[t]], vertices[triangles[t + 1]], vertices[triangles[t + 2]]) - p).sqrMagnitude;
                                    if (d < best) best = d;
                                }
                            }
                    if (best <= (ring * cell) * (ring * cell)) break;
                }
                sum += float.IsPositiveInfinity(best) ? cap : Math.Min(Mathf.Sqrt(best), cap);
            }
            return sum / referencePoints.Count;
        }

        static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a; var ac = c - a; var ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            var bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + d1 / (d1 - d3) * ab;
            var cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + d2 / (d2 - d6) * ac;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) return b + (d4 - d3) / ((d4 - d3) + (d5 - d6)) * (c - b);
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }
    }
}
#endif
