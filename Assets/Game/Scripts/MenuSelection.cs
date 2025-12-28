using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

public class MenuSelection : MonoBehaviour
{
    [Tooltip("Assign the three menu button GameObjects in order: Play, Join, Exit.")]
    public GameObject[] buttons;

    [Header("Actions")]
    [Tooltip("Scene name to load when Play is activated.")]
    public string playSceneName = "LevelScene";

    [Tooltip("Optional popup GameObject to show when Join is selected.")]
    public GameObject joinPopup;

    

    int currentIndex = 0;

    void Start()
    {
        // Ensure join popup is hidden on start (moved to Awake for safer early init)

        // Require explicit button assignments for this app-specific menu.
        if (buttons == null || buttons.Length == 0)
        {
            Debug.LogError("MenuSelection: no buttons assigned. Assign Play, Join, Exit in the Inspector.");
            return;
        }

        currentIndex = Mathf.Clamp(currentIndex, 0, buttons.Length - 1);
        // Ensure each button has a listener so mouse over/down events forward to this menu.
        for (int i = 0; i < buttons.Length; i++)
        {
            var go = buttons[i];
            if (go == null)
                continue;

            var listener = go.GetComponent<MenuButtonListener>();
            if (listener == null)
                listener = go.AddComponent<MenuButtonListener>();

            listener.menu = this;
            listener.index = i;
        }

        UpdateArrows();
    }

    void Awake()
    {
        // Hide popup early to avoid Nova processing it before initialization
        if (joinPopup != null)
            joinPopup.SetActive(false);
    }

    void Update()
    {
        HandleInput();
    }

