
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;

public class SyncForgeWindow : EditorWindow
{
    private SyncForgeProfileDatabase db =
        new SyncForgeProfileDatabase();

    private int selectedProfileIndex;
    private GameObject selectedPrefab;

    private string hostIP = "127.0.0.1";
    private int port = 7777;

    private int selectedTab;

    private readonly string[] tabs =
    {
        "Perfil & Identidade",
        "Conexão",
        "Sistema & Update"
    };

    private const string GlobalPrefsKey =
        "SyncForge_Global_Absolute_Profiles_DB_V5";

    private const string BackupPrefsKey =
        "SyncForge_Backup_Safety_Key_V5";

    private const string ActiveTabPrefsKey =
        "SyncForge_Active_Tab_Index";

    private const string GitHubPackageUrl =
        "https://github.com/RibasDEV22/Sync-Forge/raw/main/SyncForge.unitypackage";

    [MenuItem("Tools/SyncForge")]
    public static void ShowWindow()
    {
        GetWindow<SyncForgeWindow>("SyncForge");
    }

    private void OnEnable()
    {
        LoadProfiles();

        selectedTab = Mathf.Clamp(
            EditorPrefs.GetInt(ActiveTabPrefsKey, 0),
            0,
            tabs.Length - 1
        );

        SyncCurrentProfileToRuntime();
    }

    private void OnDisable()
    {
        EditorPrefs.SetInt(ActiveTabPrefsKey, selectedTab);
        SaveProfiles();
    }

    private void OnGUI()
    {
        GUILayout.Space(10);

        EditorGUILayout.LabelField(
            "SyncForge - Real-Time Co-Editor",
            EditorStyles.boldLabel
        );

        GUILayout.Space(8);

        selectedTab = GUILayout.Toolbar(selectedTab, tabs);

        GUILayout.Space(12);

        switch (selectedTab)
        {
            case 0:
                DrawProfilesTab();
                break;

            case 1:
                DrawConnectionTab();
                break;

            case 2:
                DrawUpdateTab();
                break;
        }
    }

    private void DrawProfilesTab()
    {
        EditorGUILayout.LabelField(
            "Gerenciamento de Identidade",
            EditorStyles.boldLabel
        );

        GUILayout.Space(8);

        if (db.profiles.Count > 0)
        {
            string[] profileNames = new string[db.profiles.Count];

            for (int i = 0; i < db.profiles.Count; i++)
                profileNames[i] = db.profiles[i].profileName;

            int newIndex = EditorGUILayout.Popup(
                "Perfil Ativo",
                selectedProfileIndex,
                profileNames
            );

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
            CreateNewProfile();
        }

        using (new EditorGUI.DisabledScope(db.profiles.Count <= 1))
        {
            if (GUILayout.Button("Excluir Perfil"))
            {
                DeleteSelectedProfile();
            }
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(12);

        if (db.profiles.Count == 0)
            return;

        selectedProfileIndex = Mathf.Clamp(
            selectedProfileIndex,
            0,
            db.profiles.Count - 1
        );

        SyncForgeProfileData profile =
            db.profiles[selectedProfileIndex];

        EditorGUI.BeginChangeCheck();

        profile.profileName = EditorGUILayout.TextField(
            "Nome do Perfil",
            profile.profileName
        );

        profile.nickname = EditorGUILayout.TextField(
            "Nickname",
            profile.nickname
        );

        Color color = EditorGUILayout.ColorField(
            "Cor do Avatar",
            new Color(profile.r, profile.g, profile.b)
        );

        profile.r = color.r;
        profile.g = color.g;
        profile.b = color.b;

        GUILayout.Space(10);

        EditorGUILayout.LabelField(
            "Identificação Visual",
            EditorStyles.boldLabel
        );

        selectedPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Prefab Personalizado",
            selectedPrefab,
            typeof(GameObject),
            false
        );

        bool changed = EditorGUI.EndChangeCheck();

        if (changed)
            profile.Normalize();

        GUILayout.Space(10);

        if (GUILayout.Button(
            "Salvar e Sincronizar Alterações",
            GUILayout.Height(30)
        ))
        {
            SaveSelectedProfile();
        }

        if (changed)
            Repaint();
    }

    private void CreateNewProfile()
    {
        SyncForgeProfileData newProfile =
            new SyncForgeProfileData
            {
                profileName = "Perfil_" + (db.profiles.Count + 1),
                nickname = "Desenvolvedor"
            };

        db.profiles.Add(newProfile);

        selectedProfileIndex = db.profiles.Count - 1;
        selectedPrefab = null;

        SaveProfiles();
        SyncCurrentProfileToRuntime();
    }

