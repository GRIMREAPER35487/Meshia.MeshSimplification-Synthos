#nullable enable
#if ENABLE_VRCFURY

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Meshia.MeshSimplification.Editor
{
    /// <summary>
    /// Decides which blend shapes of a renderer matter for <see cref="MeshSimplifierOptions.PreserveBlendShapes"/>.
    /// A blend shape matters when it is animated by any controller on the avatar, or when it is non-zero on the renderer.
    /// This runs after VRCFury, so blend shapes baked by VRCFury's Blendshape Optimizer are already gone from the mesh.
    /// </summary>
    internal sealed class MeshiaBlendShapeUsage
    {
        const string BlendShapePropertyPrefix = "blendShape.";

        readonly GameObject avatarRoot;
        // (binding path, blend shape name) pairs of every SkinnedMeshRenderer blend shape curve on the avatar.
        readonly List<(string Path, string Name)> animatedBlendShapes = new();

        public MeshiaBlendShapeUsage(GameObject avatarRoot)
        {
            this.avatarRoot = avatarRoot;

            var clips = new HashSet<AnimationClip>();
            var dependencies = EditorUtility.CollectDependencies(new Object[] { avatarRoot });
            foreach (var dependency in dependencies)
            {
                switch (dependency)
                {
                    case AnimationClip clip:
                        clips.Add(clip);
                        break;
                    case RuntimeAnimatorController controller:
                        clips.UnionWith(controller.animationClips.Where(c => c != null));
                        break;
                }
            }
            foreach (var animator in avatarRoot.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController != null)
                {
                    clips.UnionWith(animator.runtimeAnimatorController.animationClips.Where(c => c != null));
                }
            }

            var seen = new HashSet<(string, string)>();
            foreach (var clip in clips)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith(BlendShapePropertyPrefix))
                    {
                        var entry = (binding.path, binding.propertyName.Substring(BlendShapePropertyPrefix.Length));
                        if (seen.Add(entry))
                        {
                            animatedBlendShapes.Add(entry);
                        }
                    }
                }
            }
        }

        bool IsAnimated(string rendererPath, string blendShapeName)
        {
            foreach (var (path, name) in animatedBlendShapes)
            {
                if (name != blendShapeName)
                {
                    continue;
                }
                // Curves on the avatar root animator use the full path. Curves on a nested animator use a path relative to it.
                if (path == rendererPath || (path.Length != 0 && rendererPath.EndsWith("/" + path)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Computes the per blend shape error weight array for <paramref name="mesh"/>, or <see langword="null"/> when <paramref name="options"/> does not preserve blend shapes.
        /// </summary>
        public float[]? GetBlendShapeWeights(Renderer renderer, Mesh mesh, MeshSimplifierOptions options, IReadOnlyList<MeshiaBlendShapeSetting>? settings)
        {
            if (!options.PreserveBlendShapes || mesh.blendShapeCount == 0)
            {
                return null;
            }
            var rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot.transform);
            var skinnedMeshRenderer = renderer as SkinnedMeshRenderer;

            var weights = new float[mesh.blendShapeCount];
            var considered = new List<string>();
            var ignored = 0;
            for (int shapeIndex = 0; shapeIndex < weights.Length; shapeIndex++)
            {
                var name = mesh.GetBlendShapeName(shapeIndex);
                var setting = settings?.FirstOrDefault(s => s.Name == name);
                var mode = setting?.Mode ?? MeshiaBlendShapeMode.Auto;
                var weight = setting?.Weight ?? 1f;
                string? reason = null;
                switch (mode)
                {
                    case MeshiaBlendShapeMode.Include:
                        reason = "included";
                        break;
                    case MeshiaBlendShapeMode.Exclude:
                        break;
                    default:
                        if (IsAnimated(rendererPath, name))
                        {
                            reason = "animated";
                        }
                        else if (skinnedMeshRenderer != null && skinnedMeshRenderer.GetBlendShapeWeight(shapeIndex) != 0)
                        {
                            reason = "active";
                        }
                        break;
                }
                if (reason != null && weight > 0)
                {
                    weights[shapeIndex] = weight;
                    considered.Add(weight == 1f ? $"{name} ({reason})" : $"{name} ({reason}, x{weight:0.##})");
                }
                else
                {
                    ignored++;
                }
            }
            Debug.Log($"[Meshia] '{renderer.name}': blend shape candidates: {(considered.Count == 0 ? "none" : string.Join(", ", considered))}; {ignored} ignored (excluded, or not animated and zero).", renderer);
            return weights;
        }
    }
}
#endif
