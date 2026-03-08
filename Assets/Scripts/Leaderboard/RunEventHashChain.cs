using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class RunEventHashChain
{
    public readonly struct Payload
    {
        public readonly string eventChain;
        public readonly string eventChainHash;
        public readonly int eventCount;

        public Payload(string eventChain, string eventChainHash, int eventCount)
        {
            this.eventChain = eventChain;
            this.eventChainHash = eventChainHash;
            this.eventCount = eventCount;
        }
    }

    private readonly struct Checkpoint
    {
        public readonly int second;
        public readonly int kills;
        public readonly int score;

        public Checkpoint(int second, int kills, int score)
        {
            this.second = second;
            this.kills = kills;
            this.score = score;
        }
    }

    private const int MaxCheckpointCount = 512;
    private static readonly List<Checkpoint> checkpoints = new(MaxCheckpointCount);
    private static bool initialized;
    private static int lastRecordedSecond = -1;

    public static void ResetRun()
    {
        checkpoints.Clear();
        initialized = true;
        lastRecordedSecond = -1;
        AddCheckpoint(0, 0, 0);
    }

    public static void RecordCheckpoint(float elapsedTimeSec, int kills, int score)
    {
        EnsureInitialized();

        int second = Mathf.Max(0, Mathf.FloorToInt(elapsedTimeSec));
        int safeKills = Mathf.Max(0, kills);
        int safeScore = Mathf.Max(0, score);

        if (checkpoints.Count > 0)
        {
            Checkpoint last = checkpoints[checkpoints.Count - 1];
            second = Mathf.Max(second, last.second);
            safeKills = Mathf.Max(safeKills, last.kills);
            safeScore = Mathf.Max(safeScore, last.score);

            bool sameSnapshot = second == last.second && safeKills == last.kills && safeScore == last.score;
            if (sameSnapshot)
                return;
        }

        if (second == lastRecordedSecond && checkpoints.Count > 0)
        {
            Checkpoint last = checkpoints[checkpoints.Count - 1];
            if (safeKills == last.kills && safeScore == last.score)
                return;
        }

        AddCheckpoint(second, safeKills, safeScore);
        lastRecordedSecond = second;
    }

    public static Payload BuildPayload(string runId, string nonce, GameRunStats finalStats)
    {
        EnsureInitialized();
        RecordCheckpoint(finalStats.timeSurvived, finalStats.kills, finalStats.finalScore);

        string eventChain = SerializeChain(checkpoints);
        string eventChainHash = ComputeChainHash(runId, nonce, checkpoints);
        return new Payload(eventChain, eventChainHash, checkpoints.Count);
    }

    private static void EnsureInitialized()
    {
        if (!initialized)
            ResetRun();
    }

    private static void AddCheckpoint(int second, int kills, int score)
    {
        Checkpoint checkpoint = new(second, kills, score);
        if (checkpoints.Count >= MaxCheckpointCount)
        {
            checkpoints[checkpoints.Count - 1] = checkpoint;
            return;
        }

        checkpoints.Add(checkpoint);
    }

    private static string SerializeChain(List<Checkpoint> source)
    {
        StringBuilder builder = new(source.Count * 18);
        for (int i = 0; i < source.Count; i++)
        {
            if (i > 0)
                builder.Append(';');

            Checkpoint c = source[i];
            builder.Append(c.second.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(c.kills.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(c.score.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string ComputeChainHash(string runId, string nonce, List<Checkpoint> source)
    {
        string safeRunId = (runId ?? string.Empty).Trim();
        string safeNonce = (nonce ?? string.Empty).Trim();
        string hash = ComputeSha256Hex($"seed|{safeRunId}|{safeNonce}");
        for (int i = 0; i < source.Count; i++)
        {
            Checkpoint c = source[i];
            hash = ComputeSha256Hex($"{hash}|{c.second}|{c.kills}|{c.score}");
        }

        return hash;
    }

    private static string ComputeSha256Hex(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(bytes);
        StringBuilder builder = new(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
            builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));

        return builder.ToString();
    }
}
