using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SyncForgeSceneSync
{
    private static SyncForgeNetworkManager netManager;
    private static SyncForgePeerData localData;
    private static SyncForgePeerData remoteData;
    private static double lastSendTime;

    private static Mesh cachedMesh;
    private static Material cachedMaterial;
    private static string lastLoadedPrefabPath;

    static SyncForgeSceneSync()
    {
        netManager = new SyncForgeNetworkManager();
        netManager.OnDataReceived += HandleDataReceived;

        EditorApplication.update += UpdateLoop;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    public static bool IsHosting => netManager != null && netManager.IsHosting;
    public static bool IsClientConnected => netManager != null && netManager.IsClientConnected;
    public static bool IsConnected => netManager != null && netManager.IsConnected;

    public static void InitConnectionAsHost(int port) => netManager.StartHost(port);
    public static void InitConnectionAsClient(string ip, int port) => netManager.ConnectClient(ip, port);
    public static void Disconnect() => netManager.Disconnect();

    public static void UpdateLocalProfile(SyncForgeProfileData profile, GameObject prefabObj)
    {
        if (localData == null) localData = new SyncForgePeerData();

        localData.nickname = profile.nickname;
        localData.profileName = profile.profileName;
        localData.r = profile.r;
        localData.g = profile.g;
        localData.b = profile.b;

        localData.prefabAssetPath = prefabObj != null ? AssetDatabase.GetAssetPath(prefabObj) : "";
    }

    private static void UpdateLoop()
    {
        if (!IsConnected) return;

        if (EditorApplication.timeSinceStartup - lastSendTime > 0.05)
        {
            lastSendTime = EditorApplication.timeSinceStartup;

            if (SceneView.lastActiveSceneView != null)
            {
                Camera cam = SceneView.lastActiveSceneView.camera;
                if (cam != null)
                {
                    localData.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
                    netManager.SendData("CAM_SYNC", localData);
                }
            }
        }
    }

    private static void HandleDataReceived(SyncForgePeerData data)
    {
        remoteData = data;
        SceneView.RepaintAll();
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (remoteData == null || !IsConnected) return;

        Vector3 remotePos = remoteData.GetPosition();
        Quaternion remoteRot = remoteData.GetRotation();
        Color peerColor = remoteData.GetColor();

        LoadPrefabAssets(remoteData.prefabAssetPath);

        if (cachedMesh != null && cachedMaterial != null)
        {
            MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
            propBlock.SetColor("_Color", peerColor);
            Graphics.DrawMesh(cachedMesh, remotePos, remoteRot, cachedMaterial, 0, null, 0, propBlock);
        }
        else
        {
            Handles.color = peerColor;
            Handles.CubeHandleCap(0, remotePos, remoteRot, 0.6f, EventType.Repaint);
        }

        GUIStyle style = new GUIStyle();
        style.normal.textColor = peerColor;
        style.fontSize = 12;
        style.fontStyle = FontStyle.Bold;

        Handles.Label(remotePos + Vector3.up * 1.0f, $"{remoteData.nickname} [{remoteData.profileName}]", style);
    }

    private static void LoadPrefabAssets(string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            cachedMesh = null;
            cachedMaterial = null;
            lastLoadedPrefabPath = "";
            return;
        }

        if (prefabPath != lastLoadedPrefabPath)
        {
            lastLoadedPrefabPath = prefabPath;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab != null)
            {
                MeshFilter mf = prefab.GetComponentInChildren<MeshFilter>();
                MeshRenderer mr = prefab.GetComponentInChildren<MeshRenderer>();

                cachedMesh = mf != null ? mf.sharedMesh : null;
                cachedMaterial = mr != null ? mr.sharedMaterial : null;
            }
            else
            {
                cachedMesh = null;
                cachedMaterial = null;
            }
        }
    }
}