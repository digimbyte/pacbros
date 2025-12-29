using UnityEngine;
using TMPro;

public class friendCode : MonoBehaviour
{
    public TMP_Text textBlock;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (textBlock != null)
        {
            textBlock.text = "offline";
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (ClientSessionMarker.Instance != null && !string.IsNullOrEmpty(ClientSessionMarker.Instance.friendCode))
        {
            if (textBlock != null)
            {
                textBlock.text = ClientSessionMarker.Instance.friendCode;
            }
        }
        else
        {
            if (textBlock != null)
            {
                textBlock.text = "offline";
            }
        }
    }
}
