using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.AI;

namespace AlgoritmaPuncakMod.AI
{
    internal sealed class AISensorSuite
    {
        private readonly Dictionary<int, PlayerSnapshot> _playerSnapshots = new();
        private readonly List<EnemyAI> _allyBuffer = new(8);
        private readonly int _playerLayerMask = LayerMask.GetMask("Default", "Player", "Environment");

        internal void Scan(EnemyAI enemy, NavMeshAgent agent, AIBlackboard blackboard, AIBalanceProfile profile, float deltaTime)
        {
            if (enemy == null) return;

            var trackedPlayers = GatherPlayers();
            UpdatePlayerSnapshots(enemy, trackedPlayers, deltaTime);
            var snapshot = SelectTarget(enemy.transform.position, profile.HuntAggroDistance * 1.5f);

            if (snapshot != null)
            {
                bool visible = HasLineOfSight(enemy.transform.position + Vector3.up, snapshot.Position + Vector3.up * 0.8f);
                float noise = Mathf.Clamp01(snapshot.VelocityMagnitude * 0.25f);
                blackboard.UpdatePlayerInfo(enemy.transform.position, snapshot.Position, visible, noise, deltaTime);
            }
            else
            {
                blackboard.MarkPlayerLost(deltaTime);
            }

            var allies = CollectNearbyAllies(enemy, profile.PackCohesionRadius);
            blackboard.UpdateAllies(allies);
        }

        private static readonly List<PlayerControllerB> PlayerBuffer = new(4);

        private static IReadOnlyList<PlayerControllerB> GatherPlayers()
        {
            PlayerBuffer.Clear();
            var round = StartOfRound.Instance;
            if (round?.allPlayerScripts == null)
            {
                return PlayerBuffer;
            }

            foreach (var player in round.allPlayerScripts)
            {
                if (player == null) continue;
                PlayerBuffer.Add(player);
            }

            return PlayerBuffer;
        }

        private void UpdatePlayerSnapshots(EnemyAI enemy, IReadOnlyList<PlayerControllerB> players, float deltaTime)
        {
            var visited = new HashSet<int>();

            foreach (var player in players)
            {
                if (!enemy.PlayerIsTargetable(player, cannotBeInShip: false, overrideInsideFactoryCheck: false)) continue;

                int id = player.GetInstanceID();
                visited.Add(id);

                if (!_playerSnapshots.TryGetValue(id, out var snapshot))
                {
                    snapshot = new PlayerSnapshot(player);
                    _playerSnapshots[id] = snapshot;
                }

                snapshot.Update(player.transform.position, deltaTime);
            }

            var toRemove = new List<int>();
            foreach (var kvp in _playerSnapshots)
            {
                if (!visited.Contains(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var id in toRemove)
            {
                _playerSnapshots.Remove(id);
            }
        }

        private PlayerSnapshot SelectTarget(Vector3 enemyPosition, float maxDistance)
        {
            PlayerSnapshot closest = null;
            float bestDistance = maxDistance;

            foreach (var snapshot in _playerSnapshots.Values)
            {
                float distance = Vector3.Distance(enemyPosition, snapshot.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = snapshot;
                }
            }

            return closest;
        }

        private bool HasLineOfSight(Vector3 origin, Vector3 target)
        {
            var direction = target - origin;
            if (!Physics.Raycast(origin, direction.normalized, out var hitInfo, direction.magnitude, _playerLayerMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return hitInfo.collider != null && hitInfo.collider.CompareTag("Player");
        }

        private List<EnemyAI> CollectNearbyAllies(EnemyAI enemy, float radius)
        {
            _allyBuffer.Clear();

            var round = RoundManager.Instance;
            if (round?.SpawnedEnemies != null && round.SpawnedEnemies.Count > 0)
            {
                foreach (var candidate in round.SpawnedEnemies)
                {
                    if (candidate == null || candidate == enemy) continue;
                    if (Vector3.Distance(enemy.transform.position, candidate.transform.position) <= radius)
                    {
                        _allyBuffer.Add(candidate);
                    }
                }
            }
            else
            {
                var everyone = Object.FindObjectsOfType<EnemyAI>();
                foreach (var candidate in everyone)
                {
                    if (candidate == null || candidate == enemy) continue;
                    if (Vector3.Distance(enemy.transform.position, candidate.transform.position) <= radius)
                    {
                        _allyBuffer.Add(candidate);
                    }
                }
            }

            return _allyBuffer;
        }

        private sealed class PlayerSnapshot
        {
            private Vector3 _previousPosition;
            private readonly PlayerControllerB _player;

            internal PlayerSnapshot(PlayerControllerB player)
            {
                _player = player;
                Position = player.transform.position;
                _previousPosition = Position;
            }

            internal Vector3 Position { get; private set; }
            internal float VelocityMagnitude { get; private set; }

            internal void Update(Vector3 newPosition, float deltaTime)
            {
                Position = newPosition;
                var displacement = newPosition - _previousPosition;
                _previousPosition = newPosition;
                if (deltaTime > 0f)
                {
                    VelocityMagnitude = displacement.magnitude / deltaTime;
                }
            }
        }
    }
}
