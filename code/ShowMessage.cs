using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace DemonGameRules.code
{
    public static class NotificationHelper
    {
        // 颜色和布局设置
        private static readonly Color GoldColor = new Color(1f, 0.843f, 0f, 1f);
        private static NotificationSlot singleSlot;
        private const float yPosition = 50f;

        static NotificationHelper()
        {
            // 初始化单个位置槽
            singleSlot = new NotificationSlot
            {
                yPosition = yPosition,
                isAvailable = true
            };
        }

        /// <summary>
        /// 显示预格式化的通知消息（调用方需确保消息已正确处理）
        /// string ShowMessage = "测试测试！";
        /// NotificationHelper.ShowThisMessage(ShowMessage);
        /// <param name="message">已格式化的完整通知文本</param>
        public static void ShowThisMessage(string message, float yOffset = 0f)
        {
            if (string.IsNullOrEmpty(message))
            {
                Debug.LogWarning("通知消息为空，已忽略");
                return;
            }

            // 如果有正在显示的通知，先清除它
            if (!singleSlot.isAvailable && singleSlot.currentController != null)
            {
                singleSlot.currentController.ForceDestroy();
            }

            // 标记槽位为占用
            singleSlot.isAvailable = false;

            // 创建UI对象
            GameObject notificationObj = new GameObject($"Notification_{Time.time}");
            notificationObj.AddComponent<CanvasGroup>().blocksRaycasts = false;
            notificationObj.transform.SetParent(GetCanvas().transform, false);

            // 设置文本
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(notificationObj.transform, false);
            Text text = textObj.AddComponent<Text>();
            text.raycastTarget = false;
            text.font = TryLoadFont("Arial.ttf") ?? CreateDefaultFont();
            text.text = message; // 直接使用传入的预格式化消息
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 9;
            text.color = GoldColor;
            text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = 9;

            // 文字效果
            Shadow shadow = textObj.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.85f);
            Outline outline = textObj.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 1.0f);

            // 定位
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.9f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(600, 60);
            textRect.anchoredPosition = new Vector2(-270, singleSlot.yPosition + yOffset);

            // 添加控制器
            var controller = notificationObj.AddComponent<NotificationController>();
            controller.Init(text, singleSlot);
        }

        public static void ReleaseSlot(NotificationSlot slot)
        {
            slot.isAvailable = true;
            slot.currentController = null;
        }

        private static Font TryLoadFont(string path) => Resources.GetBuiltinResource<Font>(path);
        private static Font CreateDefaultFont() => new Font("Arial");

        private static Canvas GetCanvas()
        {
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject go = new GameObject("Canvas");
                canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000;
                go.AddComponent<CanvasScaler>();
                go.AddComponent<GraphicRaycaster>();
            }
            return canvas;
        }
    }

    public class NotificationSlot
    {
        public float yPosition;
        public bool isAvailable;
        public NotificationController currentController;
    }

    public class NotificationController : MonoBehaviour
    {
        private const float showDuration = 5f;
        public float createTime { get; private set; }
        private NotificationSlot slot;
        private Text textComponent;

        public void Init(Text text, NotificationSlot slot)
        {
            createTime = Time.time;
            textComponent = text;
            this.slot = slot;
            slot.currentController = this;
            StartCoroutine(DestroyAfterDelay());
        }

        IEnumerator DestroyAfterDelay()
        {
            yield return new WaitForSeconds(showDuration);
            ForceDestroy();
        }

        public void ForceDestroy()
        {
            if (textComponent != null) Destroy(textComponent.gameObject);
            NotificationHelper.ReleaseSlot(slot);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            ForceDestroy();
        }
    }
}