using System.Collections.Generic;
using UnityEngine;

public class HomeDisplayArranger : MonoBehaviour
{
    public Transform CameraPos;
    public Transform AttackerAnchor;
    public Transform DefenserAnchor;
    private void Awake()
    {
        if (CameraPos == null || AttackerAnchor == null || DefenserAnchor == null)
        {
            throw new System.Exception();
        }
    }
}