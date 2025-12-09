using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace AlgoritmaPuncakMod.AI
{
    internal enum BTStatus
    {
        Failure,
        Success,
        Running
    }

    internal sealed class BTContext
    {
        internal BTContext(EnemyAI enemy, NavMeshAgent agent, AIBlackboard blackboard)
        {
            Enemy = enemy;
            Agent = agent;
            Blackboard = blackboard;
        }

        internal EnemyAI Enemy { get; }
        internal NavMeshAgent Agent { get; }
        internal AIBlackboard Blackboard { get; }
        internal AIBalanceProfile Profile { get; private set; }
        internal float DeltaTime { get; private set; }
        internal string ActiveAction { get; private set; }

        internal void Update(AIBalanceProfile profile, float deltaTime)
        {
            Profile = profile;
            DeltaTime = deltaTime;
            ActiveAction = null;
        }

        internal void SetActiveAction(string actionName)
        {
            ActiveAction = actionName;
        }
    }

    internal abstract class BTNode
    {
        protected BTNode(string name)
        {
            Name = name;
        }

        internal string Name { get; }
        internal abstract BTStatus Tick(BTContext context);
    }

    internal abstract class BTComposite : BTNode
    {
        protected BTComposite(string name, IEnumerable<BTNode> children) : base(name)
        {
            _children = new List<BTNode>(children);
        }

        protected readonly List<BTNode> _children;
    }

    internal sealed class BTPrioritySelector : BTComposite
    {
        internal BTPrioritySelector(string name, params BTNode[] children) : base(name, children) { }

        internal override BTStatus Tick(BTContext context)
        {
            foreach (var child in _children)
            {
                var status = child.Tick(context);
                if (status != BTStatus.Failure)
                {
                    return status;
                }
            }

            return BTStatus.Failure;
        }
    }

    internal sealed class BTSequence : BTComposite
    {
        internal BTSequence(string name, params BTNode[] children) : base(name, children) { }

        internal override BTStatus Tick(BTContext context)
        {
            for (int i = 0; i < _children.Count; i++)
            {
                var status = _children[i].Tick(context);
                if (status != BTStatus.Success)
                {
                    return status;
                }
            }

            return BTStatus.Success;
        }
    }

    internal sealed class BTConditionNode : BTNode
    {
        private readonly Func<BTContext, bool> _predicate;

        internal BTConditionNode(string name, Func<BTContext, bool> predicate) : base(name)
        {
            _predicate = predicate;
        }

        internal override BTStatus Tick(BTContext context)
        {
            return _predicate(context) ? BTStatus.Success : BTStatus.Failure;
        }
    }

    internal sealed class BTActionNode : BTNode
    {
        private readonly Func<BTContext, BTStatus> _action;

        internal BTActionNode(string name, Func<BTContext, BTStatus> action) : base(name)
        {
            _action = action;
        }

        internal override BTStatus Tick(BTContext context)
        {
            return _action(context);
        }
    }

    internal static class BehaviorConditions
    {
        internal static bool PlayerInsideTerritory(BTContext context)
        {
            return context.Blackboard.PlayerInsideTerritory(context.Enemy.transform.position);
        }

        internal static bool ShouldSwarm(BTContext context)
        {
            return context.Blackboard.ShouldSwarm(context.Profile);
        }

        internal static bool ShouldHunt(BTContext context)
        {
            return context.Blackboard.ShouldHunt(context.Profile);
        }

        internal static bool ShouldLure(BTContext context)
        {
            return context.Blackboard.ShouldLure(context.Profile);
        }

        internal static bool ShouldStalk(BTContext context)
        {
            return context.Blackboard.ShouldStalk(context.Profile);
        }
    }

    internal static class BehaviorActions
    {
        internal static BTStatus ExecuteTerritorial(BTContext context)
        {
            var agent = context.Agent;
            if (agent == null)
            {
                return BTStatus.Failure;
            }

            var playerPos = context.Blackboard.LastKnownPlayerPosition;
            var enemyPos = context.Enemy.transform.position;
            bool playerInside = context.Blackboard.PlayerInsideTerritory(enemyPos);

            Vector3 target = playerInside && !float.IsPositiveInfinity(playerPos.x)
                ? playerPos
                : context.Blackboard.TerritoryCenter;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(target, out hit, context.Profile.TerritoryRadius, NavMesh.AllAreas))
            {
                agent.speed = playerInside ? 7f : 3f;
                agent.SetDestination(hit.position);
                context.SetActiveAction("Territorial");
                return BTStatus.Running;
            }

            return BTStatus.Failure;
        }

        internal static BTStatus ExecutePack(BTContext context)
        {
            var agent = context.Agent;
            if (agent == null)
            {
                return BTStatus.Failure;
            }

            var allies = context.Blackboard.NearbyAllies;
            if (allies.Count == 0)
            {
                return BTStatus.Failure;
            }

            Vector3 separation = Vector3.zero;
            Vector3 alignment = Vector3.zero;
            Vector3 cohesion = Vector3.zero;

            for (int i = 0; i < allies.Count; i++)
            {
                var ally = allies[i];
                if (ally == null)
                {
                    continue;
                }

                var toAlly = ally.transform.position - context.Enemy.transform.position;
                separation -= toAlly / Mathf.Max(toAlly.sqrMagnitude, 0.1f);
                alignment += ally.transform.forward;
                cohesion += ally.transform.position;
            }

            separation /= allies.Count;
            alignment = (alignment / allies.Count).normalized;
            cohesion = ((cohesion / allies.Count) - context.Enemy.transform.position).normalized;

            var playerPos = context.Blackboard.LastKnownPlayerPosition;
            var target = playerPos.Equals(Vector3.positiveInfinity) ? context.Blackboard.PackCenter : playerPos;
            var direction = (separation * 1.5f + alignment + cohesion).normalized;
            var offset = direction * context.Profile.PackCohesionRadius * 0.5f;

            if (target != Vector3.zero)
            {
                target += offset;
            }

            NavMeshHit hit;
            if (NavMesh.SamplePosition(target, out hit, context.Profile.PackCohesionRadius, NavMesh.AllAreas))
            {
                agent.speed = 5f;
                agent.SetDestination(hit.position);
                context.SetActiveAction("Pack");
                return BTStatus.Running;
            }

            return BTStatus.Failure;
        }

        internal static BTStatus ExecuteHunt(BTContext context)
        {
            var agent = context.Agent;
            if (agent == null)
            {
                return BTStatus.Failure;
            }

            var enemyPos = context.Enemy.transform.position;
            var lastPos = context.Blackboard.LastKnownPlayerPosition;
            if (float.IsPositiveInfinity(lastPos.x))
            {
                return BTStatus.Failure;
            }

            var direction = (lastPos - enemyPos).normalized;
            var predicted = lastPos + direction * context.Profile.HuntPredictionLead;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(predicted, out hit, 8f, NavMesh.AllAreas))
            {
                agent.speed = Mathf.Max(agent.speed, 6f);
                agent.acceleration = Mathf.Max(agent.acceleration, 16f);
                agent.stoppingDistance = 0.5f;
                agent.SetDestination(hit.position);
                context.SetActiveAction("Hunt");
                return BTStatus.Running;
            }

            return BTStatus.Failure;
        }

        internal static BTStatus ExecuteLure(BTContext context)
        {
            var agent = context.Agent;
            if (agent == null)
            {
                return BTStatus.Failure;
            }

            context.Blackboard.ResetLureTimer();

            var anchor = context.Enemy.transform.position + UnityEngine.Random.insideUnitSphere * 4f;
            anchor.y = context.Enemy.transform.position.y;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(anchor, out hit, 3f, NavMesh.AllAreas))
            {
                agent.speed = 2.5f;
                agent.SetDestination(hit.position);
                context.SetActiveAction("Lure");
                return BTStatus.Running;
            }

            return BTStatus.Failure;
        }

        internal static BTStatus ExecuteStalk(BTContext context)
        {
            var agent = context.Agent;
            if (agent == null)
            {
                return BTStatus.Failure;
            }

            if (context.Blackboard.LastKnownPlayerPosition.Equals(Vector3.positiveInfinity))
            {
                return BTStatus.Failure;
            }

            var enemyPos = context.Enemy.transform.position;
            var toPlayer = context.Blackboard.LastKnownPlayerPosition - enemyPos;
            float distance = toPlayer.magnitude;
            var desired = context.Blackboard.LastKnownPlayerPosition - toPlayer.normalized * context.Profile.StalkMinDistance;

            if (distance > context.Profile.StalkMaxDistance)
            {
                desired = context.Blackboard.LastKnownPlayerPosition - toPlayer.normalized * (context.Profile.StalkMaxDistance * 0.85f);
            }

            agent.speed = Mathf.Min(agent.speed, 3.5f);
            agent.stoppingDistance = context.Profile.StalkMinDistance * 0.5f;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(desired, out hit, 5f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                context.SetActiveAction("Stalk");
                return BTStatus.Running;
            }

            return BTStatus.Failure;
        }

        internal static BTStatus ExecuteReactive(BTContext context)
        {
            var agent = context.Agent;
            if (agent == null)
            {
                return BTStatus.Failure;
            }

            var threat = context.Blackboard.ReactiveThreatScore(context.Profile);
            agent.speed = Mathf.Lerp(2f, 6f, Mathf.Clamp01(threat));

            var patrolDirection = Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.25f) * 45f, 0f) * context.Enemy.transform.forward;
            var target = context.Enemy.transform.position + patrolDirection * 3f;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(target, out hit, 3f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                context.SetActiveAction("Reactive");
                return BTStatus.Running;
            }

            return BTStatus.Failure;
        }
    }

    internal static class BehaviorTreeFactory
    {
        internal static BTNode CreateTree()
        {
            return new BTPrioritySelector("Root",
                BuildTerritorialBranch(),
                BuildPackBranch(),
                BuildHuntBranch(),
                BuildLureBranch(),
                BuildStalkBranch(),
                new BTActionNode("Reactive", BehaviorActions.ExecuteReactive));
        }

        private static BTNode BuildTerritorialBranch()
        {
            return new BTSequence("TerritorialSequence",
                new BTConditionNode("PlayerInsideTerritory", BehaviorConditions.PlayerInsideTerritory),
                new BTActionNode("TerritorialAction", BehaviorActions.ExecuteTerritorial));
        }

        private static BTNode BuildPackBranch()
        {
            return new BTSequence("PackSequence",
                new BTConditionNode("ShouldSwarm", BehaviorConditions.ShouldSwarm),
                new BTActionNode("PackAction", BehaviorActions.ExecutePack));
        }

        private static BTNode BuildHuntBranch()
        {
            return new BTSequence("HuntSequence",
                new BTConditionNode("ShouldHunt", BehaviorConditions.ShouldHunt),
                new BTActionNode("HuntAction", BehaviorActions.ExecuteHunt));
        }

        private static BTNode BuildLureBranch()
        {
            return new BTSequence("LureSequence",
                new BTConditionNode("ShouldLure", BehaviorConditions.ShouldLure),
                new BTActionNode("LureAction", BehaviorActions.ExecuteLure));
        }

        private static BTNode BuildStalkBranch()
        {
            return new BTSequence("StalkSequence",
                new BTConditionNode("ShouldStalk", BehaviorConditions.ShouldStalk),
                new BTActionNode("StalkAction", BehaviorActions.ExecuteStalk));
        }
    }
}
