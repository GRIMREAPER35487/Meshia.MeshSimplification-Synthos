#if ENABLE_VRCFURY

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Pool;
using System.Diagnostics.CodeAnalysis;
using System.Collections;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    [AddComponentMenu("Meshia Mesh Simplification/Meshia Cascading Avatar Mesh Simplifier")]
    public class MeshiaCascadingAvatarMeshSimplifier : MonoBehaviour
#if ENABLE_VRCHAT_BASE
    , VRC.SDKBase.IEditorOnly
#endif
    {
        public List<MeshiaCascadingAvatarMeshSimplifierRendererEntry> Entries = new();
        public int TargetTriangleCount = 70000;
        public bool AutoAdjustEnabled = true;
        public bool AccountForBase = false;
        [Tooltip("Distribute the budget by simplification error: meshes that lose little detail per removed triangle are reduced first, instead of reducing every mesh by the same percentage.")]
        public bool SmartAllocation = true;

        [Tooltip("Lock the renderer list. Entries are never added or removed automatically, so this simplifier keeps exactly the meshes it has now.")]
        public bool LockEntries = false;

        /// <summary>
        /// Makes the entries match the renderers this simplifier owns: adds new ones and removes entries whose renderer is
        /// outside its scope or owned by another simplifier. Entries with a missing renderer are kept (see Remove Invalid Entries).
        /// Does nothing when <see cref="LockEntries"/> is set.
        /// </summary>
        public void RefreshEntries()
        {
            if (LockEntries) return;
            using (ListPool<Renderer>.Get(out var ownedRenderers))
            {
                GetOwnedRenderers(ownedRenderers);
                var owned = new HashSet<Renderer>(ownedRenderers);

                // Cross-talk cleanup: drop entries for renderers this simplifier does not own.
                Entries.RemoveAll(entry => entry.TargetRenderer != null && !owned.Contains(entry.TargetRenderer));

                var currentEntries = Entries.Select(t => t.TargetRenderer);
                var addedEntries = ownedRenderers.Except(currentEntries).Where(MeshiaCascadingAvatarMeshSimplifierRendererEntry.IsValidTarget).Select(renderer => new MeshiaCascadingAvatarMeshSimplifierRendererEntry(renderer!)).ToArray();

                Entries.AddRange(addedEntries);
            }
        }

        /// <summary>
        /// The root of the hierarchy this simplifier manages. On an object that has meshes below it (e.g. an outfit root)
        /// that is the object itself; on a dedicated empty object it is the parent.
        /// </summary>
        public Transform? GetScopeOrigin()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is MeshRenderer or SkinnedMeshRenderer)
                {
                    return transform;
                }
            }
            return transform.parent;
        }

        /// <summary>Whether <paramref name="renderer"/> is in this simplifier's scope and not in a nested scope of another simplifier.</summary>
        public bool Owns(Renderer renderer, IReadOnlyCollection<Transform> otherScopeOrigins)
        {
            var origin = GetScopeOrigin();
            if (origin == null || !renderer.transform.IsChildOf(origin)) return false;
            for (var current = renderer.transform; current != origin; current = current.parent)
            {
                if (otherScopeOrigins.Contains(current)) return false;
            }
            return true;
        }

        private void GetOwnedRenderers(List<Renderer> ownedRenderers)
        {
            var myScopeOrigin = GetScopeOrigin();

            if (myScopeOrigin == null)
            {
                throw new InvalidOperationException($"{nameof(MeshiaCascadingAvatarMeshSimplifier)} should not be attached to root GameObject.");
            }

            // Look at every simplifier on the avatar, including ones on inactive outfits, so ownership does not depend on what is toggled on.
            var otherScopeOrigins = new HashSet<Transform>();
            foreach (var other in myScopeOrigin.root.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(true))
            {
                if (other == this) continue;
                var otherScopeOrigin = other.GetScopeOrigin();
                if (otherScopeOrigin == null) continue;
                if (otherScopeOrigin == myScopeOrigin)
                {
                    throw new InvalidOperationException($"Multiple {nameof(MeshiaCascadingAvatarMeshSimplifier)} manage the same object '{myScopeOrigin.name}'. Put each one on its own outfit (or on its own child object).");
                }
                otherScopeOrigins.Add(otherScopeOrigin);
            }

            foreach (var childRenderer in myScopeOrigin.GetComponentsInChildren<Renderer>(true))
            {
                if (childRenderer is MeshRenderer or SkinnedMeshRenderer && Owns(childRenderer, otherScopeOrigins))
                {
                    ownedRenderers.Add(childRenderer);
                }
            }
        }

        public static BitArray? GetPreserveBorderEdgesBoneIndices(GameObject avatarRoot, MeshiaCascadingAvatarMeshSimplifier avatarMeshSimplifier, MeshiaCascadingAvatarMeshSimplifierRendererEntry entry)
        {
            if (avatarRoot.TryGetComponent(out Animator avatarAnimator) && entry.TargetRenderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                var bones = skinnedMeshRenderer.bones;
                var preserveBorderEdgeBoneIndices = new BitArray(bones.Length);

                for (ulong boneMask = entry.PreserveBorderEdgesBones; boneMask != 0ul; boneMask &= boneMask - 1)
                {
                    var bone = (HumanBodyBones)math.tzcnt(boneMask);
                    var boneTransform = avatarAnimator.GetBoneTransform(bone);
                    if (boneTransform != null)
                    {
                        var boneIndex = Array.IndexOf(bones, boneTransform);
                        if (boneIndex != -1)
                        {
                            preserveBorderEdgeBoneIndices.Set(boneIndex, true);
                        }
                    }
                }
                return preserveBorderEdgeBoneIndices;
            }
            else
            {
                return null;
            }
        }
    }

    public enum MeshiaBlendShapeMode
    {
        /// <summary>Considered if it is animated or currently non-zero on the renderer.</summary>
        Auto,
        /// <summary>Always considered.</summary>
        Include,
        /// <summary>Never considered.</summary>
        Exclude,
    }

    [Serializable]
    public class MeshiaBlendShapeSetting
    {
        public string Name = "";
        public MeshiaBlendShapeMode Mode = MeshiaBlendShapeMode.Auto;
        [Tooltip("Multiplier of this blend shape's error. Lower it for rarely used shapes.")]
        [Min(0)]
        public float Weight = 1f;
    }

    [Serializable]
    public record MeshiaCascadingAvatarMeshSimplifierRendererEntry
    {
        public Renderer? TargetRenderer;
        public int TargetTriangleCount;
        public MeshSimplifierOptions Options = MeshSimplifierOptions.Default;
        public ulong PreserveBorderEdgesBones =
            (1ul << (int)HumanBodyBones.LeftHand) |
            (1ul << (int)HumanBodyBones.RightHand) |
            (1ul << (int)HumanBodyBones.LeftThumbProximal) |
            (1ul << (int)HumanBodyBones.LeftThumbIntermediate) |
            (1ul << (int)HumanBodyBones.LeftThumbDistal) |
            (1ul << (int)HumanBodyBones.LeftIndexProximal) |
            (1ul << (int)HumanBodyBones.LeftIndexIntermediate) |
            (1ul << (int)HumanBodyBones.LeftIndexDistal) |
            (1ul << (int)HumanBodyBones.LeftMiddleProximal) |
            (1ul << (int)HumanBodyBones.LeftMiddleIntermediate) |
            (1ul << (int)HumanBodyBones.LeftMiddleDistal) |
            (1ul << (int)HumanBodyBones.LeftRingProximal) |
            (1ul << (int)HumanBodyBones.LeftRingIntermediate) |
            (1ul << (int)HumanBodyBones.LeftRingDistal) |
            (1ul << (int)HumanBodyBones.LeftLittleProximal) |
            (1ul << (int)HumanBodyBones.LeftLittleIntermediate) |
            (1ul << (int)HumanBodyBones.LeftLittleDistal) |
            (1ul << (int)HumanBodyBones.RightThumbProximal) |
            (1ul << (int)HumanBodyBones.RightThumbIntermediate) |
            (1ul << (int)HumanBodyBones.RightThumbDistal) |
            (1ul << (int)HumanBodyBones.RightIndexProximal) |
            (1ul << (int)HumanBodyBones.RightIndexIntermediate) |
            (1ul << (int)HumanBodyBones.RightIndexDistal) |
            (1ul << (int)HumanBodyBones.RightMiddleProximal) |
            (1ul << (int)HumanBodyBones.RightMiddleIntermediate) |
            (1ul << (int)HumanBodyBones.RightMiddleDistal) |
            (1ul << (int)HumanBodyBones.RightRingProximal) |
            (1ul << (int)HumanBodyBones.RightRingIntermediate) |
            (1ul << (int)HumanBodyBones.RightRingDistal) |
            (1ul << (int)HumanBodyBones.RightLittleProximal) |
            (1ul << (int)HumanBodyBones.RightLittleIntermediate) |
            (1ul << (int)HumanBodyBones.RightLittleDistal);
        public bool Enabled = true;
        public bool Fixed = false;
        /// <summary>Importance multiplier used by smart allocation. Higher keeps more triangles on this mesh.</summary>
        [Tooltip("Smart Allocation importance. Higher keeps more triangles on this mesh (e.g. 2-4 for face/hair), lower lets it be reduced first (e.g. 0.25 for clothing hidden under other clothing).")]
        [Min(0)]
        public float Priority = 1f;
        /// <summary>
        /// Per blend shape settings used when <see cref="MeshSimplifierOptions.PreserveBlendShapes"/> is enabled.
        /// Blend shapes not listed here use <see cref="MeshiaBlendShapeMode.Auto"/> with weight 1.
        /// </summary>
        public List<MeshiaBlendShapeSetting> BlendShapeSettings = new();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("For serialization only", true)]
#pragma warning disable CS8618
        private MeshiaCascadingAvatarMeshSimplifierRendererEntry()
#pragma warning restore CS8618
        {
        }
        public MeshiaCascadingAvatarMeshSimplifierRendererEntry(Renderer renderer)
        {
            TargetRenderer = renderer;
            TargetTriangleCount = RendererUtility.GetMesh(renderer)?.GetTriangleCount() ?? 0;
        }

        internal static bool IsValidTarget([NotNullWhen(true)] Renderer? renderer)
        {
            if (renderer == null) return false;
            if (IsEditorOnlyInHierarchy(renderer.gameObject)) return false;
            if (renderer is not SkinnedMeshRenderer and not MeshRenderer) return false;
            var mesh = RendererUtility.GetMesh(renderer);
            if (mesh == null || mesh.GetTriangleCount() == 0) return false;
            return true;
        }

        internal bool IsValid(MeshiaCascadingAvatarMeshSimplifier container) => IsValidTarget(TargetRenderer);

        internal static bool IsEditorOnlyInHierarchy(GameObject gameObject)
        {
            if (gameObject == null) return false;
            Transform current = gameObject.transform;
            while (current != null)
            {
                if (current.CompareTag("EditorOnly"))
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }
    }
}
#endif
