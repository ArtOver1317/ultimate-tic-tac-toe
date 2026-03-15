#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.AI.Search
{
    internal static class MinimaxCandidateSelector
    {
        private const float _minimumNoiseTemperature = 0.01f;
        private const float _neutralRiskBiasRank = 0.5f;

        public static CellId SelectFromCandidates(List<(CellId move, float score)> scored, BotProfileData profile, IBotRandom rng)
        {
            scored.Sort((left, right) => right.score.CompareTo(left.score));

            var topN = Math.Min(profile.TopCandidateCount, scored.Count);
            
            if (profile.Noise <= 0f || topN <= 1)
                return scored[0].move;

            var maxScore = scored[0].score;
            var minScore = scored[topN - 1].score;
            var scoreRange = maxScore - minScore;
            Span<float> weights = stackalloc float[topN];
            var totalWeight = FillCandidateWeights(scored, topN, profile, maxScore, scoreRange, weights);

            return totalWeight > 0f
                ? SelectWeightedCandidate(scored, topN, totalWeight, weights, rng)
                : scored[0].move;
        }

        private static float FillCandidateWeights(
            List<(CellId move, float score)> scored,
            int topN,
            BotProfileData profile,
            float maxScore,
            float scoreRange,
            Span<float> weights)
        {
            var totalWeight = 0f;

            for (var i = 0; i < topN; i++)
            {
                var weight = CalculateCandidateWeight(scored[i].score, maxScore, scoreRange, profile.Noise);
                weight = ApplyRiskBias(weight, i, topN, profile.RiskBias);
                weights[i] = weight;
                totalWeight += weight;
            }

            return totalWeight;
        }

        private static float CalculateCandidateWeight(float score, float maxScore, float scoreRange, float noise)
        {
            var delta = score - maxScore;
            var temperature = Math.Max(noise, _minimumNoiseTemperature);
            var normalizedDelta = scoreRange > 0f ? delta / scoreRange : 0f;
            return MathF.Exp(normalizedDelta / temperature);
        }

        private static float ApplyRiskBias(float weight, int index, int topN, float riskBias)
        {
            if (riskBias == 0f)
                return weight;

            var rank = 1f - (float)index / Math.Max(topN - 1, 1);
            weight *= 1f + riskBias * (rank - _neutralRiskBiasRank);
            return Math.Max(0f, weight);
        }

        private static CellId SelectWeightedCandidate(
            List<(CellId move, float score)> scored,
            int topN,
            float totalWeight,
            Span<float> weights,
            IBotRandom rng)
        {
            var roll = rng.NextFloat01() * totalWeight;
            var cumulative = 0f;

            for (var i = 0; i < topN; i++)
            {
                cumulative += weights[i];
                
                if (roll <= cumulative)
                    return scored[i].move;
            }

            return scored[0].move;
        }
    }
}