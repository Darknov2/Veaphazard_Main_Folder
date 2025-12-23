using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Inventory manages a simple array of hotbar slots (Item + count) and updates HotbarUI.
/// Emits onSlotChanged(index) when an individual slot is modified so UI / other systems can react.
/// </summary>
[DisallowMultipleComponent]
public class Inventory : MonoBehaviour
{
    [Tooltip("Reference to the hotbar UI in scene (will be updated automatically).")]
    public HotbarUI hotbarUI;

    [Tooltip("Number of slots in the hotbar/inventory.")]
    public int slotCount = 10;

    [Serializable]
    public class Slot
    {
        public ItemData item;
        public int count;
        public bool IsEmpty => item == null || count <= 0;
    }

    public List<Slot> slots;

    // Event invoked when a single slot changes. Parameter = slot index.
    public UnityEvent<int> onSlotChanged = new UnityEvent<int>();

    private void Reset()
    {
        if (hotbarUI == null)
            hotbarUI = SceneFind.First<HotbarUI>();
    }

    void Awake()
    {
        if (slots == null || slots.Count != slotCount)
        {
            slots = new List<Slot>(slotCount);
            for (int i = 0; i < slotCount; i++) slots.Add(new Slot());
        }
    }

    void Start()
    {
        if (hotbarUI == null) hotbarUI = SceneFind.First<HotbarUI>();
        RefreshUI();
    }

    #region Public API

    public int AddItem(ItemData item, int amount = 1)
    {
        if (item == null || amount <= 0) return amount;

        int remaining = amount;
        var modified = new HashSet<int>();

        for (int i = 0; i < slotCount && remaining > 0; i++)
        {
            var s = slots[i];
            if (s.item == item && s.count < Mathf.Max(1, item.maxStack))
            {
                int space = item.maxStack - s.count;
                int toAdd = Mathf.Min(space, remaining);
                s.count += toAdd;
                remaining -= toAdd;
                modified.Add(i);
            }
        }

        for (int i = 0; i < slotCount && remaining > 0; i++)
        {
            var s = slots[i];
            if (s.IsEmpty)
            {
                int toAdd = Mathf.Min(remaining, Mathf.Max(1, item.maxStack));
                s.item = item;
                s.count = toAdd;
                remaining -= toAdd;
                modified.Add(i);
            }
        }

        RefreshUI();

        foreach (var idx in modified) onSlotChanged?.Invoke(idx);

        return remaining;
    }

    public int RemoveFromSlot(int slotIndex, int amount)
    {
        if (!IsValidIndex(slotIndex) || amount <= 0) return 0;
        var s = slots[slotIndex];
        if (s.IsEmpty) return 0;

        int removed = Mathf.Min(s.count, amount);
        s.count -= removed;
        if (s.count <= 0) { s.item = null; s.count = 0; }

        RefreshUI();

        if (removed > 0) onSlotChanged?.Invoke(slotIndex);
        return removed;
    }

    public int RemoveItem(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return 0;

        int remaining = amount;
        var modified = new HashSet<int>();

        for (int i = 0; i < slotCount && remaining > 0; i++)
        {
            var s = slots[i];
            if (s.IsEmpty) continue;
            if (s.item != item) continue;

            int take = Mathf.Min(s.count, remaining);
            s.count -= take;
            remaining -= take;
            modified.Add(i);
            if (s.count <= 0) { s.item = null; s.count = 0; }
        }

        int removed = amount - remaining;
        if (removed > 0)
        {
            RefreshUI();
            foreach (var idx in modified) onSlotChanged?.Invoke(idx);
        }
        return removed;
    }

    public int GetTotalCount(ItemData item)
    {
        if (item == null) return 0;
        int total = 0;
        for (int i = 0; i < slotCount; i++)
        {
            var s = slots[i];
            if (s.IsEmpty) continue;
            if (s.item == item) total += s.count;
        }
        return total;
    }

    public bool UseItemInSlot(int slotIndex, int amount = 1)
    {
        if (!IsValidIndex(slotIndex)) return false;
        var s = slots[slotIndex];
        if (s.IsEmpty) return false;

        RemoveFromSlot(slotIndex, amount);
        return true;
    }

    public void MoveOrSwapSlots(int fromIndex, int toIndex)
    {
        if (!IsValidIndex(fromIndex) || !IsValidIndex(toIndex) || fromIndex == toIndex) return;

        var src = slots[fromIndex];
        var dst = slots[toIndex];

        if (src.IsEmpty) return;

        if (!dst.IsEmpty && dst.item == src.item && dst.count < dst.item.maxStack)
        {
            int space = dst.item.maxStack - dst.count;
            int toMove = Mathf.Min(space, src.count);
            dst.count += toMove;
            src.count -= toMove;
            if (src.count <= 0) { src.item = null; src.count = 0; }
        }
        else if (dst.IsEmpty)
        {
            dst.item = src.item;
            dst.count = src.count;
            src.item = null;
            src.count = 0;
        }
        else
        {
            var tmpItem = dst.item; int tmpCount = dst.count;
            dst.item = src.item; dst.count = src.count;
            src.item = tmpItem; src.count = tmpCount;
        }

        RefreshUI();
        onSlotChanged?.Invoke(fromIndex);
        onSlotChanged?.Invoke(toIndex);
    }

    #endregion

    #region Helpers

    bool IsValidIndex(int i) => i >= 0 && i < slotCount;

    public void RefreshUI()
    {
        if (hotbarUI == null) return;
        for (int i = 0; i < slotCount; i++)
        {
            var s = slots[i];
            if (s.IsEmpty)
                hotbarUI.ClearItem(i);
            else
                hotbarUI.SetItem(i, s.item, s.count);
        }
    }

    #endregion
}