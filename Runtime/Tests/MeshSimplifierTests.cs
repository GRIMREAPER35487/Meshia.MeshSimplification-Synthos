using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meshia.MeshSimplification;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;

namespace Meshia.MeshSimplification.Tests
{
    public class MeshSimplifierTests
    {
        static Mesh GetPrimitiveMesh(PrimitiveType type)
        {
            var gameObject = GameObject.CreatePrimitive(type);
            var mesh = gameObject.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(gameObject);
            return mesh;
        }
        [TestCase(PrimitiveType.Sphere)]
        [TestCase(PrimitiveType.Capsule)]
        [TestCase(PrimitiveType.Cylinder)]
        public async Task ShouldSimplifyPrimitive(PrimitiveType type)
        {
            var mesh = GetPrimitiveMesh(type);

            MeshSimplificationTarget target = new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.5f,
            };
            Mesh simplifiedMesh = new();
            await MeshSimplifier.SimplifyAsync(mesh, target, MeshSimplifierOptions.Default, simplifiedMesh);
            Assert.LessOrEqual(simplifiedMesh.vertexCount, mesh.vertexCount * 0.5f);
            Object.Destroy(simplifiedMesh);
        }
        [TestCase(PrimitiveType.Sphere)]
        [TestCase(PrimitiveType.Capsule)]
        [TestCase(PrimitiveType.Cylinder)]
        public async Task ShouldSimplifyPrimitiveIncrementally(PrimitiveType type)
        {
            var allocator = Allocator.Persistent;
            var mesh = GetPrimitiveMesh(type);
            using var blendShapes = BlendShapeData.GetMeshBlendShapes(mesh, allocator);

            Assert.Zero(blendShapes.Length, "Primitive meshes should not have blend shapes by default.");
            var meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            var meshData = meshDataArray[0];

            using MeshSimplifier meshSimplifier = new(allocator);

            var load = meshSimplifier.ScheduleLoadMeshData(meshData, MeshSimplifierOptions.Default);

            while (!load.IsCompleted)
            {
                await Task.Yield();
            }

            load.Complete();

            Mesh simplifiedMesh1 = await SimplifyToTarget(new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.5f,
            });

            Assert.LessOrEqual(simplifiedMesh1.vertexCount, mesh.vertexCount * 0.5f);

