using UnityEngine;
using UnityEngine.EventSystems;
namespace ChronoWild.UI
{
    public sealed class SlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public GameHUD hud; public int index;
        public void OnPointerEnter(PointerEventData eventData) { if (hud) hud.HoverSlot(index); }
        public void OnPointerExit(PointerEventData eventData) { if (hud) hud.ClearHover(); }
    }
}
