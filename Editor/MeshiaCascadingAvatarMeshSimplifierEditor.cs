#nullable enable
#if ENABLE_VRCFURY

using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using VRC.SDKBase;

namespace Meshia.MeshSimplification.Editor
{
    [CustomEditor(typeof(MeshiaCascadingAvatarMeshSimplifier))]
    internal class MeshiaCascadingAvatarMeshSimplifierEditor : UnityEditor.Editor
    {
        [SerializeField] VisualTreeAsset editorVisualTreeAsset = null!;
        [SerializeField] VisualTreeAsset entryEditorVisualTreeAsset = null!;
        private MeshiaCascadingAvatarMeshSimplifier Target => (MeshiaCascadingAvatarMeshSimplifier)target;

        private SerializedProperty AutoAdjustEnabledProperty => serializedObject.FindProperty(nameof(MeshiaCascadingAvatarMeshSimplifier.AutoAdjustEnabled));
        private SerializedProperty AccountForBaseProperty => serializedObject.FindProperty(nameof(MeshiaCascadingAvatarMeshSimplifier.AccountForBase));
        private SerializedProperty TargetTriangleCountProperty => serializedObject.FindProperty(nameof(MeshiaCascadingAvatarMeshSimplifier.TargetTriangleCount));
        private SerializedProperty EntriesProperty => serializedObject.FindProperty(nameof(MeshiaCascadingAvatarMeshSimplifier.Entries));


        [MenuItem("GameObject/Meshia Mesh Simplification/Meshia Cascading Avatar Mesh Simplifier", false, 0)]
        static void AddCascadingAvatarMeshSimplifier()
        {
            var go = new GameObject("Meshia Cascading Avatar Mesh Simplifier");
            go.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            go.transform.parent = Selection.activeGameObject.transform;
            Undo.RegisterCreatedObjectUndo(go, "Create Meshia Cascading Avatar Mesh Simplifier");
        }
        private void OnEnable()
        {
            RefreshEntries();
        }

        private void RefreshEntries()
        {
            if(Target.transform.parent == null)
            {
                return;
            }
            Undo.RecordObject(Target, "Get entries");
            try
            {
                Target.RefreshEntries();
            }
            catch (InvalidOperationException e)
            {
                Debug.LogException(e, target);
                return;
            }

            serializedObject.Update();
        }

