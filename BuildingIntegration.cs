using UnityEngine;

/// <summary>
/// BuildingIntegration — glue between HotbarUI/Inventory and ConstructionSystem.
/// Uses SceneFind.First<T>() to auto-locate components if not assigned.
/// </summary>
[DisallowMultipleComponent]
public class BuildingIntegration : MonoBehaviour
{
    public HotbarUI hotbarUI;
    public Inventory inventory;
    public ConstructionSystem constructionSystem;

    void Start()
    {
        if (hotbarUI == null) hotbarUI = SceneFind.First<HotbarUI>();
        if (inventory == null) inventory = SceneFind.First<Inventory>();
        if (constructionSystem == null) constructionSystem = SceneFind.First<ConstructionSystem>();

        if (hotbarUI != null)
            hotbarUI.onSelectionChanged.AddListener(OnHotbarSelectionChanged);

        if (constructionSystem != null)
            constructionSystem.onPlaced.AddListener(OnConstructionPlaced);

        if (inventory != null)
            inventory.onSlotChanged.AddListener(OnInventorySlotChanged);

        if (hotbarUI != null)
            OnHotbarSelectionChanged(hotbarUI.GetSelectedIndex());
    }

    private void OnDestroy()
    {
        if (hotbarUI != null)
            hotbarUI.onSelectionChanged.RemoveListener(OnHotbarSelectionChanged);

        if (constructionSystem != null)
            constructionSystem.onPlaced.RemoveListener(OnConstructionPlaced);

        if (inventory != null)
            inventory.onSlotChanged.RemoveListener(OnInventorySlotChanged);
    }

    private void OnHotbarSelectionChanged(int slotIndex)
    {
        UpdateBuildModeForSelectedSlot(slotIndex);
    }

    private void OnInventorySlotChanged(int slotIndex)
    {
        if (hotbarUI == null) return;
        int sel = hotbarUI.GetSelectedIndex();
        if (slotIndex == sel)
            UpdateBuildModeForSelectedSlot(sel);
    }

    private void UpdateBuildModeForSelectedSlot(int slotIndex)
    {
        if (hotbarUI == null || constructionSystem == null) return;

        var slot = hotbarUI.GetSlot(slotIndex);
        if (slot == null)
        {
            constructionSystem.buildMode = false;
            constructionSystem.RefreshGhost();
            return;
        }

        var item = slot.GetItem();
        if (item != null && item.placePrefab != null)
        {
            int idx = -1;
            var prefabs = constructionSystem.buildPrefabs;
            if (prefabs != null)
            {
                for (int i = 0; i < prefabs.Length; i++)
                {
                    if (prefabs[i] == item.placePrefab)
                    {
                        idx = i;
                        break;
                    }
                }
            }

            if (idx >= 0)
            {
                constructionSystem.SetSelectedIndex(idx);
                constructionSystem.buildMode = true;
                constructionSystem.RefreshGhost();
                return;
            }
        }

        constructionSystem.buildMode = false;
        constructionSystem.RefreshGhost();
    }

    private void OnConstructionPlaced(GameObject placedObj)
    {
        if (hotbarUI == null || inventory == null) return;

        int selectedSlot = hotbarUI.GetSelectedIndex();
        int removed = inventory.RemoveFromSlot(selectedSlot, 1);
        if (removed <= 0)
        {
            ItemData found = null;
            var entries = Resources.FindObjectsOfTypeAll<ItemData>();
            foreach (var it in entries)
            {
                if (it.placePrefab == null) continue;
                if (it.placePrefab == placedObj || it.placePrefab.name == placedObj.name)
                {
                    found = it;
                    break;
                }
            }

            if (found != null)
            {
                inventory.RemoveItem(found, 1);
            }
        }
    }
}