    void HandleInput()
    {
        // If join popup is open, only allow closing with Escape (or equivalent)
        if (joinPopup != null && joinPopup.activeSelf)
        {
            var kbcheck = Keyboard.current;
            if (kbcheck != null)
            {
                if (kbcheck.escapeKey.wasPressedThisFrame)
                    HideJoinPopup();
                return;
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                    HideJoinPopup();
                return;
            }
        }
        // Prefer the new Input System when available.
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)
                Move(1);
            else if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)
                Move(-1);

            if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                ActivateCurrent();
            return;
        }

        // Fallback to legacy Input (only works if old input system active).
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
        {
            Move(1);
        }
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
        {
            Move(-1);
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
        {
            ActivateCurrent();
        }
    }

    // Move selection by direction (1 or -1). Wraps around.
    public void Move(int dir)
    {
        if (buttons == null || buttons.Length == 0)
            return;

        currentIndex = (currentIndex + dir + buttons.Length) % buttons.Length;
        UpdateArrows();
    }

    // Called by hover helper or UI events to explicitly select an index.
    public void SelectIndex(int idx)
    {
        if (buttons == null || idx < 0 || idx >= buttons.Length)
            return;

        currentIndex = idx;
        UpdateArrows();
    }

    // NovaUI and other custom UI systems can call this when a button is hovered.
    // Pass the hovered button GameObject and the menu will find and select its index.
    public void OnButtonHover(GameObject hoveredButton)
    {
        if (hoveredButton == null || buttons == null)
            return;

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == hoveredButton)
            {
                SelectIndex(i);
                return;
            }
        }
    }

    // Alternate: Nova can call this directly with the index (0 = Play, 1 = Join, 2 = Exit).
    public void OnButtonHoverIndex(int index)
    {
        SelectIndex(index);
    }

    // Called on mouse-down / primary press. Selects and activates the button.
    // For Nova menus, this is wired from MenuButtonListener or other hover/click helpers.
    public void OnButtonDownIndex(int index)
    {
        if (buttons == null || index < 0 || index >= buttons.Length)
            return;

        // First update the current index & arrows.
        SelectIndex(index);

        // Then run the same activation path used by keyboard input (Play / Join / Exit etc.).
        ActivateCurrent();
    }

    void UpdateArrows()
    {
        if (buttons == null)
            return;

        for (int i = 0; i < buttons.Length; i++)
        {
            var go = buttons[i];
            if (go == null)
                continue;

            var arrow = go.transform.Find("Arrow");
            if (arrow != null)
                arrow.gameObject.SetActive(i == currentIndex);
        }
    }

    // --- Join popup helpers ---
    public void ShowJoinPopup()
    {
        if (joinPopup == null)
            return;

        joinPopup.SetActive(true);
        // Defer focusing until the next frame to ensure Nova/other UI systems
        // have updated internal state. This avoids transient NullReferenceExceptions
        // originating from Nova's input pipeline.
        StartCoroutine(FocusPopupNextFrame());
    }

    IEnumerator FocusPopupNextFrame()
    {
        yield return null;
        if (joinPopup == null)
            yield break;

        try
        {
            if (EventSystem.current == null)
                yield break;

            // prefer TMP then legacy InputField
            var tmp = joinPopup.GetComponentInChildren<TMP_InputField>(true);
            if (tmp != null && tmp.gameObject.activeInHierarchy)
            {
                try { tmp.Select(); } catch { }
                try { EventSystem.current.SetSelectedGameObject(tmp.gameObject); } catch { }
                yield break;
            }

            var legacy = joinPopup.GetComponentInChildren<UnityEngine.UI.InputField>(true);
            if (legacy != null && legacy.gameObject.activeInHierarchy)
            {
                try { legacy.Select(); } catch { }
                try { EventSystem.current.SetSelectedGameObject(legacy.gameObject); } catch { }
                yield break;
            }

            // If nothing focusable found, do not modify EventSystem selection — avoid touching
            // Nova's internal state which may rely on a persistent selected object.
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("MenuSelection: FocusPopupNextFrame exception: " + ex.Message);
        }
    }

    public void HideJoinPopup()
    {
        if (joinPopup == null)
            return;

        joinPopup.SetActive(false);
    }

    // Call this from the popup submit button. Overload with a code parameter
    // so NovaUI can pass the entered code directly.
    public void OnJoinSubmit(string code)
    {
        if (!string.IsNullOrEmpty(code))
            JoinRoomByCode(code);
        HideJoinPopup();
    }

    // Convenience: if the submit button doesn't pass a code, attempt to find
    // an input field inside the popup (TMP or legacy) and read its value.
    public void OnJoinSubmit()
    {
        if (joinPopup == null)
        {
            HideJoinPopup();
            return;
        }

        string code = null;
        var tmp = joinPopup.GetComponentInChildren<TMP_InputField>();
        if (tmp != null)
            code = tmp.text;
        else
        {
            var legacy = joinPopup.GetComponentInChildren<UnityEngine.UI.InputField>();
            if (legacy != null)
                code = legacy.text;
        }

        if (!string.IsNullOrEmpty(code))
            JoinRoomByCode(code);

        HideJoinPopup();
    }

    // Override or listen for this message to actually join the room.
    public void JoinRoomByCode(string code)
    {
        // Default behaviour: send message so other components can handle it.
        gameObject.SendMessage("JoinWithCode", code, SendMessageOptions.DontRequireReceiver);
        Debug.Log("Requested join with code: " + code);
    }

    void ActivateCurrent()
    {
        if (buttons == null || buttons.Length == 0)
            return;

        var go = buttons[currentIndex];
        if (go == null)
            return;

        // First, handle built-in Play / Join / Exit actions by index.
        if (currentIndex == 0)
        {
            if (!string.IsNullOrEmpty(playSceneName))
                SceneManager.LoadScene(playSceneName);
        }
        else if (currentIndex == 1)
        {
            ShowJoinPopup();
        }
        else if (currentIndex == 2)
        {
            Application.Quit();
            Debug.Log("Application.Quit() called");
        }

        // If there's a Unity UI Button component, invoke its onClick.
        var btn = go.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.Invoke();
            return;
        }

        // Fallback: try sending a message named "OnSelect" so custom Nova buttons can respond.
        go.SendMessage("OnSelect", SendMessageOptions.DontRequireReceiver);
    }
}
