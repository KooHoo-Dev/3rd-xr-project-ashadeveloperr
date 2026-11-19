using UnityEngine;
using UnityEngine.InputSystem;

namespace Untrodden
{
    /// <summary>
    /// 왼쪽 컨트롤러 메뉴 버튼을 길게 누르면 마지막 모닥불로 돌아간다(갇혔을 때 탈출용).
    /// 에디터에서는 R 키로도 동작한다.
    /// </summary>
    public class ReturnToCampfireInput : MonoBehaviour
    {
        public float holdSeconds = 1.5f;

        InputAction m_Action;
        float m_Held;
        bool m_Announced;
        bool m_Fired;

        void OnEnable()
        {
            m_Action = new InputAction("ReturnToCampfire", InputActionType.Button);
            m_Action.AddBinding("<XRController>{LeftHand}/{MenuButton}");
            m_Action.AddBinding("<XRController>{LeftHand}/menuButton");
            m_Action.AddBinding("<XRController>{LeftHand}/menu");
            m_Action.AddBinding("<Keyboard>/r");
            m_Action.Enable();
        }

        void OnDisable()
        {
            m_Action?.Disable();
            m_Action?.Dispose();
            m_Action = null;
        }

        void Update()
        {
            var gm = GameManager.Instance;
            bool pressed = m_Action != null && m_Action.IsPressed() && gm != null && gm.State == GameState.Playing;
            if (!pressed)
            {
                m_Held = 0f;
                m_Announced = false;
                m_Fired = false;
                return;
            }

            if (m_Fired) return; // 손을 뗄 때까지 다시 발동하지 않음
            m_Held += Time.deltaTime;
            if (!m_Announced && m_Held > 0.25f)
            {
                m_Announced = true;
                gm.Say("계속 누르고 있으면 마지막 모닥불로 돌아가요…");
            }
            if (m_Held >= holdSeconds)
            {
                m_Fired = true;
                gm.ReturnToCheckpoint("마지막 모닥불로 돌아왔어요.");
            }
        }
    }
}