        public override VisualElement CreateInspectorGUI()
        {
            if (editorVisualTreeAsset == null)
            {
                var guids = AssetDatabase.FindAssets("MeshiaCascadingAvatarMeshSimplifierEditor t:VisualTreeAsset");
                if (guids.Length > 0)
                {
                    editorVisualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }
            if (entryEditorVisualTreeAsset == null)
            {
                var guids = AssetDatabase.FindAssets("MeshiaCascadingAvatarMeshSimplifierRendererEntryEditor t:VisualTreeAsset");
                if (guids.Length > 0)
                {
                    entryEditorVisualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }

            VisualElement root = new();
            if (editorVisualTreeAsset != null)
            {
                editorVisualTreeAsset.CloneTree(root);
            }

            serializedObject.Update();
            
            root.Bind(serializedObject);
            var attachedToRootWarning = root.Q<HelpBox>("AttachedToRootWarning");
            var mainElement = root.Q<VisualElement>("MainElement");
            var targetTriangleCountField = root.Q<IntegerField>("TargetTriangleCountField");
            var targetTriangleCountPresetDropdownField = root.Q<DropdownField>("TargetTriangleCountPresetDropdownField");
            var adjustButton = root.Q<Button>("AdjustButton");
            var autoAdjustEnabledToggle = root.Q<Toggle>("AutoAdjustEnabledToggle");
            var accountForBaseToggle = root.Q<Toggle>("AccountForBaseToggle");
            var triangleCountLabel = root.Q<IMGUIContainer>("TriangleCountLabel");

            var removeInvalidEntriesButton = root.Q<Button>("RemoveInvalidEntriesButton");
            var resetButton = root.Q<Button>("ResetButton");
            var entriesListView = root.Q<ListView>("EntriesListView");

            if (attachedToRootWarning != null)
            {
                attachedToRootWarning.style.display = Target.transform.parent == null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (targetTriangleCountField != null)
            {
                targetTriangleCountField.RegisterValueChangedCallback(changeEvent =>
                {
                    if (!TargetTriangleCountPresetValueToName.TryGetValue(changeEvent.newValue, out var name))
                    {
                        name = "Custom";
                    }
                    if (targetTriangleCountPresetDropdownField != null)
                    {
                        targetTriangleCountPresetDropdownField.SetValueWithoutNotify(name);
                    }
                    if (AutoAdjustEnabledProperty.boolValue)
                    {
                        AdjustQuality();
                        serializedObject.ApplyModifiedProperties();
                    }
                });
            }

            if (targetTriangleCountPresetDropdownField != null)
            {
                targetTriangleCountPresetDropdownField.choices = TargetTriangleCountPresetNameToValue.Keys.ToList();
                targetTriangleCountPresetDropdownField.RegisterValueChangedCallback(changeEvent =>
                {
                    if(TargetTriangleCountPresetNameToValue.TryGetValue(changeEvent.newValue, out var value))
                    {
                        TargetTriangleCountProperty.intValue = value;
                        serializedObject.ApplyModifiedProperties();
                    }
                });
            }

            if (adjustButton != null)
            {
                adjustButton.clicked += () =>
                {
                    AdjustQuality();
                    serializedObject.ApplyModifiedProperties();
                };
            }

            if (autoAdjustEnabledToggle != null)
            {
                autoAdjustEnabledToggle.RegisterValueChangedCallback(changeEvent =>
                {
                    var autoAdjustEnabled = AutoAdjustEnabledProperty.boolValue;

                    if (autoAdjustEnabled)
                    {
                        AdjustQuality();
                        serializedObject.ApplyModifiedProperties();
                    }
                });
            }

            if (accountForBaseToggle != null)
            {
                accountForBaseToggle.RegisterValueChangedCallback(changeEvent =>
                {
                    if (AutoAdjustEnabledProperty.boolValue)
                    {
                        AdjustQuality();
                        serializedObject.ApplyModifiedProperties();
                    }
                });
            }

            if (triangleCountLabel != null)
            {
                triangleCountLabel.onGUIHandler = () =>
                {
                    var current = GetTotalSimplifiedTriangleCount(false);
                    var sum = GetTotalOriginalTriangleCount();
                    var countLabel = $"Current: {current} / {sum}";
                    var labelWidth1 = 7f * countLabel.ToString().Count();
                    var isOverflow = TargetTriangleCountProperty.intValue < current;
                    if (isOverflow) EditorGUILayout.LabelField(countLabel + " - Overflow!", GUIStyleHelper.RedStyle, GUILayout.Width(labelWidth1));
                    else EditorGUILayout.LabelField(countLabel, GUILayout.Width(labelWidth1));
                };
            }

            if (removeInvalidEntriesButton != null)
            {
                removeInvalidEntriesButton.clicked += () =>
                {
                    var target = Target;
                    var entries = target.Entries;

                    Undo.RecordObject(target, "Remove Invalid Entries");
                    for (int i = 0; i < entries.Count;)
                    {
                        var entry = entries[i];
                        if(entry.IsValid(target))
                        {
                            i++;
                        }
                        else
                        {
                            entries.RemoveAt(i);
                        }
                    }
                    serializedObject.Update();
                };
            }

            if (resetButton != null)
            {
                resetButton.clicked += () =>
                {
                    var originalTriangleCount = GetTotalOriginalTriangleCount();

                    var quality = TargetTriangleCountProperty.intValue / (float)originalTriangleCount;

                    var entriesProperty = EntriesProperty;
                    var arraySize = entriesProperty.arraySize;
                    for (int i = 0; i < arraySize; i++)
                    {
                        var entryProperty = entriesProperty.GetArrayElementAtIndex(i);
                        entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.Enabled)).boolValue = true;
                        entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.Fixed)).boolValue = false;
                    }

                    SetQualityAll(quality);
                    serializedObject.ApplyModifiedProperties();
                };
            }

