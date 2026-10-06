#nullable enable
#if ENABLE_VRCFURY
using System.Diagnostics.CodeAnalysis;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meshia.MeshSimplification.Editor
{
    [CustomEditor(typeof(MeshiaMeshSimplifier))]
    [CanEditMultipleObjects]
    public class MeshiaMeshSimplifierEditor : UnityEditor.Editor
    {
        [SerializeField]
        VisualTreeAsset visualTreeAsset = null!;
        
        public override VisualElement CreateInspectorGUI()
        {
            if (visualTreeAsset == null)
            {
                var guids = AssetDatabase.FindAssets("MeshiaMeshSimplifierEditor t:VisualTreeAsset");
                if (guids.Length > 0)
                {
                    visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }

            VisualElement root = new();
            if (visualTreeAsset != null)
            {
                visualTreeAsset.CloneTree(root);
            }
            root.Bind(serializedObject);

            var ndmfNotImportedWarning = root.Q<HelpBox>("NdmfNotImportedWarning");
            if (ndmfNotImportedWarning != null)
            {
                DisplayStyle warningDisplayStyle;
#if ENABLE_VRCFURY
                warningDisplayStyle = DisplayStyle.None;
#else
                warningDisplayStyle = DisplayStyle.Flex;
#endif
                ndmfNotImportedWarning.style.display = warningDisplayStyle;
            }

            var bakeMeshButtonContainer = root.Q<IMGUIContainer>("BakeMeshButtonContainer");
            if (bakeMeshButtonContainer != null)
            {
                bakeMeshButtonContainer.onGUIHandler = () =>
                {
                    if (targets.Length == 1)
                    {
                        var ndmfMeshSimplifier = (MeshiaMeshSimplifier)target;
                        if (TryGetTargetMesh(ndmfMeshSimplifier, out var targetMesh))
                        {
                            if (GUILayout.Button("Bake mesh"))
                            {
                                var absolutePath = EditorUtility.SaveFilePanel(
                                            title: "Save baked mesh",
                                            directory: "",
                                            defaultName: $"{targetMesh.name}-Simplified.asset",
                                            extension: "asset");

                                if (!string.IsNullOrEmpty(absolutePath))
                                {
                                    Mesh simplifiedMesh = new();

                                    MeshSimplifier.Simplify(targetMesh, ndmfMeshSimplifier.target, ndmfMeshSimplifier.options, simplifiedMesh);

                                    AssetDatabase.CreateAsset(simplifiedMesh, Path.Join("Assets/", Path.GetRelativePath(Application.dataPath, absolutePath)));
                                }
                            }
                        }
                    }
                };
            }
            
            return root;
        }

        private static bool TryGetTargetMesh(MeshiaMeshSimplifier ndmfMeshSimplifier, [NotNullWhen(true)] out Mesh? targetMesh)
        {
            targetMesh = null;
            if (ndmfMeshSimplifier.TryGetComponent<MeshFilter>(out var meshFilter))
            {
                targetMesh = meshFilter.sharedMesh;
                if (targetMesh != null) 
                {
                    return true;
                }
            }
            if (ndmfMeshSimplifier.TryGetComponent<SkinnedMeshRenderer>(out var skinnedMeshRenderer))
            {
                targetMesh = skinnedMeshRenderer.sharedMesh; 
                if (targetMesh != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