            Object.Destroy(simplifiedMesh1);
            Mesh simplifiedMesh2 = await SimplifyToTarget(new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.3f,
            });

            Assert.LessOrEqual(simplifiedMesh2.vertexCount, mesh.vertexCount * 0.3f);
            Object.Destroy(simplifiedMesh2);

            async Task<Mesh> SimplifyToTarget(MeshSimplificationTarget target)
            {
                var destinationMeshDataArray = Mesh.AllocateWritableMeshData(1);
                var destinationMeshData = destinationMeshDataArray[0];

                Mesh simplifiedMesh = new();
                var simplify = meshSimplifier.ScheduleSimplify(meshData, blendShapes, target, new JobHandle());

                using NativeList<BlendShapeData> destinationBlendShapes = new(allocator);
                var write = meshSimplifier.ScheduleWriteMeshData(meshData, blendShapes, destinationMeshData, destinationBlendShapes, simplify);

                while (!write.IsCompleted)
                {
                    await Task.Yield();
                }

                write.Complete();

                Assert.Zero(destinationBlendShapes.Length, "Primitive meshes should not have blend shapes after simplification.");

                Mesh.ApplyAndDisposeWritableMeshData(destinationMeshDataArray, simplifiedMesh);
                return simplifiedMesh;
            }
        }
        [TestCase(PrimitiveType.Sphere)]
        [TestCase(PrimitiveType.Capsule)]
        [TestCase(PrimitiveType.Cylinder)]
        public async Task ShouldSimplifyPrimitiveWithDuplicatedSubMeshes(PrimitiveType type)
        {
            var mesh = Object.Instantiate(GetPrimitiveMesh(type));
            var originalSubMeshCount = mesh.subMeshCount;
            mesh.subMeshCount += 1;
            mesh.SetTriangles(mesh.GetTriangles(originalSubMeshCount - 1), originalSubMeshCount);

            MeshSimplificationTarget target = new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.5f,
            };
            Mesh simplifiedMesh = new();
            await MeshSimplifier.SimplifyAsync(mesh, target, MeshSimplifierOptions.Default, simplifiedMesh);
            Assert.LessOrEqual(simplifiedMesh.vertexCount, mesh.vertexCount * 0.5f);
            Object.Destroy(mesh);
            Object.Destroy(simplifiedMesh);
        }

        // A flat grid whose center bulges only under a blend shape.
        static Mesh CreateGridWithBumpBlendShape(int resolution)
        {
            var vertices = new List<Vector3>();
            var deltas = new List<Vector3>();
            var triangles = new List<int>();
            for (int y = 0; y <= resolution; y++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    var u = (float)x / resolution - 0.5f;
                    var v = (float)y / resolution - 0.5f;
                    vertices.Add(new Vector3(u, 0, v));
                    var r2 = (u * u + v * v) / 0.04f;
                    deltas.Add(new Vector3(0, 0.2f * Mathf.Exp(-r2), 0));
                }
            }
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int i = y * (resolution + 1) + x;
                    triangles.AddRange(new[] { i, i + resolution + 1, i + 1, i + 1, i + resolution + 1, i + resolution + 2 });
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.AddBlendShapeFrame("Bump", 100, deltas.ToArray(), null, null);
            return mesh;
        }

        static int CountVerticesInBump(Mesh mesh)
        {
            var count = 0;
            foreach (var vertex in mesh.vertices)
            {
                if (new Vector2(vertex.x, vertex.z).magnitude < 0.2f) count++;
            }
            return count;
        }

        [Test]
        public void PreserveBlendShapesKeepsDensityWhereBlendShapeDeforms()
        {
            var mesh = CreateGridWithBumpBlendShape(40);
            MeshSimplificationTarget target = new() { Kind = MeshSimplificationTargetKind.RelativeTriangleCount, Value = 0.3f };

            var baseOptions = MeshSimplifierOptions.Default;
            baseOptions.PreserveBorderEdges = false;
            var off = new Mesh();
            MeshSimplifier.Simplify(mesh, target, baseOptions, off);

            var onOptions = baseOptions;
            onOptions.PreserveBlendShapes = true;
            var on = new Mesh();
            MeshSimplifier.Simplify(mesh, target, onOptions, on);

            Assert.AreEqual(1, on.blendShapeCount);
            Assert.LessOrEqual(on.triangles.Length / 3, mesh.triangles.Length / 3 * 0.3f + 1);
            Assert.Greater(CountVerticesInBump(on), CountVerticesInBump(off), "Blend shape aware error should keep more vertices where the blend shape deforms.");

            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(off);
            Object.DestroyImmediate(on);
        }

        [Test]
        public void PreserveBlendShapesWithZeroWeightMatchesDisabled()
        {
            var mesh = CreateGridWithBumpBlendShape(20);
            MeshSimplificationTarget target = new() { Kind = MeshSimplificationTargetKind.RelativeTriangleCount, Value = 0.5f };
            var options = MeshSimplifierOptions.Default;
            var off = new Mesh();
            MeshSimplifier.SimplifyBatch(new List<(Mesh, MeshSimplificationTarget, MeshSimplifierOptions, System.Collections.BitArray, float[], Mesh)> { (mesh, target, options, null, null, off) });

            // Every blend shape excluded: the error metric is the same as with the option disabled.
            // (Delta normals are interpolated differently, so compare geometry only.)
            options.PreserveBlendShapes = true;
            var excluded = new Mesh();
            MeshSimplifier.SimplifyBatch(new List<(Mesh, MeshSimplificationTarget, MeshSimplifierOptions, System.Collections.BitArray, float[], Mesh)> { (mesh, target, options, null, new[] { 0f }, excluded) });

            CollectionAssert.AreEqual(off.vertices, excluded.vertices);
            CollectionAssert.AreEqual(off.triangles, excluded.triangles);

            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(off);
            Object.DestroyImmediate(excluded);
        }
    }

}
