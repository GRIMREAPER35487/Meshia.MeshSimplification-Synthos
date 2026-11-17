#nullable enable
#if ENABLE_VRCFURY

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Meshia.MeshSimplification.Editor
{
    public class MeshiaVrcfuryBuildHook : IVRCSDKPreprocessAvatarCallback
    {
        /// <summary>
        /// Enabled entries with a renderer. A renderer listed by several simplifiers is simplified once: by the simplifier
        /// that owns it (nearest scope), otherwise by the first one, with a warning.
        /// </summary>
        static List<(MeshiaCascadingAvatarMeshSimplifier Simplifier, MeshiaCascadingAvatarMeshSimplifierRendererEntry Entry)> SelectCascadingEntries(MeshiaCascadingAvatarMeshSimplifier[] simplifiers)
        {
            var candidates = new List<(MeshiaCascadingAvatarMeshSimplifier Simplifier, MeshiaCascadingAvatarMeshSimplifierRendererEntry Entry)>();
            foreach (var simplifier in simplifiers)
            {
                foreach (var entry in simplifier.Entries)
                {
                    if (entry.TargetRenderer == null || !entry.Enabled) continue;
                    candidates.Add((simplifier, entry));
                }
            }

            var origins = new Dictionary<MeshiaCascadingAvatarMeshSimplifier, Transform?>();
            foreach (var simplifier in simplifiers) origins[simplifier] = simplifier.GetScopeOrigin();

            var winners = new Dictionary<Renderer, (MeshiaCascadingAvatarMeshSimplifier Simplifier, MeshiaCascadingAvatarMeshSimplifierRendererEntry Entry)>();
            foreach (var group in candidates.GroupBy(c => c.Entry.TargetRenderer!))
            {
                var claims = group.ToList();
                if (claims.Count == 1)
                {
                    winners[group.Key] = claims[0];
                    continue;
                }
                var owner = claims.FirstOrDefault(c =>
                {
                    var others = new HashSet<Transform>(origins.Where(o => o.Key != c.Simplifier && o.Value != null).Select(o => o.Value!));
                    return c.Simplifier.Owns(group.Key, others);
                });
                var winner = owner.Simplifier != null ? owner : claims[0];
                winners[group.Key] = winner;
                Debug.LogWarning($"[Meshia] '{group.Key.name}' is listed by {claims.Count} cascading simplifiers ({string.Join(", ", claims.Select(c => c.Simplifier.name))}). Only '{winner.Simplifier.name}' is applied. Remove it from the others or unlock them and reopen their inspector.", group.Key);
            }
            // Entries are records (value equality), so compare by reference.
            return candidates.Where(c => ReferenceEquals(winners[c.Entry.TargetRenderer!].Entry, c.Entry)).ToList();
        }

        // VRCFury runs at callbackOrder -10000.
        // We run at 10000 so we execute after VRCFury has merged everything, but before editor-only components are deleted.
        public int callbackOrder => 10000;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            try
            {
                Process(avatarRoot);
            }
            catch (Exception e)
            {
                Debug.LogException(e, avatarRoot);
            }
            return true;
        }

        public static void Process(GameObject avatarRoot)
        {
            // 1. Find MeshiaMeshSimplifier components
            var meshiaMeshSimplifiers = avatarRoot.GetComponentsInChildren<MeshiaMeshSimplifier>(true);
            var parameters = new List<(Mesh Mesh, MeshSimplificationTarget Target, MeshSimplifierOptions Options, BitArray? preserveBorderEdgesBoneIndices, float[]? BlendShapeWeights, Mesh Destination)>();
            MeshiaBlendShapeUsage? blendShapeUsage = null;

            foreach (var meshiaMeshSimplifier in meshiaMeshSimplifiers)
            {
                if (meshiaMeshSimplifier.enabled && meshiaMeshSimplifier.TryGetComponent<Renderer>(out var renderer))
                {
                    var sourceMesh = RendererUtility.GetRequiredMesh(renderer);
                    Mesh simplifiedMesh = new();
                    float[]? blendShapeWeights = null;
                    if (meshiaMeshSimplifier.options.PreserveBlendShapes)
                    {
                        blendShapeUsage ??= new MeshiaBlendShapeUsage(avatarRoot);
                        blendShapeWeights = blendShapeUsage.GetBlendShapeWeights(renderer, sourceMesh, meshiaMeshSimplifier.options, null);
                    }
                    parameters.Add((sourceMesh, meshiaMeshSimplifier.target, meshiaMeshSimplifier.options, null, blendShapeWeights, simplifiedMesh));
                }
            }

            // 2. Find MeshiaCascadingAvatarMeshSimplifier components
            var meshiaCascadingMeshSimplifiers = avatarRoot.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(true);
            var cascadingEntries = SelectCascadingEntries(meshiaCascadingMeshSimplifiers);
            foreach (var (cascadingMeshSimplifier, entry) in cascadingEntries)
            {
                {
                    var mesh = RendererUtility.GetRequiredMesh(entry.TargetRenderer);
                    var target = new MeshSimplificationTarget() { Kind = MeshSimplificationTargetKind.AbsoluteTriangleCount, Value = entry.TargetTriangleCount };
                    Mesh simplifiedMesh = new();

                    var preserveBorderEdgesBoneIndices = MeshiaCascadingAvatarMeshSimplifier.GetPreserveBorderEdgesBoneIndices(avatarRoot, cascadingMeshSimplifier, entry);

                    float[]? blendShapeWeights = null;
                    if (entry.Options.PreserveBlendShapes)
                    {
                        blendShapeUsage ??= new MeshiaBlendShapeUsage(avatarRoot);
                        blendShapeWeights = blendShapeUsage.GetBlendShapeWeights(entry.TargetRenderer, mesh, entry.Options, entry.BlendShapeSettings);
                    }

                    parameters.Add((mesh, target, entry.Options, preserveBorderEdgesBoneIndices, blendShapeWeights, simplifiedMesh));
                }
            }

            if (parameters.Count == 0) return;

            // Only save to disk if we are building the actual asset bundle (VRC upload / build)
            // In Unity Play Mode, in-memory meshes work perfectly and saving them to disk would cause lag and get deleted instantly.
            bool saveToDisk = !EditorApplication.isPlayingOrWillChangePlaymode;

            if (saveToDisk && !AssetDatabase.IsValidFolder("Assets/MeshiaTemp"))
            {
                AssetDatabase.CreateFolder("Assets", "MeshiaTemp");
            }

            // Run the simplification batch
            MeshSimplifier.SimplifyBatch(parameters);

            // Save simplified meshes as assets and apply them
            int i = 0;
            foreach (var meshiaMeshSimplifier in meshiaMeshSimplifiers)
            {
                if (meshiaMeshSimplifier.enabled && meshiaMeshSimplifier.TryGetComponent<Renderer>(out var renderer))
                {
                    var (_, _, _, _, _, simplifiedMesh) = parameters[i++];
                    if (saveToDisk)
                    {
                        var path = $"Assets/MeshiaTemp/Mesh_{Guid.NewGuid():N}.asset";
                        AssetDatabase.CreateAsset(simplifiedMesh, path);
                    }
                    RendererUtility.SetMesh(renderer, simplifiedMesh);
                }
            }

            foreach (var (_, entry) in cascadingEntries)
            {
                {
                    var (_, _, _, _, _, simplifiedMesh) = parameters[i++];
                    if (saveToDisk)
                    {
                        var path = $"Assets/MeshiaTemp/Mesh_{Guid.NewGuid():N}.asset";
                        AssetDatabase.CreateAsset(simplifiedMesh, path);
                    }
                    RendererUtility.SetMesh(entry.TargetRenderer, simplifiedMesh);
                }
            }

            // Schedule deletion of the temporary folder once the build finishes and control returns to the editor
            if (saveToDisk)
            {
                EditorApplication.delayCall += () =>
                {
                    if (AssetDatabase.IsValidFolder("Assets/MeshiaTemp"))
                    {
                        AssetDatabase.DeleteAsset("Assets/MeshiaTemp");
                    }
                };
            }

            // Destroy components so they don't end up on the final upload
            foreach (var meshiaMeshSimplifier in meshiaMeshSimplifiers)
            {
                UnityEngine.Object.DestroyImmediate(meshiaMeshSimplifier);
            }
            foreach (var cascadingMeshSimplifier in meshiaCascadingMeshSimplifiers)
            {
                UnityEngine.Object.DestroyImmediate(cascadingMeshSimplifier);
            }
        }
    }
}
#endif
