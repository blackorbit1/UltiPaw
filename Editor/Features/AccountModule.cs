#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// Displays account info area under the banner: avatar + username (left), logout (right), and connection state.
public class AccountModule
{
    private readonly UltiPawEditor editor;
    private readonly NetworkService networkService;

    private Texture2D avatarTexture;
    private Color avatarFallbackColor = new Color(0.23f, 0.23f, 0.23f);
    private string userName = "Unknown";
    private string connectionState = ""; // connected | limited | disconnected
    private bool isRefreshing;

    private int? accountUserId;
    private string authToken;
    private bool userInfoRequested;

    public AccountModule(UltiPawEditor editor, NetworkService networkService)
    {
        this.editor = editor;
        this.networkService = networkService;
    }

    public void Initialize()
    {
        LoadAuthData();
        ApplyUserInfoFromCache();
        RequestAccountUserInfo();
        _ = RefreshStateAsync();
    }

    public void Draw()
    {
        if (!editor.isAuthenticated) return;

        if (avatarTexture == null && accountUserId.HasValue)
        {
            var cachedAvatar = UserService.GetUserAvatar(accountUserId.Value);
            if (cachedAvatar != null)
            {
                avatarTexture = cachedAvatar;
            }
        }

        if (accountUserId.HasValue)
        {
            var cachedInfo = UserService.GetUserInfo(accountUserId.Value);
            if (cachedInfo != null && !string.IsNullOrEmpty(cachedInfo.username) && !string.Equals(userName, cachedInfo.username, StringComparison.Ordinal))
            {
                userName = cachedInfo.username;
                UpdateFallbackColor();
            }
        }

        EditorGUILayout.Space(4);
        Rect rect = EditorGUILayout.BeginHorizontal();

        try
        {
            EditorGUILayout.BeginHorizontal();
            DrawAvatarWithName();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(false));
            DrawLogoutButton();
            DrawConnectionState();
            EditorGUILayout.EndVertical();
        }
        catch (Exception ex)
        {
            if (ex is ExitGUIException) throw;
            editor.GetType().GetMethod("RecordUiException", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                  ?.Invoke(editor, new object[] { ex });
        }
        finally
        {
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);
        }
    }

    private void DrawAvatarWithName()
    {
        UpdateFallbackColor();

        EditorGUILayout.BeginHorizontal();
        // Avatar (40x40)
        Rect avatarRect = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
        EditorUIUtils.DrawCircularAvatar(avatarRect, avatarTexture, avatarFallbackColor, new Color(0.46f, 0.46f, 0.46f), 1.5f);

        if (avatarTexture == null)
        {
            string initials = GetInitials(userName);
            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(avatarRect, initials, labelStyle);
        }

        GUILayout.Space(8);

        // Text block vertically centered to the avatar
        EditorGUILayout.BeginVertical(GUILayout.Height(40));
        GUILayout.FlexibleSpace();
        var loggedAsStyle = new GUIStyle(EditorStyles.miniLabel);
        loggedAsStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f); // gray label
        GUILayout.Label("logged as", loggedAsStyle);
        GUILayout.Label(string.IsNullOrEmpty(userName) ? "(unknown)" : userName, EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawLogoutButton()
    {
        if (GUILayout.Button("Logout", EditorStyles.miniButton, GUILayout.Height(22), GUILayout.Width(90)))
        {
            if (EditorUtility.DisplayDialog("Confirm Logout", "Are you sure you want to log out?", "Logout", "Cancel"))
            {
                if (UltiPawUtils.RemoveAuth())
                {
                    editor.isAuthenticated = false;
                    editor.authToken = null;
                    ResetAccountState();
                    editor.Repaint();
                }
            }
        }
    }

    private void DrawConnectionState()
    {
        EditorGUILayout.Space(4);

        float dotSize = 8f;
        float spacing = 6f;
        string displayState = string.IsNullOrEmpty(connectionState) ? (isRefreshing ? "Checking..." : "unknown") : connectionState;
        GUIContent stateContent = new GUIContent(displayState);
        Vector2 labelSize = EditorStyles.miniLabel.CalcSize(stateContent);
        float rowHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, dotSize);
        float rowWidth = dotSize + spacing + labelSize.x;
        Rect rowRect = GUILayoutUtility.GetRect(rowWidth, rowHeight, GUILayout.ExpandWidth(false));

        // Draw status dot
        Color dotColor = GetStateColor(connectionState);
        Rect dotRect = new Rect(rowRect.x, rowRect.y + (rowHeight - dotSize) * 0.5f, dotSize, dotSize);
        Handles.BeginGUI();
        var oldColor = Handles.color;
        Handles.color = dotColor;
        Handles.DrawSolidDisc(dotRect.center, Vector3.forward, dotSize * 0.5f);
        Handles.color = oldColor;
        Handles.EndGUI();

        // Draw status label aligned vertically with the dot
        Rect labelRect = new Rect(dotRect.xMax + spacing, rowRect.y + (rowHeight - labelSize.y) * 0.5f, labelSize.x, labelSize.y);
        GUI.Label(labelRect, stateContent, EditorStyles.miniLabel);
    }

    public void Refresh()
    {
        LoadAuthData();
        ApplyUserInfoFromCache();
        RequestAccountUserInfo(force: true);
        _ = RefreshStateAsync();
        editor.Repaint();
    }

    private async Task RefreshStateAsync()
    {
        if (isRefreshing) return;
        isRefreshing = true;
        try
        {
            if (string.IsNullOrEmpty(authToken))
            {
                UltiPawLogger.LogWarning("[UltiPaw] No authentication token found. Account state set to 'disconnected'.");
                connectionState = "disconnected";
                return;
            }

            string url = UltiPawUtils.getServerUrl() + UltiPawUtils.CHECK_CONNECTION_ENDPOINT + $"?t={authToken}";
            UltiPawLogger.Log("[UltiPaw] Refreshing account state...");
            string newState = await networkService.CheckConnectionAsync(url, authToken);
            if (string.IsNullOrEmpty(newState)) newState = "disconnected";
            connectionState = newState;
        }
        catch (Exception ex)
        {
            UltiPawLogger.LogWarning($"[UltiPaw] Failed to refresh account state: {ex.Message}");
            connectionState = "disconnected";
        }
        finally
        {
            isRefreshing = false;
            editor.Repaint();
        }
    }

    private void LoadAuthData()
    {
        var auth = UltiPawUtils.GetAuth();
        if (auth == null)
        {
            // Fallback to editor state if available (e.g., auth loaded elsewhere)
            if (!string.IsNullOrEmpty(editor.authToken))
            {
                authToken = editor.authToken;
                userInfoRequested = false;
                avatarTexture = null;
                // Keep existing userName/accountUserId if any; otherwise leave as Unknown
                UpdateFallbackColor();
                return;
            }

            ResetAccountState();
            return;
        }

        authToken = auth.token;
        userInfoRequested = false;
        avatarTexture = null;

        if (int.TryParse(auth.user, out var parsedId))
        {
            accountUserId = parsedId;
            var cachedInfo = UserService.GetUserInfo(parsedId);
            if (cachedInfo != null && !string.IsNullOrEmpty(cachedInfo.username))
            {
                userName = cachedInfo.username;
            }
            else
            {
                userName = $"User {parsedId}";
            }
        }
        else if (!string.IsNullOrEmpty(auth.user))
        {
            accountUserId = null;
            userName = auth.user;
        }
        else
        {
            accountUserId = null;
            userName = "Unknown";
        }

        UpdateFallbackColor();
    }

    private void ApplyUserInfoFromCache()
    {
        if (!accountUserId.HasValue) return;

        var info = UserService.GetUserInfo(accountUserId.Value);
        if (info != null && !string.IsNullOrEmpty(info.username))
        {
            userName = info.username;
        }

        var cachedAvatar = UserService.GetUserAvatar(accountUserId.Value);
        if (cachedAvatar != null)
        {
            avatarTexture = cachedAvatar;
        }

        UpdateFallbackColor();
    }

    private void RequestAccountUserInfo(bool force = false)
    {
        if (!accountUserId.HasValue) return;

        ApplyUserInfoFromCache();

        bool hasInfo = UserService.IsUserInfoAvailable(accountUserId.Value);
        bool hasAvatar = avatarTexture != null;

        if (!force && hasInfo && hasAvatar)
        {
            return;
        }

        if (!force && userInfoRequested)
        {
            return;
        }

        userInfoRequested = true;
        // Use explicit token when available to avoid relying on auth.dat being present
        var tokenForUserInfo = authToken;
        UserService.RequestUserInfo(accountUserId.Value, tokenForUserInfo, () =>
        {
            userInfoRequested = false;
            ApplyUserInfoFromCache();
            editor.Repaint();
        });
    }

    private void ResetAccountState()
    {
        accountUserId = null;
        authToken = null;
        userInfoRequested = false;
        avatarTexture = null;
        userName = "Unknown";
        connectionState = "disconnected";
        UltiPawLogger.LogWarning("[UltiPaw] Account state set to 'disconnected' (logged out or no authentication data).");
        UpdateFallbackColor();
    }

    private void UpdateFallbackColor()
    {
        avatarFallbackColor = SeedToColor(string.IsNullOrEmpty(userName) ? "Unknown" : userName);
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        string[] parts = name.Split(new[] { ' ', '\t', '\n', '\r', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
        string a = parts[0].Substring(0, 1);
        string b = parts[parts.Length - 1].Substring(0, 1);
        return (a + b).ToUpperInvariant();
    }

    private static Color SeedToColor(string seed)
    {
        unchecked
        {
            int h = seed == null ? 0 : seed.GetHashCode();
            float r = ((h & 0xFF) / 255f) * 0.6f + 0.2f;
            float g = (((h >> 8) & 0xFF) / 255f) * 0.6f + 0.2f;
            float b = (((h >> 16) & 0xFF) / 255f) * 0.6f + 0.2f;
            return new Color(r, g, b);
        }
    }

    private static Color GetStateColor(string state)
    {
        if (string.IsNullOrEmpty(state)) return Color.gray;
        switch (state)
        {
            case "connected": return new Color(0.2f, 0.8f, 0.2f);
            case "limited": return EditorUIUtils.OrangeColor;
            case "disconnected": return new Color(0.9f, 0.2f, 0.2f);
            default: return Color.gray;
        }
    }
}
#endif
