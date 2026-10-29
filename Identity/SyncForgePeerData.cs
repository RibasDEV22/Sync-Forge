using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SyncForgePeerData
{
    public string nickname;
    public string profileName;
    public float r, g, b; // Cor do gizmo/avatar

    // Caminho do Prefab para representação visual na SceneView
    public string prefabAssetPath;

    // Dados da Câmera do Editor
    public float posX, posY, posZ;
    public float rotX, rotY, rotZ, rotW;

    public Color GetColor() => new Color(r, g, b);
    public Vector3 GetPosition() => new Vector3(posX, posY, posZ);
    public Quaternion GetRotation() => new Quaternion(rotX, rotY, rotZ, rotW);

    public void SetPositionAndRotation(Vector3 pos, Quaternion rot)
    {
        posX = pos.x; posY = pos.y; posZ = pos.z;
        rotX = rot.x; rotY = rot.y; rotZ = rot.z; rotW = rot.w;
    }
}

[Serializable]
public class SyncForgeNetworkMessage
{
    public string messageType;
    public string payloadJson;
}

[Serializable]
public class SyncForgeProfileData
{
    public string profileName = "MeuPerfil";
    public string nickname = "Dev";
    public float r = 0.2f, g = 0.8f, b = 1f;
    public string prefabPath = ""; // Prefab de identificação padrão
}

[Serializable]
public class SyncForgeProfileDatabase
{
    public List<SyncForgeProfileData> profiles = new List<SyncForgeProfileData>();
}