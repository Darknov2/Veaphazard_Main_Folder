using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class HotbarUI : MonoBehaviour
{
    [System.Serializable]
    public class SelectionChangedEvent : UnityEvent<int> {}

    [Header("Layout")]
    public int slotCount = 10;
    public int slotSize = 64;
    public int spacing = 8;
    public int bottomPadding = 16;

    [Header("References (optional)")]
    public GameObject slotPrefab;
    public Canvas targetCanvas;

    [Header("Visuals")]
    public Sprite slotBackgroundSprite;
    public Color slotColor = new Color(1,1,1,0.05f);
    public Color selectedColor = new Color(0.2f, 0.9f, 0.2f, 0.15f);

    [Header("TextMeshPro")]
    public TMP_FontAsset countFont;
    public int countFontSize = 18;

    [Header("Selection Box (scroll/select)")]
    public bool enableScrollSelection = true;
    public bool scrollWrap = true;
    public float scrollCooldown = 0.05f;
    public bool selectionSmooth = true;
    public float selectionMoveSpeed = 18f;
    public Color selectionBoxColor = new Color(1f,1f,1f,0.07f);
    public int selectionBoxPadding = 6;

    [Header("Events")]
    public SelectionChangedEvent onSelectionChanged = new SelectionChangedEvent();

    // internals
    RectTransform hotbarPanel;
    List<HotbarSlot> slots = new List<HotbarSlot>();
    int selectedIndex = 0;
    float totalWidth = 0f;

    RectTransform selectionBoxRT;
    Image selectionBoxImage;
    float lastScrollTime = -999f;

    void Awake()
    {
        if (targetCanvas == null)
        {
            targetCanvas = SceneFind.First<Canvas>();
            if (targetCanvas == null)
            {
                var go = new GameObject("Canvas");
                targetCanvas = go.AddComponent<Canvas>();
                targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                go.AddComponent<CanvasScaler>();
                go.AddComponent<GraphicRaycaster>();
            }
        }
        CreateHotbar();
    }

    void Start()
    {
        SelectSlot(0, snap:true);
    }

    void Update()
    {
        for (int i = 0; i <= 9; i++)
        {
            KeyCode key = (i == 9) ? KeyCode.Alpha0 : KeyCode.Alpha1 + i;
            if (Input.GetKeyDown(key))
            {
                SelectSlot(i);
            }
        }

        if (enableScrollSelection)
            HandleScrollSelection();
    }

    void LateUpdate()
    {
        if (selectionBoxRT != null && slots.Count > 0)
        {
            var targetSlot = slots[Mathf.Clamp(selectedIndex, 0, slots.Count - 1)];
            Vector2 targetPos = targetSlot.GetComponent<RectTransform>().anchoredPosition;
            Vector2 desired = new Vector2(targetPos.x, targetPos.y);

            if (selectionSmooth)
            {
                Vector2 cur = selectionBoxRT.anchoredPosition;
                Vector2 next = Vector2.Lerp(cur, desired, Mathf.Clamp01(Time.deltaTime * selectionMoveSpeed));
                selectionBoxRT.anchoredPosition = next;
            }
            else
            {
                selectionBoxRT.anchoredPosition = desired;
            }
        }
    }

    void HandleScrollSelection()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f && Time.time - lastScrollTime >= scrollCooldown)
        {
            lastScrollTime = Time.time;
            int delta = (scroll > 0f) ? -1 : 1;
            MoveSelectionBy(delta);
        }
    }

    void CreateHotbar()
    {
        if (hotbarPanel != null) return;

        GameObject panelGO = new GameObject("HotbarPanel", typeof(RectTransform));
        panelGO.transform.SetParent(targetCanvas.transform, false);
        hotbarPanel = panelGO.GetComponent<RectTransform>();

        hotbarPanel.anchorMin = new Vector2(0.5f, 0f);
        hotbarPanel.anchorMax = new Vector2(0.5f, 0f);
        hotbarPanel.pivot = new Vector2(0.5f, 0f);

        totalWidth = slotCount * slotSize + (slotCount - 1) * spacing;
        hotbarPanel.sizeDelta = new Vector2(totalWidth, slotSize + 12);
        hotbarPanel.anchoredPosition = new Vector2(0f, bottomPadding);

        var panelImage = panelGO.AddComponent<Image>();
        panelImage.color = new Color(0f,0f,0f,0.25f);
        panelImage.raycastTarget = false;

        CreateSelectionBox(panelGO.transform as RectTransform);

        slots.Clear();
        for (int i = 0; i < slotCount; i++)
        {
            HotbarSlot slot = CreateSlot(i, hotbarPanel, totalWidth);
            slots.Add(slot);
            slot.onClicked.AddListener(OnSlotClicked);
        }

        if (selectionBoxRT != null)
            selectionBoxRT.SetAsLastSibling();
    }

    private void CreateSelectionBox(RectTransform parent)
    {
        GameObject go = new GameObject("SelectionBox", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        selectionBoxRT = go.GetComponent<RectTransform>();

        selectionBoxRT.anchorMin = selectionBoxRT.anchorMax = new Vector2(0.5f, 0f);
        selectionBoxRT.pivot = new Vector2(0.5f, 0f);
        selectionBoxRT.localScale = Vector3.one;
        selectionBoxRT.sizeDelta = new Vector2(slotSize + selectionBoxPadding, slotSize + selectionBoxPadding);
        selectionBoxRT.anchoredPosition = Vector2.zero;

        selectionBoxImage = go.AddComponent<Image>();
        selectionBoxImage.color = selectionBoxColor;
        selectionBoxImage.raycastTarget = false;
    }

    HotbarSlot CreateSlot(int index, RectTransform parent, float parentWidth)
    {
        if (slotPrefab != null)
        {
            var go = Instantiate(slotPrefab, parent);
            var rt = go.GetComponent<RectTransform>();

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(slotSize, slotSize);

            float x = -parentWidth * 0.5f + slotSize * 0.5f + index * (slotSize + spacing);
            rt.anchoredPosition = new Vector2(x, 6f);

            var slot = go.GetComponent<HotbarSlot>();
            if (slot == null)
            {
                Debug.LogError("Provided slotPrefab must contain a HotbarSlot component.");
                slot = go.AddComponent<HotbarSlot>();
            }
            slot.Initialize(index);
            if (slot.selectionBorder != null)
                slot.selectionBorder.color = selectedColor;
            return slot;
        }
        else
        {
            GameObject go = new GameObject($"Slot_{index}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(slotSize, slotSize);

            float x = -parentWidth * 0.5f + slotSize * 0.5f + index * (slotSize + spacing);
            rt.anchoredPosition = new Vector2(x, 6f);

            var bg = go.AddComponent<Image>();
            bg.sprite = slotBackgroundSprite;
            bg.color = slotColor;
            bg.raycastTarget = false;

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            var iconRT = iconGo.GetComponent<RectTransform>();
            iconRT.anchorMin = iconRT.anchorMax = new Vector2(0.5f, 0.5f);
            iconRT.pivot = new Vector2(0.5f, 0.5f);
            iconRT.sizeDelta = new Vector2(slotSize - 8, slotSize - 8);
            iconRT.anchoredPosition = Vector2.zero;
            var iconImage = iconGo.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.enabled = false;

            GameObject countGo = new GameObject("Count", typeof(RectTransform));
            countGo.transform.SetParent(go.transform, false);
            var countRT = countGo.GetComponent<RectTransform>();
            countRT.anchorMin = countRT.anchorMax = new Vector2(1f, 0f);
            countRT.pivot = new Vector2(1f, 0f);
            countRT.anchoredPosition = new Vector2(-6f, 6f);
            countRT.sizeDelta = new Vector2(40, 20);
            var countText = countGo.AddComponent<TextMeshProUGUI>();
            countText.alignment = TextAlignmentOptions.BottomRight;
            if (countFont != null) countText.font = countFont;
            countText.fontSize = countFontSize;
            countText.color = Color.white;
            countText.raycastTarget = false;
            countText.text = "";
            countText.gameObject.SetActive(false);

            GameObject borderGo = new GameObject("Border", typeof(RectTransform));
            borderGo.transform.SetParent(go.transform, false);
            var borderRT = borderGo.GetComponent<RectTransform>();
            borderRT.anchorMin = borderRT.anchorMax = new Vector2(0.5f, 0.5f);
            borderRT.pivot = new Vector2(0.5f, 0.5f);
            borderRT.sizeDelta = new Vector2(slotSize + 6, slotSize + 6);
            borderRT.anchoredPosition = Vector2.zero;
            var borderImage = borderGo.AddComponent<Image>();
            borderImage.color = selectedColor;
            borderImage.raycastTarget = false;
            borderImage.enabled = false;

            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.9f,0.9f,0.9f);
            button.colors = colors;

            var slotComp = go.AddComponent<HotbarSlot>();
            slotComp.iconImage = iconImage;
            slotComp.countText = countText;
            slotComp.selectionBorder = borderImage;
            slotComp.button = button;
            slotComp.Initialize(index);
            return slotComp;
        }
    }

    void OnSlotClicked(int slotIndex)
    {
        SelectSlot(slotIndex);
        Debug.Log($"Hotbar: clicked slot {slotIndex + 1}");
    }

    public void SelectSlot(int index, bool snap = false)
    {
        if (slots == null || slots.Count == 0) return;
        index = Mathf.Clamp(index, 0, slots.Count - 1);
        selectedIndex = index;
        for (int i = 0; i < slots.Count; i++)
            slots[i].SetSelected(i == selectedIndex);

        if (selectionBoxRT != null && snap)
        {
            var rt = slots[selectedIndex].GetComponent<RectTransform>();
            selectionBoxRT.anchoredPosition = rt.anchoredPosition;
        }

        // Invoke selection event so other systems (building) can react
        onSelectionChanged?.Invoke(selectedIndex);
    }

    public void MoveSelectionBy(int delta)
    {
        if (slots == null || slots.Count == 0) return;

        int newIndex = selectedIndex + delta;
        if (scrollWrap)
        {
            newIndex = (newIndex % slots.Count + slots.Count) % slots.Count;
        }
        else
        {
            newIndex = Mathf.Clamp(newIndex, 0, slots.Count - 1);
        }

        SelectSlot(newIndex);
    }

    public void SetItem(int slotIndex, ItemData item, int count = 1)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count) return;
        slots[slotIndex].SetItem(item, count);
    }

    public void ClearItem(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count) return;
        slots[slotIndex].Clear();
    }

    public int GetSelectedIndex() => selectedIndex;
    public HotbarSlot GetSlot(int index) => (index >= 0 && index < slots.Count) ? slots[index] : null;
}