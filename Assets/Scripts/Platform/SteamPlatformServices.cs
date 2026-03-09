using System;
using System.Reflection;
using System.Text;
using UnityEngine;

public sealed class SteamPlatformServices : IPlatformServices
{
    private readonly Type steamApiType;
    private readonly Type steamUserType;
    private readonly Type steamFriendsType;

    private readonly MethodInfo initMethod;
    private readonly MethodInfo shutdownMethod;
    private readonly MethodInfo runCallbacksMethod;
    private readonly MethodInfo getSteamIdMethod;
    private readonly MethodInfo getPersonaNameMethod;
    private readonly MethodInfo getAuthSessionTicketMethod;
    private readonly MethodInfo openWebOverlayMethod;
    private readonly MethodInfo openWebOverlayWithModeMethod;
    private readonly MethodInfo openGenericOverlayMethod;

    public string ProviderKey => "steam";
    public bool IsAvailable { get; }
    public bool IsInitialized { get; private set; }

    public SteamPlatformServices()
    {
        steamApiType = FindType("Steamworks.SteamAPI");
        steamUserType = FindType("Steamworks.SteamUser");
        steamFriendsType = FindType("Steamworks.SteamFriends");

        initMethod = GetStaticMethod(steamApiType, "Init");
        shutdownMethod = GetStaticMethod(steamApiType, "Shutdown");
        runCallbacksMethod = GetStaticMethod(steamApiType, "RunCallbacks");
        getSteamIdMethod = GetStaticMethod(steamUserType, "GetSteamID");
        getPersonaNameMethod = GetStaticMethod(steamFriendsType, "GetPersonaName");
        getAuthSessionTicketMethod = FindGetAuthSessionTicketMethod(steamUserType);

        openWebOverlayMethod = GetStaticMethod(
            steamFriendsType,
            "ActivateGameOverlayToWebPage",
            new[] { typeof(string) }
        );
        openWebOverlayWithModeMethod = FindOverlayToWebPageWithMode(steamFriendsType);
        openGenericOverlayMethod = GetStaticMethod(
            steamFriendsType,
            "ActivateGameOverlay",
            new[] { typeof(string) }
        );

        IsAvailable = initMethod != null
            && shutdownMethod != null
            && runCallbacksMethod != null
            && getSteamIdMethod != null
            && getPersonaNameMethod != null;
    }

