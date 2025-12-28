using UnityEngine;
using UnityEngine.EventSystems;

// Attach to each menu button GameObject (wired automatically by MenuSelection.Start).
// For Nova or Unity UI, this captures pointer enter/down and forwards to MenuSelection.
// Also supports collider-based menus via OnMouseEnter / OnMouseDown so it works even
// if the Unity EventSystem / raycasters are not fully configured.
public class MenuButtonListener : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, IPointerClickHandler
{
    public MenuSelection menu;
    public int index;

    void EnsureMenu()
    {
        if (menu == null)
            menu = FindObjectOfType<MenuSelection>();
    }

    // --- Unity UI / Nova pointer events ---
    public void OnPointerEnter(PointerEventData eventData)
    {
        EnsureMenu();
        if (menu != null)
            menu.OnButtonHoverIndex(index);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        EnsureMenu();
        if (menu != null)
            menu.OnButtonDownIndex(index);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // In case your input module only sends click events, treat them the same as down.
        EnsureMenu();
        if (menu != null)
            menu.OnButtonDownIndex(index);
    }

    // --- Collider-based (non-EventSystem) menus ---
    // These fire when the object has a 2D/3D collider and you hover/click it,
    // without needing an EventSystem / GraphicRaycaster.
    void OnMouseEnter()
    {
        EnsureMenu();
        if (menu != null)
            menu.OnButtonHoverIndex(index);
    }

    void OnMouseDown()
    {
        EnsureMenu();
        if (menu != null)
            menu.OnButtonDownIndex(index);
    }
}
