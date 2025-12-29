using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RelayUI : MonoBehaviour
{
    public ClientSessionMarker marker;
    public TMP_InputField joinCodeInput;
    public TMP_Text joinCodeDisplay;
    public Button hostButton;
    public Button joinButton;
    public Button startLocalButton;

    void Start()
    {
        hostButton.onClick.AddListener(OnHostPressed);
        joinButton.onClick.AddListener(OnJoinPressed);
        startLocalButton.onClick.AddListener(OnStartLocalPressed);
    }

    public async void OnHostPressed()
    {
        if (marker != null)
        {
            string code = await marker.StartHostGame();
            if (joinCodeDisplay != null)
                joinCodeDisplay.text = $"Join Code: {code}";
            marker.LoadGameScene();
        }
    }

    public async void OnJoinPressed()
    {
        string code = joinCodeInput.text.Trim();
        if (!string.IsNullOrEmpty(code) && marker != null)
        {
            await marker.JoinGame(code);
            marker.LoadGameScene();
        }
    }

    public void OnStartLocalPressed()
    {
        if (marker != null)
            marker.StartLocalGame();
    }
}