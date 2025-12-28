using UnityEngine;
using UnityEngine.EventSystems;

// Attach to each menu button GameObject. Set `menu` to the MenuSelection instance
// and `index` to the button's index (0 = Play, 1 = Join, 2 = Exit).
public class MenuButtonHover : MonoBehaviour, IPointerEnterHandler
{
    public MenuSelection menu;
    public int index = 0;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (menu != null)
            menu.SelectIndex(index);
    }
}
