using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Small desktop overlay for the standalone mirroring demonstration.</summary>
public sealed class EVAMirroringDemoUI : MonoBehaviour
{
    [SerializeField] private EVAPoseReceiver receiver;
    [SerializeField] private EVAOneArmMirroring mirroring;

    private GameObject canvasObject;
    private GameObject ownedEventSystem;
    private Slider strengthSlider;
    private Text percentageText;
    private Text statusText;

    private void Awake()
    {
        if (receiver == null) receiver = FindFirstObjectByType<EVAPoseReceiver>();
        if (mirroring == null) mirroring = FindFirstObjectByType<EVAOneArmMirroring>();
        BuildOverlay();
    }

    private void Start()
    {
        if (mirroring == null)
        {
            statusText.text = "Mirroring-Komponente fehlt";
            return;
        }
        mirroring.SetStrength(0.7f);
        strengthSlider.SetValueWithoutNotify(mirroring.Strength);
        UpdatePercentage(mirroring.Strength);
    }

    private void Update()
    {
        if (statusText == null || receiver == null) return;
        if (receiver.HasFreshPose)
            statusText.text = mirroring != null && mirroring.IsCalibrated
                ? "Tracking aktiv · Arm kalibriert" : "Tracking aktiv · Kalibrierung";
        else if (receiver.IsConnected)
            statusText.text = receiver.LastStatus;
        else
            statusText.text = "Pose-Dienst nicht verbunden (Port 8766)";
    }

    private void BuildOverlay()
    {
        if (EventSystem.current == null)
            ownedEventSystem = new GameObject("Mirroring EventSystem", typeof(EventSystem),
                typeof(InputSystemUIInputModule));

        canvasObject = new GameObject("Mirroring Controls", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.layer = 5;
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var panel = CreateRect("Panel", canvasObject.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(28f, -28f);
        panel.sizeDelta = new Vector2(450f, 174f);
        panel.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.055f, 0.09f, 0.93f);

        CreateText("Title", panel, "Spiegelungsstärke", 26, new Vector2(22f, -15f),
            new Vector2(400f, 37f), Color.white);
        percentageText = CreateText("Percentage", panel, "70 %", 23,
            new Vector2(352f, -63f), new Vector2(75f, 35f), Color.white);
        statusText = CreateText("Status", panel, "Pose-Dienst nicht verbunden", 18,
            new Vector2(22f, -126f), new Vector2(410f, 31f), new Color(0.72f, 0.84f, 0.96f));

        var sliderRoot = CreateRect("Strength Slider", panel);
        sliderRoot.anchorMin = sliderRoot.anchorMax = new Vector2(0f, 1f);
        sliderRoot.pivot = new Vector2(0f, 1f);
        sliderRoot.anchoredPosition = new Vector2(22f, -74f);
        sliderRoot.sizeDelta = new Vector2(315f, 28f);
        var background = CreateRect("Background", sliderRoot);
        Stretch(background, new Vector2(0f, 9f), new Vector2(0f, -9f));
        background.gameObject.AddComponent<Image>().color = new Color(0.17f, 0.23f, 0.32f);
        var fill = CreateRect("Fill", sliderRoot);
        Stretch(fill, new Vector2(0f, 9f), new Vector2(0f, -9f));
        fill.gameObject.AddComponent<Image>().color = new Color(0.28f, 0.73f, 1f);
        var handle = CreateRect("Handle", sliderRoot);
        handle.anchorMin = handle.anchorMax = new Vector2(0.7f, 0.5f);
        handle.sizeDelta = new Vector2(22f, 32f);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = Color.white;

        strengthSlider = sliderRoot.gameObject.AddComponent<Slider>();
        strengthSlider.direction = Slider.Direction.LeftToRight;
        strengthSlider.minValue = 0f;
        strengthSlider.maxValue = 1f;
        strengthSlider.fillRect = fill;
        strengthSlider.handleRect = handle;
        strengthSlider.targetGraphic = handleImage;
        strengthSlider.onValueChanged.AddListener(value =>
        {
            mirroring?.SetStrength(value);
            UpdatePercentage(value);
        });
    }

    private void UpdatePercentage(float value)
    {
        if (percentageText != null)
            percentageText.text = Mathf.RoundToInt(value * 100f) + " %";
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }

    private static Text CreateText(string name, RectTransform parent, string value,
        int size, Vector2 position, Vector2 dimensions, Color color)
    {
        var rect = CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        var label = rect.gameObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAnchor.MiddleLeft;
        label.raycastTarget = false;
        label.text = value;
        return label;
    }

    private void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
        if (ownedEventSystem != null) Destroy(ownedEventSystem);
    }
}
