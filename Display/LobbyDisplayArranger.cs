using System.Collections.Generic;
using UnityEngine;

public class LobbyDisplayArranger : MonoBehaviour
{
    public Transform CameraPos;
    public List<Transform> AttackerAnchors = new();
    public List<Transform> DefenserAnchors = new();
    public Transform UIPos_AttackCamp;
    public Transform UIPos_DefenseCamp;
    private void Awake()
    {
        if (CameraPos==null||AttackerAnchors == null || DefenserAnchors == null||UIPos_AttackCamp==null||UIPos_DefenseCamp==null)
        {
            throw new System.Exception();
        }
        if (AttackerAnchors.Count != 6 || DefenserAnchors.Count != 2)
        {
            Debug.LogError("Lobby锚点数量错误");
        }
    }
}