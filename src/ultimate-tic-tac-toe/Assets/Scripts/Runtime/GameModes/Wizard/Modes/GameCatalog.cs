#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Runtime.GameModes.Wizard.Modes
{
    public sealed class GameCatalog : IGameCatalog
    {
        private readonly ReadOnlyCollection<IGameStrategy> _strategies;
        private readonly ReadOnlyCollection<GameMetadata> _metadata;
        private readonly Dictionary<string, IGameStrategy> _byId;

        public IReadOnlyList<IGameStrategy> Strategies => _strategies;
        public IReadOnlyList<GameMetadata> Metadata => _metadata;

        public GameCatalog(IEnumerable<IGameStrategy> strategies)
        {
            if (strategies == null)
                throw new ArgumentNullException(nameof(strategies));

            var list = new List<IGameStrategy>();
            var dict = new Dictionary<string, IGameStrategy>(StringComparer.Ordinal);

            foreach (var strategy in strategies)
            {
                if (strategy == null)
                    throw new ArgumentException("Strategy collection contains null.", nameof(strategies));
                
                if (string.IsNullOrWhiteSpace(strategy.GameId))
                    throw new ArgumentException("Strategy has empty GameId.", nameof(strategies));
                
                if (strategy.Metadata == null)
                    throw new ArgumentException($"Strategy '{strategy.GameId}' has null Metadata.", nameof(strategies));
                
                if (!string.Equals(strategy.Metadata.Id, strategy.GameId, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Strategy '{strategy.GameId}' has mismatched Metadata.Id: '{strategy.Metadata.Id}'.",
                        nameof(strategies));
                }

                if (!dict.TryAdd(strategy.GameId, strategy))
                    throw new ArgumentException($"Duplicate mode id in catalog: '{strategy.GameId}'.", nameof(strategies));

                list.Add(strategy);
            }

            list.Sort((a, b) =>
            {
                var byOrder = a.Metadata.SortOrder.CompareTo(b.Metadata.SortOrder);
                
                return byOrder != 0
                    ? byOrder
                    : string.CompareOrdinal(a.Metadata.Id, b.Metadata.Id);
            });

            var meta = new List<GameMetadata>(capacity: list.Count);
            
            foreach (var s in list)
            {
                meta.Add(s.Metadata);
            }

            var strategiesArray = list.ToArray();
            var metadataArray = meta.ToArray();

            _strategies = Array.AsReadOnly(strategiesArray);
            _metadata = Array.AsReadOnly(metadataArray);
            _byId = dict;
        }

        public bool TryGetStrategy(string gameId, out IGameStrategy? strategy)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                strategy = null;
                return false;
            }

            return _byId.TryGetValue(gameId, out strategy);
        }
    }
}