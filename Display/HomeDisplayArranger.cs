using System.Collections.Generic;
using UnityEngine;

public class HomeDisplayArranger : MonoBehaviour
{
    public Transform CameraPos;
    public Transform CharacterAnchor;
    private void Awake()
    {
        if (CameraPos == null || CharacterAnchor == null)
        {
            throw new System.Exception();
        }
    }
}