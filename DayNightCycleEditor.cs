#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System;

/// <summary>
/// Custom inspector & preview controls for DayNightCycle
/// - Provides a preview slider, Play/Stop preview in the editor, step controls and quick jump buttons.
/// - When "Play Preview" is active the inspector advances time using EditorApplication.update and repaints scene/views.
/// - Safe: stops preview when editor assembly reloads or the object is destroyed.
/// - Place this file under an "Editor" folder (Assets/Editor/) so Unity treats it as an editor script.
/// </summary>
[CustomEditor(typeof(DayNightCycle))]
[CanEditMultipleObjects]
public class DayNightCycleEditor : Editor
{
    private DayNightCycle cycle => (DayNightCycle)target;

    // preview state
    private bool isPreviewPlaying = false;
    private double lastEditorTime = 0.0;
    private float previewSpeedMultiplier = 1f; // multiplies Editor deltaTime when previewing
    private bool liveUpdateSceneView = true;

    // foldouts
    private bool showPreviewControls = true;

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        // ensure preview stops if script recompiles
        EditorApplication.update -= EditorUpdate;
        lastEditorTime = EditorApplication.timeSinceStartup;
    }

    private void OnDisable()
    {
        StopPreview();
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }

    public override void OnInspectorGUI()
    {
        // draw default inspector first (so user can change cycle settings)
        serializedObject.Update();
        DrawDefaultInspectorFields();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        DrawPreviewControls();
        EditorGUILayout.Space();

        // quick manual controls
        DrawQuickButtons();
    }

    private void DrawDefaultInspectorFields()
    {
        // Use standard property fields to preserve undo support and multi-editing
        SerializedProperty prop = serializedObject.GetIterator();
        prop.NextVisible(true); // skip script field
        while (prop.NextVisible(false))
        {
            // Hide fields that are helpful to keep but we manage preview UI for them (optional)
            EditorGUILayout.PropertyField(prop, true);
        }
    }

    private void DrawPreviewControls()
    {
        showPreviewControls = EditorGUILayout.Foldout(showPreviewControls, "Preview Controls", true);
        if (!showPreviewControls) return;

        EditorGUI.indentLevel++;

        // Time slider (hours)
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Time of Day (hours)", GUILayout.MaxWidth(140));
        float newHours = EditorGUILayout.Slider(cycle.timeOfDayHours, 0f, 24f);
        if (!Mathf.Approximately(newHours, cycle.timeOfDayHours))
        {
            Undo.RecordObject(cycle, "Set Time Of Day");
            cycle.SetTimeOfDay(newHours);
            EditorUtility.SetDirty(cycle);
            MarkSceneDirty();
            RepaintAndScene();
        }
        EditorGUILayout.EndHorizontal();

        // normalized slider
        float normalized = cycle.timeOfDayHours / 24f;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Normalized", GUILayout.MaxWidth(140));
        float newNorm = EditorGUILayout.Slider(normalized, 0f, 1f);
        if (!Mathf.Approximately(newNorm, normalized))
        {
            Undo.RecordObject(cycle, "Set Time Of Day (Normalized)");
            cycle.SetTimeOfDay(newNorm * 24f);
            EditorUtility.SetDirty(cycle);
            MarkSceneDirty();
            RepaintAndScene();
        }
        EditorGUILayout.EndHorizontal();

        // play controls and speed
        EditorGUILayout.BeginHorizontal();
        if (!isPreviewPlaying)
        {
            if (GUILayout.Button("Play Preview", GUILayout.Height(24)))
            {
                StartPreview();
            }
        }
        else
        {
            if (GUILayout.Button("Stop Preview", GUILayout.Height(24)))
            {
                StopPreview();
            }
        }

        if (GUILayout.Button("Step +1h", GUILayout.Height(24)))
        {
            StepHours(1f);
        }
        if (GUILayout.Button("Step -1h", GUILayout.Height(24)))
        {
            StepHours(-1f);
        }
        EditorGUILayout.EndHorizontal();

        // speed multiplier and live update toggle
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Preview Speed x", GUILayout.MaxWidth(100));
        float newSpeed = EditorGUILayout.Slider(previewSpeedMultiplier, 0.1f, 10f);
        if (!Mathf.Approximately(newSpeed, previewSpeedMultiplier))
        {
            previewSpeedMultiplier = newSpeed;
        }
        liveUpdateSceneView = EditorGUILayout.ToggleLeft("Live repaint", liveUpdateSceneView, GUILayout.MaxWidth(120));
        EditorGUILayout.EndHorizontal();

        // quick time jumps
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Sunrise (6h)")) { JumpToHours(6f); }
        if (GUILayout.Button("Noon (12h)"))   { JumpToHours(12f); }
        if (GUILayout.Button("Sunset (18h)")) { JumpToHours(18f); }
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
    }

    private void DrawQuickButtons()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Snapshot: Apply current to Scene", GUILayout.Height(22)))
        {
            // Ensure the current cycle state is applied to the scene and marked dirty
            EditorUtility.SetDirty(cycle);
            MarkSceneDirty();
            RepaintAndScene();
        }
        if (GUILayout.Button("Reset Day Length to 120s", GUILayout.Height(22)))
        {
            Undo.RecordObject(cycle, "Reset Day Length");
            cycle.SetDayLength(120f);
            EditorUtility.SetDirty(cycle);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void StartPreview()
    {
        if (isPreviewPlaying) return;
        isPreviewPlaying = true;
        lastEditorTime = EditorApplication.timeSinceStartup;
        EditorApplication.update -= EditorUpdate;
        EditorApplication.update += EditorUpdate;
        if (debugging) Debug.Log("[DayNightCycleEditor] Preview started.");
    }

    private void StopPreview()
    {
        if (!isPreviewPlaying) return;
        isPreviewPlaying = false;
        EditorApplication.update -= EditorUpdate;
        if (debugging) Debug.Log("[DayNightCycleEditor] Preview stopped.");
    }

    private void EditorUpdate()
    {
        if (!isPreviewPlaying || cycle == null)
        {
            StopPreview();
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        double delta = now - lastEditorTime;
        lastEditorTime = now;

        // Advance the cycle.timeOfDayHours manually using previewSpeedMultiplier.
        // We use the cycle.dayLengthInSeconds as base speed: faster dayLength -> faster hours per sec.
        float dayLen = Mathf.Max(0.0001f, cycle.dayLengthInSeconds);
        // hoursPerSecond from cycle
        float hoursPerSecond = 24f / dayLen;
        float advanceHours = (float)(delta * hoursPerSecond) * previewSpeedMultiplier;

        Undo.RecordObject(cycle, "Preview Advance Time");
        cycle.SetTimeOfDay(cycle.timeOfDayHours + advanceHours);
        EditorUtility.SetDirty(cycle);

        if (liveUpdateSceneView)
            RepaintAndScene();
    }

    private void StepHours(float hours)
    {
        Undo.RecordObject(cycle, "Step Time");
        cycle.SetTimeOfDay(cycle.timeOfDayHours + hours);
        EditorUtility.SetDirty(cycle);
        RepaintAndScene();
    }

    private void JumpToHours(float hours)
    {
        Undo.RecordObject(cycle, "Jump Time");
        cycle.SetTimeOfDay(hours);
        EditorUtility.SetDirty(cycle);
        RepaintAndScene();
    }

    private void RepaintAndScene()
    {
        // Repaint all views so directional light and ambient changes are visible immediately
        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate();
        // Mark active scene dirty so changes persist if needed
        MarkSceneDirty();
    }

    private void MarkSceneDirty()
    {
        if (!Application.isPlaying)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }
    }

    private void OnPlayModeStateChanged(PlayModeStateChange obj)
    {
        // Stop preview when entering playmode or exiting
        if (obj == PlayModeStateChange.ExitingEditMode || obj == PlayModeStateChange.EnteredPlayMode)
        {
            StopPreview();
        }
    }

    // small debug toggle for editor script internal logs
    private bool debugging => false;

    // Ensure preview stops on assembly reload
    [UnityEditor.Callbacks.DidReloadScripts]
    private static void OnScriptsReloaded()
    {
        EditorApplication.update -= null; // no-op; ensures no stale references (safe)
    }
}
#endif