            if (entriesListView != null)
            {
                entriesListView.bindItem = (itemElement, index) =>
                {
                    var entry = Target.Entries[index];
                    var entryProperty = EntriesProperty.GetArrayElementAtIndex(index);
                    var itemRoot = (TemplateContainer)itemElement;
                    var targetObjectField = itemRoot.Q<ObjectField>("TargetObjectField");
                    var targetPathField = itemRoot.Q<TextField>("TargetPathField");
                    var targetTriangleCountSlider = itemRoot.Q<SliderInt>("TargetTriangleCountSlider");
                    var targetTriangleCountField = itemRoot.Q<IntegerField>("TargetTriangleCountField");
                    var originalTriangleCountField = itemRoot.Q<IntegerField>("OriginalTriangleCountField");
                    var unknownOriginalTriangleCountField = itemRoot.Q<TextField>("UnknownOriginalTriangleCountField");
                    var preserveBorderEdgesBonesFoldout = itemRoot.Q<Foldout>("PreserveBorderEdgesBonesFoldout");
                    itemRoot.BindProperty(entryProperty);
                    itemRoot.userData = index;
                    
                    var targetRenderer = entry.TargetRenderer;
                    if (targetObjectField != null)
                    {
                        targetObjectField.value = targetRenderer;
                        targetObjectField.style.display = DisplayStyle.Flex;
                        if (targetRenderer != null)
                        {
                            targetObjectField.EnableInClassList("editor-only", MeshiaCascadingAvatarMeshSimplifierRendererEntry.IsEditorOnlyInHierarchy(targetRenderer.gameObject));
                        }
                    }

                    if (targetPathField != null)
                    {
                        targetPathField.style.display = DisplayStyle.None;
                    }

                    if(TryGetOriginalTriangleCount(entry, false, out var originalTriangleCount))
                    {
                        if (targetTriangleCountSlider != null)
                        {
                            targetTriangleCountSlider.highValue = originalTriangleCount;
                        }

                        if (originalTriangleCountField != null)
                        {
                            originalTriangleCountField.style.display = DisplayStyle.Flex;
                            originalTriangleCountField.value = originalTriangleCount;
                        }

                        if (unknownOriginalTriangleCountField != null)
                        {
                            unknownOriginalTriangleCountField.style.display = DisplayStyle.None;
                        }
                    }
                    else
                    {
                        if (targetTriangleCountSlider != null)
                        {
                            targetTriangleCountSlider.visible = false;
                        }
                        
                        if (unknownOriginalTriangleCountField != null)
                        {
                            unknownOriginalTriangleCountField.style.display = DisplayStyle.Flex;
                        }

                        if (originalTriangleCountField != null)
                        {
                            originalTriangleCountField.style.display = DisplayStyle.None;
                        }
                    }

                    if (preserveBorderEdgesBonesFoldout != null)
                    {
                        var humanBodyBoneIndex = 0;
                        var preserveBorderEdgesBonesProperty = EntriesProperty.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.PreserveBorderEdgesBones));
                        var preserveBorderEdgesBones = preserveBorderEdgesBonesProperty.ulongValue;
                        foreach (var preserveBorderEdgesBoneToggle in preserveBorderEdgesBonesFoldout.Children().OfType<Toggle>())
                        {
                            preserveBorderEdgesBoneToggle.value = (preserveBorderEdgesBones & (1ul << humanBodyBoneIndex)) != 0ul;

                            humanBodyBoneIndex++;
                        }
                    }
                };

