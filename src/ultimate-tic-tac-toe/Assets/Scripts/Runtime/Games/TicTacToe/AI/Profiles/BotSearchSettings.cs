#nullable enable

using UnityEngine;

namespace Runtime.Games.TicTacToe.AI.Profiles
{
    /// <summary>
    /// Shared search policy data for Minimax engine.
    /// This is global tuning independent from BotProfile difficulty values.
    /// </summary>
    public readonly struct BotSearchSettingsData
    {
        public const int DefaultYieldEveryNNodes = 1024;
        public const float DefaultSafetyBudgetMultiplier = 2f;
        public const int DefaultCandidateFilterMinBoardSize = 4;
        public const int DefaultCandidateNeighborRadius = 2;
        public const int DefaultDepthCap3OrLess = 9;
        public const int DefaultDepthCap4 = 4;
        public const int DefaultDepthCap5 = 3;
        public const int DefaultDepthCap6 = 3;
        public const int DefaultDepthCap7 = 2;
        public const int DefaultDepthCap8Plus = 2;

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
            yieldEveryNNodes: DefaultYieldEveryNNodes,
            safetyBudgetMultiplier: DefaultSafetyBudgetMultiplier,
            candidateFilterMinBoardSize: DefaultCandidateFilterMinBoardSize,
            candidateNeighborRadius: DefaultCandidateNeighborRadius,
            depthCap3OrLess: DefaultDepthCap3OrLess,
            depthCap4: DefaultDepthCap4,
            depthCap5: DefaultDepthCap5,
            depthCap6: DefaultDepthCap6,
            depthCap7: DefaultDepthCap7,
            depthCap8Plus: DefaultDepthCap8Plus);

        public int GetDepthCap(int boardSize) =>
            boardSize switch
            {
                <= 3 => DepthCap3OrLess,
                4 => DepthCap4,
                5 => DepthCap5,
                6 => DepthCap6,
                7 => DepthCap7,
                _ => DepthCap8Plus,
            };
    }

    [CreateAssetMenu(fileName = "BotSearchSettings", menuName = "TicTacToe/AI/Bot Search Settings")]
    public sealed class BotSearchSettings : ScriptableObject
    {
        private const int _yieldEveryNNodesMin = 1;
        private const int _yieldEveryNNodesMax = 10_000;

        private const float _safetyBudgetMultiplierMin = 1f;
        private const float _safetyBudgetMultiplierMax = 10f;

        private const int _candidateFilterMinBoardSizeMin = 3;
        private const int _candidateFilterMinBoardSizeMax = 10;
        private const int _candidateNeighborRadiusMin = 1;
        private const int _candidateNeighborRadiusMax = 3;

        private const int _depthCapMin = 1;
        private const int _depthCapMax = 20;

        [Header("Scheduler")]
        [SerializeField] [Min(_yieldEveryNNodesMin)] private int YieldEveryNNodes = BotSearchSettingsData.DefaultYieldEveryNNodes;
        [SerializeField] [Min(_safetyBudgetMultiplierMin)] private float SafetyBudgetMultiplier = BotSearchSettingsData.DefaultSafetyBudgetMultiplier;

        [Header("Candidate Filter")]
        [SerializeField] [Min(_candidateFilterMinBoardSizeMin)] private int CandidateFilterMinBoardSize = BotSearchSettingsData.DefaultCandidateFilterMinBoardSize;
        [SerializeField] [Range(_candidateNeighborRadiusMin, _candidateNeighborRadiusMax)] private int CandidateNeighborRadius = BotSearchSettingsData.DefaultCandidateNeighborRadius;

        [Header("Depth Caps by Board Size")]
        [SerializeField] [Min(_depthCapMin)] private int DepthCap3OrLess = BotSearchSettingsData.DefaultDepthCap3OrLess;
        [SerializeField] [Min(_depthCapMin)] private int DepthCap4 = BotSearchSettingsData.DefaultDepthCap4;
        [SerializeField] [Min(_depthCapMin)] private int DepthCap5 = BotSearchSettingsData.DefaultDepthCap5;
        [SerializeField] [Min(_depthCapMin)] private int DepthCap6 = BotSearchSettingsData.DefaultDepthCap6;
        [SerializeField] [Min(_depthCapMin)] private int DepthCap7 = BotSearchSettingsData.DefaultDepthCap7;
        [SerializeField] [Min(_depthCapMin)] private int DepthCap8Plus = BotSearchSettingsData.DefaultDepthCap8Plus;

        public BotSearchSettingsData ToValidatedData()
        {
            var yieldEvery = ClampWarn(YieldEveryNNodes, _yieldEveryNNodesMin, _yieldEveryNNodesMax, nameof(YieldEveryNNodes));
            var safety = ClampWarn(SafetyBudgetMultiplier, _safetyBudgetMultiplierMin, _safetyBudgetMultiplierMax, nameof(SafetyBudgetMultiplier));
            var minBoard = ClampWarn(CandidateFilterMinBoardSize, _candidateFilterMinBoardSizeMin, _candidateFilterMinBoardSizeMax, nameof(CandidateFilterMinBoardSize));
            var radius = ClampWarn(CandidateNeighborRadius, _candidateNeighborRadiusMin, _candidateNeighborRadiusMax, nameof(CandidateNeighborRadius));

            var cap3 = ClampWarn(DepthCap3OrLess, _depthCapMin, _depthCapMax, nameof(DepthCap3OrLess));
            var cap4 = ClampWarn(DepthCap4, _depthCapMin, _depthCapMax, nameof(DepthCap4));
            var cap5 = ClampWarn(DepthCap5, _depthCapMin, _depthCapMax, nameof(DepthCap5));
            var cap6 = ClampWarn(DepthCap6, _depthCapMin, _depthCapMax, nameof(DepthCap6));
            var cap7 = ClampWarn(DepthCap7, _depthCapMin, _depthCapMax, nameof(DepthCap7));
            var cap8 = ClampWarn(DepthCap8Plus, _depthCapMin, _depthCapMax, nameof(DepthCap8Plus));

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
            if (value >= min && value <= max) 
                return value;
            
            Debug.LogWarning($"[BotSearchSettings] {fieldName}={value} out of range [{min}..{max}], clamped.");
            return Mathf.Clamp(value, min, max);
        }

        private static int ClampWarn(int value, int min, int max, string fieldName)
        {
            if (value >= min && value <= max) 
                return value;
            
            Debug.LogWarning($"[BotSearchSettings] {fieldName}={value} out of range [{min}..{max}], clamped.");
            return Mathf.Clamp(value, min, max);
        }
    }
}