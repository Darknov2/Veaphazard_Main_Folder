using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

/// <summary>
/// WorldSelector_PersistScenes (keeps mapping and calls SceneLoadManager)
/// - Fills missing scenePerIndex slots randomly from the 'scenes' pool (mountains/main/village/etc).
/// - Persists mapping into GameState so Signs can resolve neighbors after the menu unloads.
/// - Calls SceneLoadManager using asset scene names (not instance keys).
/// - Exposes PersistMappingToGameStatePublic so SceneLoadManager can ask it to re-publish mapping.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class WorldSelector_PersistScenes : MonoBehaviour
{
    [Header("UI / Prefab")]
    public GameObject cellPrefab;
    public int numWorlds = 9;

    [Header("Scene mapping")]
    public List<string> scenePerIndex = new List<string>();
    [Tooltip("Pool of scene asset names to pick from randomly when filling empty ScenePerIndex entries.")]
    public List<string> scenes = new List<string>();

    [Header("Menu unload settings")]
    public string menuSceneName = "Menu";

    [Header("Loading")]
    public float minLoadingTime = 0.25f;

    RectTransform parentRT;
    Canvas parentCanvas;
    private bool isLoading = false;

    void Awake()
    {
        parentRT = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null) Debug.LogWarning("WorldSelector_PersistScenes should be under a Canvas.");
    }

    void OnEnable() => PopulateGrid();

    public void PopulateGrid()
    {
        if (cellPrefab == null) return;
        if (numWorlds <= 0) return;

        var grid = GetComponent<GridLayoutGroup>();
        if (grid != null && grid.enabled) grid.enabled = false;

        for (int i = parentRT.childCount - 1; i >= 0; i--) DestroyImmediate(parentRT.GetChild(i).gameObject);

        Vector2 centerLocal = ViewportToLocalPoint(new Vector2(0.5f, 0.6f));
        int gridSize = 1; while (gridSize * gridSize < numWorlds) gridSize += 2;
        int half = gridSize / 2;

        Vector2[,] positions = new Vector2[gridSize, gridSize];
        for (int r = 0; r < gridSize; r++)
            for (int c = 0; c < gridSize; c++)
                positions[r, c] = new Vector2((c - half) * (120f + 16f), -(r - half) * (120f + 16f)) + centerLocal;

        List<Vector2Int> expansion = new List<Vector2Int>();
        expansion.Add(new Vector2Int(half, half));
        for (int layer = 1; layer <= half; layer++)
        {
            int start = half - layer, end = half + layer;
            for (int c = start; c <= end; c++) expansion.Add(new Vector2Int(start, c));
            for (int r = start + 1; r <= end; r++) expansion.Add(new Vector2Int(r, end));
            for (int c = end - 1; c >= start; c--) expansion.Add(new Vector2Int(end, c));
            for (int r = end - 1; r >= start + 1; r--) expansion.Add(new Vector2Int(r, start));
        }

        int created = 0;
        for (int i = 0; i < expansion.Count && created < numWorlds; i++)
        {
            var idx = expansion[i];
            GameObject cell = Instantiate(cellPrefab, parentRT);
            cell.name = $"World_box_{created}";
            var rt = cell.GetComponent<RectTransform>();
            if (rt != null) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(120f, 120f); rt.anchoredPosition = positions[idx.x, idx.y]; }
            var wb = cell.GetComponent<WorldBox>(); if (wb != null) { wb.worldIndex = created; wb.displayName = $"World {created + 1}"; }
            var label = cell.GetComponentInChildren<TextMeshProUGUI>(); if (label != null) label.text = $"World {created + 1}";
            var btn = cell.GetComponent<Button>(); if (btn != null) { int capture = created; btn.onClick.AddListener(() => OnWorldButtonClicked(capture)); }
            created++;
        }

        if (scenePerIndex == null) scenePerIndex = new List<string>();
        while (scenePerIndex.Count < numWorlds) scenePerIndex.Add(null);

        // IMPORTANT: fill any empty scenePerIndex entries randomly from the 'scenes' pool now so
        // each world gets a random scene type (mountains/main/village/etc). This also persists mapping
        // into GameState so Signs can use it even after the menu unloads.
        PersistMappingToGameState();
    }

    private void OnWorldButtonClicked(int worldIndex)
    {
        if (isLoading) return;

        string chosen = GetSceneNameForIndex(worldIndex);
        if (string.IsNullOrEmpty(chosen)) { Debug.LogWarning($"no scene for index {worldIndex}"); return; }

        GameState.SelectedWorldIndex = worldIndex;
        GameState.SelectedWorldSceneName = chosen;
        PersistMappingToGameState();
        DisableAllButtons();
        isLoading = true;
        GameState.MenuSceneName = menuSceneName;

        if (SceneLoadManager.Instance != null)
            SceneLoadManager.Instance.LoadWorldByIndex(worldIndex, this, menuSceneName);
        else
            StartCoroutine(LoadSceneAndUnloadMenu(chosen));
    }

    public string GetSceneNameForIndex(int index)
    {
        if (scenePerIndex != null && index >= 0 && index < scenePerIndex.Count && !string.IsNullOrEmpty(scenePerIndex[index]))
            return scenePerIndex[index];
        if (scenes != null && scenes.Count > 0) return scenes[index % scenes.Count];
        return null;
    }

    private void PersistMappingToGameState()
    {
        if (scenePerIndex == null) scenePerIndex = new List<string>();
        while (scenePerIndex.Count < numWorlds) scenePerIndex.Add(null);

        // Fill empty entries randomly from the scenes pool (if provided), otherwise default to "Main"
        for (int i = 0; i < scenePerIndex.Count; i++)
        {
            if (string.IsNullOrEmpty(scenePerIndex[i]))
            {
                if (scenes != null && scenes.Count > 0)
                {
                    int pick = UnityEngine.Random.Range(0, scenes.Count);
                    scenePerIndex[i] = scenes[pick];
                }
                else
                {
                    scenePerIndex[i] = "Main";
                }
            }
        }

        GameState.ScenePerIndex = new List<string>(scenePerIndex);

        int gridSize = 1; while (gridSize * gridSize < numWorlds) gridSize += 2;
        List<Vector2Int> indexToGrid = new List<Vector2Int>(new Vector2Int[numWorlds]);
        List<int> gridToIndex = new List<int>(gridSize * gridSize);
        for (int i = 0; i < gridSize * gridSize; i++) gridToIndex.Add(-1);
        List<Vector2Int> expansion = new List<Vector2Int>(); expansion.Add(new Vector2Int(gridSize/2, gridSize/2));
        int half = gridSize/2;
        for (int layer = 1; layer <= half; layer++)
        {
            int start = half - layer, end = half + layer;
            for (int c = start; c <= end; c++) expansion.Add(new Vector2Int(start, c));
            for (int r = start + 1; r <= end; r++) expansion.Add(new Vector2Int(r, end));
            for (int c = end - 1; c >= start; c--) expansion.Add(new Vector2Int(end, c));
            for (int r = end - 1; r >= start + 1; r--) expansion.Add(new Vector2Int(r, start));
        }
        for (int idx = 0; idx < numWorlds && idx < expansion.Count; idx++)
        {
            var p = expansion[idx];
            indexToGrid[idx] = p;
            int flat = p.x * gridSize + p.y;
            if (flat >= 0 && flat < gridToIndex.Count) gridToIndex[flat] = idx;
        }
        GameState.GridSize = gridSize;
        GameState.IndexToGrid = new List<Vector2Int>(indexToGrid);
        GameState.GridToIndexFlatten = new List<int>(gridToIndex);
        GameState.RebuildGridFlatten();
        Debug.Log($"WorldSelector_PersistScenes: Persisted ScenePerIndex (count={GameState.ScenePerIndex.Count})");
    }

    public void PersistMappingToGameStatePublic() => PersistMappingToGameState();

    private void DisableAllButtons()
    {
        for (int i = 0; i < parentRT.childCount; i++)
        {
            var child = parentRT.GetChild(i);
            var btn = child.GetComponent<Button>();
            if (btn != null) btn.interactable = false;
        }
    }

    IEnumerator LoadSceneAndUnloadMenu(string sceneToLoad)
    {
        if (string.IsNullOrEmpty(sceneToLoad)) yield break;
        string previous = GameState.SelectedWorldSceneName;
        GameState.SelectedWorldSceneName = sceneToLoad;

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Additive);
        if (op == null) { Debug.LogWarning($"LoadSceneAsync returned null for {sceneToLoad}"); yield break; }
        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;
        Scene loaded = SceneManager.GetSceneByName(sceneToLoad);
        if (loaded.IsValid()) SceneManager.SetActiveScene(loaded);

        if (!string.IsNullOrEmpty(previous) && previous != sceneToLoad)
        {
            var prevScene = SceneManager.GetSceneByName(previous);
            if (prevScene.IsValid() && prevScene.isLoaded)
            {
                var u = SceneManager.UnloadSceneAsync(prevScene);
                if (u != null) while (!u.isDone) yield return null;
            }
            else
            {
                Debug.LogWarning($"WorldSelector_PersistScenes: previous scene '{previous}' not found or not loaded for unload");
            }
        }

        if (!string.IsNullOrEmpty(menuSceneName))
        {
            var menuScene = SceneManager.GetSceneByName(menuSceneName);
            if (menuScene.IsValid() && menuScene.isLoaded)
            {
                var u2 = SceneManager.UnloadSceneAsync(menuScene);
                if (u2 != null) while (!u2.isDone) yield return null;
            }
            else
            {
                TryDestroyPersistentMenuRoot();
            }
        }

        isLoading = false;
        Debug.Log($"WorldSelector_PersistScenes: Loaded '{sceneToLoad}'");
    }

    Vector2 ViewportToLocalPoint(Vector2 viewportPos)
    {
        Vector2 screenPoint = new Vector2(viewportPos.x * Screen.width, viewportPos.y * Screen.height);
        Vector2 localPoint;
        Camera cam = parentCanvas != null ? parentCanvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRT, screenPoint, cam, out localPoint);
        return localPoint;
    }

    private void TryDestroyPersistentMenuRoot()
    {
        var go = GameObject.Find(menuSceneName);
        if (go != null) Destroy(go);
        var alt = GameObject.Find("Menu"); if (alt != null) Destroy(alt);
    }
}