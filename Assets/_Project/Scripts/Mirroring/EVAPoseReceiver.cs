using System;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;

[Serializable]
public sealed class EVAPoseLandmark
{
    public float x, y, z, visibility, presence;
}

[Serializable]
public sealed class EVAPoseFrame
{
    public string type;
    public int schemaVersion;
    public string coordinateSpace;
    public long sequence;
    public long sourceTimestampMs;
    public bool valid;
    public EVAPoseLandmark[] landmarks;
}

/// <summary>Receives the latest webcam pose without depending on EVA's speech backend.</summary>
[DisallowMultipleComponent]
public sealed class EVAPoseReceiver : MonoBehaviour
{
    [SerializeField] private string serviceUrl = "ws://127.0.0.1:8766";
    [SerializeField, Range(0, 10)] private int cameraIndex;
    [SerializeField, Min(0.1f)] private float reconnectSeconds = 2f;
    [SerializeField, Min(0.05f)] private float frameTimeoutSeconds = 0.5f;

    private WebSocket socket;
    private bool connecting;
    private float nextConnectAt;
    private float receivedAt;
    private long lastSequence = -1;
    private EVAPoseFrame latestFrame;

    public string LastStatus { get; private set; } = "Disconnected";
    public bool IsConnected => socket != null && socket.State == WebSocketState.Open;
    public EVAPoseFrame LatestFrame => latestFrame;
    public bool HasFreshPose => IsConnected && latestFrame != null && latestFrame.valid
        && Time.realtimeSinceStartup - receivedAt <= frameTimeoutSeconds;

    private void OnEnable()
    {
        Application.runInBackground = true;
        nextConnectAt = 0f;
    }

    private void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        socket?.DispatchMessageQueue();
#endif
        if (!connecting && !IsConnected && Time.realtimeSinceStartup >= nextConnectAt)
            _ = ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        connecting = true;
        nextConnectAt = Time.realtimeSinceStartup + reconnectSeconds;
        var candidate = new WebSocket(serviceUrl);
        socket = candidate;
        candidate.OnOpen += () =>
        {
            if (socket != candidate || !isActiveAndEnabled) return;
            lastSequence = -1;
            latestFrame = null;
            LastStatus = "Connected; starting camera";
            _ = SendStartAsync(candidate);
        };
        candidate.OnMessage += bytes =>
        {
            if (socket == candidate && isActiveAndEnabled) Receive(bytes);
        };
        candidate.OnError += error =>
        {
            if (socket == candidate) LastStatus = "WebSocket error: " + error;
        };
        candidate.OnClose += code =>
        {
            if (socket != candidate) return;
            latestFrame = null;
            LastStatus = "Disconnected: " + code;
            nextConnectAt = Time.realtimeSinceStartup + reconnectSeconds;
        };
        try { await candidate.Connect(); }
        catch (Exception error)
        {
            if (socket == candidate) LastStatus = "Connection failed: " + error.Message;
        }
        finally
        {
            connecting = false;
            nextConnectAt = Time.realtimeSinceStartup + reconnectSeconds;
        }
    }

    private async Task SendStartAsync(WebSocket candidate)
    {
        try
        {
            await candidate.SendText("{\"type\":\"start\",\"cameraIndex\":" + cameraIndex + "}");
        }
        catch (Exception error)
        {
            if (socket == candidate) LastStatus = "Camera start failed: " + error.Message;
        }
    }

    private void Receive(byte[] bytes)
    {
        string json = Encoding.UTF8.GetString(bytes);
        EVAPoseFrame frame;
        try { frame = JsonUtility.FromJson<EVAPoseFrame>(json); }
        catch (Exception error) { LastStatus = "Invalid pose JSON: " + error.Message; return; }
        if (frame == null) return;
        if (frame.type == "status" || frame.type == "error")
        {
            LastStatus = json;
            if (frame.type == "error") latestFrame = null;
            return;
        }
        if (frame.type != "pose" || frame.schemaVersion != 1
            || frame.coordinateSpace != "mediapipe_world_m" || frame.sequence <= lastSequence)
            return;
        lastSequence = frame.sequence;
        if (!frame.valid || frame.landmarks == null || frame.landmarks.Length != 33
            || !AreFinite(frame.landmarks))
        {
            latestFrame = null;
            LastStatus = "Tracking unavailable";
            return;
        }
        latestFrame = frame;
        receivedAt = Time.realtimeSinceStartup;
        LastStatus = "Tracking";
    }

    private static bool AreFinite(EVAPoseLandmark[] landmarks)
    {
        foreach (var point in landmarks)
        {
            if (point == null || float.IsNaN(point.x) || float.IsInfinity(point.x)
                || float.IsNaN(point.y) || float.IsInfinity(point.y)
                || float.IsNaN(point.z) || float.IsInfinity(point.z)
                || float.IsNaN(point.visibility) || float.IsInfinity(point.visibility)
                || float.IsNaN(point.presence) || float.IsInfinity(point.presence)) return false;
        }
        return true;
    }

    private void OnDisable()
    {
        latestFrame = null;
        var oldSocket = socket;
        socket = null;
        oldSocket?.CancelConnection();
        if (oldSocket != null) _ = CloseSafelyAsync(oldSocket);
    }

    private static async Task CloseSafelyAsync(WebSocket oldSocket)
    {
        try { await oldSocket.Close(); }
        catch (Exception) { /* A stopped editor may already have closed the socket. */ }
    }
}
