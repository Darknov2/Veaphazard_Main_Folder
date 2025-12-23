using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/Item Data", fileName = "NewItemData")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Stacking")]
    [Tooltip("Maximum items per stack for this item. Use 1 for non-stackable.")]
    public int maxStack = 1;

    [Header("Placement")]
    [Tooltip("Prefab to instantiate when placing this item (optional).")]
    public GameObject placePrefab;
}