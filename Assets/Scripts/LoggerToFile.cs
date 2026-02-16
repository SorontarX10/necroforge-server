using System.IO;
using UnityEngine;

public class LoggerToFile : MonoBehaviour
{
    [Header("Global Log Filter")]
    [SerializeField] private bool applyRuntimeLogFilter = true;
    [SerializeField] private LogType minimumLogType = LogType.Warning;

    [Header("File Logging")]
    [SerializeField] private bool enableFileLogging = false;

    private string logFilePath;
    private bool subscribed;

    private void Awake()
    {
        if (applyRuntimeLogFilter)
            Debug.unityLogger.filterLogType = minimumLogType;

        if (!enableFileLogging)
            return;

        logFilePath = Path.Combine(Application.persistentDataPath, "debug_log.txt");
        if (File.Exists(logFilePath))
            File.Delete(logFilePath);

        Application.logMessageReceived += HandleLog;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (!subscribed)
            return;

        Application.logMessageReceived -= HandleLog;
        subscribed = false;
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (!enableFileLogging)
            return;

        if (type == LogType.Log)
            return;

        string entry = $"{System.DateTime.Now:HH:mm:ss.fff} [{type}] {logString}";

        if (type == LogType.Exception || type == LogType.Error)
            entry += "\n" + stackTrace;

        try
        {
            File.AppendAllText(logFilePath, entry + "\n");
        }
        catch (System.Exception e)
        {
            Debug.LogError("LoggerToFile: could not write to log file: " + e.Message);
        }
    }

    public string GetLogPath()
    {
        return logFilePath;
    }
}