    private void DeleteSelectedProfile()
    {
        if (db.profiles.Count <= 1)
            return;

        if (!EditorUtility.DisplayDialog(
            "Excluir Perfil",
            "Tem certeza de que deseja excluir este perfil?",
            "Sim",
            "Não"
        ))
            return;

        db.profiles.RemoveAt(selectedProfileIndex);

        selectedProfileIndex = Mathf.Clamp(
            selectedProfileIndex - 1,
            0,
            db.profiles.Count - 1
        );

        LoadSelectedProfileAssets();

        SaveProfiles();
        SyncCurrentProfileToRuntime();
    }

    private void SaveSelectedProfile()
    {
        if (db.profiles.Count == 0)
            return;

        SyncForgeProfileData profile =
            db.profiles[selectedProfileIndex];

        profile.Normalize();

        if (selectedPrefab != null)
        {
            string prefabPath = AssetDatabase.GetAssetPath(
                selectedPrefab
            );

            if (string.IsNullOrEmpty(prefabPath) ||
                !prefabPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog(
                    "Prefab inválido",
                    "Selecione um prefab válido localizado dentro de Assets.",
                    "OK"
                );

                return;
            }

            profile.prefabPath = prefabPath;
        }
        else
        {
            profile.prefabPath = "";
        }

        SaveProfiles();
        SyncCurrentProfileToRuntime();

        Debug.Log("[SyncForge] Perfil salvo com sucesso.");
    }

    private void DrawConnectionTab()
    {
        EditorGUILayout.LabelField(
            "Conexão - LAN / Radmin VPN",
            EditorStyles.boldLabel
        );

        GUILayout.Space(8);

        using (new EditorGUI.DisabledScope(
            SyncForgeSceneSync.IsConnected ||
            SyncForgeSceneSync.IsHosting
        ))
        {
            hostIP = EditorGUILayout.TextField(
                "IP do Host",
                hostIP
            );

            port = EditorGUILayout.IntField(
                "Porta",
                port
            );
        }

        GUILayout.Space(12);

        bool hosting = SyncForgeSceneSync.IsHosting;
        bool connected = SyncForgeSceneSync.IsClientConnected;

        if (hosting)
        {
            GUI.color = Color.yellow;

            EditorGUILayout.LabelField(
                connected
                    ? "Status: Host conectado a um peer"
                    : "Status: Aguardando conexão...",
                EditorStyles.boldLabel
            );
        }
        else if (connected)
        {
            GUI.color = Color.green;

            EditorGUILayout.LabelField(
                "Status: Conectado ao Host",
                EditorStyles.boldLabel
            );
        }
        else
        {
            GUI.color = Color.red;

            EditorGUILayout.LabelField(
                "Status: Desconectado",
                EditorStyles.boldLabel
            );
        }

        GUI.color = Color.white;

        GUILayout.Space(10);

        if (!hosting && !connected)
        {
            if (GUILayout.Button("Criar Sala (Host)"))
            {
                if (!IsValidPort(port))
                {
                    EditorUtility.DisplayDialog(
                        "Porta inválida",
                        "Informe uma porta entre 1 e 65535.",
                        "OK"
                    );

                    return;
                }

                SyncCurrentProfileToRuntime();

                try
                {
                    SyncForgeSceneSync.InitConnectionAsHost(port);
                }
                catch (Exception exception)
                {
                    Debug.LogError(exception.Message);
                }
            }

            if (GUILayout.Button("Conectar (Client)"))
            {
                if (!IsValidPort(port) ||
                    string.IsNullOrWhiteSpace(hostIP))
                {
                    EditorUtility.DisplayDialog(
                        "Dados inválidos",
                        "Verifique o IP e a porta informados.",
                        "OK"
                    );

                    return;
                }

                SyncCurrentProfileToRuntime();

                try
                {
                    SyncForgeSceneSync.InitConnectionAsClient(
                        hostIP,
                        port
                    );
                }
                catch (Exception exception)
                {
                    Debug.LogError(exception.Message);
                }
            }
        }
        else
        {
            GUI.backgroundColor = Color.red;

            if (GUILayout.Button(
                hosting ? "Encerrar Host" : "Desconectar"
            ))
            {
                SyncForgeSceneSync.Disconnect();
            }

            GUI.backgroundColor = Color.white;
        }

        // Atualiza a interface enquanto o estado de conexão muda.
        if (hosting || connected)
            Repaint();
    }

