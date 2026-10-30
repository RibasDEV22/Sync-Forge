
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SyncForgePeerData
{
    public int protocolVersion = 2;

    public string peerId = Guid.NewGuid().ToString("N");

    public string nickname = "Dev";
    public string profileName = "Default";

    public float r = 0.2f;
    public float g = 0.8f;
    public float b = 1f;

    public string prefabAssetPath = "";

    public float posX;
    public float posY;
    public float posZ;

    public float rotX;
    public float rotY;
    public float rotZ;
    public float rotW = 1f;

    public double timestamp;

    public Color GetColor()
    {
        return new Color(
            Mathf.Clamp01(r),
            Mathf.Clamp01(g),
            Mathf.Clamp01(b),
            1f
        );
    }

    public Vector3 GetPosition()
    {
        return new Vector3(posX, posY, posZ);
    }

    public Quaternion GetRotation()
    {
        Quaternion rotation = new Quaternion(
            rotX,
            rotY,
            rotZ,
            rotW
        );

        if (GetQuaternionMagnitudeSquared(rotation) < 0.0001f)
            return Quaternion.identity;

        return NormalizeQuaternion(rotation);
    }

    public void SetPositionAndRotation(
        Vector3 position,
        Quaternion rotation
    )
    {
        posX = position.x;
        posY = position.y;
        posZ = position.z;

        rotation = NormalizeQuaternion(rotation);

        rotX = rotation.x;
        rotY = rotation.y;
        rotZ = rotation.z;
        rotW = rotation.w;
    }

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(peerId) &&
               peerId.Length <= 64 &&
               protocolVersion == 2 &&
               !string.IsNullOrWhiteSpace(nickname) &&
               nickname.Length <= 64 &&
               !string.IsNullOrWhiteSpace(profileName) &&
               profileName.Length <= 64 &&
               IsFinite(r) &&
               IsFinite(g) &&
               IsFinite(b);
    }

    public bool HasValidTransform()
    {
        Quaternion rotation = new Quaternion(
            rotX,
            rotY,
            rotZ,
            rotW
        );

        return IsFinite(posX) &&
               IsFinite(posY) &&
               IsFinite(posZ) &&
               IsFinite(rotX) &&
               IsFinite(rotY) &&
               IsFinite(rotZ) &&
               IsFinite(rotW) &&
               GetQuaternionMagnitudeSquared(rotation) > 0.0001f;
    }

    private static float GetQuaternionMagnitudeSquared(
        Quaternion rotation
    )
    {
        return rotation.x * rotation.x +
               rotation.y * rotation.y +
               rotation.z * rotation.z +
               rotation.w * rotation.w;
    }

    private static Quaternion NormalizeQuaternion(
        Quaternion rotation
    )
    {
        float magnitudeSquared =
            GetQuaternionMagnitudeSquared(rotation);

        if (magnitudeSquared < 0.0001f ||
            !IsFinite(magnitudeSquared))
        {
            return Quaternion.identity;
        }

        float inverseMagnitude = 1f / Mathf.Sqrt(magnitudeSquared);

        return new Quaternion(
            rotation.x * inverseMagnitude,
            rotation.y * inverseMagnitude,
            rotation.z * inverseMagnitude,
            rotation.w * inverseMagnitude
        );
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value);
    }
}

[Serializable]
public class SyncForgeNetworkMessage
{
    public string messageType = "CAM_SYNC";
    public string payloadJson = "";
}

[Serializable]
public class SyncForgeProfileData
{
    public string profileName = "MeuPerfil";
    public string nickname = "Desenvolvedor";

    public float r = 0.2f;
    public float g = 0.8f;
    public float b = 1f;

    public string prefabPath = "";

    public SyncForgeProfileData Clone()
    {
        return new SyncForgeProfileData
        {
            profileName = profileName,
            nickname = nickname,
            r = r,
            g = g,
            b = b,
            prefabPath = prefabPath
        };
    }

    public void Normalize()
    {
        profileName = NormalizeText(profileName, "MeuPerfil", 64);
        nickname = NormalizeText(nickname, "Desenvolvedor", 64);

        r = NormalizeColor(r, 0.2f);
        g = NormalizeColor(g, 0.8f);
        b = NormalizeColor(b, 1f);

        prefabPath = prefabPath ?? "";
    }

    private static string NormalizeText(
        string value,
        string fallback,
        int maxLength
    )
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        value = value.Trim();

        return value.Length > maxLength
            ? value.Substring(0, maxLength)
            : value;
    }

    private static float NormalizeColor(float value, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return fallback;

        return Mathf.Clamp01(value);
    }
}

[Serializable]
public class SyncForgeProfileDatabase
{
    public List<SyncForgeProfileData> profiles =
        new List<SyncForgeProfileData>();
}
