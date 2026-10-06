#nullable enable
#if ENABLE_VRCFURY

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Meshia.MeshSimplification.Editor
{
    public class MeshiaVrcfuryBuildHook : IVRCSDKPreprocessAvatarCallback
    {
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
            var parameters = new List<(Mesh Mesh, MeshSimplificationTarget Target, MeshSimplifierOptions Options, BitArray? preserveBorderEdgesBoneIndices, Mesh Destination)>();

            foreach (var meshiaMeshSimplifier in meshiaMeshSimplifiers)
            {
                if (meshiaMeshSimplifier.enabled && meshiaMeshSimplifier.TryGetComponent<Renderer>(out var renderer))
                {
                    var sourceMesh = RendererUtility.GetRequiredMesh(renderer);
                    Mesh simplifiedMesh = new();
                    parameters.Add((sourceMesh, meshiaMeshSimplifier.target, meshiaMeshSimplifier.options, null, simplifiedMesh));
                }
            }

            // 2. Find MeshiaCascadingAvatarMeshSimplifier components
            var meshiaCascadingMeshSimplifiers = avatarRoot.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(true);
            foreach (var cascadingMeshSimplifier in meshiaCascadingMeshSimplifiers)
            {
                foreach (var entry in cascadingMeshSimplifier.Entries)
                {
                    if (entry.TargetRenderer == null || !entry.Enabled) continue;
                    var mesh = RendererUtility.GetRequiredMesh(entry.TargetRenderer);
                    var target = new MeshSimplificationTarget() { Kind = MeshSimplificationTargetKind.AbsoluteTriangleCount, Value = entry.TargetTriangleCount };
                    Mesh simplifiedMesh = new();

                    var preserveBorderEdgesBoneIndices = MeshiaCascadingAvatarMeshSimplifier.GetPreserveBorderEdgesBoneIndices(avatarRoot, cascadingMeshSimplifier, entry);

                    parameters.Add((mesh, target, entry.Options, preserveBorderEdgesBoneIndices, simplifiedMesh));
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
                    var (_, _, _, _, simplifiedMesh) = parameters[i++];
                    if (saveToDisk)
                    {
                        var path = $"Assets/MeshiaTemp/Mesh_{Guid.NewGuid():N}.asset";
                        AssetDatabase.CreateAsset(simplifiedMesh, path);
                    }
                    RendererUtility.SetMesh(renderer, simplifiedMesh);
                }
            }

            foreach (var cascadingMeshSimplifier in meshiaCascadingMeshSimplifiers)
            {
                foreach (var entry in cascadingMeshSimplifier.Entries)
                {
                    if (entry.TargetRenderer == null || !entry.Enabled) continue;
                    var (_, _, _, _, simplifiedMesh) = parameters[i++];
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
