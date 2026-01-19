#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Runtime.GameModes.Wizard
{
    public sealed class GameModeCatalog : IGameModeCatalog
    {
        private readonly IGameModeStrategy[] _strategiesArray;
        private readonly GameModeMetadata[] _metadataArray;

        private readonly ReadOnlyCollection<IGameModeStrategy> _strategies;
        private readonly ReadOnlyCollection<GameModeMetadata> _metadata;
        private readonly Dictionary<string, IGameModeStrategy> _byId;

        public IReadOnlyList<IGameModeStrategy> Strategies => _strategies;
        public IReadOnlyList<GameModeMetadata> Metadata => _metadata;

        public GameModeCatalog(IEnumerable<IGameModeStrategy> strategies)
        {
            if (strategies == null)
                throw new ArgumentNullException(nameof(strategies));

            var list = new List<IGameModeStrategy>();
            var dict = new Dictionary<string, IGameModeStrategy>(StringComparer.Ordinal);

            foreach (var strategy in strategies)
            {
                if (strategy == null)
                    throw new ArgumentException("Strategy collection contains null.", nameof(strategies));
                if (string.IsNullOrWhiteSpace(strategy.ModeId))
                    throw new ArgumentException("Strategy has empty ModeId.", nameof(strategies));
                if (strategy.Metadata == null)
                    throw new ArgumentException($"Strategy '{strategy.ModeId}' has null Metadata.", nameof(strategies));
                if (!string.Equals(strategy.Metadata.Id, strategy.ModeId, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Strategy '{strategy.ModeId}' has mismatched Metadata.Id: '{strategy.Metadata.Id}'.",
                        nameof(strategies));

                if (!dict.TryAdd(strategy.ModeId, strategy))
                    throw new ArgumentException($"Duplicate mode id in catalog: '{strategy.ModeId}'.", nameof(strategies));

                list.Add(strategy);
            }

            list.Sort((a, b) =>
            {
                var byOrder = a.Metadata.SortOrder.CompareTo(b.Metadata.SortOrder);
                return byOrder != 0
                    ? byOrder
                    : string.CompareOrdinal(a.Metadata.Id, b.Metadata.Id);
            });

            var meta = new List<GameModeMetadata>(capacity: list.Count);
            foreach (var s in list)
                meta.Add(s.Metadata);

            _strategiesArray = list.ToArray();
            _metadataArray = meta.ToArray();

            _strategies = Array.AsReadOnly(_strategiesArray);
            _metadata = Array.AsReadOnly(_metadataArray);
            _byId = dict;
        }

        public bool TryGetStrategy(string modeId, out IGameModeStrategy? strategy)
        {
            if (string.IsNullOrWhiteSpace(modeId))
            {
                strategy = null;
                return false;
            }

            return _byId.TryGetValue(modeId, out strategy);
        }
    }
}

#nullable restore
