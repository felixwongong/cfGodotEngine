using System;
using Godot;

namespace cfGodotEngine.Util;

public static class NodeUtil
{
    public static SceneTree GetSceneTree()
    {
        if(Engine.GetMainLoop() is not SceneTree sceneTree)
        {
            throw new InvalidOperationException("No SceneTree found in the current Engine main loop.");
        }
        
        return sceneTree;
    }
    
    public static T DontDestroyOnLoad<T>(this T node) where T: Node
    {
        if (node == null)
            throw new ArgumentNullException(nameof(node), "Node cannot be null.");

        var sceneTree = GetSceneTree();
        if(node.GetParent() == sceneTree.Root)
            return node;
        
        sceneTree.Root.AddChild(node);
        return node;
    }
}