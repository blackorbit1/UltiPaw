#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Reflection;

/// <summary>
/// A simple utility class to run Editor Coroutines.
/// Found originally here: https://gist.github.com/benblo/10732554
/// </summary>
public static class EditorCoroutineUtility
{
    private class EditorCoroutineRunner
    {
        private Stack<IEnumerator> coroutineStack;
        private EditorApplication.CallbackFunction updateDelegate;

        public EditorCoroutineRunner(IEnumerator coroutine)
        {
            coroutineStack = new Stack<IEnumerator>();
            coroutineStack.Push(coroutine);
            updateDelegate = Update;
        }

        public void Start()
        {
            EditorApplication.update += updateDelegate;
        }

        public void Stop()
        {
            EditorApplication.update -= updateDelegate;
        }

        private void Update()
        {
            if (coroutineStack.Count == 0)
            {
                Stop();
                return;
            }

            IEnumerator currentCoroutine = coroutineStack.Peek();

            try
            {
                if (!MoveNext(currentCoroutine))
                {
                    coroutineStack.Pop();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Exception in editor coroutine: " + ex);
                // Optionally log the stack trace of the coroutine itself
                // LogCoroutineStackTrace(currentCoroutine);
                Stop(); // Stop processing on error to prevent spam
            }
        }

        private bool MoveNext(IEnumerator coroutine)
        {
            object yielded = coroutine.Current;

            if (yielded is IEnumerator nestedCoroutine)
            {
                coroutineStack.Push(nestedCoroutine);
                return true; // Need to process the nested one first
            }

            if (yielded is Coroutine)
            {
                Debug.LogWarning("EditorCoroutineUtility: Yielding on 'UnityEngine.Coroutine' is not supported in the editor. Use 'yield return null' or another IEnumerator.");
                // Treat it like yield return null for basic cases
                return coroutine.MoveNext();
            }

             if (yielded is CustomYieldInstruction customYield)
             {
                 if (!customYield.keepWaiting)
                 {
                     return coroutine.MoveNext();
                 }
                 return true; // Keep waiting on this coroutine
             }

            // For other yield types (like null, WaitForSeconds in editor doesn't work well), just move next
            return coroutine.MoveNext();
        }

        // Optional: Helper to log stack trace if you have complex nested coroutines
        // private void LogCoroutineStackTrace(IEnumerator coroutine)
        // {
        //     // Implementation would involve reflection to inspect the coroutine's state machine fields
        //     // This is complex and often not necessary for basic usage.
        // }
    }

    public static void StartCoroutineOwnerless(IEnumerator coroutine)
    {
        if (coroutine == null)
        {
            Debug.LogError("Coroutine cannot be null.");
            return;
        }
        EditorCoroutineRunner runner = new EditorCoroutineRunner(coroutine);
        runner.Start();
    }

    // You could add StartCoroutine methods that take an owner object
    // to manage stopping coroutines if the owner is destroyed/disabled,
    // but for simple editor tasks, ownerless is often sufficient.
}
#endif