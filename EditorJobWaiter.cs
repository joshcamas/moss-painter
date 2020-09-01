using System;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using System.Diagnostics;

namespace Ardenfall.Utility
{
    public class EditorJobWaiter
    {
        private Action<bool> onComplete;
        private JobHandle handler;

        private bool running = false;

        public EditorJobWaiter(JobHandle handler, Action<bool> onComplete)
        {
            this.handler = handler;
            this.onComplete = onComplete;
        }

        public bool IsRunning => running;

        public void Start()
        {
            running = true;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += Update;
#else
            Debug.LogError("Cannot run Editor Job Waiter in playmode, rip");
#endif
        }

        /// <summary>
        /// Forces job to complete, and executes onComplete with a failure boolean
        /// </summary>
        public void ForceCancel()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= Update;
#endif

            handler.Complete();
            running = false;

            onComplete?.Invoke(false);

        }

        /// <summary>
        /// Forces job to complete, and executes onComplete with a success boolean
        /// </summary>
        public void ForceComplete()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= Update;
#endif

            handler.Complete();
            running = false;

            onComplete?.Invoke(true);

        }

        private void Update()
        {
            if(handler.IsCompleted)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.update -= Update;
#endif

                running = false;

                handler.Complete();

                onComplete?.Invoke(true);

            }
        }
    }
}
