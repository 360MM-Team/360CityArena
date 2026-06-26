using System.Collections.Generic;
using UnityEngine;

namespace MovieMap.Core
{
    /// <summary>
    /// Generates key actions.
    /// Used for testing by generating random actions.
    /// </summary>
    public class KeyActionGenerator : MonoBehaviour
    {
        [Header("Action Generation Settings")]
        [SerializeField] private bool enableRandomGeneration = true;
        [SerializeField] private float forwardBias = 0.4f; // Forward probability
        [SerializeField] private float rotationBias = 0.3f; // Rotation probability
        [SerializeField] private float stopBias = 0.3f; // Stop probability

        private readonly List<System.Action<KeyAction>> _actionList = new List<System.Action<KeyAction>>();

        private void Awake()
        {
            InitializeActionList();
        }

        private void InitializeActionList()
        {
            _actionList.Clear();
            
            // Forward actions.
            _actionList.Add(action => action.wKey = true); // Forward

            // Rotation actions.
            _actionList.Add(action => action.aKey = true); // Rotate left
            _actionList.Add(action => action.dKey = true); // Rotate right
            _actionList.Add(action => action.leftArrow = true); // Left arrow
            _actionList.Add(action => action.rightArrow = true); // Right arrow

            // Backward.
            _actionList.Add(action => action.sKey = true); // Backward

            // Stop by doing nothing.
            _actionList.Add(action => { }); // Do nothing
        }

        /// <summary>
        /// Generate a random key action.
        /// </summary>
        /// <param name="context">Context, currently unused.</param>
        /// <returns>The generated key action.</returns>
        public KeyAction GenerateKeyAction(object context = null)
        {
            var action = new KeyAction();

            if (!enableRandomGeneration)
            {
                return action; // Return the all-false state.
            }

            // Choose an action based on the configured biases.
            float randomValue = Random.Range(0f, 1f);

            if (randomValue < forwardBias)
            {
                // Forward.
                action.wKey = true;
            }
            else if (randomValue < forwardBias + rotationBias)
            {
                // Rotate.
                GenerateRotationAction(action);
            }
            else if (randomValue < forwardBias + rotationBias + (1f - stopBias))
            {
                // Other actions.
                GenerateOtherAction(action);
            }
            // else: stop by doing nothing.

            return action;
        }

        private void GenerateRotationAction(KeyAction action)
        {
            int rotationType = Random.Range(0, 4);
            switch (rotationType)
            {
                case 0:
                    action.aKey = true;
                    break;
                case 1:
                    action.dKey = true;
                    break;
                case 2:
                    action.leftArrow = true;
                    break;
                case 3:
                    action.rightArrow = true;
                    break;
            }
        }

        private void GenerateOtherAction(KeyAction action)
        {
            int actionType = Random.Range(0, 6);
            switch (actionType)
            {
                case 0:
                    action.sKey = true; // Backward
                    break;
                case 1:
                    action.qKey = true; // Q
                    break;
                case 2:
                    action.eKey = true; // E
                    break;
                case 3:
                    action.upArrow = true; // Up arrow
                    break;
                case 4:
                    action.enter = true; // Enter
                    break;
                default:
                    // Do nothing.
                    break;
            }
        }

        /// <summary>
        /// Generate a specific key action for tests.
        /// </summary>
        public KeyAction GenerateSpecificAction(string actionType)
        {
            var action = new KeyAction();
            
            switch (actionType.ToUpperInvariant())
            {
                case "W":
                case "FORWARD":
                    action.wKey = true;
                    break;
                case "S":
                case "BACKWARD":
                    action.sKey = true;
                    break;
                case "A":
                case "LEFT":
                    action.aKey = true;
                    break;
                case "D":
                case "RIGHT":
                    action.dKey = true;
                    break;
                case "Q":
                    action.qKey = true;
                    break;
                case "E":
                    action.eKey = true;
                    break;
                case "LEFTARROW":
                    action.leftArrow = true;
                    break;
                case "RIGHTARROW":
                    action.rightArrow = true;
                    break;
                case "UPARROW":
                    action.upArrow = true;
                    break;
                case "ENTER":
                    action.enter = true;
                    break;
            }

            return action;
        }

        /// <summary>
        /// Update settings.
        /// </summary>
        public void UpdateSettings(float forwardBias, float rotationBias, float stopBias)
        {
            this.forwardBias = Mathf.Clamp01(forwardBias);
            this.rotationBias = Mathf.Clamp01(rotationBias);
            this.stopBias = Mathf.Clamp01(stopBias);
        }
    }
}
