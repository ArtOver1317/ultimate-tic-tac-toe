#nullable enable

using System;
using UnityEngine;

namespace Runtime.Games.TicTacToe.AI
{
    /// <summary>
    /// Shared search policy data for Minimax engine.
    /// This is global tuning independent from BotProfile difficulty values.
    /// </summary>
    public readonly struct BotSearchSettingsData
    {
        public int YieldEveryNNodes { get; }
        public float SafetyBudgetMultiplier { get; }
        public int CandidateFilterMinBoardSize { get; }
        public int CandidateNeighborRadius { get; }
        public int DepthCap3OrLess { get; }
        public int DepthCap4 { get; }
        public int DepthCap5 { get; }
        public int DepthCap6 { get; }
        public int DepthCap7 { get; }
        public int DepthCap8Plus { get; }

        public BotSearchSettingsData(
            int yieldEveryNNodes,
            float safetyBudgetMultiplier,
            int candidateFilterMinBoardSize,
            int candidateNeighborRadius,
            int depthCap3OrLess,
            int depthCap4,
            int depthCap5,
            int depthCap6,
            int depthCap7,
            int depthCap8Plus)
        {
            YieldEveryNNodes = yieldEveryNNodes;
            SafetyBudgetMultiplier = safetyBudgetMultiplier;
            CandidateFilterMinBoardSize = candidateFilterMinBoardSize;
            CandidateNeighborRadius = candidateNeighborRadius;
            DepthCap3OrLess = depthCap3OrLess;
            DepthCap4 = depthCap4;
            DepthCap5 = depthCap5;
            DepthCap6 = depthCap6;
            DepthCap7 = depthCap7;
            DepthCap8Plus = depthCap8Plus;
        }

        public static BotSearchSettingsData FastPveDefault => new(
            yieldEveryNNodes: 1024,
            safetyBudgetMultiplier: 2f,
            candidateFilterMinBoardSize: 4,
            candidateNeighborRadius: 2,
            depthCap3OrLess: 9,
            depthCap4: 4,
            depthCap5: 3,
            depthCap6: 3,
            depthCap7: 2,
            depthCap8Plus: 2);

        public int GetDepthCap(int boardSize)
        {
            return boardSize switch
            {
                <= 3 => DepthCap3OrLess,
                4 => DepthCap4,
                5 => DepthCap5,
                6 => DepthCap6,
                7 => DepthCap7,
                _ => DepthCap8Plus,
            };
        }
    }

    [CreateAssetMenu(fileName = "BotSearchSettings", menuName = "TicTacToe/AI/Bot Search Settings")]
    public sealed class BotSearchSettings : ScriptableObject
    {
        [Header("Scheduler")]
        [SerializeField, Min(1)] private int YieldEveryNNodes = 1024;
        [SerializeField, Min(1f)] private float SafetyBudgetMultiplier = 2f;

        [Header("Candidate Filter")]
        [SerializeField, Min(3)] private int CandidateFilterMinBoardSize = 4;
        [SerializeField, Range(1, 3)] private int CandidateNeighborRadius = 2;

        [Header("Depth Caps by Board Size")]
        [SerializeField, Min(1)] private int DepthCap3OrLess = 9;
        [SerializeField, Min(1)] private int DepthCap4 = 4;
        [SerializeField, Min(1)] private int DepthCap5 = 3;
        [SerializeField, Min(1)] private int DepthCap6 = 3;
        [SerializeField, Min(1)] private int DepthCap7 = 2;
        [SerializeField, Min(1)] private int DepthCap8Plus = 2;

        public BotSearchSettingsData ToValidatedData()
        {
            int yieldEvery = ClampWarn(YieldEveryNNodes, 1, 10_000, nameof(YieldEveryNNodes));
            float safety = ClampWarn(SafetyBudgetMultiplier, 1f, 10f, nameof(SafetyBudgetMultiplier));
            int minBoard = ClampWarn(CandidateFilterMinBoardSize, 3, 10, nameof(CandidateFilterMinBoardSize));
            int radius = ClampWarn(CandidateNeighborRadius, 1, 3, nameof(CandidateNeighborRadius));

            int cap3 = ClampWarn(DepthCap3OrLess, 1, 20, nameof(DepthCap3OrLess));
            int cap4 = ClampWarn(DepthCap4, 1, 20, nameof(DepthCap4));
            int cap5 = ClampWarn(DepthCap5, 1, 20, nameof(DepthCap5));
            int cap6 = ClampWarn(DepthCap6, 1, 20, nameof(DepthCap6));
            int cap7 = ClampWarn(DepthCap7, 1, 20, nameof(DepthCap7));
            int cap8 = ClampWarn(DepthCap8Plus, 1, 20, nameof(DepthCap8Plus));

            return new BotSearchSettingsData(
                yieldEvery,
                safety,
                minBoard,
                radius,
                cap3,
                cap4,
                cap5,
                cap6,
                cap7,
                cap8);
        }

        private static float ClampWarn(float value, float min, float max, string fieldName)
        {
            if (value >= min && value <= max) return value;
            Debug.LogWarning($"[BotSearchSettings] {fieldName}={value} out of range [{min}..{max}], clamped.");
            return Mathf.Clamp(value, min, max);
        }

        private static int ClampWarn(int value, int min, int max, string fieldName)
        {
            if (value >= min && value <= max) return value;
            Debug.LogWarning($"[BotSearchSettings] {fieldName}={value} out of range [{min}..{max}], clamped.");
            return Mathf.Clamp(value, min, max);
        }
    }
}