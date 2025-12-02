using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace AlgoritmaPuncakMod.AI
{
    internal sealed class AIBehaviorController
    {
        private readonly EnemyAI _enemy;
        private readonly NavMeshAgent _agent;
        private readonly AIBlackboard _blackboard = new();
        private readonly AISensorSuite _sensors = new();
        private readonly Dictionary<BehaviorType, AIState> _states;

        private BehaviorType _currentType;
        private AIState? _currentState;

        internal AIBehaviorController(EnemyAI enemy, float defaultTerritoryRadius)
        {
            _enemy = enemy;
            _agent = enemy.GetComponent<NavMeshAgent>();
            _states = new Dictionary<BehaviorType, AIState>
            {
                { BehaviorType.Stalk, new StalkState() },
                { BehaviorType.Hunt, new HuntState() },
                { BehaviorType.Territorial, new TerritorialState() },
                { BehaviorType.Lure, new LureState() },
                { BehaviorType.Pack, new PackState() },
                { BehaviorType.Reactive, new ReactiveState() }
            };

            _blackboard.InitializeTerritory(enemy.transform.position, defaultTerritoryRadius);
            _currentType = BehaviorType.Reactive;
            _currentState = _states[_currentType];
        }

        internal void Tick(float deltaTime)
        {
            if (_enemy == null) return;

            var profile = AlgoritmaPuncakMod.BalanceProfile;
            _blackboard.TickTimers(deltaTime);
            _sensors.Scan(_enemy, _agent, _blackboard, profile, deltaTime);
            SwitchStateIfNeeded(profile, deltaTime);

            var context = new AIStateContext(_enemy, _agent, _blackboard, profile, deltaTime);
            _currentState?.Tick(context);
        }

        private void SwitchStateIfNeeded(AIBalanceProfile profile, float deltaTime)
        {
            var desired = DetermineState(profile);
            if (_currentState != null && desired == _currentType)
            {
                return;
            }

            var context = new AIStateContext(_enemy, _agent, _blackboard, profile, deltaTime);
            _currentState?.Exit(context);
            _currentType = desired;
            _currentState = _states[_currentType];
            _currentState.Enter(context);
        }

        private BehaviorType DetermineState(AIBalanceProfile profile)
        {
            var enemyPos = _enemy.transform.position;

            if (_blackboard.PlayerInsideTerritory(enemyPos))
            {
                return BehaviorType.Territorial;
            }

            if (_blackboard.ShouldSwarm(profile))
            {
                return BehaviorType.Pack;
            }

            if (_blackboard.ShouldHunt(profile))
            {
                return BehaviorType.Hunt;
            }

            if (_blackboard.ShouldLure(profile))
            {
                return BehaviorType.Lure;
            }

            if (_blackboard.ShouldStalk(profile))
            {
                return BehaviorType.Stalk;
            }

            return BehaviorType.Reactive;
        }
    }
}