                entriesListView.makeItem = () =>
                {
                    if (entryEditorVisualTreeAsset == null)
                    {
                        return new VisualElement();
                    }
                    var itemRoot = entryEditorVisualTreeAsset.CloneTree();
                    var enabledToggle = itemRoot.Q<Toggle>("EnabledToggle");
                    var targetObjectField = itemRoot.Q<ObjectField>("TargetObjectField");
                    var targetTriangleCountSlider = itemRoot.Q<SliderInt>("TargetTriangleCountSlider");
                    var targetTriangleCountField = itemRoot.Q<IntegerField>("TargetTriangleCountField");
                    var triangleCountDivider = itemRoot.Q<Label>("TriangleCountDivider");
                    var optionsToggle = itemRoot.Q<Toggle>("OptionsToggle");
                    var optionsField = itemRoot.Q<PropertyField>("OptionsField");
                    var preserveBorderEdgesBonesFoldout = itemRoot.Q<Foldout>("PreserveBorderEdgesBonesFoldout");
                    
                    if (enabledToggle != null)
                    {
                        enabledToggle.RegisterValueChangedCallback(changeEvent =>
                        {
                            var enabled = changeEvent.newValue;

                            if (targetTriangleCountSlider != null) targetTriangleCountSlider.visible = enabled;
                            if (targetTriangleCountField != null) targetTriangleCountField.visible = enabled;
                            if (triangleCountDivider != null) triangleCountDivider.visible = enabled;

                            if (AutoAdjustEnabledProperty.boolValue)
                            {
                                AdjustQuality();
                                serializedObject.ApplyModifiedProperties();
                            }
                        });
                    }

                    if (targetObjectField != null)
                    {
                        targetObjectField.objectType = typeof(Renderer);
                        targetObjectField.RegisterValueChangedCallback(changeEvent =>
                        {
                            if (itemRoot.userData is int itemIndex)
                            {
                                serializedObject.Update();
                                var entryProperty = EntriesProperty.GetArrayElementAtIndex(itemIndex);
                                var targetRendererProperty = entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.TargetRenderer));
                                targetRendererProperty.objectReferenceValue = changeEvent.newValue;
                                serializedObject.ApplyModifiedProperties();
                            }
                        });
                    }

                    if (targetTriangleCountSlider != null)
                    {
                        targetTriangleCountSlider.RegisterValueChangedCallback(changeEvent =>
                        {
                            if (itemRoot.userData is int itemIndex && AutoAdjustEnabledProperty.boolValue)
                            {
                                AdjustQuality(itemIndex);
                                serializedObject.ApplyModifiedProperties();
                            }
                        });
                    }

                    if (optionsToggle != null)
                    {
                        optionsToggle.RegisterValueChangedCallback(changeEvent =>
                        {
                            if (optionsField != null) optionsField.style.display = changeEvent.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                            if (preserveBorderEdgesBonesFoldout != null) preserveBorderEdgesBonesFoldout.style.display = changeEvent.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                        });
                    }

                    if (preserveBorderEdgesBonesFoldout != null)
                    {
                        for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++)
                        {
                            var humanBodyBoneIndex = (int)bone;
                            Toggle preserveBorderEdgesBoneToggle = new(bone.ToString());
                            preserveBorderEdgesBoneToggle.RegisterValueChangedCallback(changeEvent =>
                            {
                                if(itemRoot.userData is int itemIndex)
                                {
                                    var preserveBorderEdgesBonesProperty = EntriesProperty.GetArrayElementAtIndex(itemIndex).FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.PreserveBorderEdgesBones));
                                    serializedObject.Update();
                                    var currentMask = preserveBorderEdgesBonesProperty.ulongValue;
                                    if (changeEvent.newValue)
                                    {
                                        currentMask |= (1ul << humanBodyBoneIndex);
                                    }
                                    else
                                    {
                                        currentMask &= ~(1ul << humanBodyBoneIndex);
                                    }
                                    preserveBorderEdgesBonesProperty.ulongValue = currentMask;

                                    serializedObject.ApplyModifiedProperties();
                                }
                            });
                            preserveBorderEdgesBonesFoldout.Add(preserveBorderEdgesBoneToggle);
                        }
                    }

                    return itemRoot;
                };
            }

            return root;
        }

        static Dictionary<string, int> TargetTriangleCountPresetNameToValue { get; } = new()
        {
            ["PC-Poor-Medium-Good"] = 70000,
            ["PC-Excellent"] = 32000,
            ["Mobile-Poor"] = 20000,
            ["Mobile-Medium"] = 15000,
            ["Mobile-Good"] = 10000,
            ["Mobile-Excellent"] = 7500,
        };

        static Dictionary<int, string> TargetTriangleCountPresetValueToName { get; } = TargetTriangleCountPresetNameToValue.ToDictionary(keyValue => keyValue.Value, keyValue => keyValue.Key);


        private int GetTotalSimplifiedTriangleCount(bool usePreview)
        {
            var totalCount = 0;
            var target = Target;
            foreach (var entry in target.Entries)
            {
                if (entry.IsValid(target))
                {
                    totalCount += TryGetSimplifiedTriangleCount(entry, usePreview, out var triangleCount) ? triangleCount : 0;
                }
            }
            return totalCount;
        }

        private int GetTotalOriginalTriangleCount()
        {
            var totalCount = 0;
            var target = Target;
            foreach (var entry in target.Entries)
            {
                if (entry.IsValid(target))
                {
                    totalCount += TryGetOriginalTriangleCount(entry, false, out var triangleCount) ? triangleCount : 0;
                }
            }
            return totalCount;
        }
        private bool TryGetSimplifiedTriangleCount(MeshiaCascadingAvatarMeshSimplifierRendererEntry entry, bool preferPreview, out int triangleCount)
        {
            if (!entry.Enabled)
            {
                return TryGetOriginalTriangleCount(entry, preferPreview, out triangleCount);
            }
            if(entry.TargetRenderer is not { } targetRenderer)
            {
                triangleCount = -1;
                return false;
            }
            if (RendererUtility.GetMesh(targetRenderer) is { } mesh)
            {
                triangleCount = Math.Min(mesh.GetTriangleCount(), entry.TargetTriangleCount);
                return true;
            }
            else
            {
                triangleCount = -1;
                return false;
            }
        }
        private bool TryGetOriginalTriangleCount(MeshiaCascadingAvatarMeshSimplifierRendererEntry entry, bool preferPreview, out int triangleCount)
        {
            if (entry.TargetRenderer is not { } targetRenderer)
            {
                triangleCount = -1;
                return false;
            }
            if (RendererUtility.GetMesh(targetRenderer) is { } mesh)
            {
                triangleCount = mesh.GetTriangleCount();
                return true;
            }
            else
            {
                triangleCount = -1;
                return false;
            }
        }

        private void AdjustQuality(int fixedIndex = -1)
        {
            serializedObject.ApplyModifiedProperties();
            int targetBudget = TargetTriangleCountProperty.intValue;

            if (AccountForBaseProperty.boolValue)
            {
                // Find Animator on parent / avatar root
                var animator = Target.GetComponentInParent<Animator>();
                var rootObj = animator != null ? animator.gameObject : Target.transform.root.gameObject;

                var allRenderers = rootObj.GetComponentsInChildren<Renderer>(true);

                var managedRenderers = new HashSet<Renderer>();
                foreach (var entry in Target.Entries)
                {
                    if (entry.Enabled && entry.TargetRenderer != null)
                    {
                        managedRenderers.Add(entry.TargetRenderer);
                    }
                }

                int unmanagedTris = 0;
                foreach (var r in allRenderers)
                {
                    if (r is MeshRenderer or SkinnedMeshRenderer)
                    {
                        if (!managedRenderers.Contains(r))
                        {
                            if (MeshiaCascadingAvatarMeshSimplifierRendererEntry.IsEditorOnlyInHierarchy(r.gameObject))
                            {
                                continue;
                            }
                            var mesh = RendererUtility.GetMesh(r);
                            if (mesh != null)
                            {
                                unmanagedTris += mesh.GetTriangleCount();
                            }
                        }
                    }
                }

                targetBudget = Mathf.Max(0, targetBudget - unmanagedTris);
            }

            AdjustQualityToBudget(targetBudget, fixedIndex);
        }

        private void AdjustQualityToBudget(int targetBudget, int fixedIndex = -1)
        {
            serializedObject.ApplyModifiedProperties();
            var target = Target;
            var entries = target.Entries;
            var entriesProperty = EntriesProperty;

            Undo.RecordObject(target, "Adjust Quality");

            for (int iteration = 0; iteration < 5; iteration++)
            {
                var currentTotal = 0;
                var adjustableTotal = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];

                    if (!entry.IsValid(target))
                    {
                        continue;
                    }
                    var entryProperty = entriesProperty.GetArrayElementAtIndex(i);

                    TryGetSimplifiedTriangleCount(entry, false, out var triangleCount);

                    currentTotal += triangleCount;

                    if (entry.Enabled && !entry.Fixed && i != fixedIndex)
                    {
                        adjustableTotal += triangleCount;
                    }
                }
                
                if (adjustableTotal == 0) break;
                
                var adjustableTargetCount = targetBudget - (currentTotal - adjustableTotal);
                if (adjustableTargetCount <= 0) break;
                
                var proportion = (float)adjustableTargetCount / adjustableTotal;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (i == fixedIndex) continue;

                    var entry = entries[i];
                    if (!entry.IsValid(target))
                    {
                        continue;
                    }
                    var entryProperty = entriesProperty.GetArrayElementAtIndex(i);
                    
                    if (entry.Enabled && !entry.Fixed)
                    {
                        TryGetSimplifiedTriangleCount(entry, false, out var currentValue);
                        TryGetOriginalTriangleCount(entry, false, out var maxTriangleCount);
                        
                        var newValue = Mathf.Clamp((int)(currentValue * proportion), 0, maxTriangleCount);
                        entry.TargetTriangleCount = newValue;
                    }
                }
            }
            serializedObject.Update();
        }

        private void SetQualityAll(float ratio)
        {
            var target = Target;
            var entries = target.Entries;
            var entriesProperty = EntriesProperty;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!entry.IsValid(target))
                {
                    continue;
                }

                if (!entry.Fixed)
                {
                    var entryProperty = entriesProperty.GetArrayElementAtIndex(i);

                    TryGetOriginalTriangleCount(entry, true, out var originalTriangleCount);
                    var targetTriangleCountProperty = entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.TargetTriangleCount));

                    targetTriangleCountProperty.intValue = (int)(originalTriangleCount * ratio);
                }
            }
        }
    }

    internal static class GUIStyleHelper
    {
        private static GUIStyle? m_iconButtonStyle;
        public static GUIStyle IconButtonStyle
        {
            get
            {
                if (m_iconButtonStyle == null) m_iconButtonStyle = InitIconButtonStyle();
                return m_iconButtonStyle;
            }
        }
        static GUIStyle InitIconButtonStyle()
        {
            var style = new GUIStyle();
            return style;
        }

        private static GUIStyle? m_redStyle;
        public static GUIStyle RedStyle
        {
            get
            {
                if (m_redStyle == null) m_redStyle = InitRedStyle();
                return m_redStyle;
            }
        }
        static GUIStyle InitRedStyle()
        {
            var style = new GUIStyle();
            style.normal = new GUIStyleState() { textColor = Color.red };
            return style;
        }
    }
}
#endif
