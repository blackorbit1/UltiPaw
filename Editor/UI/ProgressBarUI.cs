#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ProgressBarUI
{
    private static readonly Color ProgressBarBg = new Color(0.3f, 0.3f, 0.3f, 1f);
    private static readonly Color ProgressBarFill = new Color(0.2f, 0.7f, 0.2f, 1f);
    private static readonly Color ProgressBarError = new Color(0.8f, 0.2f, 0.2f, 1f);
    private static readonly Color ProgressBarText = Color.white;
    
    public static void DrawTaskProgressBars()
    {
        var activeTasks = AsyncTaskManager.Instance.GetAllActiveTasks();
        
        if (activeTasks.Count == 0)
            return;

        EditorGUILayout.Space(5);
        
        // Draw container box for all progress bars
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.LabelField("Active Tasks", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);
        
        foreach (var task in activeTasks)
        {
            DrawSingleProgressBar(task);
            EditorGUILayout.Space(2);
        }
        
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(5);
    }
    
    public static void DrawSingleProgressBar(TaskProgress task)
    {
        if (task == null)
            return;

        EditorGUILayout.BeginHorizontal();
        
        // Progress bar area
        var progressRect = EditorGUILayout.GetControlRect(GUILayout.Height(18), GUILayout.ExpandWidth(true));
        var labelRect = new Rect(progressRect.x, progressRect.y, progressRect.width, progressRect.height);
        
        // Draw background
        EditorGUI.DrawRect(progressRect, ProgressBarBg);
        
        // Draw progress fill
        Color fillColor = task.hasError ? ProgressBarError : ProgressBarFill;
        var fillRect = new Rect(progressRect.x, progressRect.y, progressRect.width * task.progress, progressRect.height);
        EditorGUI.DrawRect(fillRect, fillColor);
        
        // Draw border
        DrawRectBorder(progressRect, Color.black, 1);
        
        // Draw text label
        var originalColor = GUI.color;
        GUI.color = ProgressBarText;
        
        string displayText = GetProgressDisplayText(task);
        var centeredStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11
        };
        
        // Create shadow effect for better readability
        var shadowRect = new Rect(labelRect.x + 1, labelRect.y + 1, labelRect.width, labelRect.height);
        var shadowStyle = new GUIStyle(centeredStyle) { normal = { textColor = Color.black } };
        GUI.Label(shadowRect, displayText, shadowStyle);
        
        // Draw main text
        centeredStyle.normal.textColor = ProgressBarText;
        GUI.Label(labelRect, displayText, centeredStyle);
        
        GUI.color = originalColor;
        
        // Cancel button for non-completed tasks
        if (!task.isCompleted && !task.isCancelled)
        {
            if (GUILayout.Button("✕", GUILayout.Width(20), GUILayout.Height(18)))
            {
                AsyncTaskManager.Instance.CancelTask(task.id);
            }
        }
        
        EditorGUILayout.EndHorizontal();
    }
    
    private static string GetProgressDisplayText(TaskProgress task)
    {
        if (task.isCancelled)
            return $"Cancelled: {task.description}";
        
        if (task.hasError)
            return $"Error: {task.description}";
        
        if (task.isCompleted)
            return $"Completed: {task.description}";
        
        int progressPercent = Mathf.RoundToInt(task.progress * 100);
        return $"{task.description} ({progressPercent}%)";
    }
    
    private static void DrawRectBorder(Rect rect, Color color, int borderWidth)
    {
        var originalColor = GUI.color;
        GUI.color = color;
        
        // Top
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, borderWidth), color);
        // Bottom
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - borderWidth, rect.width, borderWidth), color);
        // Left
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, borderWidth, rect.height), color);
        // Right
        EditorGUI.DrawRect(new Rect(rect.x + rect.width - borderWidth, rect.y, borderWidth, rect.height), color);
        
        GUI.color = originalColor;
    }
}

public static class ProgressBarUIExtensions
{
    public static void DrawProgressSection(this UnityEditor.Editor editor)
    {
        ProgressBarUI.DrawTaskProgressBars();
    }
}

// Specialized progress bars for specific operations
public static class UltiPawProgressBars
{
    public static void DrawHashingProgress(string filePath, float progress)
    {
        if (progress < 1.0f)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Calculating hash...", GUILayout.Width(120));
            
            var progressRect = EditorGUILayout.GetControlRect(GUILayout.Height(16));
            EditorGUI.ProgressBar(progressRect, progress, $"Hashing {System.IO.Path.GetFileName(filePath)} ({Mathf.RoundToInt(progress * 100)}%)");
            
            EditorGUILayout.EndHorizontal();
        }
    }
    
    public static void DrawVersionFetchProgress(float progress)
    {
        if (progress < 1.0f)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Fetching versions...", GUILayout.Width(120));
            
            var progressRect = EditorGUILayout.GetControlRect(GUILayout.Height(16));
            EditorGUI.ProgressBar(progressRect, progress, $"Loading version list ({Mathf.RoundToInt(progress * 100)}%)");
            
            EditorGUILayout.EndHorizontal();
        }
    }
    
    public static void DrawDownloadProgress(string versionName, float progress)
    {
        if (progress < 1.0f)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Downloading...", GUILayout.Width(120));
            
            var progressRect = EditorGUILayout.GetControlRect(GUILayout.Height(16));
            EditorGUI.ProgressBar(progressRect, progress, $"Downloading {versionName} ({Mathf.RoundToInt(progress * 100)}%)");
            
            EditorGUILayout.EndHorizontal();
        }
    }
    
    public static void DrawFileOperationProgress(string operation, float progress)
    {
        if (progress < 1.0f)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{operation}...", GUILayout.Width(120));
            
            var progressRect = EditorGUILayout.GetControlRect(GUILayout.Height(16));
            EditorGUI.ProgressBar(progressRect, progress, $"{operation} ({Mathf.RoundToInt(progress * 100)}%)");
            
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif