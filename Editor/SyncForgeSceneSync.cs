
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System;

[InitializeOnLoad]
public static class SyncForgeSceneSync
{
    private const double SendInterval = 1.0 / 30.0;
    private const double ConnectionTimeout = 6.0;
    private const float InterpolationSpeed = 15f;

    private static SyncForgeNetworkManager netManager;

    private static SyncForgePeerData localData;
    private static SyncForgePeerData remoteData;

    private static double lastSendTime;
    private static double lastPacketTime;
    private static double lastUpdateTime;

    private static Vector3 targetPos;
    private static Quaternion targetRot = Quaternion.identity;

    private static Vector3 currentPos;
    private static Quaternion currentRot = Quaternion.identity;

    private static bool hasReceivedFirstData;

    private static Mesh cachedMesh;
    private static Material cachedMaterial;

    private static Mesh primitiveCubeMesh;
    private static Material defaultCubeMaterial;

    private static string lastLoadedPrefabPath = null;

    private static readonly MaterialPropertyBlock PropertyBlock =
        new MaterialPropertyBlock();

    private static readonly GUIStyle NameLabelStyle = new GUIStyle
    {
        fontSize = 12,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter
    };

    static SyncForgeSceneSync()
    {
        Initialize();
    }

    private static void Initialize()
    {
        netManager = new SyncForgeNetworkManager();
        netManager.OnDataReceived += HandleDataReceived;

        CreatePrimitiveCubeAssets();

        EditorApplication.update += UpdateLoop;
        SceneView.duringSceneGui += OnSceneGUI;

        AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        EditorApplication.quitting += Shutdown;
    }

    public static bool IsHosting =>
        netManager != null && netManager.IsHosting;

    public static bool IsClientConnected =>
        netManager != null && netManager.IsClientConnected;

    public static bool IsConnected =>
        netManager != null && netManager.IsConnected;

    public static void InitConnectionAsHost(int port)
    {
        if (netManager == null)
            return;

        netManager.StartHost(port);
    }

    public static void InitConnectionAsClient(string ip, int port)
    {
        if (netManager == null)
            return;

        netManager.ConnectClient(ip, port);
    }

    public static void Disconnect()
    {
        netManager?.Disconnect();

        remoteData = null;
        hasReceivedFirstData = false;

        lastPacketTime = 0;
        lastSendTime = 0;

        SceneView.RepaintAll();
    }

    public static void UpdateLocalProfile(
        SyncForgeProfileData profile,
        GameObject prefabObject
    )
    {
        if (profile == null)
            return;

        profile.Normalize();

        if (localData == null)
            localData = new SyncForgePeerData();

        localData.nickname = profile.nickname;
        localData.profileName = profile.profileName;

        localData.r = profile.r;
        localData.g = profile.g;
        localData.b = profile.b;

        localData.prefabAssetPath = prefabObject != null
            ? AssetDatabase.GetAssetPath(prefabObject)
            : profile.prefabPath;
    }

    private static void UpdateLoop()
    {
        if (netManager == null)
            return;

        netManager.PumpIncomingMessages();

        double now = EditorApplication.timeSinceStartup;

        double deltaTime = lastUpdateTime > 0
            ? Math.Max(0, Math.Min(now - lastUpdateTime, 0.1))
            : 0;

        lastUpdateTime = now;

        if (!IsConnected)
            return;

        // Envio de câmera a aproximadamente 30 Hz.
        if (now - lastSendTime >= SendInterval)
        {
            lastSendTime = now;

            SendLocalCameraData();
        }

        // Timeout de comunicação.
        if (hasReceivedFirstData &&
            now - lastPacketTime > ConnectionTimeout)
        {
            remoteData = null;
            hasReceivedFirstData = false;

            SceneView.RepaintAll();
            return;
        }

        if (!hasReceivedFirstData)
            return;

        // Interpolação independente da taxa de atualização do Editor.
        float interpolation = 1f - Mathf.Exp(
            -InterpolationSpeed * (float)deltaTime
        );

        currentPos = Vector3.Lerp(
            currentPos,
            targetPos,
            interpolation
        );

        currentRot = Quaternion.Slerp(
            currentRot,
            targetRot,
            interpolation
        );

        SceneView.RepaintAll();
    }

    private static void SendLocalCameraData()
    {
        if (localData == null)
            return;

        SceneView activeSceneView = SceneView.lastActiveSceneView;

        if (activeSceneView == null)
            return;

        Camera camera = activeSceneView.camera;

        if (camera == null)
            return;

        localData.SetPositionAndRotation(
            camera.transform.position,
            camera.transform.rotation
        );

        localData.timestamp = EditorApplication.timeSinceStartup;

        netManager.SendData("CAM_SYNC", localData);
    }

