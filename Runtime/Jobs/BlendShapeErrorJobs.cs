using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Plane = Unity.Mathematics.Geometry.Plane;
namespace Meshia.MeshSimplification
{
    /// <summary>
    /// Resizes the per blend shape buffers to (blend shape count * vertex/triangle count).
    /// </summary>
    [BurstCompile]
    struct InitializeBlendShapeErrorBuffersJob : IJob
    {
        [ReadOnly]
        public Mesh.MeshData MeshData;
        public int BlendShapeCount;
        public NativeList<ErrorQuadric> BlendShapeVertexErrorQuadrics;
        public NativeList<float3> BlendShapeTriangleNormals;
        public NativeList<ErrorQuadric> BlendShapeTriangleErrorQuadrics;
        public void Execute()
        {
            var vertexCount = MeshData.vertexCount;
            var triangleCount = MeshData.GetTriangleCount();
            BlendShapeVertexErrorQuadrics.Clear();
            BlendShapeVertexErrorQuadrics.Resize(vertexCount * BlendShapeCount, NativeArrayOptions.UninitializedMemory);
            BlendShapeTriangleNormals.Clear();
            BlendShapeTriangleNormals.Resize(triangleCount * BlendShapeCount, NativeArrayOptions.UninitializedMemory);
            BlendShapeTriangleErrorQuadrics.Clear();
            BlendShapeTriangleErrorQuadrics.Resize(triangleCount * BlendShapeCount, NativeArrayOptions.UninitializedMemory);
        }
    }

    /// <summary>
    /// Same as <see cref="ComputeTriangleNormalsAndErrorQuadricsJob"/>, but for each considered blend shape fully applied.
    /// </summary>
    [BurstCompile]
    struct ComputeBlendShapeTriangleNormalsAndErrorQuadricsJob : IJobParallelForDefer
    {
        [ReadOnly]
        public NativeArray<float3> VertexPositionBuffer;
        [ReadOnly]
        public NativeArray<int3> Triangles;
        [ReadOnly]
        public NativeArray<UnsafeList<float3>> BlendShapeDeltaVertices;
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<float3> BlendShapeTriangleNormals;
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<ErrorQuadric> BlendShapeTriangleErrorQuadrics;
        public void Execute(int triangleIndex)
        {
            var triangleCount = Triangles.Length;
            var triangle = Triangles[triangleIndex];
            var baseX = VertexPositionBuffer[triangle.x];
            var baseY = VertexPositionBuffer[triangle.y];
            var baseZ = VertexPositionBuffer[triangle.z];
            for (int shape = 0; shape < BlendShapeDeltaVertices.Length; shape++)
            {
                var deltas = BlendShapeDeltaVertices[shape];
                var x = baseX + deltas[triangle.x];
                var y = baseY + deltas[triangle.y];
                var z = baseZ + deltas[triangle.z];
                var plane = new Plane(math.cross(y - x, z - x), x);
                var index = shape * triangleCount + triangleIndex;
                BlendShapeTriangleNormals[index] = plane.Normal;
                BlendShapeTriangleErrorQuadrics[index] = new ErrorQuadric(plane);
            }
        }
    }

    /// <summary>
    /// Same as <see cref="ComputeVertexErrorQuadricsJob"/>, but for each considered blend shape fully applied.
    /// </summary>
    [BurstCompile]
    struct ComputeBlendShapeVertexErrorQuadricsJob : IJobParallelForDefer
    {
        [ReadOnly]
        public NativeArray<float3> VertexPositionBuffer;
        [ReadOnly]
        public NativeArray<int3> Triangles;
        [ReadOnly]
        public NativeParallelMultiHashMap<int, int> VertexContainingTriangles;
        [ReadOnly]
        public NativeHashSet<int2> Edges;
        [ReadOnly]
        public NativeArray<UnsafeList<float3>> BlendShapeDeltaVertices;
        [ReadOnly]
        public NativeArray<ErrorQuadric> BlendShapeTriangleErrorQuadrics;
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<ErrorQuadric> BlendShapeVertexErrorQuadrics;
        public void Execute(int vertexIndex)
        {
            var vertexCount = VertexPositionBuffer.Length;
            var triangleCount = Triangles.Length;
            var basePosition = VertexPositionBuffer[vertexIndex];
            for (int shape = 0; shape < BlendShapeDeltaVertices.Length; shape++)
            {
                var vertexPosition = basePosition + BlendShapeDeltaVertices[shape][vertexIndex];
                var vertexErrorQuadric = new ErrorQuadric();
                foreach (var triangleIndex in VertexContainingTriangles.GetValuesForKey(vertexIndex))
                {
                    var triangle = Triangles[triangleIndex];

                    int2x2 belongingEdges;
                    if (vertexIndex == triangle.x)
                    {
                        belongingEdges = new(triangle.xy, triangle.zx);
                    }
                    else if (vertexIndex == triangle.y)
                    {
                        belongingEdges = new(triangle.xy, triangle.yz);
                    }
                    else if (vertexIndex == triangle.z)
                    {
                        belongingEdges = new(triangle.yz, triangle.zx);
                    }
                    else
                    {
                        throw new Exception();
                    }

                    if (!Edges.Contains(belongingEdges.c0.yx) || !Edges.Contains(belongingEdges.c1.yx))
                    {
                        vertexErrorQuadric += new ErrorQuadric(new Plane(math.right(), vertexPosition));
                        vertexErrorQuadric += new ErrorQuadric(new Plane(math.up(), vertexPosition));
                        vertexErrorQuadric += new ErrorQuadric(new Plane(math.forward(), vertexPosition));
                    }
                    else
                    {
                        vertexErrorQuadric += BlendShapeTriangleErrorQuadrics[shape * triangleCount + triangleIndex];
                    }
                }
                BlendShapeVertexErrorQuadrics[shape * vertexCount + vertexIndex] = vertexErrorQuadric;
            }
        }
    }
}
