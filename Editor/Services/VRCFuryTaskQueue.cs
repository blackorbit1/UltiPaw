#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

public static class VRCFuryTaskQueue
{
    private static readonly Queue<Action> taskQueue = new Queue<Action>();
    private static bool isProcessing = false;

    /// <summary>
    /// Enqueues a task to be run on the next Editor update, ensuring it doesn't block the current UI frame.
    /// If a task is already pending, this implementation can be tuned to either append or replace.
    /// For Sliders, we usually only care about the LATEST state.
    /// </summary>
    public static void Enqueue(Action task, bool clearExisting = true)
    {
        if (clearExisting) taskQueue.Clear();
        taskQueue.Enqueue(task);
        
        if (!isProcessing)
        {
            EditorApplication.update += ProcessQueue;
            isProcessing = true;
        }
    }

    public static bool IsWorking => taskQueue.Count > 0;

    private static void ProcessQueue()
    {
        // We only process one task per update frame to keep things responsive
        if (taskQueue.Count > 0)
        {
            var task = taskQueue.Dequeue();
            try
            {
                task?.Invoke();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[VRCFuryTaskQueue] Task failed: {ex.Message}");
            }
        }

        if (taskQueue.Count == 0)
        {
            EditorApplication.update -= ProcessQueue;
            isProcessing = false;
        }
    }
}
#endif
