using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// Play Mode 安全的 Undo / 脏标记包装。
    /// Edit Mode 下走 <see cref="Undo"/> + <see cref="EditorSceneManager.MarkSceneDirty(Scene)"/>；
    /// Play Mode 下改用纯运行时 API，并跳过脏标记（避免 "This cannot be used during play mode" 异常）。
    /// </summary>
    internal static class SceneEdit
    {
        public static void RegisterCreated(GameObject go, string name)
        {
            if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(go, name);
        }

        public static void RecordObject(UnityEngine.Object obj, string name)
        {
            if (!Application.isPlaying) Undo.RecordObject(obj, name);
        }

        public static Component AddComponent(GameObject go, Type type)
        {
            return Application.isPlaying ? go.AddComponent(type) : Undo.AddComponent(go, type);
        }

        public static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else Undo.DestroyObjectImmediate(obj);
        }

        public static void SetTransformParent(Transform child, Transform parent, string name)
        {
            if (Application.isPlaying) child.SetParent(parent, true);
            else Undo.SetTransformParent(child, parent, name);
        }

        public static void MarkDirty(GameObject go)
        {
            if (go == null || Application.isPlaying) return;
            var scene = go.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void MarkDirty(Scene scene)
        {
            if (Application.isPlaying) return;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Play Mode 下 SetDirty 仍然可调但对运行时状态意义不大，统一跳过保持一致。
        /// </summary>
        public static void SetAssetDirty(UnityEngine.Object asset)
        {
            if (asset == null || Application.isPlaying) return;
            EditorUtility.SetDirty(asset);
        }
    }
}
