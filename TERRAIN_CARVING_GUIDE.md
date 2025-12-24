# Terrain Carving Feature Guide

## Overview

The terrain carving feature allows construction objects (buildings, structures) to automatically carve into the procedurally generated marching-cubes terrain. When objects are placed, moved, or removed, the terrain automatically updates to accommodate them with smooth transitions.

## Setup

### 1. Configure Construction Layer

1. In Unity, go to **Edit > Project Settings > Tags and Layers**
2. Create a new layer called "construction" (or use an existing layer)
3. Assign this layer to all construction prefabs that should carve terrain

### 2. Configure ProceduralTerrainGenerator

In the Inspector for your ProceduralTerrainGenerator component:

1. **Construction Layer Mask**: Select the layer(s) that contain construction objects
   - Default: 0 (disabled)
   - Set this to include your "construction" layer

2. **Collider Blend Distance**: Distance for smooth falloff around colliders
   - Default: 0.15
   - Smaller values = sharper transitions
   - Larger values = smoother, more gradual carving

3. **Carve Strength**: How strongly terrain is carved
   - Range: 0.0 to 2.0
   - Default: 1.0
   - 0.0 = no carving
   - 1.0 = full carving
   - >1.0 = extra strong carving for thick terrain

### 3. Ensure Construction Prefabs Have Colliders

Each construction prefab must have at least one Collider component (MeshCollider, BoxCollider, etc.) for terrain carving to work. The collider shape determines the carved volume.

## Usage

### Placing Objects

When using the ConstructionSystem to place objects:

```csharp
// Automatic - the terrain carving happens automatically when you place via ConstructionSystem
// Right-click to place (default binding)
```

The terrain will automatically:
1. Detect overlapping terrain chunks
2. Regenerate those chunks with carved geometry
3. Update smoothly with blend falloff

### Removing Objects

To remove a placed object and restore terrain:

```csharp
// Call the public API method
constructionSystem.RemovePlacedObject(placedGameObject);
```

This will:
1. Calculate the object's bounds
2. Remove the object
3. Notify terrain to regenerate (filling in the carved area)

### Manual Notification (Advanced)

If you need to manually notify terrain of changes (e.g., for objects not placed via ConstructionSystem):

```csharp
// Get the terrain generator reference
ProceduralTerrainGenerator terrain = FindObjectOfType<ProceduralTerrainGenerator>();

// Calculate bounds of your object
Bounds objectBounds = CalculateBounds(myObject);

// Notify terrain
terrain.NotifyConstructionChanged(objectBounds);
```

## How It Works

### Carving Algorithm

1. **Chunk Identification**: When an object is placed, the system identifies all terrain chunks that overlap the object's bounds (expanded by blend distance)

2. **Collider Query**: For each affected chunk, a Physics.OverlapBox query retrieves all construction colliders within range

3. **Per-Voxel Density Adjustment**: For each voxel in the chunk:
   - Calculate the closest point on each collider
   - If the voxel is inside the collider: apply full carve strength
   - If within blend distance: apply smooth quadratic falloff
   - Subtract the carve value from the terrain density

4. **Marching Cubes**: The modified density field is processed by marching cubes to generate the carved mesh

### Smooth Falloff

The falloff uses a quadratic function for smooth transitions:

```
t = distance / blendDistance    // 0 at surface, 1 at blend distance
falloff = 1 - (t * t)            // Quadratic curve
carve = -strength * falloff      // Apply to density
```

## Performance Considerations

- **Per-Chunk Queries**: Colliders are queried once per chunk, not per voxel, for efficiency
- **Blend Distance**: Keep this reasonably small (0.1-0.3) to minimize the affected area
- **Layer Mask**: Only objects on the construction layer are considered
- **Async Generation**: Chunk regeneration uses the existing async/sync settings from ProceduralTerrainGenerator

## Troubleshooting

### Terrain Not Carving

1. Verify construction objects are on the correct layer
2. Check that `constructionLayerMask` is set in ProceduralTerrainGenerator
3. Ensure objects have colliders
4. Verify `carveStrength` is > 0

### Harsh Seams/Edges

- Increase `colliderBlendDistance` for smoother transitions
- Ensure voxel scale is appropriate for the object size

