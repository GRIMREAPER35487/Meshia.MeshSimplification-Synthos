#nullable enable
using System;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.CompilerServices;
namespace Meshia.MeshSimplification
{
    [Serializable]
    public struct MeshSimplifierOptions : IEquatable<MeshSimplifierOptions>
    {
        public static MeshSimplifierOptions Default => new()
        {
            PreserveBorderEdges = true,
            PreserveSurfaceCurvature = false,
            UseBarycentricCoordinateInterpolation = false,
            UseBarycentricCoordinateInterpolationForUV = false,
            MinNormalDot = 0.2f,
            EnableSmartLink = true,
            VertexLinkDistance = 0.0001f,
            VertexLinkMinNormalDot = 0.95f,
            VertexLinkColorDistance = 0.01f,
            VertexLinkUvDistance = 0.001f,
            PreserveBlendShapes = false,
            BlendShapeErrorMode = BlendShapeErrorMode.Max,
            MaxPreservedBlendShapes = 8,
        };

        /// <summary>
        /// If you want to suppress hole generation during simplification, enable this option.
        /// </summary>
        [Tooltip("If you want to suppress hole generation during simplification, enable this option.")]
        public bool PreserveBorderEdges;
        public bool PreserveSurfaceCurvature;
        /// <summary>
        /// If you find that the texture is distorted, try toggling this option.
        /// </summary>
        [Tooltip("If you find that the texture is distorted, try toggling this option.")]
        public bool UseBarycentricCoordinateInterpolation;
        public bool UseBarycentricCoordinateInterpolationForUV;
        /// <summary>
        /// If this option is enabled, vertices that are not originally connected but are close to each other will be included in the first merge candidates. <br/>
        /// Increases the initialization cost.
        /// </summary>
        [Tooltip("If this option is enabled, vertices that are not originally connected but are close to each other will be included in the first merge candidates. \n" +
            "Increases the initialization cost.")]
        public bool EnableSmartLink;
        [Range(-1, 1)]
        public float MinNormalDot;
        /// <summary>
        /// When smart link is enabled, this is used to select candidates for merging vertices that are not originally connected to each other. <br/>
        /// Increasing this value also increases the initialization cost.
        /// </summary>
        [Tooltip("When smart link is enabled, this is used to select candidates for merging vertices that are not originally connected to each other. \n" +
            "Increasing this value also increases the initialization cost.")]
        public float VertexLinkDistance;
        [Range(-1, 1)]
        public float VertexLinkMinNormalDot;
        // This could be HDR color, so there is no Range.
        public float VertexLinkColorDistance;
        [Range(0, 1.41421356237f)]
        public float VertexLinkUvDistance;

        /// <summary>
        /// If this option is enabled, the error of each removal is also measured on the mesh with each considered blend shape fully applied,
        /// so regions that are flat in the base mesh but deform under a blend shape keep their density. <br/>
        /// Also fixes blend shape delta normal/tangent interpolation on merged vertices.
        /// </summary>
        [Tooltip("Also measure removal error with each considered blend shape fully applied, so regions that deform under a blend shape keep their density.")]
        public bool PreserveBlendShapes;
        /// <summary>
        /// How the per blend shape errors are combined with the base shape error.
        /// </summary>
        [Tooltip("Max: a removal is only cheap if it is cheap in every shape. Weighted Sum: errors of all shapes are added up.")]
        public BlendShapeErrorMode BlendShapeErrorMode;
        /// <summary>
        /// The maximum number of blend shapes that are considered. 0 means no limit.
        /// </summary>
        [Tooltip("The maximum number of blend shapes that are considered (largest deformation first). 0 means no limit.")]
        public int MaxPreservedBlendShapes;


        public readonly override bool Equals(object obj)
        {
            return obj is MeshSimplifierOptions options && Equals(options);
        }

        public readonly bool Equals(MeshSimplifierOptions other)
        {
            return PreserveBorderEdges == other.PreserveBorderEdges &&
                   PreserveSurfaceCurvature == other.PreserveSurfaceCurvature &&
                   UseBarycentricCoordinateInterpolation == other.UseBarycentricCoordinateInterpolation &&
                   UseBarycentricCoordinateInterpolationForUV == other.UseBarycentricCoordinateInterpolationForUV &&
                   EnableSmartLink == other.EnableSmartLink &&
                   MinNormalDot == other.MinNormalDot &&
                   VertexLinkDistance == other.VertexLinkDistance &&
                   VertexLinkMinNormalDot == other.VertexLinkMinNormalDot &&
                   VertexLinkColorDistance == other.VertexLinkColorDistance &&
                   VertexLinkUvDistance == other.VertexLinkUvDistance &&
                   PreserveBlendShapes == other.PreserveBlendShapes &&
                   BlendShapeErrorMode == other.BlendShapeErrorMode &&
                   MaxPreservedBlendShapes == other.MaxPreservedBlendShapes;
        }

        public readonly override int GetHashCode()
        {
            return HashCode.Combine(PreserveBorderEdges, PreserveSurfaceCurvature, MinNormalDot);
        }

        public static bool operator ==(MeshSimplifierOptions left, MeshSimplifierOptions right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MeshSimplifierOptions left, MeshSimplifierOptions right)
        {
            return !(left == right);
        }
    }

    public enum BlendShapeErrorMode
    {
        /// <summary>The cost of a removal is the maximum of the base error and each weighted blend shape error.</summary>
        Max,
        /// <summary>The cost of a removal is the base error plus the sum of the weighted blend shape errors.</summary>
        WeightedSum,
    }
}
