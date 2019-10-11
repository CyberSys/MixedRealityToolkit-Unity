﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.MixedReality.Toolkit.SceneSystem;
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Microsoft.MixedReality.Toolkit.Editor
{
    /// <summary>
    /// Class responsible for updating scene info structs to reflect changes made to scene assets.
    /// Extends AssetPostprocessor.
    /// </summary>
    public class SceneInfoUtils : AssetPostprocessor
    {
        /// <summary>
        /// Cached scenes used by SceneInfoDrawer to keep property drawer performant.
        /// </summary>
        public static EditorBuildSettingsScene[] CachedScenes => cachedScenes;

        private static EditorBuildSettingsScene[] cachedScenes = new EditorBuildSettingsScene[0];

        /// <summary>
        /// The frame of the last update. Used to ensure we don't spam the system with updates.
        /// </summary>
        private static int frameScriptableObjectsLastUpdated;
        private static int frameScenesLastUpdated;

        /// <summary>
        /// Call this when you make a change to the build settings and need those changes to be reflected immedately.
        /// </summary>
        public static void RefreshCachedScenes()
        {
            cachedScenes = EditorBuildSettings.scenes;
        }

        /// <summary>
        /// Finds all relative properties of a SceneInfo struct.
        /// </summary>
        /// <param name="property"></param>
        /// <param name="assetProperty"></param>
        /// <param name="nameProperty"></param>
        /// <param name="pathProperty"></param>
        /// <param name="buildIndexProperty"></param>
        /// <param name="includedProperty"></param>
        /// <param name="tagProperty"></param>
        public static void GetSceneInfoRelativeProperties(
            SerializedProperty property,
            out SerializedProperty assetProperty,
            out SerializedProperty nameProperty,
            out SerializedProperty pathProperty,
            out SerializedProperty buildIndexProperty,
            out SerializedProperty includedProperty,
            out SerializedProperty tagProperty)
        {
            assetProperty = property.FindPropertyRelative("Asset");
            nameProperty = property.FindPropertyRelative("Name");
            pathProperty = property.FindPropertyRelative("Path");
            buildIndexProperty = property.FindPropertyRelative("BuildIndex");
            includedProperty = property.FindPropertyRelative("Included");
            tagProperty = property.FindPropertyRelative("Tag");
        }

        /// <summary>
        /// Finds a missing scene asset reference for a SceneInfo struct.
        /// </summary>
        /// <param name="nameProperty"></param>
        /// <param name="pathProperty"></param>
        /// <param name="asset"></param>
        /// <returns>True if scene was found.</returns>
        public static bool FindScene(SerializedProperty nameProperty, SerializedProperty pathProperty, ref UnityEngine.Object asset)
        {
            // Attempt to load via the scene path
            SceneAsset newSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(pathProperty.stringValue);
            if (newSceneAsset != null)
            {
                Debug.Log("Found missing scene at path " + pathProperty.stringValue);
                asset = newSceneAsset;
                return true;
            }
            else
            {
                // If we didn't find it this way, search for all scenes in the project and try a name match
                foreach (string sceneGUID in AssetDatabase.FindAssets("t:Scene"))
                {
                    string scenePath = AssetDatabase.GUIDToAssetPath(sceneGUID);
                    string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

                    if (sceneName == nameProperty.stringValue)
                    {
                        pathProperty.stringValue = scenePath;
                        newSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                        if (newSceneAsset != null)
                        {
                            Debug.Log("Found missing scene at path " + scenePath);
                            asset = newSceneAsset;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Updates all the serialized properties for a SceneInfo struct.
        /// </summary>
        /// <param name="asset"></param>
        /// <param name="nameProperty"></param>
        /// <param name="pathProperty"></param>
        /// <param name="buildIndexProperty"></param>
        /// <param name="includedProperty"></param>
        /// <param name="tagProperty"></param>
        /// <returns>True if a property has changed.</returns>
        public static bool RefreshSceneInfo(
            UnityEngine.Object asset,
            SerializedProperty nameProperty,
            SerializedProperty pathProperty,
            SerializedProperty buildIndexProperty,
            SerializedProperty includedProperty,
            SerializedProperty tagProperty)
        {
            bool changed = false;

            if (asset == null)
            {
                // Leave the name and path alone, but reset the build index
                if (buildIndexProperty.intValue >= 0)
                {
                    buildIndexProperty.intValue = -1;
                    changed = true;
                }
            }
            else
            {
                // Refreshing these values is very expensive
                // Especially getting build scenes
                // We may want to move this out of the property drawer
                if (nameProperty.stringValue != asset.name)
                {
                    nameProperty.stringValue = asset.name;
                    changed = true;
                }

                string scenePath = AssetDatabase.GetAssetPath(asset);
                if (pathProperty.stringValue != scenePath)
                {
                    pathProperty.stringValue = scenePath;
                    changed = true;
                }

                // This method is no longer reliable
                // so we're using out cached scenes instead
                //Scene scene = EditorSceneManager.GetSceneByPath(scenePath);
                //int buildIndex = scene.buildIndex;

                int buildIndex = -1;
                int sceneCount = 0;
                bool included = false;
                for (int i = 0; i < cachedScenes.Length; i++)
                {
                    if (cachedScenes[i].path == scenePath)
                    {   // If it's in here it's included, even if it's not enabled
                        included = true;
                        if (cachedScenes[i].enabled)
                        {   // Only store the build index if it's enabled
                            buildIndex = sceneCount;
                        }
                    }

                    if (cachedScenes[i].enabled)
                    {   // Disabled scenes don't count toward scene count
                        sceneCount++;
                    }
                }

                if (buildIndex != buildIndexProperty.intValue)
                {
                    buildIndexProperty.intValue = buildIndex;
                    changed = true;
                }

                if (included != includedProperty.boolValue)
                {
                    includedProperty.boolValue = included;
                    changed = true;
                }
            }

            if (string.IsNullOrEmpty(tagProperty.stringValue))
            {
                tagProperty.stringValue = "Untagged";
                changed = true;
            }

            return changed;
        }


        /// <summary>
        /// Searches for all components in a scene and refreshes any SceneInfo fields found.
        /// </summary>
        [PostProcessSceneAttribute]
        public static void OnPostProcessScene()
        {
            Debug.Log("OnPostProcessScene");

            foreach (Component source in GameObject.FindObjectsOfType<Component>())
            {
                foreach (FieldInfo fieldInfo in source.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (fieldInfo.FieldType == typeof(SceneInfo))
                    {
                        Debug.Log("Found scene info type in " + source.name + ": " + fieldInfo.Name);

                        SerializedObject serializedObject = new SerializedObject(source);
                        SerializedProperty property = serializedObject.FindProperty(fieldInfo.Name);
                        SerializedProperty assetProperty, nameProperty, pathProperty, buildIndexProperty, includedProperty, tagProperty;
                        GetSceneInfoRelativeProperties(property, out assetProperty, out nameProperty, out pathProperty, out buildIndexProperty, out includedProperty, out tagProperty);
                        RefreshSceneInfo(source, nameProperty, pathProperty, buildIndexProperty, includedProperty, tagProperty);
                    }
                }
            }
        }

        [InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            Debug.Log("InitializeOnLoad");

            EditorBuildSettings.sceneListChanged += SceneListChanged;
            EditorSceneManager.sceneOpened += SceneOpened;

            frameScriptableObjectsLastUpdated = -1;
            frameScenesLastUpdated = -1;

            RefreshCachedScenes();
            RefreshSceneInfoFieldsInScriptableObjects();
            RefreshSceneInfoFieldsInScenes();
        }

        private static void SceneOpened(Scene scene, OpenSceneMode mode)
        {
            Debug.Log("Scene Opened");

            RefreshSceneInfoFieldsInScenes();
        }

        /// <summary>
        /// Updates the cached scene array when build settings change.
        /// </summary>
        private static void SceneListChanged()
        {
            Debug.Log("SceneListChanged");

            RefreshCachedScenes();
            RefreshSceneInfoFieldsInScriptableObjects();
            RefreshSceneInfoFieldsInScenes();
        }

        /// <summary>
        /// Calls RefreshSceneInfoFieldsInScriptableObjects when an asset is modified.
        /// </summary>
        /// <param name="importedAssets"></param>
        /// <param name="deletedAssets"></param>
        /// <param name="movedAssets"></param>
        /// <param name="movedFromAssetPaths"></param>
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            Debug.Log("OnPostprocessAllAssets");

            RefreshSceneInfoFieldsInScriptableObjects();
            RefreshSceneInfoFieldsInScenes();
        }

        /// <summary>
        /// Searches through all ScriptableObject instances and refreshes any SceneInfo fields found.
        /// </summary>
        private static void RefreshSceneInfoFieldsInScriptableObjects()
        {
            if (Time.frameCount == frameScriptableObjectsLastUpdated)
            {   // Don't udpate more than once per frame
                Debug.Log("(Already updated, skipping frame " + frameScriptableObjectsLastUpdated + ")");
                return;
            }

            Debug.Log("Refreshing scene info properties");
            foreach (ScriptableObject source in ScriptableObjectExtensions.GetAllInstances<ScriptableObject>())
            {
                foreach (FieldInfo fieldInfo in source.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (fieldInfo.FieldType == typeof(SceneInfo))
                    {
                        Debug.Log("Found scene info type in " + source.name + ": " + fieldInfo.Name);

                        SerializedObject serializedObject = new SerializedObject(source);
                        SerializedProperty property = serializedObject.FindProperty(fieldInfo.Name);
                        SerializedProperty assetProperty, nameProperty, pathProperty, buildIndexProperty, includedProperty, tagProperty;
                        GetSceneInfoRelativeProperties(property, out assetProperty, out nameProperty, out pathProperty, out buildIndexProperty, out includedProperty, out tagProperty);
                        RefreshSceneInfo(source, nameProperty, pathProperty, buildIndexProperty, includedProperty, tagProperty);
                    }
                }
            }

            frameScriptableObjectsLastUpdated = Time.frameCount;
        }

        private static void RefreshSceneInfoFieldsInScenes()
        {
            if (Time.frameCount == frameScenesLastUpdated)
            {   // Don't udpate more than once per frame
                Debug.Log("(Already updated scenes, skipping frame " + frameScenesLastUpdated + ")");
                return;
            }

            foreach (ScriptableObject source in ScriptableObjectExtensions.GetAllInstances<ScriptableObject>())
            {
                foreach (FieldInfo fieldInfo in source.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (fieldInfo.FieldType == typeof(SceneInfo))
                    {
                        Debug.Log("Found scene info type in " + source.name + ": " + fieldInfo.Name);

                        SerializedObject serializedObject = new SerializedObject(source);
                        SerializedProperty property = serializedObject.FindProperty(fieldInfo.Name);
                        SerializedProperty assetProperty, nameProperty, pathProperty, buildIndexProperty, includedProperty, tagProperty;
                        GetSceneInfoRelativeProperties(property, out assetProperty, out nameProperty, out pathProperty, out buildIndexProperty, out includedProperty, out tagProperty);
                        RefreshSceneInfo(source, nameProperty, pathProperty, buildIndexProperty, includedProperty, tagProperty);
                    }
                }
            }

            frameScenesLastUpdated = Time.frameCount;
        }
    }
}