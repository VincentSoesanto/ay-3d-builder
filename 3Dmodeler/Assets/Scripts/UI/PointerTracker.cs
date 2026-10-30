using UnityEngine;
using UnityEngine.EventSystems;

namespace Builder.UI
{
    /// <summary>Tells the inventory which button the mouse is over or the gamepad has selected.</summary>
    public class PointerTracker : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        public System.Action OnPointed;
        public void OnPointerEnter(PointerEventData eventData) => OnPointed?.Invoke();
        public void OnSelect(BaseEventData eventData) => OnPointed?.Invoke();
    }
}
