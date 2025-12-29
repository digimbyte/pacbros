using UnityEngine;

/// <summary>
/// Shows/hides child/key UI objects based on whether the camera's current target
/// has the corresponding key ItemId (Key_Green, Key_Yellow, Key_Red, Key_Purple).
/// The script polls the camera target periodically and updates the referenced GameObjects.
/// </summary>
public class inventoryCheck : MonoBehaviour
{
    [Tooltip("CameraControl to read the current follow target from. If null, will FindObjectOfType on Start.")]
    public CameraControl cameraControl;

    [Header("Key visuals")]
    public GameObject greenKeyObject;
    public GameObject yellowKeyObject;
    public GameObject redKeyObject;
    public GameObject purpleKeyObject;

    [Header("Update")]
    [Tooltip("Seconds between inventory checks.")]
    public float refreshInterval = 0.25f;

    float _nextCheck = 0f;

    void Start()
    {
        if (cameraControl == null)
            cameraControl = FindObjectOfType<CameraControl>();

        // initialize visuals
        UpdateVisualsNone();
    }

    void Update()
    {
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + Mathf.Max(0.01f, refreshInterval);

        Transform t = cameraControl != null ? cameraControl.target : null;
        if (t == null)
        {
            UpdateVisualsNone();
            return;
        }

        // This HUD is explicitly for the Player only.
        var player = t.GetComponent<PlayerEntity>();
        if (player != null)
        {
            UpdateVisualsFromPlayer(player);
            return;
        }

        // Fallback: no player target
        UpdateVisualsNone();
    }
    void UpdateVisualsFromPlayer(PlayerEntity player)
    {
        bool hasGreen = player != null && player.Has(ItemId.Key_Green);
        bool hasYellow = player != null && player.Has(ItemId.Key_Yellow);
        bool hasRed = player != null && player.Has(ItemId.Key_Red);
        bool hasPurple = player != null && player.Has(ItemId.Key_Purple);
        SetVisuals(hasGreen, hasYellow, hasRed, hasPurple);
    }


    void UpdateVisualsNone()
    {
        SetVisuals(false, false, false, false);
    }

    void SetVisuals(bool hasGreen, bool hasYellow, bool hasRed, bool hasPurple)
    {
        if (greenKeyObject != null) greenKeyObject.SetActive(hasGreen);
        if (yellowKeyObject != null) yellowKeyObject.SetActive(hasYellow);
        if (redKeyObject != null) redKeyObject.SetActive(hasRed);
        if (purpleKeyObject != null) purpleKeyObject.SetActive(hasPurple);
    }
}