### Performance Issues

- Reduce the number of construction objects in a small area
- Decrease `colliderBlendDistance` to affect fewer voxels
- Consider using simpler collider shapes (BoxCollider vs MeshCollider)

## API Reference

### ProceduralTerrainGenerator

```csharp
// Public method to notify terrain of construction changes
public void NotifyConstructionChanged(Bounds worldBounds)

// Get construction colliders for a chunk (used internally by TerrainChunk)
public Collider[] GetConstructionCollidersForChunk(Bounds chunkBounds)

// Inspector fields
public LayerMask constructionLayerMask;
public float colliderBlendDistance = 0.15f;
public float carveStrength = 1.0f;
```

### ConstructionSystem

```csharp
// Remove a placed object and notify terrain
public void RemovePlacedObject(GameObject obj)

// Private helper (automatic on placement)
private void NotifyTerrainOfPlacement(GameObject obj)
private Bounds CalculateObjectBounds(GameObject obj)
```

### TerrainChunk

```csharp
// Internal method for computing carve delta
private float ComputeConstructionCarve(Vector3 worldPos, Collider[] colliders, 
                                       float blendDistance, float strength)
```

## Examples

### Example 1: Basic Placement

```csharp
// Setup in Unity Inspector:
// - ProceduralTerrainGenerator.constructionLayerMask = "construction"
// - ProceduralTerrainGenerator.colliderBlendDistance = 0.15
// - ProceduralTerrainGenerator.carveStrength = 1.0
// - BuildingPrefab.layer = "construction"
// - BuildingPrefab has BoxCollider

// In-game:
// Right-click to place building -> terrain carves automatically
```

### Example 2: Dynamic Object Removal

```csharp
public class DestructibleBuilding : MonoBehaviour
{
    private ConstructionSystem constructionSystem;
    
    void Start()
    {
        constructionSystem = FindObjectOfType<ConstructionSystem>();
    }
    
    public void OnDestroyed()
    {
        // Remove this building and restore terrain
        constructionSystem.RemovePlacedObject(gameObject);
    }
}
```

### Example 3: Moving Objects (Advanced)

For moveable construction objects, you'll need to notify terrain when they move:

```csharp
public class MovableBuilding : MonoBehaviour
{
    private Vector3 lastPosition;
    private ProceduralTerrainGenerator terrain;
    private Bounds objectBounds;
    
    void Start()
    {
        terrain = FindObjectOfType<ProceduralTerrainGenerator>();
        lastPosition = transform.position;
        objectBounds = CalculateBounds();
    }
    
    void Update()
    {
        if (Vector3.Distance(transform.position, lastPosition) > 0.1f)
        {
            // Notify old position to restore terrain
            Bounds oldBounds = new Bounds(lastPosition, objectBounds.size);
            terrain.NotifyConstructionChanged(oldBounds);
            
            // Notify new position to carve terrain
            Bounds newBounds = new Bounds(transform.position, objectBounds.size);
            terrain.NotifyConstructionChanged(newBounds);
            
            lastPosition = transform.position;
        }
    }
    
    private Bounds CalculateBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (var col in GetComponentsInChildren<Collider>())
            b.Encapsulate(col.bounds);
        return b;
    }
}
```

## Future Enhancements

Potential improvements for the terrain carving system:

1. **Rotation Support**: Notify terrain when objects rotate significantly
2. **Carve Modes**: Different carving styles (hard edges, super smooth, tunnels)
3. **Material Blending**: Blend terrain material at carve boundaries
4. **Undo/Redo**: Support for construction undo with terrain restoration
5. **Prefab Metadata**: Store carve settings per-prefab type

---

## Testing Checklist

- [ ] Place construction object on terrain surface
- [ ] Verify terrain carves around object's collider
- [ ] Verify smooth transitions (no harsh seams)
- [ ] Remove object using RemovePlacedObject()
- [ ] Verify terrain regenerates to fill gap
- [ ] Test with multiple objects in same area
- [ ] Test with different collider shapes (Box, Sphere, Mesh)
- [ ] Adjust blend distance and verify smoothness changes
- [ ] Adjust carve strength and verify carve depth changes
