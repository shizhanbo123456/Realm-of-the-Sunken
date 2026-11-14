using System.Collections.Generic;
using UnityEngine;

public class HomeDisplayArranger : MonoBehaviour
{
    public Transform CameraPos;
    public Transform CharacterAnchor;
    [SerializeField]private GameObject Whale;
    [SerializeField]private Transform WhaleEmitter;
    [SerializeField]private float whaleEmitInterval = 5f;
    [SerializeField]private float whaleSpeed = 4f;
    private void Awake()
    {
        if (CameraPos == null || CharacterAnchor == null)
        {
            throw new System.Exception();
        }
    }
    private float emitterTimer=99999;
    private GameObject lastEmittedWhale;
    private void Update()
    {
        emitterTimer += Time.deltaTime;
        if (emitterTimer >= whaleEmitInterval)
        {
            if(lastEmittedWhale!=null)Destroy(lastEmittedWhale);
            lastEmittedWhale = Instantiate(Whale, WhaleEmitter.position, WhaleEmitter.rotation,transform);
            emitterTimer = 0f;
        }
        else
        {
            if (lastEmittedWhale != null)
            {
                lastEmittedWhale.transform.position += lastEmittedWhale.transform.forward * whaleSpeed * Time.deltaTime;
            }
        }
    }
}