    public bool Initialize()
    {
        if (IsInitialized)
            return true;

        if (!IsAvailable)
            return false;

        try
        {
            object result = initMethod.Invoke(null, null);
            IsInitialized = result is bool ok && ok;
            return IsInitialized;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Platform/Steam] Init failed: {ex.Message}");
            IsInitialized = false;
            return false;
        }
    }

    public void Tick()
    {
        if (!IsInitialized || runCallbacksMethod == null)
            return;

        try
        {
            runCallbacksMethod.Invoke(null, null);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Platform/Steam] RunCallbacks failed: {ex.Message}");
        }
    }

    public void Shutdown()
    {
        if (!IsInitialized || shutdownMethod == null)
        {
            IsInitialized = false;
            return;
        }

        try
        {
            shutdownMethod.Invoke(null, null);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Platform/Steam] Shutdown failed: {ex.Message}");
        }
        finally
        {
            IsInitialized = false;
        }
    }

    public string GetPlayerId()
    {
        if (!IsInitialized || getSteamIdMethod == null)
            return string.Empty;

        try
        {
            object rawSteamId = getSteamIdMethod.Invoke(null, null);
            string normalizedId = ExtractSteamId(rawSteamId);
            if (string.IsNullOrWhiteSpace(normalizedId))
                return string.Empty;

            return $"steam:{normalizedId}";
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Platform/Steam] GetPlayerId failed: {ex.Message}");
            return string.Empty;
        }
    }

    public string GetPlayerName()
    {
        if (!IsInitialized || getPersonaNameMethod == null)
            return string.Empty;

        try
        {
            string value = getPersonaNameMethod.Invoke(null, null) as string;
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim();
            return normalized.Length <= 48 ? normalized : normalized[..48];
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Platform/Steam] GetPlayerName failed: {ex.Message}");
            return string.Empty;
        }
    }

    public bool OpenOverlayToLeaderboard(string leaderboardUrl)
    {
        if (!IsInitialized || string.IsNullOrWhiteSpace(leaderboardUrl))
            return false;

        string normalizedUrl = leaderboardUrl.Trim();
        try
        {
            if (openWebOverlayMethod != null)
            {
                openWebOverlayMethod.Invoke(null, new object[] { normalizedUrl });
                return true;
            }

            if (openWebOverlayWithModeMethod != null)
            {
                ParameterInfo[] parameters = openWebOverlayWithModeMethod.GetParameters();
                object modeValue = parameters.Length >= 2
                    ? CreateDefaultEnumValue(parameters[1].ParameterType)
                    : null;
                openWebOverlayWithModeMethod.Invoke(null, new[] { normalizedUrl, modeValue });
                return true;
            }

            if (openGenericOverlayMethod != null)
            {
                openGenericOverlayMethod.Invoke(null, new object[] { "community" });
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Platform/Steam] Overlay open failed: {ex.Message}");
        }

        return false;
    }

    public bool TryGetExternalAuthTicket(out string provider, out string providerUserId, out string sessionTicket)
    {
        provider = "steam";
        providerUserId = string.Empty;
        sessionTicket = string.Empty;

        if (!IsInitialized || getSteamIdMethod == null || getAuthSessionTicketMethod == null)
            return false;

        try
        {
            object rawSteamId = getSteamIdMethod.Invoke(null, null);
            string steamId = ExtractSteamId(rawSteamId);
            if (string.IsNullOrWhiteSpace(steamId))
                return false;

            byte[] ticketBuffer = new byte[2048];
            object[] args = BuildAuthTicketArgs(getAuthSessionTicketMethod, ticketBuffer);
            if (args == null)
                return false;

            getAuthSessionTicketMethod.Invoke(null, args);
            int ticketSize = ReadAuthTicketSize(getAuthSessionTicketMethod, args);
            if (ticketSize <= 0 || ticketSize > ticketBuffer.Length)
                return false;

            providerUserId = steamId;
            sessionTicket = ConvertToHex(ticketBuffer, ticketSize);
            return !string.IsNullOrWhiteSpace(sessionTicket);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Platform/Steam] GetAuthSessionTicket failed: {ex.Message}");
            providerUserId = string.Empty;
            sessionTicket = string.Empty;
            return false;
        }
    }

    private static Type FindType(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
            return null;

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(fullTypeName, false);
            if (type != null)
                return type;
        }

        return null;
    }

    private static MethodInfo GetStaticMethod(Type type, string methodName, Type[] parameterTypes = null)
    {
        if (type == null || string.IsNullOrWhiteSpace(methodName))
            return null;

        BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        if (parameterTypes == null)
            return type.GetMethod(methodName, flags);

        return type.GetMethod(methodName, flags, null, parameterTypes, null);
    }

    private static MethodInfo FindOverlayToWebPageWithMode(Type steamFriends)
    {
        if (steamFriends == null)
            return null;

        BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        MethodInfo[] methods = steamFriends.GetMethods(flags);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (!string.Equals(method.Name, "ActivateGameOverlayToWebPage", StringComparison.Ordinal))
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2)
                continue;

            if (parameters[0].ParameterType != typeof(string))
                continue;

            if (!parameters[1].ParameterType.IsEnum)
                continue;

            return method;
        }

        return null;
    }

    private static MethodInfo FindGetAuthSessionTicketMethod(Type steamUserType)
    {
        if (steamUserType == null)
            return null;

        BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        MethodInfo[] methods = steamUserType.GetMethods(flags);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (!string.Equals(method.Name, "GetAuthSessionTicket", StringComparison.Ordinal))
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length < 3 || parameters.Length > 4)
                continue;
            if (parameters[0].ParameterType != typeof(byte[]))
                continue;
            if (parameters[1].ParameterType != typeof(int))
                continue;

            Type ticketSizeType = parameters[2].ParameterType.IsByRef
                ? parameters[2].ParameterType.GetElementType()
                : parameters[2].ParameterType;
            if (ticketSizeType != typeof(uint))
                continue;

            if (parameters.Length == 4 && !parameters[3].ParameterType.IsByRef)
                continue;

            return method;
        }

        return null;
    }

    private static object[] BuildAuthTicketArgs(MethodInfo method, byte[] ticketBuffer)
    {
        if (method == null || ticketBuffer == null || ticketBuffer.Length == 0)
            return null;

        ParameterInfo[] parameters = method.GetParameters();
        object[] args = new object[parameters.Length];
        args[0] = ticketBuffer;
        args[1] = ticketBuffer.Length;
        args[2] = 0u;

        if (parameters.Length == 4)
        {
            Type identityType = parameters[3].ParameterType.GetElementType();
            args[3] = identityType == null ? null : CreateDefaultValue(identityType);
        }

        return args;
    }

    private static int ReadAuthTicketSize(MethodInfo method, object[] args)
    {
        if (method == null || args == null || args.Length < 3)
            return 0;

        try
        {
            object sizeValue = args[2];
            return Convert.ToInt32(sizeValue);
        }
        catch
        {
            return 0;
        }
    }

    private static string ConvertToHex(byte[] data, int length)
    {
        if (data == null || data.Length == 0 || length <= 0)
            return string.Empty;

        int clampedLength = Mathf.Clamp(length, 0, data.Length);
        StringBuilder builder = new StringBuilder(clampedLength * 2);
        for (int i = 0; i < clampedLength; i++)
            builder.Append(data[i].ToString("x2"));
        return builder.ToString();
    }

    private static object CreateDefaultValue(Type type)
    {
        if (type == null)
            return null;

        if (type.IsValueType)
            return Activator.CreateInstance(type);

        ConstructorInfo ctor = type.GetConstructor(Type.EmptyTypes);
        if (ctor != null)
            return ctor.Invoke(null);

        return null;
    }

    private static string ExtractSteamId(object rawSteamId)
    {
        if (rawSteamId == null)
            return string.Empty;

        Type rawType = rawSteamId.GetType();
        PropertyInfo idProperty = rawType.GetProperty("m_SteamID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (idProperty != null)
        {
            object value = idProperty.GetValue(rawSteamId);
            if (TryConvertToUlong(value, out ulong id))
                return id.ToString();
        }

        FieldInfo idField = rawType.GetField("m_SteamID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (idField != null)
        {
            object value = idField.GetValue(rawSteamId);
            if (TryConvertToUlong(value, out ulong id))
                return id.ToString();
        }

        string fallback = rawSteamId.ToString();
        return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
    }

    private static bool TryConvertToUlong(object value, out ulong output)
    {
        output = 0UL;
        if (value == null)
            return false;

        try
        {
            output = Convert.ToUInt64(value);
            return output > 0UL;
        }
        catch
        {
            return false;
        }
    }

    private static object CreateDefaultEnumValue(Type enumType)
    {
        if (enumType == null || !enumType.IsEnum)
            return null;

        Array values = Enum.GetValues(enumType);
        if (values.Length > 0)
            return values.GetValue(0);

        return Enum.ToObject(enumType, 0);
    }
}
