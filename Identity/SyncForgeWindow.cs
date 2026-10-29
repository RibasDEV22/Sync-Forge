using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class SyncForgeWindow : EditorWindow
{
    private SyncForgeProfileDatabase db = new SyncForgeProfileDatabase();
    private int selectedProfileIndex = 0;

    private GameObject selectedPrefab;

    private string hostIP = "127.0.0.1";
    private int port = 7777;

    private int selectedTab = 0;
    private readonly string[] tabs = { "Perfil & Identidade", "Conexão" };
    private const string PrefsKey = "SyncForge_Profiles_DB_V2";

    [MenuItem("Tools/SyncForge")]
    public static void ShowWindow()
    {
        GetWindow<SyncForgeWindow>("SyncForge");
    }

    private void OnEnable()
    {
        LoadProfiles();
        SyncCurrentProfileToRuntime();
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("SyncForge - Co-Editor em Tempo Real", EditorStyles.boldLabel);
        GUILayout.Space(5);

        selectedTab = GUILayout.Toolbar(selectedTab, tabs);
        GUILayout.Space(10);

        if (selectedTab == 0)
        {
            DrawProfilesTab();
        }
        else
        {
            DrawConnectionTab();
        }
    }

    private void DrawProfilesTab()
    {
        GUILayout.Label("Gerenciamento de Perfis", EditorStyles.boldLabel);

        if (db.profiles.Count > 0)
        {
            string[] profileNames = db.profiles.ConvertAll(p => p.profileName).ToArray();
            int newIndex = EditorGUILayout.Popup("Perfil Ativo", selectedProfileIndex, profileNames);

            if (newIndex != selectedProfileIndex)
            {
                selectedProfileIndex = newIndex;
                LoadSelectedProfileAssets();
                SyncCurrentProfileToRuntime();
            }
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Novo Perfil"))
        {
            SyncForgeProfileData newP = new SyncForgeProfileData { profileName = "Perfil_" + (db.profiles.Count + 1) };
            db.profiles.Add(newP);
            selectedProfileIndex = db.profiles.Count - 1;
            selectedPrefab = null;
            SaveProfiles();
        }
        if (db.profiles.Count > 1 && GUILayout.Button("Excluir Perfil"))
        {
            db.profiles.RemoveAt(selectedProfileIndex);
            selectedProfileIndex = Mathf.Clamp(selectedProfileIndex - 1, 0, db.profiles.Count - 1);
            LoadSelectedProfileAssets();
            SaveProfiles();
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        if (db.profiles.Count > 0)
        {
            SyncForgeProfileData p = db.profiles[selectedProfileIndex];

            p.profileName = EditorGUILayout.TextField("Nome do Perfil", p.profileName);
            p.nickname = EditorGUILayout.TextField("Nickname", p.nickname);

            Color col = EditorGUILayout.ColorField("Cor do Avatar", new Color(p.r, p.g, p.b));
            p.r = col.r; p.g = col.g; p.b = col.b;

            GUILayout.Space(10);
            GUILayout.Label("Identificação Visual (Prefab Padrão)", EditorStyles.boldLabel);

            selectedPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab de Identidade", selectedPrefab, typeof(GameObject), false);

            if (GUILayout.Button("Salvar e Sincronizar Alterações"))
            {
                p.prefabPath = selectedPrefab != null ? AssetDatabase.GetAssetPath(selectedPrefab) : "";
                SaveProfiles();
                SyncCurrentProfileToRuntime();
                Debug.Log("[SyncForge] Perfil salvo e aplicado com sucesso!");
            }
        }
    }

    private void DrawConnectionTab()
    {
        GUILayout.Label("Conexão (Radmin VPN)", EditorStyles.boldLabel);
        hostIP = EditorGUILayout.TextField("IP do Host", hostIP);
        port = EditorGUILayout.IntField("Porta", port);

        GUILayout.Space(15);

        bool isHosting = SyncForgeSceneSync.IsHosting;
        bool isClient = SyncForgeSceneSync.IsClientConnected;

        if (isHosting)
        {
            GUI.color = Color.yellow;
            GUILayout.Label("Status: Hospedando (Aguardando conexões...)", EditorStyles.boldLabel);
        }
        else if (isClient)
        {
            GUI.color = Color.green;
            GUILayout.Label("Status: Conectado ao Host", EditorStyles.boldLabel);
        }
        else
        {
            GUI.color = Color.red;
            GUILayout.Label("Status: Desconectado", EditorStyles.boldLabel);
        }
        GUI.color = Color.white;

        GUILayout.Space(10);

        if (!isHosting && !isClient)
        {
            if (GUILayout.Button("Criar Sala (Host)"))
            {
                SyncCurrentProfileToRuntime();
                SyncForgeSceneSync.InitConnectionAsHost(port);
            }

            if (GUILayout.Button("Conectar (Client)"))
            {
                SyncCurrentProfileToRuntime();
                SyncForgeSceneSync.InitConnectionAsClient(hostIP, port);
            }
        }
        else
        {
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button(isHosting ? "Encerrar Host" : "Desconectar"))
            {
                SyncForgeSceneSync.Disconnect();
            }
            GUI.backgroundColor = Color.white;
        }
    }

    private void SyncCurrentProfileToRuntime()
    {
        if (db.profiles.Count > 0 && selectedProfileIndex < db.profiles.Count)
        {
            SyncForgeSceneSync.UpdateLocalProfile(db.profiles[selectedProfileIndex], selectedPrefab);
        }
    }

    private void SaveProfiles()
    {
        string json = JsonUtility.ToJson(db);
        EditorPrefs.SetString(PrefsKey, json);
    }

    private void LoadProfiles()
    {
        if (EditorPrefs.HasKey(PrefsKey))
        {
            string json = EditorPrefs.GetString(PrefsKey);
            db = JsonUtility.FromJson<SyncForgeProfileDatabase>(json);
        }

        if (db.profiles == null || db.profiles.Count == 0)
        {
            db.profiles = new List<SyncForgeProfileData> { new SyncForgeProfileData() };
            selectedProfileIndex = 0;
        }

        LoadSelectedProfileAssets();
    }

    private void LoadSelectedProfileAssets()
    {
        if (db.profiles.Count > 0 && selectedProfileIndex < db.profiles.Count)
        {
            SyncForgeProfileData p = db.profiles[selectedProfileIndex];
            selectedPrefab = !string.IsNullOrEmpty(p.prefabPath) ? AssetDatabase.LoadAssetAtPath<GameObject>(p.prefabPath) : null;
        }
    }
}
