#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Decision
{
    internal static class UltimateBotCandidateSelector
    {
        private const float _lowSkillNoiseThreshold = 0.95f;
        private const float _lowSkillHardRuleProbabilityThreshold = 0.2f;
        private const float _veryLowSkillHardRuleProbabilityThreshold = 0.05f;
        private const int _veryLowSkillCandidateBandNumerator = 4;
        private const int _veryLowSkillCandidateBandDenominator = 5;
        private const int _lowSkillCandidateBandNumerator = 2;
        private const int _lowSkillCandidateBandDenominator = 3;
        private const float _rankWeightDecayMultiplier = 4f;

        public static List<BotCandidateScore> TakeTopCandidates(IReadOnlyList<BotCandidateScore> rankedCandidates, int topCount)
        {
            var limitedCount = Math.Min(topCount, rankedCandidates.Count);
            var result = new List<BotCandidateScore>(limitedCount);

            for (var i = 0; i < limitedCount; i++)
            {
                result.Add(rankedCandidates[i]);
            }

            return result;
        }

        public static BotCandidateScore ApplyNoise(
            IReadOnlyList<BotCandidateScore> candidates,
            UltimateBotDifficultyProfileData profile,
            IBotRngSession rng)
        {
            if (candidates.Count == 0) 
                throw new InvalidOperationException("Candidates cannot be empty.");

            if (candidates.Count == 1 || profile.Noise <= 0f) 
                return candidates[0];

            if (IsLowSkillProfile(profile))
            {
                var lowerStart = IsVeryLowSkillProfile(profile)
                    ? CalculateCandidateBandStart(
                        candidates.Count,
                        _veryLowSkillCandidateBandNumerator,
                        _veryLowSkillCandidateBandDenominator)
                    : CalculateCandidateBandStart(
                        candidates.Count,
                        _lowSkillCandidateBandNumerator,
                        _lowSkillCandidateBandDenominator);

                var index = rng.NextInt(lowerStart, candidates.Count);
                return candidates[index];
            }

            var weights = new float[candidates.Count];
            var totalWeight = 0f;
            
            for (var i = 0; i < candidates.Count; i++)
            {
                var rankWeight = 1f / (1f + i * (1f - profile.Noise) * _rankWeightDecayMultiplier);
                weights[i] = rankWeight;
                totalWeight += rankWeight;
            }

            var threshold = rng.NextFloat01() * totalWeight;
            var cumulative = 0f;
            
            for (var i = 0; i < weights.Length; i++)
            {
                cumulative += weights[i];
                
                if (threshold <= cumulative) 
                    return candidates[i];
            }

            return candidates[^1];
        }

        private static bool IsLowSkillProfile(UltimateBotDifficultyProfileData profile) =>
            profile is
            {
                Noise: >= _lowSkillNoiseThreshold,
                MustWinGlobalNowProbability: <= _lowSkillHardRuleProbabilityThreshold,
                MustBlockGlobalNowProbability: <= _lowSkillHardRuleProbabilityThreshold,
                MustWinLocalNowProbability: <= _lowSkillHardRuleProbabilityThreshold,
                MustBlockLocalNowProbability: <= _lowSkillHardRuleProbabilityThreshold,
            };

        private static bool IsVeryLowSkillProfile(UltimateBotDifficultyProfileData profile) =>
            profile is
            {
                MustWinGlobalNowProbability: <= _veryLowSkillHardRuleProbabilityThreshold,
                MustBlockGlobalNowProbability: <= _veryLowSkillHardRuleProbabilityThreshold,
                MustWinLocalNowProbability: <= _veryLowSkillHardRuleProbabilityThreshold,
                MustBlockLocalNowProbability: <= _veryLowSkillHardRuleProbabilityThreshold,
            };

        private static int CalculateCandidateBandStart(int candidateCount, int numerator, int denominator)
            => Math.Max(0, candidateCount * numerator / denominator);

        public static int CompareDeterministically(BotCandidateScore left, BotCandidateScore right)
        {
            var scoreCompare = right.Score.CompareTo(left.Score);
            
            if (scoreCompare != 0) 
                return scoreCompare;

            var majorCompare = left.Move.Major.CompareTo(right.Move.Major);
            return majorCompare != 0 ? majorCompare : left.Move.Minor.CompareTo(right.Move.Minor);
        }
    }
}