    private void DrawUpdateTab()
    {
        EditorGUILayout.LabelField(
            "Sistema de Atualização",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Acesse o repositório oficial para obter a versão mais recente do SyncForge.",
            MessageType.Info
        );

        GUILayout.Space(10);

        GUI.backgroundColor = Color.cyan;

        if (GUILayout.Button(
            "Abrir Repositório / Download",
            GUILayout.Height(35)
        ))
        {
            Application.OpenURL(GitHubPackageUrl);
        }

        GUI.backgroundColor = Color.white;
    }

    private void SyncCurrentProfileToRuntime()
    {
        if (db == null ||
            db.profiles == null ||
            db.profiles.Count == 0)
            return;

        selectedProfileIndex = Mathf.Clamp(
            selectedProfileIndex,
            0,
            db.profiles.Count - 1
        );

        SyncForgeSceneSync.UpdateLocalProfile(
            db.profiles[selectedProfileIndex],
            selectedPrefab
        );
    }

    private void SaveProfiles()
    {
        if (db == null)
            db = new SyncForgeProfileDatabase();

        if (db.profiles == null)
            db.profiles = new List<SyncForgeProfileData>();

        foreach (SyncForgeProfileData profile in db.profiles)
            profile?.Normalize();

        try
        {
            string json = JsonUtility.ToJson(db);

            // Backup anterior preservado até o novo JSON ser gerado.
            if (EditorPrefs.HasKey(GlobalPrefsKey))
            {
                string previous = EditorPrefs.GetString(
                    GlobalPrefsKey,
                    ""
                );

                if (!string.IsNullOrEmpty(previous))
                    EditorPrefs.SetString(BackupPrefsKey, previous);
            }

            EditorPrefs.SetString(GlobalPrefsKey, json);

            // Primeiro salvamento também cria um backup.
            if (!EditorPrefs.HasKey(BackupPrefsKey))
                EditorPrefs.SetString(BackupPrefsKey, json);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[SyncForge] Erro ao salvar perfis: " + exception.Message
            );
        }
    }

    private void LoadProfiles()
    {
        db = null;

        if (TryLoadDatabase(GlobalPrefsKey))
        {
            NormalizeDatabase();
            return;
        }

        Debug.LogWarning(
            "[SyncForge] Banco principal inválido. Tentando backup."
        );

        if (TryLoadDatabase(BackupPrefsKey))
        {
            NormalizeDatabase();

            SaveProfiles();

            return;
        }

        db = new SyncForgeProfileDatabase();

        db.profiles.Add(new SyncForgeProfileData());

        selectedProfileIndex = 0;

        SaveProfiles();
    }

    private bool TryLoadDatabase(string key)
    {
        if (!EditorPrefs.HasKey(key))
            return false;

        try
        {
            string json = EditorPrefs.GetString(key, "");

            if (string.IsNullOrWhiteSpace(json))
                return false;

            SyncForgeProfileDatabase loaded =
                JsonUtility.FromJson<SyncForgeProfileDatabase>(json);

            if (loaded == null || loaded.profiles == null)
                return false;

            db = loaded;

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[SyncForge] Falha ao recuperar dados: " +
                exception.Message
            );

            return false;
        }
    }

    private void NormalizeDatabase()
    {
        if (db == null)
            db = new SyncForgeProfileDatabase();

        if (db.profiles == null)
            db.profiles = new List<SyncForgeProfileData>();

        db.profiles.RemoveAll(profile => profile == null);

        foreach (SyncForgeProfileData profile in db.profiles)
            profile.Normalize();

        if (db.profiles.Count == 0)
            db.profiles.Add(new SyncForgeProfileData());

        selectedProfileIndex = Mathf.Clamp(
            selectedProfileIndex,
            0,
            db.profiles.Count - 1
        );

        LoadSelectedProfileAssets();
    }

    private void LoadSelectedProfileAssets()
    {
        if (db == null ||
            db.profiles == null ||
            db.profiles.Count == 0)
        {
            selectedPrefab = null;
            return;
        }

        selectedProfileIndex = Mathf.Clamp(
            selectedProfileIndex,
            0,
            db.profiles.Count - 1
        );

        SyncForgeProfileData profile =
            db.profiles[selectedProfileIndex];

        if (string.IsNullOrEmpty(profile.prefabPath))
        {
            selectedPrefab = null;
            return;
        }

        selectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            profile.prefabPath
        );
    }

    private static bool IsValidPort(int value)
    {
        return value >= 1 && value <= 65535;
    }
}
