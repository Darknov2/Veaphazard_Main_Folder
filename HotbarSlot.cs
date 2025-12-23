using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

[System.Serializable]
public class HotbarSlotEvent : UnityEvent<int> { }

/// <summary>
/// HotbarSlot: small helper component that manages the UI for a single hotbar slot.
/// </summary>
[DisallowMultipleComponent]
public class HotbarSlot : MonoBehaviour
{
    public Image iconImage;
    public TMP_Text countText;                 // switched to TextMeshPro
    public Image selectionBorder;
    public Button button;

    int slotIndex = -1;
    ItemData currentItem;
    int currentCount;

    public HotbarSlotEvent onClicked = new HotbarSlotEvent();

    public void Initialize(int index)
    {
        slotIndex = index;
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnButtonClicked);
        }
        Clear();
        SetSelected(false);
    }

    void OnButtonClicked()
    {
        onClicked?.Invoke(slotIndex);
    }

    public void SetItem(ItemData item, int count = 1)
    {
        currentItem = item;
        currentCount = count;
        if (iconImage != null)
        {
            iconImage.sprite = item != null ? item.icon : null;
            iconImage.enabled = item != null && item.icon != null;
        }
        if (countText != null)
        {
            countText.text = (count > 1) ? count.ToString() : "";
            countText.gameObject.SetActive(count > 1);
        }
    }

    public void Clear()
    {
        currentItem = null;
        currentCount = 0;
        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }
        if (countText != null)
        {
            countText.text = "";
            countText.gameObject.SetActive(false);
        }
    }

    public void SetSelected(bool selected)
    {
        if (selectionBorder != null)
            selectionBorder.enabled = selected;
    }

    public ItemData GetItem() => currentItem;
    public int GetCount() => currentCount;
    public int GetIndex() => slotIndex;
}