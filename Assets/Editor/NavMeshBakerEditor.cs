using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor menu helper for NavMesh baking.
/// Provides menu items to manually bake NavMesh for selected GameObjects or procedural terrain.
/// </summary>
public class NavMeshBakerEditor : Editor
{
    /// <summary>
    /// Bakes NavMesh for the currently selected GameObject(s) in the hierarchy.
    /// </summary>
    [MenuItem("Tools/Veaphazard/Bake NavMesh for Selected", false, 100)]
    public static void BakeNavMeshForSelected()
    {
        GameObject[] selectedObjects = Selection.gameObjects;

        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            Debug.LogWarning("[NavMeshBakerEditor] No GameObjects selected. " +
                "Please select one or more GameObjects in the Hierarchy.");
            return;
        }

        foreach (GameObject obj in selectedObjects)
        {
            NavMeshBaker.Bake(obj);
        }

        Debug.Log("[NavMeshBakerEditor] NavMesh baking completed for " + 
            selectedObjects.Length + " selected object(s).");
    }

    /// <summary>
    /// Validates the menu item for baking selected objects.
    /// </summary>
    [MenuItem("Tools/Veaphazard/Bake NavMesh for Selected", true)]
    public static bool ValidateBakeNavMeshForSelected()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    /// <summary>
    /// Bakes NavMesh for procedural terrain by finding it via tag.
    /// Uses the default tag "ProceduralTerrain".
    /// </summary>
    [MenuItem("Tools/Veaphazard/Bake NavMesh for Procedural Terrain", false, 101)]
    public static void BakeNavMeshForProceduralTerrain()
    {
        const string terrainTag = "ProceduralTerrain";
        
        GameObject terrain = GameObject.FindWithTag(terrainTag);

        if (terrain == null)
        {
            // Try to find by name as fallback
            terrain = GameObject.Find("ProceduralTerrain");
        }

        if (terrain == null)
        {
            Debug.LogWarning("[NavMeshBakerEditor] Could not find procedural terrain. " +
                "Ensure a GameObject exists with the tag '" + terrainTag + "' or name 'ProceduralTerrain'.");
            return;
        }

        NavMeshBaker.Bake(terrain);
    }
}