    private static void HandleDataReceived(SyncForgePeerData data)
    {
        // Este callback agora é executado na thread principal.
        if (data == null ||
            !data.IsValid() ||
            !data.HasValidTransform())
            return;

        // Não aceita pacotes do próprio usuário.
        if (localData != null &&
            data.peerId == localData.peerId)
            return;

        remoteData = data;

        targetPos = data.GetPosition();
        targetRot = data.GetRotation();

        lastPacketTime = EditorApplication.timeSinceStartup;

        if (!hasReceivedFirstData)
        {
            currentPos = targetPos;
            currentRot = targetRot;

            hasReceivedFirstData = true;
        }

        SceneView.RepaintAll();
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (sceneView == null ||
            remoteData == null ||
            !IsConnected ||
            !hasReceivedFirstData)
            return;

        Color peerColor = remoteData.GetColor();

        LoadPrefabAssets(remoteData.prefabAssetPath);

        Mesh meshToDraw = cachedMesh != null
            ? cachedMesh
            : primitiveCubeMesh;

        Material materialToDraw = cachedMaterial != null
            ? cachedMaterial
            : defaultCubeMaterial;

        if (meshToDraw != null && materialToDraw != null)
        {
            PropertyBlock.Clear();
            PropertyBlock.SetColor("_Color", peerColor);

            Matrix4x4 matrix = Matrix4x4.TRS(
                currentPos,
                currentRot,
                Vector3.one
            );

            Graphics.DrawMesh(
                meshToDraw,
                matrix,
                materialToDraw,
                0,
                sceneView.camera,
                0,
                PropertyBlock,
                ShadowCastingMode.Off,
                false
            );
        }
        else
        {
            Handles.color = peerColor;

            Handles.CubeHandleCap(
                0,
                currentPos,
                currentRot,
                1f,
                EventType.Repaint
            );
        }

        NameLabelStyle.normal.textColor = peerColor;

        string nickname = SanitizeLabel(remoteData.nickname);
        string profile = SanitizeLabel(remoteData.profileName);

        Handles.Label(
            currentPos + Vector3.up * 1.2f,
            "[Player] " + nickname + " (" + profile + ")",
            NameLabelStyle
        );
    }

    private static string SanitizeLabel(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "Dev";

        return value.Length > 64
            ? value.Substring(0, 64)
            : value;
    }

    private static void LoadPrefabAssets(string prefabPath)
    {
        prefabPath = prefabPath ?? "";

        if (prefabPath == lastLoadedPrefabPath)
            return;

        lastLoadedPrefabPath = prefabPath;

        cachedMesh = null;
        cachedMaterial = null;

        if (string.IsNullOrWhiteSpace(prefabPath))
            return;

        if (!prefabPath.StartsWith("Assets/", StringComparison.Ordinal))
            return;

        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (prefab == null)
            return;

        MeshFilter meshFilter =
            prefab.GetComponentInChildren<MeshFilter>(true);

        MeshRenderer meshRenderer =
            prefab.GetComponentInChildren<MeshRenderer>(true);

        if (meshFilter != null && meshRenderer != null)
        {
            cachedMesh = meshFilter.sharedMesh;
            cachedMaterial = meshRenderer.sharedMaterial;
        }
    }

    private static void CreatePrimitiveCubeAssets()
    {
        GameObject temporaryCube = GameObject.CreatePrimitive(
            PrimitiveType.Cube
        );

        try
        {
            temporaryCube.hideFlags = HideFlags.HideAndDontSave;

            MeshFilter meshFilter =
                temporaryCube.GetComponent<MeshFilter>();

            MeshRenderer meshRenderer =
                temporaryCube.GetComponent<MeshRenderer>();

            if (meshFilter != null)
                primitiveCubeMesh = meshFilter.sharedMesh;

            if (meshRenderer != null &&
                meshRenderer.sharedMaterial != null)
            {
                defaultCubeMaterial = new Material(
                    meshRenderer.sharedMaterial
                );

                defaultCubeMaterial.hideFlags =
                    HideFlags.HideAndDontSave;
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(temporaryCube);
        }
    }

    private static void Shutdown()
    {
        EditorApplication.update -= UpdateLoop;
        SceneView.duringSceneGui -= OnSceneGUI;

        AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
        EditorApplication.quitting -= Shutdown;

        if (netManager != null)
        {
            netManager.OnDataReceived -= HandleDataReceived;
            netManager.Dispose();
            netManager = null;
        }

        if (defaultCubeMaterial != null)
        {
            UnityEngine.Object.DestroyImmediate(defaultCubeMaterial);
            defaultCubeMaterial = null;
        }

        primitiveCubeMesh = null;
        cachedMesh = null;
        cachedMaterial = null;

        remoteData = null;
        localData = null;

        hasReceivedFirstData = false;
    }
}
