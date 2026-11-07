using System.Collections.Generic;
using UnityEngine;

namespace ShootDemo.Enemy
{
    /// <summary>Shared entrance waypoints; every enemy may enter without reservations.</summary>
    public sealed class EnemyNarrowPassage : MonoBehaviour
    {
        [SerializeField] private Rect interior;
        [SerializeField] private Vector2 entrance;
        [SerializeField] private Vector2 outside;
        private static readonly List<EnemyNarrowPassage> passages = new List<EnemyNarrowPassage>();
        public static bool BlocksSpawn(UnityEngine.SceneManagement.Scene scene, Vector2 position)
        {
            foreach (var passage in passages)
                if (passage != null && passage.gameObject.scene == scene && passage.interior.Contains(position)) return true;
            return false;
        }
        private void OnEnable() { if (!passages.Contains(this)) passages.Add(this); }
        private void OnDisable() { passages.Remove(this); }

        public static Vector2 ConstrainEntranceSeparation(UnityEngine.SceneManagement.Scene scene,
            Vector2 position, Vector2 separation)
        {
            foreach (var passage in passages)
            {
                if (passage == null || passage.gameObject.scene != scene) continue;
                Vector2 axis = (passage.entrance - passage.outside).normalized;
                Vector2 offset = position - passage.outside;
                float forward = Vector2.Dot(offset, axis);
                float lateral = Mathf.Abs(Vector2.Dot(offset, new Vector2(-axis.y, axis.x)));
                // Keep aligned actors centered near the posts, but let the wider crowd
                // spread freely outside. Longitudinal spacing still applies at the mouth.
                if ((passage.interior.Contains(position) || (forward >= -1.5f && forward <= 4f)) && lateral < 0.4f)
                    return axis * Vector2.Dot(separation, axis);
            }
            return separation;
        }

        public static bool TryGetEntranceRoute(UnityEngine.SceneManagement.Scene scene, Vector2 target,
            out Vector2 outsidePoint, out Vector2 entrancePoint)
        {
            foreach (var passage in passages)
                if (passage != null && passage.gameObject.scene == scene && passage.interior.Contains(target))
                {
                    outsidePoint = passage.outside;
                    entrancePoint = passage.entrance;
                    return true;
                }
            outsidePoint = entrancePoint = target;
            return false;
        }

        public static bool AllowsRetreat(UnityEngine.SceneManagement.Scene scene, Vector2 from, Vector2 to, Vector2 target)
        {
            foreach (var passage in passages)
            {
                if (passage == null || passage.gameObject.scene != scene || !passage.interior.Contains(target)
                    || !passage.interior.Contains(from) || from.y < passage.entrance.y - 0.2f) continue;
                // Once a firing position has been reached, backing out through the
                // entrance would reactivate guidance and undo the retreat next tick.
                return passage.interior.Contains(to) && to.y >= passage.entrance.y - 0.2f;
            }
            return true;
        }

        public static bool HasClearedEntrance(UnityEngine.SceneManagement.Scene scene, Vector2 position, Vector2 target)
        {
            foreach (var passage in passages)
                if (passage != null && passage.gameObject.scene == scene && passage.interior.Contains(target)
                    && passage.interior.Contains(position) && position.y >= passage.entrance.y - 0.2f) return true;
            return false;
        }

        public static bool Guide(EnemyHealth actor, Vector2 target, out Vector2 waypoint)
        {
            waypoint = target;
            foreach (var passage in passages)
            {
                if (passage == null || passage.gameObject.scene != actor.gameObject.scene || !passage.interior.Contains(target)) continue;
                Vector2 position = actor.transform.position;
                if (passage.interior.Contains(position))
                {
                    if (position.y >= passage.entrance.y - 0.2f) return false;
                    waypoint = passage.entrance;
                    return true;
                }
                bool aligned = Mathf.Abs(position.x - passage.outside.x) < 0.035f
                    && position.y >= passage.outside.y - 0.1f && position.y <= passage.entrance.y;
                // Align before turning through the posts. A diagonal from the old
                // 0.12m tolerance can be narrower than the actor's navigation clearance.
                waypoint = aligned ? passage.entrance
                    : position.y >= passage.outside.y - 0.1f && position.y <= passage.entrance.y
                        && Mathf.Abs(position.x - passage.outside.x) < 0.5f
                        ? new Vector2(passage.outside.x, position.y) : passage.outside;
                return true;
            }
            return false;
        }
    }
}
