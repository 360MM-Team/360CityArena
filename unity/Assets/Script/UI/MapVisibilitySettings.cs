using UnityEngine;

namespace MovieMap.UI
{
    /// <summary>
    /// Centralizes whether the in-game map UI is allowed to be shown.
    /// Unity will default to showing the map unless explicitly disabled via Python handshake.
    /// </summary>
    public static class MapVisibilitySettings
    {
        private static bool _mapAllowed = true;
        private static GameObject _registeredMapParent;

        /// <summary>
        /// Returns whether the map may be displayed.
        /// </summary>
        public static bool IsMapAllowed => _mapAllowed;

        /// <summary>
        /// Registers the map parent GameObject so visibility changes can be applied immediately.
        /// </summary>
        public static void RegisterMapParent(GameObject mapParent)
        {
            _registeredMapParent = mapParent;
            if (!_mapAllowed && _registeredMapParent != null && _registeredMapParent.activeSelf)
            {
                _registeredMapParent.SetActive(false);
            }
        }

        /// <summary>
        /// Updates map visibility permission and hides the map if disallowed.
        /// </summary>
        public static void SetMapAllowed(bool allowed)
        {
            _mapAllowed = allowed;
            if (!_mapAllowed && _registeredMapParent != null && _registeredMapParent.activeSelf)
            {
                _registeredMapParent.SetActive(false);
            }
        }
    }
}
