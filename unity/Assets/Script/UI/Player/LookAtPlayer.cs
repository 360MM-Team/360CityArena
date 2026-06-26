using UnityEngine;

internal class LookAtPlayer : MonoBehaviour
{
    [SerializeField]
    Transform _player;

    private void Update()
    {
        // Rotate this object toward the target.
        transform.LookAt(_player);
    }
}
