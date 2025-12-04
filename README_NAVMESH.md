# NavMesh Baker for Procedural Terrain

This module provides runtime NavMesh baking for procedural terrain in Veaphazard. It allows enemies and other AI agents to navigate procedurally generated terrain by automatically baking a NavMesh surface after terrain generation completes.

## Prerequisites

### Installing NavMeshComponents

The NavMesh Baker requires Unity's NavMeshComponents package. If not already installed, add it using one of these methods:

#### Option 1: Package Manager (Recommended)
1. Open **Window > Package Manager**
2. Click the **+** button and select **Add package from git URL**
3. Enter: `https://github.com/Unity-Technologies/NavMeshComponents.git`
4. Click **Add**

#### Option 2: Manual Installation
1. Clone or download the [NavMeshComponents repository](https://github.com/Unity-Technologies/NavMeshComponents)
2. Copy the contents into your `Assets` folder

## Usage

### Automatic Baking (Recommended)

1. Create an empty GameObject in your scene (e.g., "NavMeshBaker")
2. Add the `NavMeshBaker` component to it
3. Configure the component:
   - **Auto Bake**: Enable to automatically bake on Start
   - **Terrain Root**: (Optional) Drag your terrain root GameObject here
   - **Tag To Find**: Default is "ProceduralTerrain" - set this to match your terrain's tag
   - **Bake Delay**: Time to wait before baking (ensures all mesh data is ready)

The NavMeshBaker will automatically find and bake the NavMesh for your procedural terrain when the scene starts.

### Manual Integration with Terrain Generator

If you want precise control over when NavMesh baking occurs, call the static `Bake` method from your terrain generator after generation completes:

```csharp
// In your procedural terrain generator script
public class MyTerrainGenerator : MonoBehaviour
{
    private void GenerateTerrain()
    {
        // ... your terrain generation code ...
        
        // After terrain is fully generated:
        NavMeshBaker.Bake(this.gameObject);
    }
}
```

### Editor Tools

Manual baking is available through the Unity Editor menu:

- **Tools > Veaphazard > Bake NavMesh for Selected**  
  Bakes NavMesh for the currently selected GameObject(s) in the Hierarchy.

- **Tools > Veaphazard > Bake NavMesh for Procedural Terrain**  
  Finds and bakes NavMesh for a GameObject tagged "ProceduralTerrain".

## Component Settings

| Setting | Description |
|---------|-------------|
| **Auto Bake** | When enabled, automatically bakes NavMesh on Start |
| **Terrain Root** | Direct reference to the terrain root GameObject |
| **Tag To Find** | Tag used to find terrain if Terrain Root is not set |
| **Bake Delay** | Seconds to wait before baking (default: 0.5s) |
| **Collect Objects** | How to collect geometry for baking |
| **Use Layers** | Filter objects by layer mask |
| **Layer Mask** | Layers to include when Use Layers is enabled |

## Integration Notes

- The NavMesh is baked at runtime using `NavMeshSurface.BuildNavMesh()`
- For streaming terrain, you may need to rebake when new chunks are loaded
- Consider calling `NavMeshBaker.Bake()` after significant terrain modifications

## Troubleshooting

**"NavMeshSurface component not available" warning:**  
Install the NavMeshComponents package as described in the Prerequisites section.

**"Could not find terrain root" warning:**  
Either set the `Terrain Root` field directly, or ensure your terrain GameObject has the tag specified in `Tag To Find`.

**NavMesh not updating after terrain changes:**  
Call `NavMeshBaker.Bake(terrainRoot)` after modifying the terrain to rebuild the NavMesh.

## API Reference

### NavMeshBaker

#### Static Methods

```csharp
/// <summary>
/// Bakes NavMesh for the specified root GameObject.
/// Call this from your terrain generator after generation completes.
/// </summary>
public static void Bake(GameObject root)
```

#### Instance Methods

```csharp
/// <summary>
/// Bakes NavMesh for the specified root GameObject using instance settings.
/// </summary>
public void BakeFor(GameObject root)
```

## License

This module is part of the Veaphazard project.
