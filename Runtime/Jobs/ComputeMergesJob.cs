using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
namespace Meshia.MeshSimplification
{
    [BurstCompile]
    struct ComputeMergesJob : IJobParallelForDefer
    {
        [ReadOnly]
        public NativeArray<float3> VertexPositionBuffer;
        [ReadOnly]
        public NativeArray<ErrorQuadric> VertexErrorQuadrics;
        [ReadOnly]
        public NativeArray<float3> TriangleNormals;
        [ReadOnly]
        public NativeParallelMultiHashMap<int, int> VertexContainingTriangles;
        [ReadOnly]
        public NativeBitArray VertexIsBorderEdgeBits;
        [ReadOnly]
        public NativeArray<int2> Edges;
        [ReadOnly]
        public NativeArray<uint> VertexBlendIndicesBuffer;

        [ReadOnly]
        public NativeBitArray PreserveBorderEdgesBoneIndices;
        [WriteOnly]
        public NativeArray<VertexMerge> UnorderedDirtyVertexMerges;
        public bool PreserveBorderEdges;
        public bool PreserveSurfaceCurvature;
        [ReadOnly]
        public NativeArray<UnsafeList<float3>> BlendShapeDeltaVertices;
        [ReadOnly]
        public NativeArray<float> BlendShapeWeights;
        [ReadOnly]
        public NativeArray<ErrorQuadric> BlendShapeVertexErrorQuadrics;
        public BlendShapeErrorMode BlendShapeErrorMode;
        public void Execute(int index)
        {
            var mergeFactory = new MergeFactory
            {
                VertexPositionBuffer = VertexPositionBuffer,
                VertexErrorQuadrics = VertexErrorQuadrics,
                VertexContainingTriangles = VertexContainingTriangles,
                VertexIsBorderEdgeBits = VertexIsBorderEdgeBits,
                TriangleNormals = TriangleNormals,
                PreserveBorderEdges = PreserveBorderEdges,
                PreserveSurfaceCurvature = PreserveSurfaceCurvature,
                PreserveBorderEdgesBoneIndices = PreserveBorderEdgesBoneIndices,
                VertexBlendIndicesBuffer = VertexBlendIndicesBuffer,
                BlendShapeDeltaVertices = BlendShapeDeltaVertices,
                BlendShapeWeights = BlendShapeWeights,
                BlendShapeVertexErrorQuadrics = BlendShapeVertexErrorQuadrics,
                BlendShapeErrorMode = BlendShapeErrorMode,
            };
            var edge = Edges[index];
            VertexMerge merge;
            if (mergeFactory.TryComputeMerge(edge, out var position, out var cost))
            {
                merge = new()
                {
                    VertexAIndex = edge.x,
                    VertexBIndex = edge.y,
                    VertexAVersion = 0,
                    VertexBVersion = 0,
                    Position = position,
                    Cost = cost,
                };

            }
            else
            {

                merge = new()
                {
                    VertexAIndex = edge.x,
                    VertexBIndex = edge.y,
                    VertexAVersion = 0,
                    VertexBVersion = 0,
                    Position = float.NaN,
                    Cost = float.PositiveInfinity,
                };
            }
            UnorderedDirtyVertexMerges[index] = merge;

        }
    }
}


