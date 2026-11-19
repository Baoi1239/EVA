using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Small desktop overlay for the standalone mirroring demonstration.</summary>
public sealed class EVAMirroringDemoUI : MonoBehaviour
{
    [SerializeField] private EVAPoseReceiver receiver;
    [SerializeField] private EVAUpperBodyMirroring mirroring;

    private GameObject canvasObject;
    private GameObject ownedEventSystem;
    private Slider strengthSlider;
    private Text percentageText;
    private Text statusText;
    private int lastTrackedParts = -1;

    private void Awake()
    {
        if (receiver == null) receiver = FindFirstObjectByType<EVAPoseReceiver>();
        if (mirroring == null) mirroring = FindFirstObjectByType<EVAUpperBodyMirroring>();
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
        if (!statusText || !receiver || !mirroring) return;
        if (!receiver.HasFreshPose)
        {
            lastTrackedParts = -1;
            statusText.text = receiver.IsConnected ? receiver.LastStatus : "Pose-Dienst nicht verbunden";
            return;
        }
        if (!mirroring.IsCalibrated)
        {
            lastTrackedParts = -1;
            statusText.text = mirroring.CalibrationStatus;
            return;
        }
        int parts = (mirroring.TracksLeftArm ? 1 : 0) | (mirroring.TracksRightArm ? 2 : 0)
            | (mirroring.TracksTorso ? 4 : 0) | (mirroring.TracksHead ? 8 : 0);
        if (parts == lastTrackedParts) return;
        lastTrackedParts = parts;
        var tracked = new System.Collections.Generic.List<string>(4);
        if (mirroring.TracksLeftArm) tracked.Add("Arm L");
        if (mirroring.TracksRightArm) tracked.Add("Arm R");
        if (mirroring.TracksTorso) tracked.Add("Rumpf");
        if (mirroring.TracksHead) tracked.Add("Kopf");
        statusText.text = parts == 0 ? "Keine Körperregion erkannt" : "EVA · " + string.Join(" · ", tracked);
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
        BuildPoseDiagram();

        var panel = CreateRect("Panel", canvasObject.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(28f, -28f);
        panel.sizeDelta = new Vector2(450f, 324f);
        panel.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.055f, 0.09f, 0.93f);

        CreateText("Title", panel, "Spiegelungsstärke", 26, new Vector2(22f, -15f),
            new Vector2(400f, 37f), Color.white);
        percentageText = CreateText("Percentage", panel, "70 %", 23,
            new Vector2(352f, -63f), new Vector2(75f, 35f), Color.white);
        statusText = CreateText("Status", panel, "Pose-Dienst nicht verbunden", 18,
            new Vector2(22f, -248f), new Vector2(410f, 62f), new Color(0.72f, 0.84f, 0.96f));

        strengthSlider = CreateSlider("Strength Slider", panel, -74f);
        strengthSlider.onValueChanged.AddListener(value =>
        {
            mirroring?.SetStrength(value);
            UpdatePercentage(value);
        });
        CreateText("Smoothing", panel, "Glättung", 22, new Vector2(22,-108), new Vector2(220,32), Color.white);
        Text milliseconds = CreateText("Milliseconds", panel, "", 20, new Vector2(350,-148), new Vector2(95,34), Color.white);
        var filter = CreateSlider("Smoothing Slider", panel, -156f);
        filter.maxValue = 250;
        filter.wholeNumbers = true;
        filter.value = mirroring ? mirroring.SmoothingSeconds * 1000 : 100;
        milliseconds.text = filter.value + " ms";
        filter.onValueChanged.AddListener(value =>
        {
            mirroring?.SetSmoothingSeconds(value / 1000f);
            milliseconds.text = value + " ms";
        });
        var caption = CreateText("Neu kalibrieren", panel, "Neu kalibrieren", 20,
            new Vector2(22,-202), new Vector2(200,38), Color.white);
        caption.alignment = TextAnchor.MiddleCenter;
        var buttonRoot = CreateRect("Calibration Button", panel);
        buttonRoot.anchorMin = buttonRoot.anchorMax = caption.rectTransform.anchorMin;
        buttonRoot.pivot = caption.rectTransform.pivot;
        buttonRoot.anchoredPosition = caption.rectTransform.anchoredPosition;
        buttonRoot.sizeDelta = caption.rectTransform.sizeDelta;
        var background = buttonRoot.gameObject.AddComponent<Image>();
        background.color = new Color(.14f,.28f,.4f);
        var button = buttonRoot.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => mirroring?.RequestCalibration());
        caption.transform.SetAsLastSibling();
    }

    private void BuildPoseDiagram()
    {
        var panel = CreateRect("Pose comparison", canvasObject.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
        panel.anchoredPosition = new Vector2(-28,-28);
        panel.sizeDelta = new Vector2(560,318);
        panel.gameObject.AddComponent<Image>().color = new Color(.035f,.055f,.09f,.93f);
        CreateText("Input", panel, "MediaPipe-Landmarks", 20, new Vector2(16,-4),
            new Vector2(265,32), new Color(.33f,.87f,.97f));
        CreateText("Output", panel, "EVA-Skelett", 20, new Vector2(302,-4),
            new Vector2(240,32), new Color(1,.73f,.35f));

        var plot = CreateRect("Diagram", panel);
        Stretch(plot,new Vector2(8,48),new Vector2(-8,-36));
        var diagram = plot.gameObject.AddComponent<EVAPoseDiagram>();
        diagram.Configure(receiver, mirroring ? mirroring.GetComponent<EVAAnimationController>() : null);

        var switcher = CreateRect("Change view", panel);
        switcher.anchorMin = switcher.anchorMax = switcher.pivot = Vector2.zero;
        switcher.anchoredPosition = new Vector2(12,8);
        switcher.sizeDelta = new Vector2(536,36);
        var background = switcher.gameObject.AddComponent<Image>();
        background.color = new Color(.14f,.28f,.4f);
        var button = switcher.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        var label = CreateText("View", switcher, "Frontansicht · zur Seitenansicht wechseln", 18,
            Vector2.zero, switcher.sizeDelta, Color.white);
        label.alignment = TextAnchor.MiddleCenter;
        bool side = false;
        button.onClick.AddListener(() =>
        {
            side = !side;
            diagram.SetSideView(side);
            label.text = side ? "Seitenansicht · zur Frontansicht wechseln" : "Frontansicht · zur Seitenansicht wechseln";
        });
    }

    private static Slider CreateSlider(string name, RectTransform panel, float y)
    {
        var sliderRoot = CreateRect(name, panel);
        sliderRoot.anchorMin = sliderRoot.anchorMax = new Vector2(0f, 1f);
        sliderRoot.pivot = new Vector2(0f, 1f);
        sliderRoot.anchoredPosition = new Vector2(22f, y);
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

        var slider = sliderRoot.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        return slider;
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
