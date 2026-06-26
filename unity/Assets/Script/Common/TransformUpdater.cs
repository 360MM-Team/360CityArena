using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Increment local position, rotation, and scale in Update.
public class TransformUpdater : MonoBehaviour
{
    [SerializeField]
    bool _isLocal;
    [SerializeField]
    Vector3 _positionDiff;
    [SerializeField]
    Vector3 _rotationDiff;
    [SerializeField]
    Vector3 _scaleDiff;

    void Update()
    {
        if (_isLocal)
        {
            transform.localPosition += _positionDiff;
            transform.Rotate(_rotationDiff);
            transform.localScale += _scaleDiff;
        }
        else
        {
            transform.position += _positionDiff;
            transform.Rotate(_rotationDiff, Space.World);
            if(transform.lossyScale.x == 0 || transform.lossyScale.y == 0 || transform.lossyScale.z == 0) { return; }
            Vector3 v = Vector3.Scale(_scaleDiff, transform.localScale);
            transform.localScale += Vector3.Scale(v, new Vector3(1 / transform.lossyScale.x, 1 / transform.lossyScale.y, 1 / transform.lossyScale.z));
        }
    }
}
